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
        protected float stunTimer;
        protected bool isStunned;
        protected SpriteRenderer spriteRenderer;

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
        public virtual bool CanHitAir => data != null && (data.position == OperatorPosition.Ranged || data.position == OperatorPosition.Any || data.operatorClass == OperatorClass.Caster);
        public virtual bool IgnoresMissMechanic => false;
        public virtual bool IsCamouflaged => false;
        public virtual bool IsTargetable => !IsCamouflaged;
        public virtual bool HasTaunt => false;

        // --- Deployment & Combat Phase Tracking ---
        public bool HasFoughtInWave { get; set; } = false;
        public bool DeployedDuringCurrentPreparation { get; set; } = true;
        public bool IsCopiedFromFramiliar { get; set; } = false;
        public OperatorData OriginalFramiliarData { get; set; } = null;
        public OperatorRarity OriginalFramiliarRarity { get; set; } = OperatorRarity.Star2;

        /// <summary>
        /// Checks whether this operator taunts the specified attacker position.
        /// Ranged enemies prioritize this operator if the attacker is in a surrounding tile.
        /// </summary>
        public virtual bool ShouldTauntAttacker(Vector3 attackerWorldPosition, GridManager grid = null)
        {
            if (!HasTaunt || !isDeployed || currentHP <= 0 || !IsTargetable)
                return false;

            if (grid == null)
            {
                grid = FindAnyObjectByType<GridManager>();
            }

            if (grid != null)
            {
                Vector2Int opGrid = deployedCell != null
                    ? deployedCell.GridPosition
                    : grid.WorldToGridPosition(transform.position);
                Vector2Int attackerGrid = grid.WorldToGridPosition(attackerWorldPosition);

                int dx = Mathf.Abs(attackerGrid.x - opGrid.x);
                int dy = Mathf.Abs(attackerGrid.y - opGrid.y);
                return dx <= 1 && dy <= 1;
            }

            // Fallback for scenes/tests without GridManager
            float cellSize = 1.0f;
            return Mathf.Abs(attackerWorldPosition.x - transform.position.x) <= cellSize * 1.5f &&
                   Mathf.Abs(attackerWorldPosition.y - transform.position.y) <= cellSize * 1.5f;
        }

        public bool IsStunned => isStunned;
        public float StunTimer => stunTimer;
        public IReadOnlyList<EnemyBase> BlockedEnemies => blockedEnemies;
        public int HeatStacks { get; set; }
        public bool IsBurning { get; set; }
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

        /// <summary>
        /// Sets HP to the value saved on the deck card when this operator last retreated (call after Initialize).
        /// This is not a heal: it bypasses IsCombatPaused and raises no heal effects.
        /// </summary>
        public void RestoreSavedHP(int hp)
        {
            currentHP = Mathf.Clamp(hp, 1, maxHP);
            if (currentHP < maxHP)
            {
                TrashTD.Combat.WorldHealthBar.UpdateFor(gameObject, currentHP, maxHP);
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

            var slipperyEffects = GetComponents<Combat.SlipperyEffect>();
            for (int i = 0; i < slipperyEffects.Length; i++)
            {
                if (slipperyEffects[i] != null)
                {
                    slipperyEffects[i].ClearWithoutRestoring();
                }
            }
            blockCountModifier = 0;

            deployedCell?.Vacate();
            deployedCell = null;
            isDeployed = false;
            IsBurning = false;
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
            if (target == null || !isDeployed || isStunned) return;

            HasFoughtInWave = true;
            DeployedDuringCurrentPreparation = false;

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
            if (data == null) return;

            SfxId fallback = data.damageType == DamageType.Arts ? SfxId.GameplayMagicAttack
                : data.position == OperatorPosition.Ranged ? SfxId.GameplayRangedPhysicalAttack
                : SfxId.GameplayMeleeAttack;
            PlayAttackSound(fallback);
        }

        /// <summary>Attack sound: the operator's own Attack clip, else the given shared sound.</summary>
        protected void PlayAttackSound(SfxId fallback)
        {
            if (data == null || AudioManager.Instance == null) return;

            AudioSource voice;
            if (!UnitSfx.Play(data, UnitSfxEvent.Attack, out voice))
                voice = AudioManager.Instance.PlaySfx(fallback);
            TrackAttackSound(voice);
        }

        /// <summary>
        /// The operator's own Impact clip (projectile landing, explosion), if it has one. Called whenever one of its
        /// hits lands; repeats within a moment are throttled, so an AoE hit plays it once. When the impact plays, the
        /// attack sound still running (a long launch or charge-up) is cut so the two don't overlap.
        /// </summary>
        public void PlayImpactSound()
        {
            if (UnitSfx.Play(data, UnitSfxEvent.Impact, out AudioSource impactVoice) && impactVoice != null)
                CutAttackSound();
        }

        // Melee swings land in the same moment they start; an attack sound younger than this is left alone, or the
        // swing would be silenced.
        private const float MinAttackSoundBeforeCut = 0.1f;

        private AudioSource attackVoice;
        private AudioClip attackVoiceClip;
        private float attackVoiceStartTime;

        private void TrackAttackSound(AudioSource voice)
        {
            attackVoice = voice;
            attackVoiceClip = voice != null ? voice.clip : null;
            attackVoiceStartTime = Time.unscaledTime;
        }

        private void CutAttackSound()
        {
            if (attackVoice == null) return;

            if (Time.unscaledTime - attackVoiceStartTime >= MinAttackSoundBeforeCut)
                AudioManager.Instance?.StopSfx(attackVoice, attackVoiceClip);
            attackVoice = null;
            attackVoiceClip = null;
        }

        /// <summary>The operator's own Ability clip, else the given shared sound (if any).</summary>
        protected void PlayAbilitySound(SfxId? fallback = null)
        {
            if (data == null || UnitSfx.Play(data, UnitSfxEvent.Ability)) return;
            if (fallback.HasValue) AudioManager.Instance?.PlaySfx(fallback.Value);
        }

        /// <summary>
        /// A named ability clip (abilityName "Dodge" -> Ability_Dodge) for operators with more than one trigger,
        /// else the operator's plain Ability clip, else the given shared sound (if any).
        /// </summary>
        protected void PlayAbilitySound(string abilityName, SfxId? fallback = null)
        {
            if (data == null || UnitSfx.PlayAbility(data, abilityName)) return;
            if (fallback.HasValue) AudioManager.Instance?.PlaySfx(fallback.Value);
        }

        public Vector2Int[] GetRangePattern() => data.rangePattern;
        public virtual Vector2Int[] GetTalentRangePattern() => data != null ? data.GetTalentRangePattern() : null;
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
            if (enemy == null || !isDeployed || isStunned) return false;
            if (blockedEnemies.Count >= GetBlockCount()) return false;
            if (enemy.Data != null && enemy.Data.isUnblockable) return false;
            if (enemy is Enemies.OculoidCarrierEnemy carrier && (GetBlockCount() - blockedEnemies.Count) <= carrier.BlockCountReduction) return false;

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

        private int blockCountModifier = 0;
        public int BlockCountModifier => blockCountModifier;

        public void ModifyBlockCount(int delta)
        {
            blockCountModifier += delta;
            while (blockedEnemies.Count > GetBlockCount() && blockedEnemies.Count > 0)
            {
                ReleaseBlock(blockedEnemies[blockedEnemies.Count - 1]);
            }
        }

        public int GetBlockCount() => (isStunned || data == null) ? 0 : Mathf.Max(0, data.blockCount + blockCountModifier);
        public int GetCurrentBlockCount() => blockedEnemies.Count;

        // ========================
        // Stun System
        // ========================

        /// <summary>
        /// Applies a stun to this operator. While stunned, an operator cannot attack and block.
        /// </summary>
        public virtual void ApplyStun(float duration)
        {
            if (!isDeployed || currentHP <= 0 || duration <= 0f) return;

            stunTimer = Mathf.Max(stunTimer, duration);
            if (!isStunned)
            {
                isStunned = true;
                TrashTD.Combat.FloatingCombatNumber.ShowText(transform.position, "STUNNED!", new Color(1f, 0.9f, 0.2f));
                AudioManager.Instance?.PlaySfx(SfxId.CombatStun);
            }

            // Immediately release any currently blocked enemies
            for (int i = blockedEnemies.Count - 1; i >= 0; i--)
            {
                ReleaseBlock(blockedEnemies[i]);
            }

            UpdateStunVisual();
        }

        public void UpdateStunStatus(float dt)
        {
            if (stunTimer > 0f)
            {
                stunTimer = Mathf.Max(0f, stunTimer - dt);
                if (stunTimer <= 0f)
                {
                    isStunned = false;
                    UpdateStunVisual();
                }
            }
        }

        protected virtual void UpdateStunVisual()
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
                if (spriteRenderer == null)
                    spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }
            if (spriteRenderer != null)
            {
                Color current = spriteRenderer.color;
                if (isStunned)
                {
                    spriteRenderer.color = new Color(1f, 0.95f, 0.4f, current.a);
                }
                else
                {
                    spriteRenderer.color = new Color(1f, 1f, 1f, current.a);
                }
            }
        }

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
                    AudioManager.Instance?.PlaySfx(SfxId.TileTrapTick, 0.6f);
                    TakeTrueDamage(TrapTileRules.DamagePerTick);
                }
                if (!isDeployed) return;
            }
            else
            {
                trapDamageTimer = 0f;
            }

            if (isStunned)
            {
                UpdateStunStatus(Time.deltaTime);
                return;
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
            if (isStunned || target == null || target.IsDead || target.IsInsideHole) return false;
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

            int damage = Combat.DamageCalculator.CalculateDamage(rawATK, CurrentDEF, currentRES, damageType);
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
        /// Take true damage directly (ignoring DEF and RES).
        /// If ignoreShieldNegation is true (e.g. Stage 5 traffic collision),
        /// damage bypasses shield negation but still pops the bubble.
        /// If canDodge is true (e.g. Stage 2 falling items), operators with dodge (e.g. Stag-ger) can roll to dodge.
        /// Returns true if damage was dealt, false if prevented, dodged or absorbed.
        /// </summary>
        public virtual bool TakeTrueDamage(int damage, bool ignoreShieldNegation = false, bool canDodge = false)
        {
            if (IsCombatPaused || currentHP <= 0 || damage <= 0) return false;

            var shield = GetComponent<BubbleShield>();
            if (shield != null && shield.IsActive)
            {
                if (ignoreShieldNegation)
                {
                    shield.Pop(false);
                }
                else if (shield.TryConsume(damage, DamageType.True))
                {
                    return false;
                }
            }

            int previousHP = currentHP;
            currentHP = Mathf.Max(0, currentHP - damage);
            int actualDamage = previousHP - currentHP;
            if (actualDamage > 0)
            {
                TrashTD.Combat.FloatingCombatNumber.Show(transform.position, actualDamage, DamageType.True);
                TrashTD.Combat.WorldHealthBar.UpdateFor(gameObject, currentHP, maxHP);
            }

            if (currentHP <= 0)
            {
                Die();
            }

            return actualDamage > 0;
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
            if (!UnitSfx.Play(data, UnitSfxEvent.Death))
                AudioManager.Instance?.PlaySfx(SfxId.GameplayOperatorDeath);
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
        protected virtual void OnRetreated()
        {
            isStunned = false;
            stunTimer = 0f;
            UpdateStunVisual();
        }

        /// <summary>
        /// Rolls the number of 3-star operator cards granted by an upgraded Drawgoo on death.
        /// 60% chance for 1 card, 40% chance for 2 cards.
        /// </summary>
        public static int RollUpgradedDrawgooCardCount(float roll = -1f)
        {
            if (roll < 0f) roll = UnityEngine.Random.value;
            return roll < 0.60f ? 1 : 2;
        }

        /// <summary>
        /// Gets the skill description for this operator, which may vary depending on rarity/upgrades.
        /// </summary>
        public virtual string GetSkillDescription()
        {
            if (data == null) return string.Empty;
            if (data.drawTwoCardsOnDeath)
            {
                if (currentRarity >= OperatorRarity.Star2)
                {
                    return "When Drawgoo dies, draw 1 (60%) or 2 (40%) random 3-star operator cards from the pool.";
                }
                return "When Drawgoo dies, draw two random operator cards from the pool.";
            }
            return data.skillDescription;
        }

        /// <summary>Called when the operator dies.</summary>
        protected virtual void OnDeath()
        {
            if (data == null || !data.drawTwoCardsOnDeath) return;

            TrashTD.Systems.CardDraftSystem draftSystem =
                TrashTD.Systems.CardDraftSystem.Instance != null
                    ? TrashTD.Systems.CardDraftSystem.Instance
                    : FindAnyObjectByType<TrashTD.Systems.CardDraftSystem>();
            if (draftSystem == null)
            {
                Debug.LogError($"{data.operatorName} died, but no CardDraftSystem is active to resolve its death skill.");
                return;
            }

            PlayAbilitySound();

            if (currentRarity >= OperatorRarity.Star2)
            {
                int count = RollUpgradedDrawgooCardCount();
                draftSystem.DrawRandomCardsToDeck(count, OperatorRarity.Star3);
            }
            else
            {
                draftSystem.DrawRandomCardsToDeck(2);
            }
        }
    }
}
