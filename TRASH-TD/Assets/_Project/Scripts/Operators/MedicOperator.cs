using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Medic class — Healer (GDD 1.3).
    /// Restores HP to allied operators in range. Does not attack enemies.
    /// Targets the ally with the lowest HP percentage (most critical healing priority).
    /// Implements IHealer for the healing interface.
    /// </summary>
    public class MedicOperator : OperatorBase, IHealer
    {
        protected override EnemyBase FindTarget()
        {
            // Medics do not target enemies
            return null;
        }

        protected override void Update()
        {
            if (!isDeployed) return;

            equippedSkill?.UpdateSkill(Time.deltaTime);

            // Heal timing (uses attackInterval as heal tick interval)
            attackTimer += Time.deltaTime;
            if (attackTimer >= data.attackInterval)
            {
                attackTimer = 0f;
                OperatorBase healTarget = FindHealTarget();
                if (healTarget != null)
                {
                    Heal(healTarget);
                }
            }
        }

        public override void Attack(EnemyBase target)
        {
            // Medics do not deal damage
        }

        public void Heal(OperatorBase target)
        {
            if (target == null) return;
            target.Heal(GetHealAmount());
        }

        public int GetHealAmount()
        {
            return currentATK;
        }

        /// <summary>
        /// Finds the allied operator within heal range with the lowest HP percentage below 100%.
        /// </summary>
        private OperatorBase FindHealTarget()
        {
            if (OperatorManager.Instance == null || deployedCell == null || data == null || data.rangePattern == null)
                return null;

            var gridManager = FindFirstObjectByType<GridManager>();
            if (gridManager == null) return null;

            var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern);
            var candidates = OperatorManager.Instance.GetOperatorsInCells(rangeCells);

            OperatorBase mostInjured = null;
            float lowestRatio = 1.0f; // Only heal units under 100% HP

            for (int i = 0; i < candidates.Count; i++)
            {
                var ally = candidates[i];
                if (ally == null || !ally.IsDeployed || ally.CurrentHP >= ally.MaxHP) continue;

                float ratio = (float)ally.CurrentHP / ally.MaxHP;
                if (ratio < lowestRatio)
                {
                    lowestRatio = ratio;
                    mostInjured = ally;
                }
            }

            return mostInjured;
        }
    }
}
