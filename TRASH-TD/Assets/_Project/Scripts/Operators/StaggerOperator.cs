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
    /// 1x upgrade (3★): Block count increases by 1. Dodge chance increases to 30% and heals 5% of max HP when successfully dodging.
    /// 2nd upgrade (4★): Dodge chance increases to 40% and heals 10% of max HP when killing an enemy.
    ///   Every enemy that comes to the tile before or to the sides of his front tile range gets pulled into it and stopped for 1 second (except tanks and flying enemies).
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
        public const float PullStopDuration = 1.0f;
        public const float PullMoveDuration = 0.2f;
        public const int ExtraBlockCount = 1;

        public static float GetDodgeChance(OperatorRarity rarity)
        {
            if (rarity >= OperatorRarity.Star4) return Star4DodgeChance;
            if (rarity >= OperatorRarity.Star3) return Star3DodgeChance;
            return BaseDodgeChance;
        }

        public float CurrentDodgeChance => GetDodgeChance(currentRarity);
        public bool HasDodgeHealUpgrade => currentRarity >= OperatorRarity.Star3;
        public bool HasExtraBlockUpgrade => currentRarity >= OperatorRarity.Star3;
        public bool HasKillHealUpgrade => currentRarity >= OperatorRarity.Star4;
        public bool HasPullUpgrade => currentRarity >= OperatorRarity.Star4;

        public override int GetMaxBlockCount()
        {
            int blockCount = base.GetMaxBlockCount();
            return blockCount > 0 && HasExtraBlockUpgrade ? blockCount + ExtraBlockCount : blockCount;
        }

        public void SetRarityForTesting(OperatorRarity rarity)
        {
            currentRarity = rarity;
        }

        public void SetDeployedForTesting(GridCell cell, OperatorFacing facingDirection = OperatorFacing.Right)
        {
            deployedCell = cell;
            isDeployed = true;
            facing = facingDirection;
        }

        private readonly List<EnemyBase> targetBuffer = new List<EnemyBase>();
        private readonly HashSet<EnemyBase> pulledEnemies = new HashSet<EnemyBase>();
        private bool hasDodgeAtkBuff;
        private StaggerSpriteAnimation spriteAnimation;

        public bool HasPulledEnemy(EnemyBase enemy) => enemy != null && pulledEnemies.Contains(enemy);
        public void ResetPulledEnemiesForTesting() => pulledEnemies.Clear();

        public static bool IsImmuneToPull(EnemyBase enemy)
        {
            if (enemy == null) return true;
            if (enemy.Data != null)
            {
                if (enemy.Data.archetype == EnemyArchetype.Tank) return true;
                if (enemy.Data.movementType == EnemyMovementType.Air || enemy.Data.archetype == EnemyArchetype.Flyer) return true;
            }
            if (enemy is TankEnemy || enemy is JunkernautEnemy) return true;
            if (enemy is FlyerEnemy) return true;
            return false;
        }

        public static Vector2Int GetFacingOffset(OperatorFacing facing)
        {
            return facing switch
            {
                OperatorFacing.Up => new Vector2Int(0, 1),
                OperatorFacing.Left => new Vector2Int(-1, 0),
                OperatorFacing.Down => new Vector2Int(0, -1),
                _ => new Vector2Int(1, 0)
            };
        }

        public static Vector2Int GetSideOffset(OperatorFacing facing)
        {
            return facing switch
            {
                OperatorFacing.Up => new Vector2Int(-1, 0),
                OperatorFacing.Down => new Vector2Int(1, 0),
                OperatorFacing.Left => new Vector2Int(0, -1),
                _ => new Vector2Int(0, 1) // OperatorFacing.Right
            };
        }

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
            ResetPulledEnemies();
        }

        protected override void OnDeployed()
        {
            base.OnDeployed();
            hasDodgeAtkBuff = false;
            ResetPulledEnemies();
            EnsureStaggerAnimation();
            SubscribeToGameEvents();
        }

        protected override void OnRetreated()
        {
            hasDodgeAtkBuff = false;
            ResetPulledEnemies();
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
                wm.OnSingleWaveFinished -= ResetPulledEnemies;
                wm.OnSingleWaveFinished += ResetPulledEnemies;
                wm.OnAllWavesCleared -= ResetPulledEnemies;
                wm.OnAllWavesCleared += ResetPulledEnemies;
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
                cachedWaveManager.OnSingleWaveFinished -= ResetPulledEnemies;
                cachedWaveManager.OnAllWavesCleared -= ResetPulledEnemies;
            }
        }

        private void HandlePhaseChanged(TrashTD.Core.GameLoop.StagePhase phase)
        {
            if (phase == TrashTD.Core.GameLoop.StagePhase.Preparation || phase == TrashTD.Core.GameLoop.StagePhase.CardPick)
            {
                ResetPulledEnemies();
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

        public void ResetPulledEnemies()
        {
            pulledEnemies.Clear();
        }

        public void ReloadFatalPrevention()
        {
            ResetPulledEnemies();
        }

        protected override void Update()
        {
            base.Update();

            if (!isDeployed) return;

            CheckWaveStateTransitions();

            if (!IsCombatPaused)
            {
                CheckPullEnemies();
            }
        }

        public void CheckPullEnemies()
        {
            if (!HasPullUpgrade || !isDeployed || deployedCell == null || isStunned || isBound) return;

            GridManager gridManager = FindAnyObjectByType<GridManager>();
            if (gridManager == null) return;

            Vector2Int forwardDir = GetFacingOffset(Facing);
            Vector2Int sideDir = GetSideOffset(Facing);

            Vector2Int frontPos = deployedCell.GridPosition + forwardDir;
            GridCell frontCell = gridManager.GetCell(frontPos);
            if (frontCell == null) return;

            Vector2Int aheadPos = frontPos + forwardDir;
            Vector2Int sidePos1 = frontPos + sideDir;
            Vector2Int sidePos2 = frontPos - sideDir;

            var triggerCells = new List<GridCell>(3);
            GridCell aheadCell = gridManager.GetCell(aheadPos);
            if (aheadCell != null) triggerCells.Add(aheadCell);

            GridCell sideCell1 = gridManager.GetCell(sidePos1);
            if (sideCell1 != null) triggerCells.Add(sideCell1);

            GridCell sideCell2 = gridManager.GetCell(sidePos2);
            if (sideCell2 != null) triggerCells.Add(sideCell2);

            if (triggerCells.Count == 0) return;

            Vector3 destination = frontCell.WorldPosition;

            pulledEnemies.RemoveWhere(e => e == null || e.IsDead);

            List<EnemyBase> candidates = null;
            if (EnemyManager.Instance != null)
            {
                candidates = EnemyManager.Instance.GetEnemiesInCells(triggerCells);
            }
            else
            {
                candidates = new List<EnemyBase>();
                EnemyBase[] allEnemies = FindObjectsByType<EnemyBase>();
                for (int i = 0; i < allEnemies.Length; i++)
                {
                    EnemyBase e = allEnemies[i];
                    if (e != null && !e.IsDead)
                    {
                        Vector2Int ePos = gridManager.WorldToGridPosition(e.transform.position);
                        if (ePos == aheadPos || ePos == sidePos1 || ePos == sidePos2)
                        {
                            candidates.Add(e);
                        }
                    }
                }
            }

            if (candidates == null || candidates.Count == 0) return;

            for (int i = 0; i < candidates.Count; i++)
            {
                EnemyBase enemy = candidates[i];
                if (enemy == null || enemy.IsDead || enemy.IsInsideHole) continue;
                if (pulledEnemies.Contains(enemy)) continue;
                if (IsImmuneToPull(enemy)) continue;

                pulledEnemies.Add(enemy);
                bool pulled = enemy.PullTo(destination, PullStopDuration, PullMoveDuration);
                if (pulled)
                {
                    MeleeSwipeVisual.Play(transform.position, destination, new Color(1f, 0.6f, 0.2f, 0.8f));
                    PlayAbilitySound("Pull", SfxId.CombatStun);
                }
            }
        }

        private void CheckWaveStateTransitions()
        {
            var wm = GetWaveManager();
            if (wm != null)
            {
                bool isWaveActive = wm.IsWaveInProgress;
                if (wasWaveActive && !isWaveActive)
                {
                    ResetPulledEnemies();
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
                Combat.DodgeAfterimageVisual.Play(gameObject);
                FloatingCombatNumber.ShowText(transform.position, "DODGE!", new Color(0.2f, 0.85f, 1f), this);
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
            currentHP = Mathf.Max(0, currentHP - damage);

            int actualDamage = previousHP - currentHP;
            if (actualDamage > 0)
            {
                FloatingCombatNumber.Show(transform.position, actualDamage, damageType, false, this);
                WorldHealthBar.UpdateFor(gameObject, currentHP, maxHP);
                HitReaction.Play(gameObject, transform.position, actualDamage, maxHP);
            }

            if (currentHP <= 0)
            {
                Die();
            }

            return actualDamage;
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

                    // 2x Upgrade: Heals 10% of max HP when killing an enemy
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

        public override int GetDisplayATK() => hasDodgeAtkBuff ? Mathf.RoundToInt(base.GetDisplayATK() * 1.5f) : base.GetDisplayATK();

        public override void CollectStatEffects(System.Collections.Generic.List<StatEffect> effects)
        {
            base.CollectStatEffects(effects);

            if (HasExtraBlockUpgrade)
                effects.Add(new StatEffect("3\u2605 UPGRADE", $"Block +{ExtraBlockCount}", "BLOCK"));
            if (hasDodgeAtkBuff)
                effects.Add(new StatEffect("DODGE", "Next hit ATK +50%", "ATK"));
        }
    }
}
