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

        [Header("Healing")]
        [Tooltip("Healers skip this operator: Medics, Panacea, Mossmo's reactive heal (Drawgoo). Self-healing still works.")]
        public bool cannotBeHealed;

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

        [Header("Devour (Mlem)")]
        [Tooltip("Seconds Mlem spends digesting a swallowed Rusher before he can attack again")]
        [Min(0f)] public float digestSecondsRusher = 3f;

        [Tooltip("Seconds Mlem spends digesting a swallowed Grunt")]
        [Min(0f)] public float digestSecondsGrunt = 4.5f;

        [Tooltip("Seconds Mlem spends digesting a swallowed Caster")]
        [Min(0f)] public float digestSecondsCaster = 6f;

        [Tooltip("Seconds Mlem spends digesting a swallowed Tank")]
        [Min(0f)] public float digestSecondsTank = 12f;

        [Tooltip("Bonus DEF while digesting, as a fraction of his DEF (0.15 = +15%)")]
        [Min(0f)] public float digestDefBonusFraction = 0.15f;

        [Tooltip("Bonus max HP while digesting, as a fraction of his max HP (0.3 = +30%). It raises the cap only; eating doesn't heal him")]
        [Min(0f)] public float digestMaxHpBonusFraction = 0.3f;

        [Tooltip("HP recovered per second while digesting, as a fraction of max HP (base)")]
        [Min(0f)] public float digestRegenFractionPerSecond = 0f;

        [Tooltip("1st upgrade: digestion time is shortened by this fraction (0.2 = 20% faster)")]
        [Range(0f, 1f)] public float digestTimeReductionUpgrade1 = 0.2f;

        [Tooltip("1st upgrade: HP recovered per second while digesting, as a fraction of max HP")]
        [Min(0f)] public float digestRegenFractionPerSecondUpgrade1 = 0.04f;

        [Tooltip("Enemies swallowed by one tongue attack (base)")]
        [Min(1)] public int devourMaxSwallowBase = 1;

        [Tooltip("Enemies swallowed by one tongue attack with the 2nd upgrade")]
        [Min(1)] public int devourMaxSwallowUpgrade2 = 3;

        [Tooltip("Whether a swallowed enemy's on-death effect (an exploding enemy's blast, a carrier's spawn) still triggers")]
        public bool devourTriggersDeathEffects = false;

        [Header("Visuals")]
        [Tooltip("Sprite for card-draft UI and info panels")]
        public Sprite portrait;

        [Tooltip("Prefab to instantiate when deployed on the grid")]
        public GameObject operatorPrefab;

        [Tooltip("Where shots leave the sprite (launcher tip, mouth, orb...), in world units from the sprite centre " +
                 "with the art facing left as drawn; mirrored when the operator faces right. Zero = sprite centre.")]
        public Vector2 projectileOrigin;

        [Header("Reclaimers' Intel")]
        [Tooltip("One-line quote in the operator's voice, shown in Reclaimers' Intel")]
        public string intelQuote;

        [Tooltip("Field notes (lore, personality) shown in Reclaimers' Intel")]
        [TextArea(3, 6)]
        public string intelLore;

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

        [Header("Talent Activation Range")]
        [Tooltip("Optional talent activation range pattern. If unset and operator has bonusWhenIsolated or is Shadeslice, defaults to the diamond pattern.")]
        public Vector2Int[] talentRangePattern;

        public static readonly Vector2Int[] ShadesliceTalentPattern = new Vector2Int[]
        {
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 2),
            new Vector2Int(0, -2),
            new Vector2Int(2, 0),
            new Vector2Int(-2, 0),
            new Vector2Int(1, 1),
            new Vector2Int(1, -1),
            new Vector2Int(-1, 1),
            new Vector2Int(-1, -1)
        };

        public virtual Vector2Int[] GetTalentRangePattern()
        {
            if (talentRangePattern != null && talentRangePattern.Length > 0)
                return talentRangePattern;

            if (bonusWhenIsolated || operatorName == "Shadeslice")
                return ShadesliceTalentPattern;

            return null;
        }

        /// <summary>
        /// Gets the effective range pattern for this operator at the given rarity tier,
        /// accounting for range expansion upgrades (e.g. Pyrolite at 2★+, Ignisaur at 3★+, Mossmo at 5★, Progeny at 3★+).
        /// </summary>
        public Vector2Int[] GetRangePatternForRarity(OperatorRarity rarity)
        {
            if (operatorName == "Pyrolite")
            {
                return TrashTD.Operators.PyroliteOperator.GetRangePatternForRarity(this, rarity);
            }
            if (operatorName == "Ignisaur")
            {
                return TrashTD.Operators.IgnisaurOperator.GetRangePatternForRarity(this, rarity);
            }
            if (operatorName == "Mossmo")
            {
                return TrashTD.Operators.MossmoOperator.GetRangePatternForRarity(this, rarity);
            }
            if (operatorName == "Progeny")
            {
                return TrashTD.Operators.ProgenyOperator.GetRangePatternForRarity(this, rarity);
            }
            if (operatorName == "Proxishot" || operatorName == "Proxyshot")
            {
                return TrashTD.Operators.ProxishotOperator.GetRangePatternForRarity(this, rarity);
            }
            if (operatorName == "Thornchin")
            {
                return TrashTD.Operators.ThornchinOperator.GetRangePatternForRarity(this, rarity);
            }
            return rangePattern;
        }
    }
}
