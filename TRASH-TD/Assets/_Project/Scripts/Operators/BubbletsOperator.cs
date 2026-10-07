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
    /// Bubblets — 3-Star Medic.
    /// Range covers self and 8 surrounding tiles (3x3 area).
    /// Does not heal directly; every 1.5 seconds, grants an allied operator in range
    /// a protective bubble shield that negates one instance of damage (even at full HP).
    /// When the bubble pops, it heals the recipient and deals Arts damage equal to Bubblets' ATK
    /// to enemies on that tile or directly in front.
    /// </summary>
    public class BubbletsOperator : MedicOperator
    {
        protected override EnemyBase FindTarget()
        {
            // Bubblets does not target enemies
            return null;
        }

        public override void Attack(EnemyBase target)
        {
            // Bubblets does not deal basic attack damage directly to enemies
        }

        public override void Heal(OperatorBase target)
        {
            // Forward healing requests to shielding
            if (target != null && isDeployed)
            {
                Shield(target);
            }
        }

        protected override void Update()
        {
            if (!isDeployed) return;

            equippedSkill?.UpdateSkill(Time.deltaTime);

            // Shield cast interval (uses attackInterval as cycle interval)
            attackTimer += Time.deltaTime;
            float interval = Combat.StageCombatModifiers.GetAttackInterval(data != null ? data.attackInterval : 1.5f);
            if (attackTimer >= interval)
            {
                attackTimer = 0f;
                OperatorBase target = FindShieldTarget();
                if (target != null)
                {
                    Shield(target);
                }
            }
        }

        public void Shield(OperatorBase target)
        {
            if (target == null || !isDeployed) return;

            PlayAttackSound();

            CombatProjectileVisual.Fire(
                transform.position,
                target.transform.position,
                new Color(0.4f, 0.85f, 1f, 0.9f),
                11f,
                0.15f,
                0.08f,
                true,
                () =>
                {
                    if (target != null && target.IsDeployed)
                    {
                        BubbleShield.Apply(target, currentATK, this);
                    }
                });
        }

        /// <summary>
        /// Selects the best unshielded ally in range to grant a bubble shield.
        /// Prioritizes actively blocking allies, then injured allies, then unshielded full HP allies.
        /// </summary>
        private OperatorBase FindShieldTarget()
        {
            if (OperatorManager.Instance == null || deployedCell == null || data == null || data.rangePattern == null)
                return null;

            GridManager gridManager = FindFirstObjectByType<GridManager>();
            if (gridManager == null) return null;

            GridCell[] rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
            List<OperatorBase> candidates = OperatorManager.Instance.GetOperatorsInCells(rangeCells);
            if (candidates == null || candidates.Count == 0) return null;

            OperatorBase bestTarget = null;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < candidates.Count; i++)
            {
                OperatorBase ally = candidates[i];
                if (ally == null || !ally.IsDeployed || BubbleShield.HasActiveShield(ally))
                    continue;

                float score = 10f;

                // Priority 1: Actively blocking enemies (taking front-line pressure)
                if (ally.GetCurrentBlockCount() > 0)
                {
                    score += 1000f;
                }

                // Priority 2: Injured allies (lower HP percentage gets higher score)
                if (ally.MaxHP > 0)
                {
                    float missingRatio = 1f - Mathf.Clamp01((float)ally.CurrentHP / ally.MaxHP);
                    score += missingRatio * 100f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = ally;
                }
            }

            return bestTarget;
        }
    }
}

