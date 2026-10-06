using System.Collections.Generic;
using UnityEngine;
using TrashTD.Audio;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Long-range explosive sniper. It can target only cells two to seven tiles
    /// in front of it and damages the target cell plus its eight neighbors.
    /// </summary>
    public sealed class BasurocketOperator : SniperOperator
    {
        private const int BlastRadius = 1;
        private const float BlastDamageMultiplier = 1f;

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed || EnemyManager.Instance == null)
                return;

            GridManager gridManager = FindFirstObjectByType<GridManager>();
            if (gridManager == null)
                return;

            Vector2Int impactPosition = gridManager.WorldToGridPosition(target.transform.position);
            List<GridCell> blastCells = new List<GridCell>();
            for (int y = -BlastRadius; y <= BlastRadius; y++)
            {
                for (int x = -BlastRadius; x <= BlastRadius; x++)
                {
                    GridCell cell = gridManager.GetCell(impactPosition.x + x, impactPosition.y + y);
                    if (cell != null)
                        blastCells.Add(cell);
                }
            }

            if (EnemyManager.Instance.GetEnemiesInCells(blastCells).Count == 0)
                return;

            PlayAttackSound();
            CombatProjectileVisual.Fire(
                transform.position,
                target.transform.position,
                new Color(1f, 0.25f, 0.08f),
                14f,
                0.18f,
                0.1f,
                true,
                () => ApplyBlastDamage(gridManager, impactPosition));
        }

        private void ApplyBlastDamage(GridManager gridManager, Vector2Int impactPosition)
        {
            if (gridManager == null || EnemyManager.Instance == null)
                return;

            List<GridCell> blastCells = new List<GridCell>();
            for (int y = -BlastRadius; y <= BlastRadius; y++)
            {
                for (int x = -BlastRadius; x <= BlastRadius; x++)
                {
                    GridCell cell = gridManager.GetCell(impactPosition.x + x, impactPosition.y + y);
                    if (cell != null)
                        blastCells.Add(cell);
                }
            }

            List<EnemyBase> blastTargets = EnemyManager.Instance.GetEnemiesInCells(blastCells);
            HashSet<EnemyBase> uniqueTargets = new HashSet<EnemyBase>();
            for (int i = 0; i < blastTargets.Count; i++)
            {
                EnemyBase blastTarget = blastTargets[i];
                if (blastTarget == null || blastTarget.IsDead || !uniqueTargets.Add(blastTarget))
                    continue;

                int damage = Mathf.RoundToInt(currentATK * BlastDamageMultiplier);
                damage = DamageCalculator.CalculateDamage(damage, blastTarget.CurrentDEF);
                blastTarget.TakeDamage(damage, DamageType.Physical);
            }
        }
    }
}
