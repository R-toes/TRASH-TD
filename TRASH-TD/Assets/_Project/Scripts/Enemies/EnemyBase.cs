using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
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

        private float chillAmount;
        private float slowTimer;
        private float freezeTimer;
        private float speedBeforeChill;
        private float chillSlowMultiplier = 1f;
        private bool isFrozen;
        private SpriteRenderer[] chillRenderers;
        private Color[] originalRendererColors;

        // --- Properties ---
        public EnemyData Data => data;
        public int CurrentHP => currentHP;
        public int MaxHP => maxHP;
        public int CurrentATK => currentATK;
        public int CurrentDEF => currentDEF;
        public int CurrentRES => currentRES;
        public float CurrentMoveSpeed => currentMoveSpeed;
        public bool IsBlocked => isBlocked;
        public bool IsDead => isDead;
        public bool IsFrozen => isFrozen;
        public float ChillAmount => chillAmount;
        public EnemyMovementType MovementType => data.movementType;
        public float DistanceToGoal
        {
            get
            {
                if (path == null || currentPathIndex >= path.Count) return 0f;

                float distance = Vector3.Distance(transform.position, path[currentPathIndex]);
                for (int i = currentPathIndex; i < path.Count - 1; i++)
                {
                    distance += Vector3.Distance(path[i], path[i + 1]);
                }

                return distance;
            }
        }

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
            chillAmount = 0f;
            slowTimer = 0f;
            freezeTimer = 0f;
            speedBeforeChill = currentMoveSpeed;
            chillSlowMultiplier = 1f;
            isFrozen = false;
            CacheChillRenderers();
            UpdateChillVisual();

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

            UpdateChillStatus(Time.deltaTime);
            if (isFrozen) return;

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
            if (isDead || damage <= 0) return;

            int previousHP = currentHP;
            currentHP = Mathf.Max(0, currentHP - damage);
            int actualDamage = previousHP - currentHP;
            if (actualDamage > 0)
            {
                FloatingCombatNumber.Show(transform.position, actualDamage, damageType);
                WorldHealthBar.UpdateFor(gameObject, currentHP, maxHP);
            }

            if (currentHP <= 0)
            {
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

        /// <summary>
        /// Applies a timed movement slow and accumulates chill toward a temporary freeze.
        /// </summary>
        public void ApplyChill(
            float amount,
            float slowMultiplier,
            float slowDuration,
            float freezeThreshold,
            float freezeDuration)
        {
            if (isDead || data == null || amount <= 0f || slowDuration <= 0f || freezeThreshold <= 0f)
                return;

            if (slowTimer <= 0f && !isFrozen)
            {
                speedBeforeChill = currentMoveSpeed;
            }

            chillSlowMultiplier = Mathf.Min(chillSlowMultiplier, Mathf.Clamp01(slowMultiplier));
            slowTimer = Mathf.Max(slowTimer, slowDuration);
            if (!isFrozen)
            {
                currentMoveSpeed = speedBeforeChill * chillSlowMultiplier;
            }

            chillAmount += amount;
            if (chillAmount >= freezeThreshold && freezeDuration > 0f)
            {
                chillAmount = 0f;
                isFrozen = true;
                freezeTimer = Mathf.Max(freezeTimer, freezeDuration);
                currentMoveSpeed = 0f;
            }

            UpdateChillVisual();
        }

        protected void UpdateChillStatus(float deltaTime)
        {
            bool wasSlowed = slowTimer > 0f;
            bool wasFrozen = isFrozen;

            if (slowTimer > 0f)
            {
                slowTimer = Mathf.Max(0f, slowTimer - deltaTime);
            }

            if (freezeTimer > 0f)
            {
                freezeTimer = Mathf.Max(0f, freezeTimer - deltaTime);
                if (freezeTimer <= 0f)
                {
                    isFrozen = false;
                }
            }

            if (!isFrozen)
            {
                if (slowTimer > 0f)
                {
                    currentMoveSpeed = speedBeforeChill * chillSlowMultiplier;
                }
                else if (wasSlowed || wasFrozen)
                {
                    currentMoveSpeed = speedBeforeChill;
                    chillSlowMultiplier = 1f;
                }
            }

            UpdateChillVisual();
        }

        private void CacheChillRenderers()
        {
            chillRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            originalRendererColors = new Color[chillRenderers.Length];
            for (int i = 0; i < chillRenderers.Length; i++)
            {
                originalRendererColors[i] = chillRenderers[i].color;
            }
        }

        private void UpdateChillVisual()
        {
            if (chillRenderers == null || originalRendererColors == null) return;

            float tintAmount = isFrozen ? 0.95f : slowTimer > 0f ? 0.8f : 0f;
            for (int i = 0; i < chillRenderers.Length; i++)
            {
                if (chillRenderers[i] == null) continue;

                Color original = originalRendererColors[i];
                Color chilled = new Color(0.1f, 0.6f, 1f, original.a);
                chillRenderers[i].color = Color.Lerp(original, chilled, tintAmount);
            }
        }
    }
}
