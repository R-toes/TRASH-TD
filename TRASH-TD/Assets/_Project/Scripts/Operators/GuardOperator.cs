using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Guard class — Melee DPS (GDD 1.3).
    /// High single-target damage, blocks 1–2 enemies.
    /// Targets the enemy with the lowest HP among blocked/nearby enemies (focus fire).
    /// </summary>
    public class GuardOperator : OperatorBase
    {
        private SpriteRenderer isolationAuraOuter;
        private SpriteRenderer isolationAuraInner;
        private SpriteRenderer operatorSpriteRenderer;

        protected override void Update()
        {
            base.Update();
            UpdateIsolationAura();
        }

        protected override void OnDeployed()
        {
            base.OnDeployed();
            CreateIsolationAura();
        }

        protected override void OnRetreated()
        {
            if (isolationAuraOuter != null) isolationAuraOuter.enabled = false;
            if (isolationAuraInner != null) isolationAuraInner.enabled = false;
            base.OnRetreated();
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            if (data != null && data.bonusWhenIsolated)
            {
                bool isIsolated = !HasAdjacentOperator();
                MeleeSwipeVisual.PlayStab(
                    transform.position,
                    target.transform.position,
                    isIsolated
                        ? new Color(0.12f, 0.008f, 0.015f, 1f)
                        : new Color(0.22f, 0.015f, 0.025f, 1f));

                if (isIsolated)
                {
                    PlayAttackSound();
                    int attackPower = Mathf.RoundToInt(currentATK * Mathf.Max(1f, data.isolatedAttackMultiplier));
                    CombatProjectileVisual.Fire(
                        transform.position,
                        target.transform.position,
                        new Color(1f, 0.2f, 0.15f),
                        12f,
                        0.08f,
                        0.04f,
                        false,
                        () => ApplyIsolatedDamage(target, attackPower));
                }
                else
                {
                    base.Attack(target);
                }

                return;
            }

            MeleeSwipeVisual.Play(transform.position, target.transform.position, new Color(1f, 0.78f, 0.3f, 1f));
            base.Attack(target);
        }

        private void ApplyIsolatedDamage(EnemyBase target, int attackPower)
        {
            if (target == null || target.IsDead) return;

            int damage = DamageCalculator.CalculateDamage(attackPower, GetTargetMitigation(target));
            target.TryTakeAttackDamage(damage, data.damageType);
        }

        private bool HasAdjacentOperator()
        {
            if (OperatorManager.Instance == null || deployedCell == null) return false;

            Vector2Int ownPosition = deployedCell.GridPosition;
            var deployedOperators = OperatorManager.Instance.DeployedOperators;
            for (int i = 0; i < deployedOperators.Count; i++)
            {
                OperatorBase other = deployedOperators[i];
                if (other == null || other == this || !other.IsDeployed || other.DeployedCell == null)
                    continue;

                Vector2Int offset = other.DeployedCell.GridPosition - ownPosition;
                if (Mathf.Abs(offset.x) <= 2 && Mathf.Abs(offset.y) <= 2 &&
                    (offset.x != 0 || offset.y != 0))
                {
                    return true;
                }
            }

            return false;
        }

        private void CreateIsolationAura()
        {
            if (data == null || !data.bonusWhenIsolated || isolationAuraOuter != null) return;

            operatorSpriteRenderer = GetComponent<SpriteRenderer>();
            if (operatorSpriteRenderer == null)
            {
                Debug.LogError($"{data.operatorName} requires a SpriteRenderer for its isolation aura.", this);
                return;
            }

            isolationAuraOuter = CreateAuraLayer(
                "IsolationAuraOuter",
                operatorSpriteRenderer.sortingOrder - 2,
                new Color(1f, 0.12f, 0.015f, 0.82f));
            isolationAuraInner = CreateAuraLayer(
                "IsolationAuraInner",
                operatorSpriteRenderer.sortingOrder - 1,
                new Color(1f, 0.55f, 0.06f, 0.72f));
        }

        private SpriteRenderer CreateAuraLayer(string objectName, int sortingOrder, Color color)
        {
            var auraObject = new GameObject(objectName);
            auraObject.transform.SetParent(transform, false);
            auraObject.transform.localPosition = Vector3.zero;

            var auraRenderer = auraObject.AddComponent<SpriteRenderer>();
            auraRenderer.sprite = operatorSpriteRenderer.sprite;
            auraRenderer.color = color;
            auraRenderer.flipX = operatorSpriteRenderer.flipX;
            auraRenderer.flipY = operatorSpriteRenderer.flipY;
            auraRenderer.sortingLayerID = operatorSpriteRenderer.sortingLayerID;
            auraRenderer.sortingOrder = sortingOrder;
            auraRenderer.sharedMaterial = operatorSpriteRenderer.sharedMaterial;
            auraRenderer.enabled = false;
            return auraRenderer;
        }

        private void UpdateIsolationAura()
        {
            if (data == null || !data.bonusWhenIsolated || !isDeployed) return;
            if (isolationAuraOuter == null) CreateIsolationAura();
            if (isolationAuraOuter == null || isolationAuraInner == null) return;

            bool buffActive = !HasAdjacentOperator();
            if (isolationAuraOuter.enabled != buffActive)
            {
                isolationAuraOuter.enabled = buffActive;
                isolationAuraInner.enabled = buffActive;
            }

            if (buffActive)
            {
                UpdateIsolationAuraAnimation(Time.time);
            }
        }

        private void UpdateIsolationAuraAnimation(float time)
        {
            isolationAuraOuter.sprite = operatorSpriteRenderer.sprite;
            isolationAuraInner.sprite = operatorSpriteRenderer.sprite;
            isolationAuraOuter.flipX = operatorSpriteRenderer.flipX;
            isolationAuraOuter.flipY = operatorSpriteRenderer.flipY;
            isolationAuraInner.flipX = operatorSpriteRenderer.flipX;
            isolationAuraInner.flipY = operatorSpriteRenderer.flipY;

            float pulse = 0.5f + 0.5f * Mathf.Sin(time * 5f);
            float flicker = Mathf.Sin(time * 13f) * 0.025f;
            isolationAuraOuter.transform.localScale = Vector3.one * (1.22f + pulse * 0.035f + flicker);
            isolationAuraInner.transform.localScale = Vector3.one * (1.11f + pulse * 0.025f - flicker * 0.5f);
            isolationAuraOuter.transform.localPosition = new Vector3(0f, Mathf.Sin(time * 7f) * 0.012f, 0.01f);
            isolationAuraInner.transform.localPosition = new Vector3(0f, Mathf.Sin(time * 9f + 1f) * 0.008f, 0.005f);

            Color outerColor = new Color(1f, Mathf.Lerp(0.06f, 0.22f, pulse), 0.015f, 0.7f + pulse * 0.22f);
            Color innerColor = new Color(1f, Mathf.Lerp(0.28f, 0.72f, pulse), 0.06f, 0.45f + pulse * 0.35f);
            isolationAuraOuter.color = outerColor;
            isolationAuraInner.color = innerColor;
        }

        protected override EnemyBase FindTarget()
        {
            // Priority 1: Attack blocked enemies first (focus on lowest HP among blocked)
            if (blockedEnemies.Count > 0)
            {
                EnemyBase lowestHP = null;
                int lowestHPValue = int.MaxValue;

                for (int i = 0; i < blockedEnemies.Count; i++)
                {
                    var enemy = blockedEnemies[i];
                    if (enemy != null && !enemy.IsDead && enemy.CurrentHP < lowestHPValue)
                    {
                        lowestHP = enemy;
                        lowestHPValue = enemy.CurrentHP;
                    }
                }

                if (lowestHP != null) return lowestHP;
            }

            // Priority 2: If not blocking, search for enemies within attack range pattern
            if (EnemyManager.Instance != null && deployedCell != null && data != null && data.rangePattern != null)
            {
                var gridManager = FindFirstObjectByType<GridManager>();
                if (gridManager != null)
                {
                    var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
                    var candidates = EnemyManager.Instance.GetEnemiesInCells(rangeCells);

                    EnemyBase lowestHP = null;
                    int lowestHPValue = int.MaxValue;

                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var enemy = candidates[i];
                        if (enemy != null && !enemy.IsDead && enemy.CurrentHP < lowestHPValue)
                        {
                            lowestHP = enemy;
                            lowestHPValue = enemy.CurrentHP;
                        }
                    }

                    return lowestHP;
                }
            }

            return null;
        }
    }
}
