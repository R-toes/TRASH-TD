using System.Collections.Generic;
using UnityEngine;
using TrashTD.Enemies;
using TrashTD.Operators;

namespace TrashTD.Combat
{
    /// <summary>
    /// Handles the blocking mechanic (GDD 1.4.4):
    /// - Evaluates whether an enemy reaching a tile with a deployed operator gets blocked.
    /// - Respects each operator's max block count.
    /// - Respects enemy unblockable traits (e.g., flyers, immune archetypes).
    /// - Re-evaluates blocking when blockers or enemies die/retreat.
    /// </summary>
    public static class BlockingSystem
    {
        /// <summary>
        /// Check if an enemy can be blocked by a given operator.
        /// </summary>
        public static bool CanBlock(OperatorBase blocker, EnemyBase enemy)
        {
            if (blocker == null || enemy == null) return false;
            if (!blocker.IsDeployed) return false;
            if (enemy.IsDead || enemy.IsBlocked) return false;

            // Flyers ignore ground blockers
            if (enemy.MovementType == Data.EnemyMovementType.Air) return false;

            // Enemy specific immunity
            if (enemy.Data != null && enemy.Data.isUnblockable) return false;

            // Blocker capacity check
            int availableSlots = blocker.GetBlockCount() - blocker.GetCurrentBlockCount();
            int requiredWeight = enemy.Data != null ? enemy.Data.blockWeight : 1;

            return availableSlots >= requiredWeight;
        }

        /// <summary>
        /// Attempt to engage blocking between an operator and an enemy.
        /// Returns true if successfully blocked.
        /// </summary>
        public static bool TryEngageBlock(OperatorBase blocker, EnemyBase enemy)
        {
            if (!CanBlock(blocker, enemy)) return false;

            return blocker.TryBlock(enemy);
        }

        /// <summary>
        /// Releases all enemies currently blocked by an operator.
        /// </summary>
        public static void ReleaseAllBlocks(OperatorBase blocker, List<EnemyBase> blockedList)
        {
            if (blockedList == null) return;

            for (int i = blockedList.Count - 1; i >= 0; i--)
            {
                if (blockedList[i] != null)
                {
                    blocker.ReleaseBlock(blockedList[i]);
                }
            }
            blockedList.Clear();
        }
    }
}
