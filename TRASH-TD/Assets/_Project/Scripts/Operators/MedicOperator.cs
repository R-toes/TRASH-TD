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

            // Heal timing (uses attackInterval as heal tick interval). Stays ready until someone
            // needs healing, so the first heal lands immediately.
            float interval = GetAttackInterval();
            attackTimer = Mathf.Min(attackTimer + Time.deltaTime, interval);

            // No heals between waves.
            if (IsCombatPaused) return;

            if (isStunned || isBound)
            {
                if (isStunned) UpdateStunStatus(Time.deltaTime);
                if (isBound) UpdateBindStatus(Time.deltaTime);
                return;
            }

            equippedSkill?.UpdateSkill(Time.deltaTime);

            if (attackTimer >= interval)
            {
                OperatorBase healTarget = FindHealTarget();
                if (healTarget != null)
                {
                    attackTimer = 0f;
                    Heal(healTarget);
                }
            }
        }

        public override void Attack(EnemyBase target)
        {
            // Medics do not deal damage
        }

        public virtual void Heal(OperatorBase target)
        {
            if (target == null || isStunned || isBound) return;
            target.Heal(GetHealAmount());
            // A Medic's heal is its "attack"; it only has a sound if the operator has its own Attack clip.
            Audio.UnitSfx.Play(data, Audio.UnitSfxEvent.Attack);
        }

        public virtual int GetHealAmount()
        {
            return currentATK;
        }

        /// <summary>
        /// Finds the allied operator within heal range with the lowest HP percentage below 100%.
        /// </summary>
        protected virtual OperatorBase FindHealTarget()
        {
            if (OperatorManager.Instance == null || deployedCell == null || data == null || data.rangePattern == null)
                return null;

            var gridManager = FindAnyObjectByType<GridManager>();
            if (gridManager == null) return null;

            var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
            var candidates = OperatorManager.Instance.GetOperatorsInCells(rangeCells);

            OperatorBase mostInjured = null;
            float lowestRatio = 1.0f; // Only heal units under 100% HP

            for (int i = 0; i < candidates.Count; i++)
            {
                var ally = candidates[i];
                if (ally == null || !ally.IsDeployed || !ally.CanReceiveHealing ||
                    ally.CurrentHP >= ally.MaxHP) continue;

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
