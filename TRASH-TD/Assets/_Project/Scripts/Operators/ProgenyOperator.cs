using System.Collections.Generic;
using UnityEngine;
using TrashTD.Audio;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Progeny — 2-Star Medic.
    /// Range covers own tile and the 4 adjacent tiles.
    /// Ability: Chain Heal — The heal bounces between 2 operators (healing reduced by 50% on bounce).
    /// The bounce can travel outside of Progeny's range as long as the second operator is in the
    /// surrounding 8 tiles of the first healed operator.
    /// </summary>
    public class ProgenyOperator : MedicOperator
    {
        private static readonly Color PrimaryHealColor = new Color(0.2f, 0.95f, 0.55f);
        private static readonly Color BounceHealColor = new Color(0.35f, 1f, 0.75f);

        public override void Heal(OperatorBase target)
        {
            if (target == null || !isDeployed) return;

            int primaryHeal = GetHealAmount();
            target.Heal(primaryHeal);

            PlayHealSound();

            // Projectile visual from Progeny to primary heal recipient
            CombatProjectileVisual.Fire(
                transform.position,
                target.transform.position,
                PrimaryHealColor,
                14f,
                0.13f,
                0.06f,
                true);

            // Chain heal: Bounce to a second operator in the surrounding 8 tiles of the first target
            OperatorBase bounceTarget = FindBounceTarget(target);
            if (bounceTarget != null)
            {
                int bounceHeal = Mathf.Max(1, Mathf.RoundToInt(primaryHeal * 0.5f));
                bounceTarget.Heal(bounceHeal);

                // Projectile visual from first target to bounce target
                CombatProjectileVisual.Fire(
                    target.transform.position,
                    bounceTarget.transform.position,
                    BounceHealColor,
                    14f,
                    0.11f,
                    0.05f,
                    true);
            }
        }

        /// <summary>
        /// Finds the most injured allied operator in the 8 surrounding tiles of the primary target.
        /// Can bounce outside Progeny's own range.
        /// </summary>
        private OperatorBase FindBounceTarget(OperatorBase primaryTarget)
        {
            if (primaryTarget == null || primaryTarget.DeployedCell == null || OperatorManager.Instance == null)
            {
                return null;
            }

            GridManager gridManager = FindFirstObjectByType<GridManager>();
            if (gridManager == null)
            {
                return null;
            }

            Vector2Int center = primaryTarget.DeployedCell.GridPosition;
            List<GridCell> surroundingCells = new List<GridCell>(8);

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;

                    GridCell cell = gridManager.GetCell(center.x + dx, center.y + dy);
                    if (cell != null)
                    {
                        surroundingCells.Add(cell);
                    }
                }
            }

            var candidates = OperatorManager.Instance.GetOperatorsInCells(surroundingCells);
            OperatorBase bestTarget = null;
            float lowestRatio = 1.0f; // Only heal injured allies below 100% HP

            for (int i = 0; i < candidates.Count; i++)
            {
                var ally = candidates[i];
                if (ally == null || !ally.IsDeployed || !ally.CanReceiveHealing) continue;
                if (ReferenceEquals(ally, primaryTarget)) continue; // Must bounce to another operator
                if (ally.CurrentHP >= ally.MaxHP) continue;

                float ratio = (float)ally.CurrentHP / ally.MaxHP;
                if (ratio < lowestRatio)
                {
                    lowestRatio = ratio;
                    bestTarget = ally;
                }
            }

            return bestTarget;
        }

        private void PlayHealSound()
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfx(SfxId.GameplayMagicAttack);
            }
        }
    }
}

