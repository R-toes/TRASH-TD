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

        private static readonly Color GlassColor = new Color(0.72f, 0.93f, 1f, 1f);

        private float attackPauseTimer;
        private OperatorBase lockedTarget;

        protected abstract bool IsTargetable(OperatorBase op);
        protected abstract int GetTargetPriority(OperatorBase op);
        protected abstract bool IsTargetInRange(OperatorBase op);
        protected abstract void PerformAirAttack(OperatorBase target);

        protected override bool HandlesOwnAttacks => true;

        /// <summary>The operator this flyer is fighting this frame (null with nothing in range).</summary>
        protected OperatorBase CurrentAirTarget => lockedTarget;

        public override void Initialize(EnemyData enemyData, int difficultyLevel)
        {
            base.Initialize(enemyData, difficultyLevel);
            attackPauseTimer = 0f;
            lockedTarget = null;
            // Ready to strike on first contact; flyers pass through range quickly.
            attackTimer = GetEffectiveAttackInterval();
        }

        protected override void Update()
        {
            // Base handles chill/freeze, the hover shadow and calls MoveAlongPath (gated below while attacking).
            base.Update();
            if (IsDead) return;

            if (attackPauseTimer > 0f)
                attackPauseTimer = Mathf.Max(0f, attackPauseTimer - Time.deltaTime);

            if (IsFrozen || IsStunned) return;

            // Re-evaluated every frame so the flyer slows down as soon as something is in range.
            lockedTarget = FindTarget();

            float interval = GetEffectiveAttackInterval();
            attackTimer = Mathf.Min(attackTimer + Time.deltaTime, interval);
            if (attackTimer < interval || lockedTarget == null) return;

            attackTimer = 0f;
            attackPauseTimer = GetAttackPause(interval);
            PlayAirAttackSound();
            PerformAirAttack(lockedTarget);
        }

        /// <summary>Plays as each attack starts. Talon Rat plays its own when the claws land instead.</summary>
        protected virtual void PlayAirAttackSound() => PlayAttackSound();

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
            currentMoveSpeed *= EngagedSpeed;
            base.MoveAlongPath();
            currentMoveSpeed = normalSpeed;
        }

        /// <summary>How long the flyer stops still on each attack. Default: most of the attack interval (attack, creep a little, attack).</summary>
        protected virtual float GetAttackPause(float attackInterval) => attackInterval * AttackPauseFraction;

        /// <summary>Movement speed multiplier while a valid target is in range.</summary>
        protected virtual float EngagedSpeed => EngagedSpeedMultiplier;

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

                int priority = candidate.ShouldTauntAttacker(transform.position, GridManagerCache)
                    ? -1
                    : GetTargetPriority(candidate);
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
            if (op == null || !op.IsDeployed || !op.IsTargetable || !IsTargetInRange(op)) return false;
            if (op.ShouldTauntAttacker(transform.position, GridManagerCache)) return true;
            return IsTargetable(op);
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

        protected bool IsWithinCircularRange(OperatorBase op, float radiusInTiles)
        {
            if (op == null) return false;

            float radius = CellSize * radiusInTiles;
            Vector3 offset = op.transform.position - transform.position;
            offset.z = 0f;
            return offset.sqrMagnitude <= radius * radius;
        }

        /// <summary>Operator on the tile below the flyer or one of the 4 orthogonally adjacent tiles (no diagonals).</summary>
        protected bool IsOnOrAdjacent(OperatorBase op)
        {
            GridManager grid = GridManagerCache;
            if (grid == null || op == null) return false;

            Vector2Int ownPosition = grid.WorldToGridPosition(transform.position);
            Vector2Int operatorPosition = op.DeployedCell != null
                ? op.DeployedCell.GridPosition
                : grid.WorldToGridPosition(op.transform.position);
            return Mathf.Abs(operatorPosition.x - ownPosition.x) +
                   Mathf.Abs(operatorPosition.y - ownPosition.y) <= 1;
        }

        protected void PlayAirMeleeAttack(OperatorBase target)
        {
            PlayAirMeleeSwipe(target);
            target.TryTakeAttackDamage(CurrentATK, Data.damageType, this);
        }

        /// <summary>The claw marks of an air melee strike, from the talons (ProjectileOrigin) to the target.</summary>
        protected void PlayAirMeleeSwipe(OperatorBase target)
        {
            if (target == null) return;
            MeleeSwipeVisual.Play(ProjectileOrigin, target.transform.position, new Color(1f, 0.3f, 0.18f, 1f), 1f, MeleeStyle.Claw);
        }

        /// <summary>
        /// A glass shard thrown from the sprite that shatters on the target, hitting it for full damage and
        /// splashing operators within <paramref name="blastRadiusInTiles"/> of it for <paramref name="collateralRatio"/> of ATK.
        /// </summary>
        protected void PlayAirRangedAttack(OperatorBase target, float blastRadiusInTiles, float collateralRatio)
        {
            CombatProjectileVisual.Fire(
                ProjectileOrigin,
                target.transform.position,
                GlassColor,
                9f,
                0.12f,
                0.08f,
                false,
                () => ApplyRangedImpact(target, blastRadiusInTiles, collateralRatio),
                ProjectileStyle.Glass);
        }

        private void ApplyRangedImpact(OperatorBase target, float blastRadiusInTiles, float collateralRatio)
        {
            if (target == null || !target.IsDeployed) return;

            Vector3 impactPoint = target.transform.position;
            float radius = CellSize * blastRadiusInTiles;
            PlayImpactSound();
            target.TryTakeAttackDamage(CurrentATK, Data.damageType, this);
            AoeBlastVisual.PlayCircle(impactPoint, radius, GlassColor, AoeStyle.Glass);

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
    }
}
