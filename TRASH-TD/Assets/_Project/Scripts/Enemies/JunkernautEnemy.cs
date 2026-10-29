using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Operators;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Junkernaut - Heavy Tank enemy with Taunt.
    /// Characteristics:
    /// - High HP and DEF, slow movement speed.
    /// - Taunt: Operators prioritize targeting Junkernaut when in range or when choosing between blocked enemies.
    /// - Protection Aura: All other enemies on Junkernaut's tile and its surrounding tiles gain damage reduction
    ///   from operator attacks:
    ///     1 Junkernaut  -> -50% damage taken
    ///     2 Junkernauts -> -60% damage taken
    ///     3 Junkernauts -> -70% damage taken
    ///     4 Junkernauts -> -80% damage taken
    ///     5+ Junkernauts -> -90% damage taken (capped at 90%)
    ///   Junkernauts themselves never receive this damage reduction and always take full damage.
    /// </summary>
    public class JunkernautEnemy : EnemyBase
    {
        public const float BaseDamageReductionPercent = 0.50f;
        public const float AdditionalReductionPerExtraJunkernaut = 0.10f;
        public const float MaxDamageReductionPercent = 0.90f;
        public const float DefaultDamageReductionPercent = BaseDamageReductionPercent;

        [Header("Junkernaut Ability Settings")]
        [Range(0f, 1f)]
        [SerializeField] private float damageReductionPercent = DefaultDamageReductionPercent;

        private static readonly List<JunkernautEnemy> activeJunkernauts = new List<JunkernautEnemy>();

        public static IReadOnlyList<JunkernautEnemy> ActiveJunkernauts => activeJunkernauts;
        public static float DamageReductionPercent => DefaultDamageReductionPercent;

        public float ProtectionDamageReductionPercent
        {
            get => damageReductionPercent;
            set => damageReductionPercent = Mathf.Clamp01(value);
        }

        public override bool HasTaunt => !IsDead && !IsInsideHole;

        protected virtual void Awake()
        {
            RegisterActive(this);
        }

        protected virtual void OnEnable()
        {
            RegisterActive(this);
        }

        protected virtual void OnDisable()
        {
            UnregisterActive(this);
        }

        protected virtual void OnDestroy()
        {
            UnregisterActive(this);
        }

        protected override void Die()
        {
            UnregisterActive(this);
            base.Die();
        }

        public static void RegisterActive(JunkernautEnemy junkernaut)
        {
            if (junkernaut != null && !activeJunkernauts.Contains(junkernaut))
            {
                activeJunkernauts.Add(junkernaut);
            }
        }

        public static void UnregisterActive(JunkernautEnemy junkernaut)
        {
            if (junkernaut != null)
            {
                activeJunkernauts.Remove(junkernaut);
            }
        }

        public static void ClearRegistry()
        {
            activeJunkernauts.Clear();
        }

        /// <summary>
        /// Checks whether the specified world position lies within this Junkernaut's tile
        /// or one of its 8 surrounding tiles (Chebyshev distance <= 1 tile).
        /// </summary>
        public bool IsWithinProtectionRange(Vector3 otherWorldPos)
        {
            if (IsDead || IsInsideHole) return false;

            var grid = CurrentGrid ?? FindAnyObjectByType<GridManager>();
            if (grid != null)
            {
                Vector2Int myGrid = grid.WorldToGridPosition(transform.position);
                Vector2Int otherGrid = grid.WorldToGridPosition(otherWorldPos);
                return Mathf.Abs(myGrid.x - otherGrid.x) <= 1 && Mathf.Abs(myGrid.y - otherGrid.y) <= 1;
            }

            // Fallback for isolated test environments without GridManager
            Vector3 diff = transform.position - otherWorldPos;
            return Mathf.Abs(diff.x) <= 1.5f && Mathf.Abs(diff.y) <= 1.5f;
        }

        /// <summary>
        /// Counts how many distinct active Junkernauts are currently protecting the target enemy.
        /// Junkernauts themselves always return 0 (never protected).
        /// </summary>
        public static int GetProtectingJunkernautCount(EnemyBase target)
        {
            if (target == null || target.IsDead || target.IsInsideHole || target is JunkernautEnemy) return 0;

            int count = 0;
            for (int i = 0; i < activeJunkernauts.Count; i++)
            {
                var j = activeJunkernauts[i];
                if (j == null || j == target || j.IsDead || j.IsInsideHole) continue;

                if (j.IsWithinProtectionRange(target.transform.position))
                {
                    count++;
                }
            }

            // Fallback for tests or instances not yet registered in activeJunkernauts
            if (count == 0 && activeJunkernauts.Count == 0)
            {
                JunkernautEnemy[] sceneJunkernauts = FindObjectsByType<JunkernautEnemy>();
                for (int i = 0; i < sceneJunkernauts.Length; i++)
                {
                    var j = sceneJunkernauts[i];
                    if (j == null || j == target || j.IsDead || j.IsInsideHole) continue;

                    if (j.IsWithinProtectionRange(target.transform.position))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Calculates the percentage of damage reduced based on the number of protecting Junkernauts:
        /// 1 -> 50%
        /// 2 -> 60%
        /// 3 -> 70%
        /// 4 -> 80%
        /// 5+ -> 90% (cap)
        /// </summary>
        public static float GetDamageReductionPercent(int count)
        {
            if (count <= 0) return 0f;
            return Mathf.Min(BaseDamageReductionPercent + (count - 1) * AdditionalReductionPerExtraJunkernaut, MaxDamageReductionPercent);
        }

        /// <summary>
        /// Gets the damage reduction percentage granted to the target enemy by nearby Junkernauts.
        /// Junkernauts themselves always return 0f.
        /// </summary>
        public static float GetDamageReductionPercent(EnemyBase target)
        {
            int count = GetProtectingJunkernautCount(target);
            return GetDamageReductionPercent(count);
        }

        /// <summary>
        /// Determines if the given enemy is currently protected by at least one active Junkernaut.
        /// </summary>
        public static bool IsProtectedByJunkernaut(EnemyBase target)
        {
            return GetProtectingJunkernautCount(target) > 0;
        }
    }
}
