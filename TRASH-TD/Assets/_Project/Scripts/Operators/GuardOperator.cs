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
        private static Material isolationIndicatorMaterial;
        private LineRenderer isolationIndicator;

        protected override void Update()
        {
            base.Update();
            UpdateIsolationIndicator();
        }

        protected override void OnDeployed()
        {
            base.OnDeployed();
            CreateIsolationIndicator();
        }

        protected override void OnRetreated()
        {
            if (isolationIndicator != null)
            {
                isolationIndicator.gameObject.SetActive(false);
            }
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
            target.TakeDamage(damage, data.damageType);
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

        private void CreateIsolationIndicator()
        {
            if (data == null || !data.bonusWhenIsolated || isolationIndicator != null) return;

            var indicatorObject = new GameObject("IsolationBuffIndicator");
            indicatorObject.transform.SetParent(transform, false);
            indicatorObject.transform.localPosition = new Vector3(0f, 0f, 0.05f);

            isolationIndicator = indicatorObject.AddComponent<LineRenderer>();
            isolationIndicator.useWorldSpace = false;
            isolationIndicator.loop = true;
            isolationIndicator.positionCount = 17;
            isolationIndicator.startWidth = 0.02f;
            isolationIndicator.endWidth = 0.02f;
            isolationIndicator.sortingOrder = 4;
            isolationIndicator.sharedMaterial = GetIsolationIndicatorMaterial();

            const float outerRadius = 0.46f;
            const float innerRadius = 0.24f;
            for (int i = 0; i < isolationIndicator.positionCount; i++)
            {
                float angle = i / 16f * Mathf.PI * 2f;
                float radius = i % 2 == 0 ? outerRadius : innerRadius;
                isolationIndicator.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * radius,
                    Mathf.Sin(angle) * radius,
                    0f));
            }

            isolationIndicator.gameObject.SetActive(false);
        }

        private void UpdateIsolationIndicator()
        {
            if (data == null || !data.bonusWhenIsolated || !isDeployed) return;
            if (isolationIndicator == null) CreateIsolationIndicator();
            if (isolationIndicator == null) return;

            bool buffActive = !HasAdjacentOperator();
            if (isolationIndicator.gameObject.activeSelf != buffActive)
            {
                isolationIndicator.gameObject.SetActive(buffActive);
            }

            if (buffActive)
            {
                float pulse = 0.78f + Mathf.Sin(Time.time * 5f) * 0.18f;
                Color indicatorColor = new Color(1f, 0.08f, 0.06f, pulse);
                isolationIndicator.startColor = indicatorColor;
                isolationIndicator.endColor = indicatorColor;
            }
        }

        private static Material GetIsolationIndicatorMaterial()
        {
            if (isolationIndicatorMaterial != null) return isolationIndicatorMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            isolationIndicatorMaterial = new Material(shader);
            return isolationIndicatorMaterial;
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
