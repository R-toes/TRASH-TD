using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Combat
{
    /// <summary>
    /// Combat damage formulas.
    ///
    /// Physical damage is mitigated flat by DEF (GDD 1.4.5):
    ///     Final Damage = max(ATK - DEF, ATK * 0.05)
    ///
    /// Arts damage is mitigated by RES as a percentage (0-100):
    ///     Final Damage = max(ATK * (1 - RES / 100), ATK * 0.05)
    /// This deviates from GDD 1.4.5 (which applied RES flat like DEF): enemy RES values (e.g. 35, 80)
    /// are authored as percentages, and flat RES made Arts damage nearly useless against them.
    /// </summary>
    public static class DamageCalculator
    {
        /// <summary>
        /// Minimum damage percentage floor (5% of ATK).
        /// Ensures units always deal chip damage regardless of high DEF/RES.
        /// </summary>
        public const float MIN_DAMAGE_RATIO = 0.05f;

        /// <summary>RES is a percentage and is clamped to this range.</summary>
        public const int MAX_RES = 100;

        /// <summary>
        /// Physical damage: Final Damage = max(ATK - DEF, ATK * 0.05).
        /// </summary>
        /// <param name="rawATK">Attacker's attack power</param>
        /// <param name="def">Defender's DEF</param>
        /// <returns>Final integer damage (at least 1 if ATK > 0)</returns>
        public static int CalculateDamage(int rawATK, int def)
        {
            if (rawATK <= 0) return 0;

            float netDamage = rawATK - def;
            return ApplyFloor(rawATK, netDamage);
        }

        /// <summary>
        /// Arts damage: Final Damage = max(ATK * (1 - RES / 100), ATK * 0.05).
        /// </summary>
        public static int CalculateArtsDamage(int rawATK, int res)
        {
            if (rawATK <= 0) return 0;

            float resRatio = Mathf.Clamp(res, 0, MAX_RES) / (float)MAX_RES;
            return ApplyFloor(rawATK, rawATK * (1f - resRatio));
        }

        /// <summary>
        /// Damage against the mitigation stat that matches the damage type (DEF for Physical, RES for Arts).
        /// </summary>
        public static int CalculateDamage(int rawATK, int mitigation, DamageType damageType)
        {
            return damageType == DamageType.Arts
                ? CalculateArtsDamage(rawATK, mitigation)
                : CalculateDamage(rawATK, mitigation);
        }

        /// <summary>
        /// Convenience method taking both mitigation stats and the damage type.
        /// </summary>
        public static int CalculateDamage(int rawATK, int def, int res, DamageType damageType)
        {
            return damageType == DamageType.Arts
                ? CalculateArtsDamage(rawATK, res)
                : CalculateDamage(rawATK, def);
        }

        private static int ApplyFloor(int rawATK, float netDamage)
        {
            float finalDamage = Mathf.Max(netDamage, rawATK * MIN_DAMAGE_RATIO);
            return Mathf.Max(1, Mathf.RoundToInt(finalDamage));
        }
    }
}
