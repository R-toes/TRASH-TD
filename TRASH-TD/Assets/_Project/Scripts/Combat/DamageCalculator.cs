using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Combat
{
    /// <summary>
    /// Implements the exact combat damage formula specified in GDD 1.4.5:
    /// Final Damage = max(ATK - DEF, ATK * 0.05)
    /// 
    /// Physical damage is mitigated by DEF.
    /// Arts damage is mitigated by RES.
    /// In both cases, the formula applies identically using the respective mitigation stat.
    /// </summary>
    public static class DamageCalculator
    {
        /// <summary>
        /// Minimum damage percentage floor (5% of ATK).
        /// Ensures units always deal chip damage regardless of high DEF/RES.
        /// </summary>
        public const float MIN_DAMAGE_RATIO = 0.05f;

        /// <summary>
        /// Calculate final damage according to GDD 1.4.5:
        /// Final Damage = max(ATK - DEF, ATK * 0.05)
        /// </summary>
        /// <param name="rawATK">Attacker's attack power</param>
        /// <param name="mitigation">Defender's DEF (for physical) or RES (for arts)</param>
        /// <returns>Final integer damage (at least 1 if ATK > 0)</returns>
        public static int CalculateDamage(int rawATK, int mitigation)
        {
            if (rawATK <= 0) return 0;

            float netDamage = rawATK - mitigation;
            float minDamageFloor = rawATK * MIN_DAMAGE_RATIO;

            float finalDamage = Mathf.Max(netDamage, minDamageFloor);
            return Mathf.Max(1, Mathf.RoundToInt(finalDamage));
        }

        /// <summary>
        /// Convenience method taking damage type into account.
        /// </summary>
        public static int CalculateDamage(int rawATK, int def, int res, DamageType damageType)
        {
            int mitigation = damageType == DamageType.Physical ? def : res;
            return CalculateDamage(rawATK, mitigation);
        }
    }
}
