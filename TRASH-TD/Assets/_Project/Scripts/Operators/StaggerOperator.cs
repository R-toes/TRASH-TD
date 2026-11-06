using System;
using System.Collections.Generic;
using UnityEngine;
using TrashTD.Audio;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Stag-ger — 2-Star Guard (2 Block).
    /// Ability: Six-Limb Slugger — Pure AoE melee brawler.
    /// Base (2★): When attacking, strikes all enemies on his tile and in range ahead simultaneously.
    ///   Has a 20% chance to dodge any attack; a successful dodge increases the next attack's damage by 50%.
    /// 1st upgrade (3★): Dodge chance increases to 30% and heals 10% of max HP when killing an enemy.
    /// 2nd upgrade (4★): Dodge chance increases to 40% and heals 5% of max HP when successfully dodging.
    ///   When receiving fatal damage, prevents HP from falling below 1 for 10 seconds (recharges upon reaching full HP or when the wave ends).
    /// Uses 48x32 Six-Limb Slugger attack animation with right 32x32 pivot offset (2/3, 0.5)
    /// to keep Stag-ger centered on the deployed tile while the flurry of punches extends into the forward tile.
    /// </summary>
    public class StaggerOperator : GuardOperator
    {
        public const float BaseDodgeChance = 0.20f;
        public const float Star3DodgeChance = 0.30f;
        public const float Star4DodgeChance = 0.40f;
        public const float KillHealPercent = 0.10f;
        public const float DodgeHealPercent = 0.05f;
        public const float FatalPreventionDuration = 10.0f;

        public static float GetDodgeChance(OperatorRarity rarity)
        {
            if (rarity >= OperatorRarity.Star4) return Star4DodgeChance;
            if (rarity >= OperatorRarity.Star3) return Star3DodgeChance;
            return BaseDodgeChance;
        }

        public float CurrentDodgeChance => GetDodgeChance(currentRarity);
        public bool HasKillHealUpgrade => currentRarity >= OperatorRarity.Star3;
        public bool HasDodgeHealUpgrade => currentRarity >= OperatorRarity.Star4;
        public bool HasFatalPreventionUpgrade => currentRarity >= OperatorRarity.Star4;

        public bool IsFatalPreventionReady => isFatalPreventionReady;
        public bool IsFatalPreventionActive => fatalPreventionTimer > 0f;
        public float FatalPreventionTimer => fatalPreventionTimer;

        private readonly List<EnemyBase> targetBuffer = new List<EnemyBase>();
        private bool hasDodgeAtkBuff;
        private StaggerSpriteAnimation spriteAnimation;

        private bool isFatalPreventionReady = true;
        private float fatalPreventionTimer = 0f;
        private bool wasWaveActive;
        private TrashTD.Core.GameLoop.WaveManager cachedWaveManager;

        public bool HasDodgeAtkBuff => hasDodgeAtkBuff;

        public StaggerSpriteAnimation SpriteAnimation
        {
            get
            {
                if (spriteAnimation == null)
                {
                    spriteAnimation = GetComponentInChildren<StaggerSpriteAnimation>(true);
                    if (spriteAnimation == null)
                    {
                        spriteAnimation = gameObject.AddComponent<StaggerSpriteAnimation>();
                    }
                }
                return spriteAnimation;
            }
        }

        private void Awake()
        {
            // If the GameObject has generic OperatorSpriteAnimation, disable it so StaggerSpriteAnimation controls playback
            var genericAnim = GetComponent<OperatorSpriteAnimation>();
            if (genericAnim != null && genericAnim.enabled)
            {
                genericAnim.enabled = false;
            }

            EnsureStaggerAnimation();
        }

        private void EnsureStaggerAnimation()
        {
            var anim = SpriteAnimation;
            if (anim != null && !anim.enabled)
            {
                anim.enabled = true;
            }
        }

        /// <summary>
        /// Random value generator for dodge roll in [0, 1).
        /// Overridable in unit tests for deterministic testing.
        /// </summary>
        public Func<float> RandomValueProvider { get; set; } = () => UnityEngine.Random.value;

        public override void Initialize(OperatorData operatorData, OperatorRarity rarity, OperatorFacing operatorFacing = OperatorFacing.Right)
        {
            base.Initialize(operatorData, rarity, operatorFacing);
            ReloadFatalPrevention();
        }

        protected override void OnDeployed()
        {
            base.OnDeployed();
            hasDodgeAtkBuff = false;
            ReloadFatalPrevention();
            EnsureStaggerAnimation();
            SubscribeToGameEvents();
        }

        protected override void OnRetreated()
        {
            hasDodgeAtkBuff = false;
            ReloadFatalPrevention();
            SpriteAnimation?.CancelAttack();
            UnsubscribeFromGameEvents();
            base.OnRetreated();
        }

        private void OnDestroy()
        {
            UnsubscribeFromGameEvents();
        }

        private void SubscribeToGameEvents()
        {
            if (TrashTD.Core.GameLoop.GameManager.Instance != null)
            {
                TrashTD.Core.GameLoop.GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
                TrashTD.Core.GameLoop.GameManager.Instance.OnPhaseChanged += HandlePhaseChanged;
            }

            var wm = GetWaveManager();
            if (wm != null)
            {
                wm.OnSingleWaveFinished -= ReloadFatalPrevention;
                wm.OnSingleWaveFinished += ReloadFatalPrevention;
                wm.OnAllWavesCleared -= ReloadFatalPrevention;
                wm.OnAllWavesCleared += ReloadFatalPrevention;
            }
        }

        private void UnsubscribeFromGameEvents()
        {
            if (TrashTD.Core.GameLoop.GameManager.Instance != null)
            {
                TrashTD.Core.GameLoop.GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
            }

            if (cachedWaveManager != null)
            {
                cachedWaveManager.OnSingleWaveFinished -= ReloadFatalPrevention;
                cachedWaveManager.OnAllWavesCleared -= ReloadFatalPrevention;
            }
        }

        private void HandlePhaseChanged(TrashTD.Core.GameLoop.StagePhase phase)
        {
            if (phase == TrashTD.Core.GameLoop.StagePhase.Preparation || phase == TrashTD.Core.GameLoop.StagePhase.CardPick)
            {
                ReloadFatalPrevention();
            }
        }

        private TrashTD.Core.GameLoop.WaveManager GetWaveManager()
        {
            if (cachedWaveManager == null)
            {
                cachedWaveManager = FindAnyObjectByType<TrashTD.Core.GameLoop.WaveManager>();
            }
            return cachedWaveManager;
        }

        public void ReloadFatalPrevention()
        {
            fatalPreventionTimer = 0f;
            isFatalPreventionReady = true;
        }

        protected override void OnHealed(int amount)
        {
            base.OnHealed(amount);
            CheckFullHpReload();
        }

        public void CheckFullHpReload()
        {
            if (HasFatalPreventionUpgrade && !isFatalPreventionReady && currentHP >= maxHP)
            {
                ReloadFatalPrevention();
            }
        }

        protected override void Update()
        {
            base.Update();

            if (!isDeployed) return;

            if (fatalPreventionTimer > 0f && !IsCombatPaused)
            {
                fatalPreventionTimer = Mathf.Max(0f, fatalPreventionTimer - Time.deltaTime);
            }

            CheckFullHpReload();
            CheckWaveStateTransitions();
        }

        private void CheckWaveStateTransitions()
        {
            if (IsCombatPaused && (!isFatalPreventionReady || fatalPreventionTimer > 0f))
            {
                ReloadFatalPrevention();
            }

            var wm = GetWaveManager();
            if (wm != null)
            {
                bool isWaveActive = wm.IsWaveInProgress;
                if (wasWaveActive && !isWaveActive)
                {
                    ReloadFatalPrevention();
                }
                wasWaveActive = isWaveActive;
            }
        }

        public bool TryRollDodge()
        {
            float roll = RandomValueProvider != null ? RandomValueProvider() : UnityEngine.Random.value;
            if (roll < CurrentDodgeChance)
            {
                hasDodgeAtkBuff = true;
                FloatingCombatNumber.ShowText(transform.position, "DODGE!", new Color(0.2f, 0.85f, 1f));
                PlayAbilitySound("Dodge");

                if (HasDodgeHealUpgrade)
                {
                    int healAmount = Mathf.Max(1, Mathf.RoundToInt(maxHP * DodgeHealPercent));
                    Heal(healAmount);
                }

                return true;
            }

            return false;
        }

        public override void TakeDamage(int rawATK, DamageType damageType, EnemyBase attacker = null)
        {
            if (IsCombatPaused || currentHP <= 0 || rawATK <= 0) return;

            // Attacks from enemies can be dodged
            // Environmental damage (overheat from stage 4, acid pools, etc.) has attacker == null and cannot be dodged.
            if (attacker != null && TryRollDodge())
            {
                return;
            }

            var shield = GetComponent<BubbleShield>();
            if (shield != null && shield.TryConsume(rawATK, damageType))
            {
                return;
            }

            int damage = Combat.DamageCalculator.CalculateDamage(rawATK, CurrentDEF, CurrentRES, damageType);
            ApplyCalculatedDamage(damage, damageType);
        }

        public override bool TakeTrueDamage(int damage, bool ignoreShieldNegation = false, bool canDodge = false)
        {
            if (IsCombatPaused || currentHP <= 0 || damage <= 0) return false;

            // Vehicles from Stage 5 call TakeTrueDamage with ignoreShieldNegation: true and can be dodged.
            // Stage 2 falling items call TakeTrueDamage with canDodge: true and can be dodged.
            // Acid rain, acid tiles, and stage 4 heat pass canDodge: false, ignoreShieldNegation: false and cannot be dodged.
            if ((ignoreShieldNegation || canDodge) && TryRollDodge())
            {
                return false;
            }

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

            return ApplyCalculatedDamage(damage, DamageType.True) > 0;
        }

        private int ApplyCalculatedDamage(int damage, DamageType damageType)
        {
            if (damage <= 0 || currentHP <= 0) return 0;

            int previousHP = currentHP;

            if (HasFatalPreventionUpgrade)
            {
                if (fatalPreventionTimer > 0f)
                {
                    // Continuously prevent HP from falling below 1 for the 10-second duration
                    currentHP = Mathf.Max(1, currentHP - damage);
                }
                else if (damage >= currentHP && isFatalPreventionReady)
                {
                    // Fatal damage received: trigger 10s immortality window and clamp HP to 1
                    TriggerFatalPrevention();
                    currentHP = 1;
                }
                else
                {
                    currentHP = Mathf.Max(0, currentHP - damage);
                }
            }
            else
            {
                currentHP = Mathf.Max(0, currentHP - damage);
            }

            int actualDamage = previousHP - currentHP;
            if (actualDamage > 0)
            {
                FloatingCombatNumber.Show(transform.position, actualDamage, damageType);
                WorldHealthBar.UpdateFor(gameObject, currentHP, maxHP);
                HitReaction.Play(gameObject, transform.position, actualDamage, maxHP);
            }
            else if (fatalPreventionTimer > 0f)
            {
                HitReaction.Play(gameObject, transform.position, 0, maxHP);
            }

            if (currentHP <= 0)
            {
                Die();
            }

            return actualDamage;
        }

        private void TriggerFatalPrevention()
        {
            isFatalPreventionReady = false;
            fatalPreventionTimer = FatalPreventionDuration;
            FloatingCombatNumber.ShowText(transform.position, "UNDYING!", new Color(1f, 0.45f, 0.1f));
            PlayAbilitySound("Undying", SfxId.CombatStun);
        }

        protected override void Die()
        {
            if (HasFatalPreventionUpgrade)
            {
                if (fatalPreventionTimer > 0f)
                {
                    currentHP = 1;
                    WorldHealthBar.UpdateFor(gameObject, currentHP, maxHP);
                    return;
                }

                if (isFatalPreventionReady)
                {
                    TriggerFatalPrevention();
                    currentHP = 1;
                    WorldHealthBar.UpdateFor(gameObject, currentHP, maxHP);
                    return;
                }
            }

            base.Die();
        }

        public List<EnemyBase> GetTargetsInRange()
        {
            targetBuffer.Clear();
            if (!isDeployed || deployedCell == null) return targetBuffer;

            GridManager gridManager = FindAnyObjectByType<GridManager>();
            if (gridManager == null || EnemyManager.Instance == null) return targetBuffer;

            List<GridCell> reachCells = new List<GridCell> { deployedCell };
            if (data != null && data.rangePattern != null)
            {
                GridCell[] forwardCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
                if (forwardCells != null)
                {
                    for (int i = 0; i < forwardCells.Length; i++)
                    {
                        GridCell cell = forwardCells[i];
                        if (cell != null && !reachCells.Contains(cell))
                        {
                            reachCells.Add(cell);
                        }
                    }
                }
            }

            List<EnemyBase> candidates = EnemyManager.Instance.GetEnemiesInCells(reachCells, data != null ? data.position : OperatorPosition.Melee);
            if (candidates != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    EnemyBase enemy = candidates[i];
                    if (enemy != null && !enemy.IsDead && !targetBuffer.Contains(enemy))
                    {
                        targetBuffer.Add(enemy);
                    }
                }
            }

            // Also check any enemies directly blocked by Stag-ger
            for (int i = 0; i < blockedEnemies.Count; i++)
            {
                EnemyBase enemy = blockedEnemies[i];
                if (enemy != null && !enemy.IsDead && !targetBuffer.Contains(enemy))
                {
                    targetBuffer.Add(enemy);
                }
            }

            return targetBuffer;
        }

        protected override EnemyBase FindTarget()
        {
            if (!isDeployed) return null;

            List<EnemyBase> targets = GetTargetsInRange();
            if (targets == null || targets.Count == 0) return null;

            EnemyBase bestTarget = null;
            int lowestHP = int.MaxValue;
            for (int i = 0; i < targets.Count; i++)
            {
                EnemyBase enemy = targets[i];
                if (enemy == null || enemy.IsDead) continue;

                bool isTaunt = enemy.HasTaunt;
                bool bestIsTaunt = bestTarget != null && bestTarget.HasTaunt;
                if (bestTarget == null || (isTaunt && !bestIsTaunt) || (isTaunt == bestIsTaunt && enemy.CurrentHP < lowestHP))
                {
                    bestTarget = enemy;
                    lowestHP = enemy.CurrentHP;
                }
            }

            return bestTarget;
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed || isStunned || isBound) return;

            HasFoughtInWave = true;
            DeployedDuringCurrentPreparation = false;

            // Trigger the 48x32 Six-Limb Slugger attack animation
            SpriteAnimation?.PlayAttack();

            int attackAtk = hasDodgeAtkBuff
                ? Mathf.RoundToInt(currentATK * 1.5f)
                : currentATK;
            if (hasDodgeAtkBuff) PlayAbilitySound("Empowered");

            PlayAttackSound();

            List<EnemyBase> targets = GetTargetsInRange();
            if (!targets.Contains(target))
            {
                targets.Add(target);
            }

            bool hitAny = false;
            for (int i = 0; i < targets.Count; i++)
            {
                EnemyBase enemy = targets[i];
                if (enemy == null || enemy.IsDead) continue;

                MeleeSwipeVisual.Play(
                    transform.position,
                    enemy.VisualPosition,
                    hasDodgeAtkBuff
                        ? new Color(1f, 0.92f, 0.25f, 1f)
                        : new Color(1f, 0.45f, 0.1f, 1f));

                int damage = DamageCalculator.CalculateDamage(attackAtk, GetTargetMitigation(enemy));
                if (enemy.TryTakeAttackDamage(damage, data.damageType, this))
                {
                    hitAny = true;

                    // 1x Upgrade: Heals 10% of max HP when killing an enemy
                    if (HasKillHealUpgrade && (enemy.IsDead || enemy.CurrentHP <= 0))
                    {
                        int healAmount = Mathf.Max(1, Mathf.RoundToInt(maxHP * KillHealPercent));
                        Heal(healAmount);
                    }
                }
            }

            if (hitAny)
            {
                PlayImpactSound();
            }

            hasDodgeAtkBuff = false;
        }
    }
}
