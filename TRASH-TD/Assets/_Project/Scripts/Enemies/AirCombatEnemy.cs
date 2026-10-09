using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Shared airborne combat behavior. Air enemies keep flying along their path and attack
    /// the highest-priority (then nearest) valid operator in range whenever their attack is
    /// ready. They stop only for a brief pause on each attack, then resume moving while the
    /// attack interval recharges. Ground melee operators cannot hit them (OperatorBase.CanAttackTarget).
    /// </summary>
    public abstract class AirCombatEnemy : FlyerEnemy
    {
        /// <summary>Hover time per attack, as a fraction of the attack interval: attack, creep a little, attack.</summary>
        private const float AttackPauseFraction = 0.7f;
        /// <summary>Movement speed multiplier while a valid target is in range, so the flyer lingers over the fight.</summary>
        private const float EngagedSpeedMultiplier = 0.5f;
        /// <summary>How far ahead (in tiles) along the flight path targets are scored from.</summary>
        private const float LookAheadTiles = 1f;

        // Air units draw above ground units (enemies 4, operators 5) and foreground artwork (10).
        private const int AirSortingOrder = 12;
        // The ground shadow draws over operators/ground enemies so the flyer visibly passes above them.
        private const int ShadowSortingOrder = 6;
        private const float ShadowDrop = 0.34f;

        private static Sprite shadowSprite;

        private SpriteRenderer shadowRenderer;
        private float attackPauseTimer;
        private float hoverPhase;
        private OperatorBase lockedTarget;

        protected abstract bool IsTargetable(OperatorBase op);
        protected abstract int GetTargetPriority(OperatorBase op);
        protected abstract bool IsTargetInRange(OperatorBase op);
        protected abstract void PerformAirAttack(OperatorBase target);

        public override void Initialize(EnemyData enemyData, int difficultyLevel)
        {
            base.Initialize(enemyData, difficultyLevel);
            CreateAirborneVisuals();
            attackPauseTimer = 0f;
            lockedTarget = null;
            hoverPhase = Random.value * Mathf.PI * 2f;
            // Ready to strike on first contact; flyers pass through range quickly.
            attackTimer = StageCombatModifiers.GetAttackInterval(data.attackInterval);
        }

        protected override void Update()
        {
            // Base handles chill/freeze and calls MoveAlongPath (gated below while attacking).
            base.Update();
            if (IsDead) return;

            if (attackPauseTimer > 0f)
                attackPauseTimer = Mathf.Max(0f, attackPauseTimer - Time.deltaTime);

            UpdateHoverShadow();
            if (IsFrozen) return;

            // Re-evaluated every frame so the flyer slows down as soon as something is in range.
            lockedTarget = FindTarget();

            float interval = StageCombatModifiers.GetAttackInterval(data.attackInterval);
            attackTimer = Mathf.Min(attackTimer + Time.deltaTime, interval);
            if (attackTimer < interval || lockedTarget == null) return;

            attackTimer = 0f;
            attackPauseTimer = interval * AttackPauseFraction;
            PerformAirAttack(lockedTarget);
        }

        protected override void MoveAlongPath()
        {
            if (attackPauseTimer > 0f) return;

            if (lockedTarget == null)
            {
                base.MoveAlongPath();
                return;
            }

            // Engaged: creep forward between attacks instead of flying past the target.
            float normalSpeed = currentMoveSpeed;
            currentMoveSpeed *= EngagedSpeedMultiplier;
            base.MoveAlongPath();
            currentMoveSpeed = normalSpeed;
        }

        /// <summary>
        /// Keeps the current target while it stays valid and in range, unless a higher-priority
        /// class comes into range. New targets: highest priority, then the operator closest to a
        /// point just ahead on the flight path (it stays in range longest).
        /// </summary>
        protected OperatorBase FindTarget()
        {
            if (OperatorManager.Instance == null) return null;

            bool lockStillValid = IsValidTarget(lockedTarget);
            int lockedPriority = lockStillValid ? GetTargetPriority(lockedTarget) : int.MaxValue;

            Vector3 aimPoint = GetLookAheadPoint();
            var operators = OperatorManager.Instance.DeployedOperators;
            OperatorBase best = null;
            int bestPriority = int.MaxValue;
            float bestScore = float.PositiveInfinity;

            for (int i = 0; i < operators.Count; i++)
            {
                OperatorBase candidate = operators[i];
                if (!IsValidTarget(candidate)) continue;

                int priority = GetTargetPriority(candidate);
                Vector3 offset = candidate.transform.position - aimPoint;
                offset.z = 0f;
                float score = offset.sqrMagnitude;
                if (best == null || priority < bestPriority ||
                    (priority == bestPriority && score < bestScore))
                {
                    best = candidate;
                    bestPriority = priority;
                    bestScore = score;
                }
            }

            if (lockStillValid && bestPriority >= lockedPriority)
                return lockedTarget;
            return best;
        }

        private bool IsValidTarget(OperatorBase op)
        {
            return op != null && op.IsDeployed && IsTargetable(op) && IsTargetInRange(op);
        }

        /// <summary>A point slightly ahead along the remaining path (the flyer's own position if none).</summary>
        private Vector3 GetLookAheadPoint()
        {
            Vector3 point = transform.position;
            if (path == null) return point;

            float remaining = CellSize * LookAheadTiles;
            for (int i = currentPathIndex; i < path.Count && remaining > 0f; i++)
            {
                Vector3 segment = path[i] - point;
                segment.z = 0f;
                float length = segment.magnitude;
                if (length <= 0.0001f) continue;
                if (length >= remaining) return point + segment / length * remaining;
                point += segment;
                remaining -= length;
            }
            return point;
        }

        protected static float CellSize
        {
            get
            {
                GridManager grid = GridManagerCache;
                return grid != null ? grid.CellSize : 1f;
            }
        }

        private static GridManager cachedGrid;
        private static GridManager GridManagerCache
        {
            get
            {
                if (cachedGrid == null) cachedGrid = FindFirstObjectByType<GridManager>();
                return cachedGrid;
            }
        }

        protected bool IsWithinCircularRange(OperatorBase op, float radiusInTiles)
        {
            if (op == null) return false;

            float radius = CellSize * radiusInTiles;
            Vector3 offset = op.transform.position - transform.position;
            offset.z = 0f;
            return offset.sqrMagnitude <= radius * radius;
        }

        /// <summary>Operator on the tile below the flyer or any of the 8 surrounding tiles.</summary>
        protected bool IsOnOrAdjacent(OperatorBase op)
        {
            GridManager grid = GridManagerCache;
            if (grid == null || op == null) return false;

            Vector2Int ownPosition = grid.WorldToGridPosition(transform.position);
            Vector2Int operatorPosition = op.DeployedCell != null
                ? op.DeployedCell.GridPosition
                : grid.WorldToGridPosition(op.transform.position);
            return Mathf.Abs(operatorPosition.x - ownPosition.x) <= 1 &&
                   Mathf.Abs(operatorPosition.y - ownPosition.y) <= 1;
        }

        protected void PlayAirMeleeAttack(OperatorBase target)
        {
            MeleeSwipeVisual.Play(transform.position, target.transform.position, new Color(1f, 0.3f, 0.18f, 1f));
            target.TryTakeAttackDamage(CurrentATK, Data.damageType, this);
        }

        /// <summary>
        /// Projectile that hits the target for full damage and splashes operators within
        /// <paramref name="blastRadiusInTiles"/> of it for <paramref name="collateralRatio"/> of ATK.
        /// </summary>
        protected void PlayAirRangedAttack(OperatorBase target, float blastRadiusInTiles, float collateralRatio)
        {
            CombatProjectileVisual.Fire(
                transform.position,
                target.transform.position,
                new Color(0.75f, 0.45f, 1f, 1f),
                9f,
                0.12f,
                0.08f,
                false,
                () => ApplyRangedImpact(target, blastRadiusInTiles, collateralRatio));
        }

        private void ApplyRangedImpact(OperatorBase target, float blastRadiusInTiles, float collateralRatio)
        {
            if (target == null || !target.IsDeployed) return;

            Vector3 impactPoint = target.transform.position;
            float radius = CellSize * blastRadiusInTiles;
            target.TryTakeAttackDamage(CurrentATK, Data.damageType, this);
            AoeBlastVisual.PlayCircle(impactPoint, radius, new Color(0.7f, 0.4f, 1f, 0.35f));

            if (OperatorManager.Instance == null) return;

            // Copy: damage can kill/undeploy operators and modify the deployed list.
            var operators = new List<OperatorBase>(OperatorManager.Instance.DeployedOperators);
            int collateralATK = Mathf.Max(1, Mathf.RoundToInt(CurrentATK * collateralRatio));
            for (int i = 0; i < operators.Count; i++)
            {
                OperatorBase collateral = operators[i];
                if (collateral == null || collateral == target || !collateral.IsDeployed) continue;

                Vector3 offset = collateral.transform.position - impactPoint;
                offset.z = 0f;
                if (offset.sqrMagnitude <= radius * radius)
                    collateral.TryTakeAttackDamage(collateralATK, Data.damageType, this);
            }
        }

        // ========================
        // Airborne visuals
        // ========================

        private void CreateAirborneVisuals()
        {
            SpriteRenderer renderer = GetComponent<SpriteRenderer>();
            if (renderer == null) return;

            renderer.sortingOrder = AirSortingOrder;
            renderer.color = GetAirColor();

            if (shadowRenderer != null) return;

            GameObject shadow = new GameObject("AirShadow");
            shadow.transform.SetParent(transform, false);
            shadow.transform.localPosition = new Vector3(0f, -ShadowDrop, 0.1f);
            shadowRenderer = shadow.AddComponent<SpriteRenderer>();
            shadowRenderer.sprite = GetShadowSprite();
            shadowRenderer.color = new Color(0f, 0f, 0f, 0.4f);
            shadowRenderer.sortingLayerID = renderer.sortingLayerID;
            shadowRenderer.sortingOrder = ShadowSortingOrder;
            UpdateHoverShadow();
        }

        /// <summary>Shadow breathes as if the flyer bobs up and down; shrinks/fades when "higher".</summary>
        private void UpdateHoverShadow()
        {
            if (shadowRenderer == null) return;

            hoverPhase += Time.deltaTime * 3f;
            float bob = (Mathf.Sin(hoverPhase) + 1f) * 0.5f; // 0 = low, 1 = high
            float size = CellSize * Mathf.Lerp(0.62f, 0.5f, bob);
            Vector3 parentScale = transform.lossyScale;
            shadowRenderer.transform.localScale = new Vector3(
                size / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
                size * 0.32f / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
                1f);
            Color color = shadowRenderer.color;
            color.a = Mathf.Lerp(0.45f, 0.28f, bob);
            shadowRenderer.color = color;
        }

        /// <summary>Soft-edged 1x1-unit ellipse generated once and shared by all flyers.</summary>
        private static Sprite GetShadowSprite()
        {
            if (shadowSprite != null) return shadowSprite;

            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / center;
                    float dy = (y - center) / center;
                    float alpha = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.SmoothStep(0f, 1f, alpha * 1.6f) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            shadowSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return shadowSprite;
        }

        protected virtual Color GetAirColor()
        {
            return Color.white;
        }
    }
}
