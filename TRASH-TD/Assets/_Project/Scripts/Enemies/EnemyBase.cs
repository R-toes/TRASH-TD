using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Core.Pathfinding;
using TrashTD.Data;
using TrashTD.Operators;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Abstract base class for all enemies (GDD 1.5).
    /// Handles movement along path, taking damage, being blocked, and reaching exits.
    /// Subclasses override behavior for archetype-specific logic.
    /// 
    /// Parallel design to OperatorBase — demonstrates software reuse through
    /// shared enemy lifecycle with archetype-specific overrides.
    /// </summary>
    public abstract class EnemyBase : MonoBehaviour
    {
        [Header("Enemy Data")]
        [SerializeField] protected EnemyData data;

        // --- Runtime Stats (scaled by difficulty) ---
        protected int currentHP;
        protected int maxHP;
        protected int currentATK;
        protected int currentDEF;
        protected int currentRES;
        protected float currentMoveSpeed;

        // --- Pathfinding ---
        protected List<Vector3> path;
        protected int currentPathIndex;

        // --- State ---
        protected bool isBlocked;
        protected OperatorBase blockingOperator;
        protected bool isDead;
        protected float attackTimer;

        // --- Properties ---
        public EnemyData Data => data;
        public int CurrentHP => currentHP;
        public int MaxHP => maxHP;
        public int CurrentATK => currentATK;
        public int CurrentDEF => currentDEF;
        public int CurrentRES => currentRES;
        public bool IsBlocked => isBlocked;
        public bool IsDead => isDead;
        public EnemyMovementType MovementType => data.movementType;

        // --- Events ---
        /// <summary>Fired when this enemy reaches an exit point.</summary>
        public System.Action<EnemyBase> OnReachedExit;

        /// <summary>Fired when this enemy dies.</summary>
        public System.Action<EnemyBase> OnDied;

        /// <summary>
        /// Initialize this enemy with data and difficulty scaling.
        /// Call after instantiation, before path assignment.
        /// </summary>
        public virtual void Initialize(EnemyData enemyData, int difficultyLevel)
        {
            data = enemyData;
            maxHP = data.GetScaledHP(difficultyLevel);
            currentHP = maxHP;
            currentATK = data.GetScaledATK(difficultyLevel);
            currentDEF = data.GetScaledDEF(difficultyLevel);
            currentRES = data.baseRES;
            currentMoveSpeed = data.moveSpeed;

            isBlocked = false;
            blockingOperator = null;
            isDead = false;
            attackTimer = 0f;
            currentPathIndex = 0;
        }

        /// <summary>
        /// Assign a movement path (from A* or predefined waypoints).
        /// </summary>
        public void SetPath(List<Vector3> newPath)
        {
            path = newPath;
            currentPathIndex = 0;

            if (path != null && path.Count > 0)
            {
                transform.position = path[0];
            }
        }

        // ========================
        // MonoBehaviour Lifecycle
        // ========================

        protected virtual void Update()
        {
            if (isDead) return;

            if (isBlocked)
            {
                // Attack the blocking operator while blocked
                AttackBlocker();
            }
            else
            {
                // Move along path
                MoveAlongPath();
            }
        }

        // ========================
        // Movement
        // ========================

        /// <summary>
        /// Move along the assigned path toward the exit.
        /// </summary>
        protected virtual void MoveAlongPath()
        {
            if (path == null || currentPathIndex >= path.Count)
                return;

            Vector3 targetPos = path[currentPathIndex];
            float step = currentMoveSpeed * Time.deltaTime;

            transform.position = Vector3.MoveTowards(transform.position, targetPos, step);

            if (Vector3.Distance(transform.position, targetPos) < 0.01f)
            {
                currentPathIndex++;

                if (currentPathIndex >= path.Count)
                {
                    ReachExit();
                }
            }
        }

        /// <summary>
        /// Called when the enemy reaches the exit point.
        /// Costs the player life points (defined in EnemyData.lifePointCost).
        /// </summary>
        protected virtual void ReachExit()
        {
            OnReachedExit?.Invoke(this);
            Destroy(gameObject);
        }

        // ========================
        // Damage & Death
        // ========================

        /// <summary>
        /// Take damage from an operator or effect.
        /// Damage formula: max(ATK - DEF, ATK * 0.05) — applied by DamageCalculator before calling this.
        /// </summary>
        public virtual void TakeDamage(int damage, DamageType damageType)
        {
            if (isDead) return;

            currentHP -= damage;

            if (currentHP <= 0)
            {
                currentHP = 0;
                Die();
            }
        }

        /// <summary>
        /// Handle enemy death.
        /// </summary>
        protected virtual void Die()
        {
            isDead = true;

            // Release from blocker if blocked
            if (isBlocked && blockingOperator != null)
            {
                blockingOperator.ReleaseBlock(this);
            }

            OnDied?.Invoke(this);

            // TODO: Death animation, loot drops
            Destroy(gameObject);
        }

        // ========================
        // Blocking (GDD 1.4.4)
        // ========================

        /// <summary>
        /// Called when this enemy is blocked by an operator.
        /// </summary>
        public virtual void OnBlocked(OperatorBase blocker)
        {
            isBlocked = true;
            blockingOperator = blocker;
        }

        /// <summary>
        /// Called when this enemy is released from blocking.
        /// </summary>
        public virtual void OnUnblocked()
        {
            isBlocked = false;
            blockingOperator = null;
        }

        /// <summary>
        /// Attack the blocking operator while blocked.
        /// </summary>
        protected virtual void AttackBlocker()
        {
            if (blockingOperator == null) return;

            attackTimer += Time.deltaTime;
            if (attackTimer >= data.attackInterval)
            {
                attackTimer = 0f;
                blockingOperator.TakeDamage(currentATK, data.damageType);
            }
        }

        // ========================
        // Speed Modifiers
        // ========================

        /// <summary>
        /// Apply a speed modifier (for slows, stuns, etc).
        /// </summary>
        public void ApplySpeedModifier(float multiplier)
        {
            currentMoveSpeed = data.moveSpeed * multiplier;
        }

        /// <summary>
        /// Reset speed to base value.
        /// </summary>
        public void ResetSpeed()
        {
            currentMoveSpeed = data.moveSpeed;
        }
    }
}
