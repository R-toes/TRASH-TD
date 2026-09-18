using UnityEngine;

namespace TrashTD.Data
{
    /// <summary>
    /// ScriptableObject defining an enemy's static data (GDD 1.5).
    /// New enemy types are added by creating new .asset instances — no code changes required.
    /// </summary>
    [CreateAssetMenu(fileName = "New Enemy", menuName = "TRASH TD/Enemy Data")]
    public class EnemyData : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Display name of the enemy")]
        public string enemyName;

        [Tooltip("Enemy archetype (Grunt, Rusher, Tank, Caster, Flyer)")]
        public EnemyArchetype archetype;

        [Tooltip("Movement category — Ground follows paths, Air ignores ground pathing")]
        public EnemyMovementType movementType = EnemyMovementType.Ground;

        [Header("Base Stats")]
        [Tooltip("Base hit points")]
        public int baseHP = 100;

        [Tooltip("Base attack power (for enemies that attack operators)")]
        public int baseATK = 30;

        [Tooltip("Base physical defense")]
        public int baseDEF = 10;

        [Tooltip("Base magic resistance")]
        public int baseRES = 0;

        [Tooltip("Movement speed in tiles per second")]
        public float moveSpeed = 1.0f;

        [Header("Damage")]
        [Tooltip("Type of damage dealt by this enemy")]
        public DamageType damageType = DamageType.Physical;

        [Tooltip("Attack interval in seconds (for enemies that attack)")]
        public float attackInterval = 2.0f;

        [Tooltip("Attack range in grid cells (0 = melee only)")]
        public int attackRange = 0;

        [Header("Blocking")]
        [Tooltip("If true, this enemy cannot be blocked by operators")]
        public bool isUnblockable = false;

        [Tooltip("Weight — how many 'block slots' this enemy occupies (usually 1)")]
        public int blockWeight = 1;

        [Header("Rewards")]
        [Tooltip("Life points lost when this enemy reaches the exit")]
        public int lifePointCost = 1;

        [Header("Visuals")]
        [Tooltip("Sprite for enemy display")]
        public Sprite sprite;

        [Tooltip("Prefab to instantiate when spawning")]
        public GameObject enemyPrefab;

        // TODO: Special abilities per archetype — GDD lists counters but not
        // specific enemy skills. Awaiting design input.

        /// <summary>
        /// Get stats scaled by difficulty level multiplier.
        /// Difficulty scaling per GDD 1.5: "Stats (HP/ATK) scale per level."
        /// Exact scaling formula TBD — using placeholder linear scaling.
        /// </summary>
        public int GetScaledHP(int difficultyLevel)
        {
            float multiplier = 1f + ((difficultyLevel - 1) * 0.25f);
            return Mathf.RoundToInt(baseHP * multiplier);
        }

        public int GetScaledATK(int difficultyLevel)
        {
            float multiplier = 1f + ((difficultyLevel - 1) * 0.2f);
            return Mathf.RoundToInt(baseATK * multiplier);
        }

        public int GetScaledDEF(int difficultyLevel)
        {
            float multiplier = 1f + ((difficultyLevel - 1) * 0.15f);
            return Mathf.RoundToInt(baseDEF * multiplier);
        }
    }
}
