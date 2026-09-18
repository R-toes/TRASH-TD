using UnityEngine;
using TrashTD.Enemies;

namespace TrashTD.Operators
{
    /// <summary>
    /// Defender class — Tank (GDD 1.3).
    /// High DEF/HP, blocks 2–4 enemies, low damage.
    /// Targets the enemy with the highest remaining HP among blocked enemies (hold the biggest threat).
    /// </summary>
    public class DefenderOperator : OperatorBase
    {
        protected override EnemyBase FindTarget()
        {
            // Priority: attack blocked enemies — target highest HP (chip down the toughest)
            if (blockedEnemies.Count > 0)
            {
                EnemyBase highestHP = null;
                int highestHPValue = 0;

                foreach (EnemyBase enemy in blockedEnemies)
                {
                    if (enemy != null && enemy.CurrentHP > highestHPValue)
                    {
                        highestHP = enemy;
                        highestHPValue = enemy.CurrentHP;
                    }
                }

                if (highestHP != null) return highestHP;
            }

            // Defenders have very short range — only attack blocked enemies
            return null;
        }

        /// <summary>
        /// Defenders have a passive damage reduction bonus beyond their DEF stat.
        /// </summary>
        public override void TakeDamage(int rawATK, Data.DamageType damageType)
        {
            // Defenders take slightly reduced damage as a class trait
            // TODO: Exact class trait bonus — not specified in GDD, using placeholder 10% reduction
            int reducedATK = Mathf.RoundToInt(rawATK * 0.9f);
            base.TakeDamage(reducedATK, damageType);
        }
    }
}
