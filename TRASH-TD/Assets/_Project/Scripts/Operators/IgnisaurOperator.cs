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
    /// Ignisaur — 2-Star Guard (1 Block).
    /// Range:
    /// - Own tile: Deals Physical damage with a melee slash to enemies on his tile / blocked enemies.
    /// - Range tiles (yellow front tile & green surrounding tiles): Deals Arts damage with ranged projectile attacks.
    /// Trait / Ability (Protean):
    /// - Reduces attack interval when not blocking an enemy (faster attacks).
    /// - Increases attack power by 25% when blocking an enemy.
    /// - Capable of targeting and attacking aerial/drone enemies.
    /// </summary>
    public class IgnisaurOperator : GuardOperator
    {
        private const float UnblockedIntervalMultiplier = 0.80f; // 20% faster attacks when not blocking
        private const float BlockedAtkMultiplier = 1.25f; // +25% ATK when blocking an enemy

        private static readonly Color SlashColor = new Color(1f, 0.82f, 0.2f, 1f);
        private static readonly Color ArtsProjectileColor = new Color(1f, 0.45f, 0.12f, 1f);

        public override bool CanHitAir => true;

        public bool HasFirstUpgrade => currentRarity >= OperatorRarity.Star3;
        public bool HasSecondUpgrade => currentRarity >= OperatorRarity.Star4;

        public static readonly Vector2Int[] UpgradedRangePattern = new[]
        {
            new Vector2Int(1, 0),
            new Vector2Int(1, 1),
            new Vector2Int(1, -1),
            new Vector2Int(2, 0),
            new Vector2Int(2, 1),
            new Vector2Int(2, -1),
            new Vector2Int(3, 0)
        };

        public override Vector2Int[] GetRangePattern()
        {
            if (data == null || data.rangePattern == null) return null;
            return HasFirstUpgrade ? UpgradedRangePattern : data.rangePattern;
        }

        public static Vector2Int[] GetRangePatternForRarity(OperatorData operatorData, OperatorRarity rarity)
        {
            if (operatorData == null || operatorData.rangePattern == null) return null;
            return rarity >= OperatorRarity.Star3 ? UpgradedRangePattern : operatorData.rangePattern;
        }

        public bool IsBlocking => blockedEnemies != null && blockedEnemies.Count > 0;

        public override int CurrentATK
        {
            get
            {
                int baseAtk = base.CurrentATK;
                bool applyBuff = HasSecondUpgrade || IsBlocking;
                return applyBuff ? Mathf.RoundToInt(baseAtk * BlockedAtkMultiplier) : baseAtk;
            }
        }

        public override float GetAttackInterval()
        {
            float baseInterval = base.GetAttackInterval();
            bool applySpeedBuff = HasSecondUpgrade || !IsBlocking;
            return applySpeedBuff ? baseInterval * UnblockedIntervalMultiplier : baseInterval;
        }

        public DamageType DetermineBestDamageType(EnemyBase target)
        {
            if (target == null) return DamageType.Physical;

            int physicalDamage = DamageCalculator.CalculateDamage(CurrentATK, target.CurrentDEF);
            int artsDamage = DamageCalculator.CalculateDamage(CurrentATK, target.CurrentRES);
            if (OutgoingArtsDamageMultiplier > 1f)
            {
                artsDamage = Mathf.Max(1, Mathf.RoundToInt(artsDamage * OutgoingArtsDamageMultiplier));
            }

            return physicalDamage > artsDamage ? DamageType.Physical : DamageType.Arts;
        }

        private IgnisaurSpriteAnimation spriteAnimation;

        public IgnisaurSpriteAnimation SpriteAnimation
        {
            get
            {
                if (spriteAnimation == null) spriteAnimation = GetComponentInChildren<IgnisaurSpriteAnimation>(true);
                return spriteAnimation;
            }
        }

        private bool HasAttackAnimations => SpriteAnimation != null && SpriteAnimation.enabled &&
                                            SpriteAnimation.HasSwipeAnimation && SpriteAnimation.HasThrowAnimation &&
                                            SpriteAnimation.HasFireballFrames;

        protected override void Update()
        {
            // A stunned or bound Ignisaur drops the attack he was animating (its hit never lands)
            if ((isStunned || isBound) && SpriteAnimation != null) SpriteAnimation.CancelAttack();
            base.Update();
        }

        protected override void OnRetreated()
        {
            if (SpriteAnimation != null) SpriteAnimation.CancelAttack();
            base.OnRetreated();
        }

        /// <summary>
        /// Attacks play to completion before the next one can start, so the animation never restarts or overlaps.
        /// The attack timer stays charged meanwhile, so the next attack starts the moment he is back on idle.
        /// </summary>
        public override bool CanAttackTarget(EnemyBase target)
        {
            if (SpriteAnimation != null && SpriteAnimation.IsAttacking) return false;
            return base.CanAttackTarget(target);
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed || isStunned || isBound) return;

            if (HasAttackAnimations)
            {
                // Distance picks the attack: his own tile (or a blocked enemy) gets the claw swipe, anything further
                // in range gets the fireball. The damage and the fireball are released by the animation's event frames.
                bool started = IsOwnTileTarget(target)
                    ? SpriteAnimation.PlaySwipe(() => LandSwipe(target))
                    : SpriteAnimation.PlayThrow(() => ReleaseFireball(target));
                if (started)
                {
                    HasFoughtInWave = true;
                    DeployedDuringCurrentPreparation = false;
                    return;
                }
            }

            AttackWithoutAnimation(target);
        }

        /// <summary>Swipe strike frame: the claw lands. The crescent is drawn in the swipe frames themselves.</summary>
        private void LandSwipe(EnemyBase target)
        {
            if (this == null || !isDeployed || isStunned || isBound || target == null || target.IsDead) return;

            DamageType chosenDamageType = HasSecondUpgrade ? DetermineBestDamageType(target) : DamageType.Physical;
            PlayAttackSound(chosenDamageType == DamageType.Arts ? SfxId.GameplayMagicAttack : SfxId.GameplayMeleeAttack);
            ApplyDamage(target, chosenDamageType);
        }

        /// <summary>Throw release frame: the fireball leaves his hand and flies to the target, which takes the hit on arrival.</summary>
        private void ReleaseFireball(EnemyBase target)
        {
            if (this == null || !isDeployed || isStunned || isBound || target == null || target.IsDead) return;

            DamageType chosenDamageType = HasSecondUpgrade ? DetermineBestDamageType(target) : DamageType.Arts;
            PlayAttackSound(chosenDamageType == DamageType.Physical ? SfxId.GameplayRangedPhysicalAttack : SfxId.GameplayMagicAttack);

            Vector2 offset = IgnisaurSpriteAnimation.FireballSpawnOffset;
            if (facing == OperatorFacing.Right) offset.x = -offset.x; // the art faces left and is mirrored when facing right
            Vector3 scale = transform.lossyScale;
            Vector3 spawn = transform.position + new Vector3(offset.x * scale.x, offset.y * scale.y, 0f);

            IgnisaurFireball.Fire(
                spawn,
                target,
                SpriteAnimation.FireballFlyFrames,
                SpriteAnimation.FireballImpactFrames,
                10f,
                () => ApplyDamage(target, chosenDamageType));
        }

        /// <summary>The original instant attack, used only when the prefab has no Ignisaur attack animations.</summary>
        private void AttackWithoutAnimation(EnemyBase target)
        {
            bool isOwnTile = IsOwnTileTarget(target);
            DamageType chosenDamageType = HasSecondUpgrade
                ? DetermineBestDamageType(target)
                : (isOwnTile ? DamageType.Physical : DamageType.Arts);

            if (isOwnTile)
            {
                // Physical melee slash on own tile (or chosen type at 2x Upgrade)
                PlayAttackSound(chosenDamageType == DamageType.Arts ? SfxId.GameplayMagicAttack : SfxId.GameplayMeleeAttack);
                MeleeSwipeVisual.Play(transform.position, target.VisualPosition, SlashColor, 1.35f, MeleeStyle.FlameSlash);
                ApplyDamage(target, chosenDamageType);
            }
            else
            {
                // Arts ranged projectile on range tiles (or chosen type at 2x Upgrade)
                PlayAttackSound(chosenDamageType == DamageType.Physical ? SfxId.GameplayRangedPhysicalAttack : SfxId.GameplayMagicAttack);
                CombatProjectileVisual.Fire(
                    ProjectileOrigin,
                    target,
                    chosenDamageType == DamageType.Physical ? SlashColor : ArtsProjectileColor,
                    10f,
                    0.2f,
                    0.12f,
                    burstOnImpact: true,
                    () => ApplyDamage(target, chosenDamageType),
                    style: ProjectileStyle.FireOrb);
            }
        }
        public DamageType GetDamageTypeForTarget(EnemyBase target)
        {
            if (HasSecondUpgrade && target != null)
            {
                return DetermineBestDamageType(target);
            }
            return IsOwnTileTarget(target) ? DamageType.Physical : DamageType.Arts;
        }

        public bool IsOwnTileTarget(EnemyBase target)
        {
            if (target == null) return false;

            // Blocked enemies are engaged directly on the operator's tile
            if (blockedEnemies != null && blockedEnemies.Contains(target))
            {
                return true;
            }

            var gridManager = FindAnyObjectByType<GridManager>();
            if (gridManager != null && deployedCell != null)
            {
                Vector2Int targetPos = gridManager.WorldToGridPosition(target.transform.position);
                Vector2Int localOffset = GetLocalGridOffset(targetPos);
                return localOffset == Vector2Int.zero;
            }

            // Fallback for tests or unassigned gridManager
            Vector3 diff = target.transform.position - transform.position;
            int localX = Mathf.RoundToInt(diff.x);
            int localY = Mathf.RoundToInt(diff.y);
            return localX == 0 && localY == 0;
        }

        private Vector2Int GetLocalGridOffset(Vector2Int targetGridPos)
        {
            if (deployedCell == null) return new Vector2Int(1, 0);

            Vector2Int diff = targetGridPos - deployedCell.GridPosition;
            return Facing switch
            {
                OperatorFacing.Up => new Vector2Int(diff.y, -diff.x),
                OperatorFacing.Left => new Vector2Int(-diff.x, -diff.y),
                OperatorFacing.Down => new Vector2Int(-diff.y, diff.x),
                _ => diff // OperatorFacing.Right
            };
        }

        protected override EnemyBase FindTarget()
        {
            // Priority 1: Attack blocked enemies first
            if (blockedEnemies.Count > 0)
            {
                EnemyBase lowestHP = null;
                int lowestHPValue = int.MaxValue;

                for (int i = 0; i < blockedEnemies.Count; i++)
                {
                    var enemy = blockedEnemies[i];
                    bool isTaunt = enemy != null && enemy.HasTaunt;
                    bool bestIsTaunt = lowestHP != null && lowestHP.HasTaunt;
                    if (enemy != null && !enemy.IsDead && (lowestHP == null || (isTaunt && !bestIsTaunt) || (isTaunt == bestIsTaunt && enemy.CurrentHP < lowestHPValue)))
                    {
                        lowestHP = enemy;
                        lowestHPValue = enemy.CurrentHP;
                    }
                }

                if (lowestHP != null) return lowestHP;
            }

            // Priority 2: Enemies in range pattern (ground and aerial)
            var rangePattern = GetRangePattern();
            if (EnemyManager.Instance != null && deployedCell != null && rangePattern != null)
            {
                var gridManager = FindAnyObjectByType<GridManager>();
                if (gridManager != null)
                {
                    var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, rangePattern, Facing);
                    var rangeCellList = new List<GridCell>(rangeCells);
                    if (deployedCell != null && !rangeCellList.Contains(deployedCell))
                    {
                        rangeCellList.Add(deployedCell);
                    }
                    var candidates = EnemyManager.Instance.GetEnemiesInCells(rangeCellList);

                    EnemyBase lowestHP = null;
                    int lowestHPValue = int.MaxValue;

                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var enemy = candidates[i];
                        bool isTaunt = enemy != null && enemy.HasTaunt;
                        bool bestIsTaunt = lowestHP != null && lowestHP.HasTaunt;
                        if (enemy != null && CanAttackTarget(enemy) && (lowestHP == null || (isTaunt && !bestIsTaunt) || (isTaunt == bestIsTaunt && enemy.CurrentHP < lowestHPValue)))
                        {
                            lowestHP = enemy;
                            lowestHPValue = enemy.CurrentHP;
                        }
                    }

                    return lowestHP;
                }
            }

            return null;
        }

        private void ApplyDamage(EnemyBase target, DamageType damageType)
        {
            if (target == null || target.IsDead) return;

            int mitigation = damageType == DamageType.Physical ? target.CurrentDEF : target.CurrentRES;
            int damage = DamageCalculator.CalculateDamage(CurrentATK, mitigation);
            if (target.TryTakeAttackDamage(damage, damageType, this, canHitAir: true))
            {
                PlayImpactSound();
            }
        }
    }
}
