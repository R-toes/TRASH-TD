using UnityEngine;

namespace TrashTD.Data
{
    /// <summary>
    /// ScriptableObject defining an operator's static data (GDD 1.3).
    /// New operators are added by creating new .asset instances — no code changes required.
    /// </summary>
    [CreateAssetMenu(fileName = "New Operator", menuName = "TRASH TD/Operator Data")]
    public class OperatorData : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Display name of the operator")]
        public string operatorName;

        [Tooltip("Operator class (Guard, Defender, Sniper, Caster, Medic)")]
        public OperatorClass operatorClass;

        [Tooltip("Fixed operator rarity. Draft weights control tier drop rates; 4★–5★ are reached via upgrades.")]
        public OperatorRarity baseRarity = OperatorRarity.Star1;

        [Tooltip("Melee (Low Ground) or Ranged (High Ground) deployment")]
        public OperatorPosition position;

        [Tooltip("Role tags for filtering/display (e.g., AoE, Slow, DPS, Survival)")]
        public string[] roleTags;

        [Header("Base Stats")]
        [Tooltip("Base hit points")]
        public int baseHP = 100;

        [Tooltip("Base attack power")]
        public int baseATK = 50;

        [Tooltip("Base physical defense")]
        public int baseDEF = 10;

        [Tooltip("Base magic resistance (0–100, percentage-like for Arts damage mitigation)")]
        public int baseRES = 0;

        [Tooltip("Max enemies this operator can block simultaneously")]
        public int blockCount = 0;

        [Tooltip("Attack range in grid cells (directional or radius — see rangePattern)")]
        public int attackRange = 1;

        [Tooltip("Seconds between attacks")]
        public float attackInterval = 1.0f;

        [Header("Range Pattern")]
        [Tooltip("Grid-relative attack cells. (0,0) = self. E.g., (1,0), (0,1), (-1,0), (0,-1) for adjacent.")]
        public Vector2Int[] rangePattern;

        [Header("Damage")]
        [Tooltip("Type of damage dealt by this operator's basic attack")]
        public DamageType damageType = DamageType.Physical;

        [Header("Death Skill")]
        [Tooltip("Draw two random cards from the operator pool when this operator dies")]
        public bool drawTwoCardsOnDeath;

        [Header("Isolation")]
        [Tooltip("Enable an attack bonus when no other deployed operator is in an adjacent grid cell")]
        public bool bonusWhenIsolated;

        [Tooltip("Attack multiplier while no other operator is adjacent")]
        [Min(1f)]
        public float isolatedAttackMultiplier = 1.5f;

        [Tooltip("Short mechanic description shown when inspecting the operator")]
        [TextArea(2, 3)]
        public string skillDescription;

        [Header("Chill")]
        [Tooltip("Chill applied per hit. Set to 0 for operators without chill attacks.")]
        public float chillPerHit;

        [Tooltip("Movement speed multiplier while chilled")]
        [Range(0f, 1f)]
        public float chillSlowMultiplier = 0.65f;

        [Tooltip("Duration of the movement slow in seconds")]
        public float chillSlowDuration = 2.5f;

        [Tooltip("Accumulated chill required to freeze the target")]
        public float chillFreezeThreshold = 100f;

        [Tooltip("Freeze duration in seconds")]
        public float chillFreezeDuration = 1.5f;

        [Header("Deployment")]
        [Tooltip("Deployment Point cost to place this operator")]
        public int dpCost = 10;

        [Tooltip("Cooldown (seconds) before this operator can be redeployed after retreating")]
        public float redeployCooldown = 60f;

        [Header("Visuals")]
        [Tooltip("Sprite for card-draft UI and info panels")]
        public Sprite portrait;

        [Tooltip("Prefab to instantiate when deployed on the grid")]
        public GameObject operatorPrefab;

        // TODO: Skill data — GDD mentions skills/abilities with cooldowns but
        // doesn't define specific skills per class. Awaiting design input from Raim.

        // TODO: Stat scaling per rarity — exact multipliers not defined in GDD.
        // Currently using placeholder linear scaling in OperatorBase.

        /// <summary>
        /// Get stats scaled by rarity. Placeholder scaling — exact formula TBD.
        /// </summary>
        public int GetScaledHP(OperatorRarity rarity)
        {
            float multiplier = 1f + (((int)rarity - 1) * 0.2f); // +20% per rarity tier
            return Mathf.RoundToInt(baseHP * multiplier);
        }

        public int GetScaledATK(OperatorRarity rarity)
        {
            float multiplier = 1f + (((int)rarity - 1) * 0.15f); // +15% per rarity tier
            return Mathf.RoundToInt(baseATK * multiplier);
        }

        public int GetScaledDEF(OperatorRarity rarity)
        {
            float multiplier = 1f + (((int)rarity - 1) * 0.15f);
            return Mathf.RoundToInt(baseDEF * multiplier);
        }

        public int GetScaledRES(OperatorRarity rarity)
        {
            // RES scales linearly: +5 per rarity tier above 1★
            return baseRES + (((int)rarity - 1) * 5);
        }
    }
}
