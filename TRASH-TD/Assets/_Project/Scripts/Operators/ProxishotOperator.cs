using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Short-range physical sniper that fires a fan of pellets and damages a small area.
    /// Trait: For every enemy type killed, Increase attack by 3% (cap at 5 stacks) (resets when retreated).
    /// </summary>
    public class ProxishotOperator : SniperOperator
    {
        private const int PelletCount = 9;
        private const int MaxKillBonusStacks = 5;
        private const float AtkBonusPerType = 0.03f;

        private readonly HashSet<string> killedEnemyTypes = new HashSet<string>();

        public int KilledTypeCount => killedEnemyTypes.Count;

        public override int CurrentATK
        {
            get
            {
                int stacks = Mathf.Min(MaxKillBonusStacks, killedEnemyTypes.Count);
                return Mathf.RoundToInt(base.CurrentATK * (1f + (stacks * AtkBonusPerType)));
            }
        }

        protected override void OnDeployed()
        {
            killedEnemyTypes.Clear();
            base.OnDeployed();
        }

        protected override void OnRetreated()
        {
            killedEnemyTypes.Clear();
            base.OnRetreated();
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed || EnemyManager.Instance == null ||
                deployedCell == null || data == null || data.rangePattern == null)
                return;

            var gridManager = FindFirstObjectByType<GridManager>();
            if (gridManager == null) return;

            var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
            var targets = EnemyManager.Instance.GetEnemiesInCells(rangeCells, data.position);
            if (targets.Count == 0) return;

            Vector3 facingDirection = GetFacingDirection();
            Vector3 perpendicular = Vector3.Cross(facingDirection, Vector3.forward).normalized;
            float rangeDistance = 0f;
            float minLateralOffset = float.PositiveInfinity;
            float maxLateralOffset = float.NegativeInfinity;
            for (int i = 0; i < rangeCells.Length; i++)
            {
                var rangeCell = rangeCells[i];
                if (rangeCell == null) continue;
                Vector3 toCell = rangeCell.WorldPosition - transform.position;
                rangeDistance = Mathf.Max(rangeDistance, Vector3.Dot(toCell, facingDirection));
                float lateralOffset = Vector3.Dot(toCell, perpendicular);
                minLateralOffset = Mathf.Min(minLateralOffset, lateralOffset);
                maxLateralOffset = Mathf.Max(maxLateralOffset, lateralOffset);
            }
            if (rangeDistance <= 0f) return;

            PlayAttackSound();

            minLateralOffset -= gridManager.CellSize * 0.5f;
            maxLateralOffset += gridManager.CellSize * 0.5f;

            CombatProjectileVisual.FireShotgunSpread(
                transform.position,
                facingDirection,
                rangeDistance,
                minLateralOffset,
                maxLateralOffset,
                new Color(1f, 0.58f, 0.2f),
                8f,
                0.12f,
                0.06f,
                PelletCount,
                () => ApplyShotgunDamage(targets));
        }

        private void ApplyShotgunDamage(List<EnemyBase> targets)
        {
            if (targets == null) return;

            for (int i = 0; i < targets.Count; i++)
            {
                var inRangeTarget = targets[i];
                if (inRangeTarget == null || inRangeTarget.IsDead) continue;

                EnemyData enemyData = inRangeTarget.Data;
                int hpBefore = inRangeTarget.CurrentHP;

                int damage = DamageCalculator.CalculateDamage(CurrentATK, inRangeTarget.CurrentDEF);
                bool hit = inRangeTarget.TryTakeAttackDamage(damage, DamageType.Physical, this);

                if (hit && hpBefore > 0 && (inRangeTarget == null || inRangeTarget.IsDead || inRangeTarget.CurrentHP <= 0))
                {
                    RecordKilledEnemy(enemyData);
                }
            }
        }

        private void RecordKilledEnemy(EnemyData enemyData)
        {
            if (enemyData == null) return;

            string typeKey = !string.IsNullOrEmpty(enemyData.enemyName) ? enemyData.enemyName : enemyData.archetype.ToString();
            if (killedEnemyTypes.Add(typeKey))
            {
                int currentStacks = Mathf.Min(MaxKillBonusStacks, killedEnemyTypes.Count);
                FloatingCombatNumber.ShowText(transform.position + Vector3.up * 0.6f, $"+3% ATK ({currentStacks}/5)", new Color(1f, 0.85f, 0.3f));
            }
        }

        private Vector3 GetFacingDirection()
        {
            return Facing switch
            {
                OperatorFacing.Right => Vector3.right,
                OperatorFacing.Up => Vector3.up,
                OperatorFacing.Left => Vector3.left,
                OperatorFacing.Down => Vector3.down,
                _ => Vector3.right
            };
        }
    }
}
