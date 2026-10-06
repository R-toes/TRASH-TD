using System.Collections.Generic;
using UnityEngine;
using TrashTD.Audio;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;

namespace TrashTD.Operators
{
    /// <summary>
    /// Abstract base class for all operators (GDD 1.3).
    /// Implements shared lifecycle, stats, deployment, attack timing, blocking, and damage handling.
    /// Subclasses override targeting, attack behavior, and blocking rules per class archetype.
    /// 
    /// Demonstrates software reuse: shared behavior in base class, class-specific behavior in subclasses,
    /// skill behavior via ISkill composition.
    /// </summary>
    public abstract class OperatorBase : MonoBehaviour, IDeployable, IAttacker, IBlocker
    {
        [Header("Operator Data")]
        [SerializeField] protected OperatorData data;

        // --- Runtime Stats (scaled by rarity) ---
        protected int currentHP;
        protected int maxHP;
        protected int currentATK;
        protected int currentDEF;
        protected int currentRES;

        // --- State ---
        protected OperatorRarity currentRarity;
        protected OperatorFacing facing = OperatorFacing.Right;
        protected GridCell deployedCell;
        protected bool isDeployed;
        protected float attackTimer;
        protected List<EnemyBase> blockedEnemies = new List<EnemyBase>();
        protected EnemyBase currentTarget;

        // --- Skill (composition) ---
        protected ISkill equippedSkill;

        // --- Properties ---
        public OperatorData Data => data;
        public bool IsDeployed => isDeployed;
        public GridCell DeployedCell => deployedCell;
        public int CurrentHP => currentHP;
        public int MaxHP => maxHP;
        public int CurrentATK => currentATK;
        public int CurrentDEF => currentDEF;
        public int CurrentRES => currentRES;
        public OperatorRarity CurrentRarity => currentRarity;
        public OperatorFacing Facing => facing;
        public OperatorClass OperatorClass => data.operatorClass;

        /// <summary>
        /// Initialize this operator with data and rarity.
        /// Call this after instantiation, before deployment.
        /// </summary>
        public virtual void Initialize(OperatorData operatorData, OperatorRarity rarity, OperatorFacing operatorFacing = OperatorFacing.Right)
        {
            data = operatorData;
            currentRarity = rarity;
            facing = operatorFacing;

            // Scale stats by rarity
            maxHP = data.GetScaledHP(rarity);
            currentHP = maxHP;
            currentATK = data.GetScaledATK(rarity);
            currentDEF = data.GetScaledDEF(rarity);
            currentRES = data.GetScaledRES(rarity);

            attackTimer = 0f;
            isDeployed = false;
        }

        // ========================
        // IDeployable Implementation
        // ========================

        public bool Deploy(GridCell cell)
        {
            if (isDeployed) return false;
            if (!cell.CanDeploy(data.position)) return false;

            deployedCell = cell;
            isDeployed = true;
            cell.Deploy(gameObject);
            transform.position = cell.WorldPosition;

            OnDeployed();
            return true;
        }

        public void Retreat()
        {
            if (!isDeployed) return;

            // Release all blocked enemies
            for (int i = blockedEnemies.Count - 1; i >= 0; i--)
            {
                ReleaseBlock(blockedEnemies[i]);
            }

            deployedCell?.Vacate();
            deployedCell = null;
            isDeployed = false;
            currentTarget = null;

            OnRetreated();
        }

        public int GetDPCost() => data.dpCost;

        // ========================
        // IAttacker Implementation
        // ========================

        public virtual void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            PlayAttackSound();
            CombatProjectileVisual.Fire(
                transform.position,
                target.transform.position,
                new Color(1f, 0.9f, 0.35f),
                12f,
                0.1f,
                0.05f,
                false,
                () => ApplyBasicAttackDamage(target));
        }

        private void ApplyBasicAttackDamage(EnemyBase target)
        {
            if (target == null || target.IsDead) return;

            int damage = Combat.DamageCalculator.CalculateDamage(currentATK, GetTargetMitigation(target));
            target.TakeDamage(damage, data.damageType);
        }

        protected void PlayAttackSound()
        {
            if (data == null || AudioManager.Instance == null) return;

            if (data.damageType == DamageType.Arts)
            {
                AudioManager.Instance.PlaySfx(SfxId.GameplayMagicAttack);
            }
            else if (data.position == OperatorPosition.Ranged)
            {
                AudioManager.Instance.PlaySfx(SfxId.GameplayRangedPhysicalAttack);
            }
            else
            {
                AudioManager.Instance.PlaySfx(SfxId.GameplayMeleeAttack);
            }
        }

        public Vector2Int[] GetRangePattern() => data.rangePattern;
        public float GetAttackInterval() => data.attackInterval;
        public DamageType GetDamageType() => data.damageType;

        /// <summary>
        /// Get the relevant mitigation stat from the target based on damage type.
        /// Physical → DEF, Arts → RES.
        /// </summary>
        protected int GetTargetMitigation(EnemyBase target)
        {
            return data.damageType == DamageType.Physical ? target.CurrentDEF : target.CurrentRES;
        }

        // ========================
        // IBlocker Implementation
        // ========================

        public virtual bool TryBlock(EnemyBase enemy)
        {
            if (enemy == null || !isDeployed) return false;
            if (blockedEnemies.Count >= data.blockCount) return false;
            if (enemy.Data.isUnblockable) return false;

            blockedEnemies.Add(enemy);
            enemy.OnBlocked(this);
            return true;
        }

        public void ReleaseBlock(EnemyBase enemy)
        {
            if (blockedEnemies.Remove(enemy))
            {
                enemy.OnUnblocked();
            }
        }

        public int GetBlockCount() => data.blockCount;
        public int GetCurrentBlockCount() => blockedEnemies.Count;

        // ========================
        // MonoBehaviour Lifecycle
        // ========================

        protected virtual void Update()
        {
            if (!isDeployed) return;

            // Update skill cooldowns
            equippedSkill?.UpdateSkill(Time.deltaTime);

            // Attack timing
            attackTimer += Time.deltaTime;
            if (attackTimer >= data.attackInterval)
            {
                attackTimer = 0f;
                currentTarget = FindTarget();
                if (currentTarget != null)
                {
                    Attack(currentTarget);
                }
            }
        }

        // ========================
        // Targeting (overridden by subclasses)
        // ========================

        /// <summary>
        /// Find the best target to attack. Subclasses override this for
        /// class-specific targeting priority (e.g., lowest HP, closest, etc).
        /// </summary>
        protected abstract EnemyBase FindTarget();

        // ========================
        // Damage Handling
        // ========================

        /// <summary>
        /// Take damage from an enemy or effect.
        /// </summary>
        public virtual void TakeDamage(int rawATK, DamageType damageType)
        {
            if (currentHP <= 0 || rawATK <= 0) return;

            int mitigation = damageType == DamageType.Physical ? currentDEF : currentRES;
            int damage = Combat.DamageCalculator.CalculateDamage(rawATK, mitigation);
            int previousHP = currentHP;
            currentHP = Mathf.Max(0, currentHP - damage);
            int actualDamage = previousHP - currentHP;
            if (actualDamage > 0)
            {
                TrashTD.Combat.FloatingCombatNumber.Show(transform.position, actualDamage, damageType);
                TrashTD.Combat.WorldHealthBar.UpdateFor(gameObject, currentHP, maxHP);
            }

            if (currentHP <= 0)
            {
                Die();
            }
        }

        /// <summary>
        /// Heal this operator.
        /// </summary>
        public void Heal(int amount)
        {
            if (amount <= 0 || currentHP <= 0) return;

            int previousHP = currentHP;
            currentHP = Mathf.Min(currentHP + amount, maxHP);
            int actualHealing = currentHP - previousHP;
            if (actualHealing > 0)
            {
                TrashTD.Combat.FloatingCombatNumber.Show(transform.position, actualHealing, DamageType.Physical, true);
                TrashTD.Combat.WorldHealthBar.UpdateFor(gameObject, currentHP, maxHP);
            }
        }

        /// <summary>
        /// Handle operator death — retreat and destroy.
        /// </summary>
        protected virtual void Die()
        {
            Retreat();
            TrashTD.Systems.OperatorManager.Instance?.HandleOperatorDeath(this);
            OnDeath();
            // TODO: Death animation, respawn cooldown timer
            Destroy(gameObject);
        }

        // ========================
        // Skill System
        // ========================

        /// <summary>
        /// Equip a skill (composition — any ISkill implementation can be attached).
        /// </summary>
        public void EquipSkill(ISkill skill)
        {
            equippedSkill = skill;
        }

        /// <summary>
        /// Activate the equipped skill if ready.
        /// </summary>
        public void ActivateSkill()
        {
            if (equippedSkill != null && equippedSkill.IsReady())
            {
                equippedSkill.Activate(this);
            }
        }

        // ========================
        // Virtual Hooks (for subclass-specific behavior)
        // ========================

        /// <summary>Called after successful deployment.</summary>
        protected virtual void OnDeployed() { }

        /// <summary>Called after retreat.</summary>
        protected virtual void OnRetreated() { }

        /// <summary>Called when the operator dies.</summary>
        protected virtual void OnDeath()
        {
            if (data == null || !data.drawTwoCardsOnDeath) return;

            TrashTD.Systems.CardDraftSystem draftSystem =
                FindFirstObjectByType<TrashTD.Systems.CardDraftSystem>();
            if (draftSystem == null)
            {
                Debug.LogError($"{data.operatorName} died, but no CardDraftSystem is active to resolve its death skill.");
                return;
            }

            draftSystem.DrawRandomCardsToDeck(2);
        }
    }
}
