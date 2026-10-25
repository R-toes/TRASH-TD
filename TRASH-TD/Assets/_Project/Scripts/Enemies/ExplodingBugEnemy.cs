using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Exploding Bug — a Rusher that detonates on death, dealing its ATK as physical damage
    /// to all operators on adjacent (4-directional) tiles.
    /// Same base stats as the Toxic Rusher.
    /// </summary>
    public class ExplodingBugEnemy : RusherEnemy
    {
        private static readonly Vector2Int[] AdjacentOffsets =
        {
            new Vector2Int( 1,  0),
            new Vector2Int(-1,  0),
            new Vector2Int( 0,  1),
            new Vector2Int( 0, -1),
        };

        protected override void Die()
        {
            Explode();
            base.Die();
        }

        private void Explode()
        {
            // Visual feedback — orange/yellow burst using the existing projectile VFX system.
            var explosionPos = VisualPosition;
            Color flashColor = new Color(1f, 0.55f, 0.1f, 1f);

            // Expanding ring blast visual — radius 1.5 covers the full adjacent-tile area.
            AoeBlastVisual.PlayCircle(explosionPos, 1.5f, flashColor, AoeStyle.Explosion);

            // Flash a "BOOM!" text above the enemy.
            FloatingCombatNumber.ShowText(explosionPos, "BOOM!", flashColor);

            // Damage operators on the 4 adjacent tiles.
            if (OperatorManager.Instance == null || data == null)
                return;

            // GridManager has no static singleton — use FindAnyObjectByType (same as EnemyManager.Awake).
            GridManager gridManager = FindAnyObjectByType<GridManager>();
            if (gridManager == null)
                return;

            Vector2Int selfCell = gridManager.WorldToGridPosition(transform.position);
            var deployed = OperatorManager.Instance.DeployedOperators;

            for (int i = 0; i < deployed.Count; i++)
            {
                OperatorBase op = deployed[i];
                if (op == null || !op.IsDeployed || op.DeployedCell == null)
                    continue;

                Vector2Int opCell = op.DeployedCell.GridPosition;
                Vector2Int delta = opCell - selfCell;

                // Hit if the operator is on one of the 4 adjacent tiles.
                bool isAdjacent = (delta.x == 0 && (delta.y == 1 || delta.y == -1)) ||
                                  (delta.y == 0 && (delta.x == 1 || delta.x == -1));

                if (isAdjacent)
                {
                    // True damage so the explosion bypasses operator DEF — 3× ATK for a meaningful burst.
                    op.TakeTrueDamage(currentATK * 3);
                }
            }
        }
    }
}
