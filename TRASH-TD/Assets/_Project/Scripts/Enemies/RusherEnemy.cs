using UnityEngine;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Rusher enemy — fast movement (GDD 1.5).
    /// Higher move speed than standard enemies. Countered by slows and stuns.
    /// </summary>
    public class RusherEnemy : EnemyBase
    {
        [Header("Rusher Settings")]
        [Tooltip("Speed multiplier applied on top of base moveSpeed")]
        [SerializeField] private float speedMultiplier = 1.5f;

        public override void Initialize(Data.EnemyData enemyData, int difficultyLevel)
        {
            base.Initialize(enemyData, difficultyLevel);
            // Rushers are inherently faster
            currentMoveSpeed = data.moveSpeed * speedMultiplier;
        }

        /// <summary>
        /// Rushers take slightly more damage (glass cannon — fast but fragile).
        /// </summary>
        public override void TakeDamage(int damage, Data.DamageType damageType)
        {
            // TODO: Vulnerability multiplier — not specified in GDD, using placeholder
            base.TakeDamage(damage, damageType);
        }
    }
}
