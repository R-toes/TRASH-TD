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
    /// - Protection Aura: All other enemies on Junkernaut's tile and its surrounding tiles gain -50% damage
    ///   taken from operators. Junkernaut himself does not receive this damage reduction.
    /// </summary>
    public class JunkernautEnemy : EnemyBase
    {
        public const float DefaultDamageReductionPercent = 0.50f;

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
        /// Determines if the given enemy is currently protected by at least one active Junkernaut.
        /// Other enemies on Junkernaut's tile and surrounding tiles take 50% reduced damage from operators.
        /// Junkernaut himself does not receive this reduction.
        /// </summary>
        public static bool IsProtectedByJunkernaut(EnemyBase target)
        {
            if (target == null || target.IsDead || target.IsInsideHole) return false;

            // Check active Junkernauts list
            for (int i = 0; i < activeJunkernauts.Count; i++)
            {
                var j = activeJunkernauts[i];
                if (j == null || j == target || j.IsDead || j.IsInsideHole) continue;

                if (j.IsWithinProtectionRange(target.transform.position))
                {
                    return true;
                }
            }

            // Fallback for tests or instances not yet registered in activeJunkernauts
            if (activeJunkernauts.Count == 0)
            {
                JunkernautEnemy[] sceneJunkernauts = FindObjectsByType<JunkernautEnemy>();
                for (int i = 0; i < sceneJunkernauts.Length; i++)
                {
                    var j = sceneJunkernauts[i];
                    if (j == null || j == target || j.IsDead || j.IsInsideHole) continue;

                    if (j.IsWithinProtectionRange(target.transform.position))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
