using System.Collections.Generic;
using UnityEngine;
using TrashTD.Audio;
using TrashTD.Combat;
using TrashTD.Core.GameLoop;
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
        private float trapDamageTimer;
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
        public virtual int CurrentATK => currentATK;
        public virtual int CurrentDEF => currentDEF;
        public int CurrentRES => currentRES;
        public OperatorRarity CurrentRarity => currentRarity;
        public OperatorFacing Facing => facing;
        public OperatorClass OperatorClass => data.operatorClass;
        public virtual bool CanReceiveHealing => true;
        public virtual bool CanHitAir => data != null && (data.position == OperatorPosition.Ranged || data.operatorClass == OperatorClass.Caster);
        public virtual bool IgnoresMissMechanic => false;
        public virtual bool IsCamouflaged => false;
        public virtual bool IsTargetable => !IsCamouflaged;
        public IReadOnlyList<EnemyBase> BlockedEnemies => blockedEnemies;
        // Operators are frozen whenever no wave is running (Card Pick and Preparation): no damage (from enemies,
        // traps or weather), no healing, no shields and no abilities. Sandbox stays in Preparation for its whole
        // session (so cards can be deployed mid-wave), so the lock must not apply there.
        public bool IsCombatPaused =>
            GameManager.Instance != null && !GameManager.Instance.IsSandbox &&
            GameManager.Instance.CurrentPhase != StagePhase.WaveActive;

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
            trapDamageTimer = 0f;
            isDeployed = false;

            ApplyFacingVisuals();

            // Safety net: ensure sprite is visible even if prefab asset reference was broken
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null && (sr.sprite == null || sr.sprite.texture == null) && data != null && data.portrait != null)
            {
                sr.sprite = data.portrait;
            }

            var anim = GetComponent<OperatorSpriteAnimation>();
            if (anim != null)
            {
                anim.RefreshFromOperatorDataOrRenderer(data?.portrait);
            }
        }

        public void SetFacing(OperatorFacing newFacing)
        {
            facing = newFacing;
            ApplyFacingVisuals();
        }

        public virtual void ApplyFacingVisuals()
        {
            bool mirrorHorizontally = facing == OperatorFacing.Right;
            SpriteRenderer sr = GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.flipX = mirrorHorizontally;
            }
            else
            {
                SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].flipX = mirrorHorizontally;
                }
            }
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
            trapDamageTimer = 0f;
            cell.Deploy(gameObject);
            transform.position = cell.WorldPosition;

            ApplyFacingVisuals();

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
            trapDamageTimer = 0f;
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
                target,
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

            int damage = Combat.DamageCalculator.CalculateDamage(CurrentATK, GetTargetMitigation(target));
            if (target.TryTakeAttackDamage(damage, data.damageType)) PlayImpactSound();
        }

        /// <summary>Attack sound: the operator's own Attack clip, else the shared one for its damage type.</summary>
        protected void PlayAttackSound()
        {
            if (data == null || AudioManager.Instance == null) return;
            if (UnitSfx.Play(data, UnitSfxEvent.Attack)) return;

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

        /// <summary>
        /// The operator's own Impact clip (projectile landing, explosion), if it has one. Called whenever one of its
        /// hits lands; repeats within a moment are throttled, so an AoE hit plays it once.
        /// </summary>
        public void PlayImpactSound() => UnitSfx.Play(data, UnitSfxEvent.Impact);

        /// <summary>The operator's own Ability clip, else the given shared sound (if any).</summary>
        protected void PlayAbilitySound(SfxId? fallback = null)
        {
            if (data == null || UnitSfx.Play(data, UnitSfxEvent.Ability)) return;
            if (fallback.HasValue) AudioManager.Instance?.PlaySfx(fallback.Value);
        }

        public Vector2Int[] GetRangePattern() => data.rangePattern;
        public virtual float GetAttackInterval() => Combat.StageCombatModifiers.GetAttackInterval(data.attackInterval);
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

            if (IsCombatPaused)
            {
                // Between waves nothing ticks: no trap damage, skills or attacks. The attack stays charged
                // so the first attack of the next wave isn't delayed.
                trapDamageTimer = 0f;
                attackTimer = Mathf.Min(attackTimer + Time.deltaTime, GetAttackInterval());
                return;
            }

            if (deployedCell != null && deployedCell.TileType == TileType.Trap)
            {
                trapDamageTimer += Time.deltaTime;
                while (trapDamageTimer >= TrapTileRules.DamageIntervalSeconds && isDeployed)
                {
                    trapDamageTimer -= TrapTileRules.DamageIntervalSeconds;
                    TrashTD.Combat.AcidDamageFlash.Flash(gameObject);
                    TakeDamage(TrapTileRules.DamagePerTick, DamageType.Physical);
                }
                if (!isDeployed) return;
            }
            else
            {
                trapDamageTimer = 0f;
            }

            // Update skill cooldowns
            equippedSkill?.UpdateSkill(Time.deltaTime);

            // Attack timing: once charged, the attack stays ready until a target is available,
            // so an enemy entering range is hit immediately instead of waiting a full interval.
            float interval = GetAttackInterval();
            attackTimer = Mathf.Min(attackTimer + Time.deltaTime, interval);
            if (attackTimer >= interval)
            {
                currentTarget = FindTarget();
                if (currentTarget != null && CanAttackTarget(currentTarget))
                {
                    attackTimer = 0f;
                    Attack(currentTarget);
                }
            }
        }

        protected virtual bool CanAttackTarget(EnemyBase target)
        {
            if (target == null || target.IsDead || target.IsInsideHole) return false;
            return target.MovementType != EnemyMovementType.Air || CanHitAir;
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
        public virtual void TakeDamage(int rawATK, DamageType damageType, EnemyBase attacker = null)
        {
            if (IsCombatPaused || currentHP <= 0 || rawATK <= 0) return;

            var shield = GetComponent<BubbleShield>();
            if (shield != null && shield.TryConsume(rawATK, damageType))
            {
                return;
            }

            int mitigation = damageType == DamageType.Physical ? CurrentDEF : currentRES;
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

        public bool TryTakeAttackDamage(int rawATK, DamageType damageType, EnemyBase attacker = null)
        {
            if (IsCombatPaused) return false;
            if (!Combat.StageCombatModifiers.TryAttackHit(transform.position))
                return false;

            TakeDamage(rawATK, damageType, attacker);
            return true;
        }

        /// <summary>
        /// Heal this operator.
        /// </summary>
        public void Heal(int amount)
        {
            if (IsCombatPaused || !CanReceiveHealing) return;
            ApplyHealing(amount);
        }

        protected void HealSelf(int amount)
        {
            ApplyHealing(amount);
        }

        private void ApplyHealing(int amount)
        {
            if (IsCombatPaused || amount <= 0 || currentHP <= 0) return;

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
            UnitSfx.Play(data, UnitSfxEvent.Death);
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
