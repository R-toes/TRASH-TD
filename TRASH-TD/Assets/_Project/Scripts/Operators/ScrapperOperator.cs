using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Scrapper operator — Guard.
    /// Trait:
    /// - Base (1★): When attacking, has a 20% chance to Stun the target for 2 seconds.
    /// - 1st upgrade (2★): Stun chance increases to 30%.
    /// - 2nd upgrade (3★): Stun chance increases to 50% and attacks on the first tile become AoE.
    /// Uses 64x64 sledgehammer attack animation with bottom-right 32x32 pivot offset (0.75, 0.25)
    /// to keep Scrapper centered on the deployed tile while the hammer swings through the forward tiles.
    /// Direct melee damage is applied instantly on swing without any projectile.
    /// </summary>
    public class ScrapperOperator : GuardOperator
    {
        public const float BaseStunChance = 0.20f;
        public const float Star2StunChance = 0.30f;
        public const float Star3StunChance = 0.50f;
        public const float StunDuration = 2.0f;

        public float CurrentStunChance => GetStunChance(currentRarity);
        public bool HasFirstTileAoeUpgrade => currentRarity >= OperatorRarity.Star3;

        public static float GetStunChance(OperatorRarity rarity)
        {
            if (rarity >= OperatorRarity.Star3) return Star3StunChance;
            if (rarity >= OperatorRarity.Star2) return Star2StunChance;
            return BaseStunChance;
        }

        protected override MeleeStyle MeleeSlashStyle => MeleeStyle.ScrapSlash;
        protected override float MeleeSlashSize => 1.3f;

        private ScrapperSpriteAnimation spriteAnimation;
        private GridManager gridManager;

        public ScrapperSpriteAnimation SpriteAnimation
        {
            get
            {
                if (spriteAnimation == null)
                {
                    spriteAnimation = GetComponentInChildren<ScrapperSpriteAnimation>(true);
                    if (spriteAnimation == null)
                    {
                        spriteAnimation = gameObject.AddComponent<ScrapperSpriteAnimation>();
                    }
                }
                return spriteAnimation;
            }
        }

        private GridManager GetGridManager()
        {
            if (gridManager == null)
            {
                gridManager = FindAnyObjectByType<GridManager>();
            }
            return gridManager;
        }

        public GridCell GetFirstForwardCell()
        {
            var gm = GetGridManager();
            if (gm == null || deployedCell == null) return null;
            Vector2Int forwardOffset = Facing == OperatorFacing.Right ? Vector2Int.right : Vector2Int.left;
            return gm.GetCell(deployedCell.GridPosition + forwardOffset);
        }

        private void Awake()
        {
            // If the GameObject has the generic OperatorSpriteAnimation, disable it so ScrapperSpriteAnimation controls playback
            var genericAnim = GetComponent<OperatorSpriteAnimation>();
            if (genericAnim != null && genericAnim.enabled)
            {
                genericAnim.enabled = false;
            }

            EnsureScrapperAnimation();
        }

        protected override void OnDeployed()
        {
            base.OnDeployed();
            EnsureScrapperAnimation();
        }

        protected override void OnRetreated()
        {
            SpriteAnimation?.CancelAttack();
            base.OnRetreated();
        }

        private void EnsureScrapperAnimation()
        {
            var anim = SpriteAnimation;
            if (anim != null && !anim.enabled)
            {
                anim.enabled = true;
            }
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed || isStunned || isBound) return;

            HasFoughtInWave = true;
            DeployedDuringCurrentPreparation = false;

            // Trigger the 64x64 sledgehammer attack animation
            SpriteAnimation?.PlayAttack();

            // Direct melee strike audio and visual swipe (no projectile!)
            PlayAttackSound();
            MeleeSwipeVisual.Play(transform.position, target.VisualPosition, new Color(1f, 0.78f, 0.3f, 1f), MeleeSlashSize, MeleeSlashStyle);

            GridCell firstCell = GetFirstForwardCell();
            bool isFirstTileAttack = false;
            if (firstCell != null)
            {
                var gm = GetGridManager();
                if (gm != null)
                {
                    Vector2Int targetPos = gm.WorldToGridPosition(target.transform.position);
                    if (targetPos == firstCell.GridPosition)
                    {
                        isFirstTileAttack = true;
                    }
                }
            }

            float stunChance = CurrentStunChance;
            DamageType dmgType = data != null ? data.damageType : DamageType.Physical;

            if (HasFirstTileAoeUpgrade && isFirstTileAttack && firstCell != null && EnemyManager.Instance != null)
            {
                // 2x Upgraded (3★): attacks from the first tile become AoE (hitting all enemies on that tile)
                List<EnemyBase> enemiesOnTile = EnemyManager.Instance.GetEnemiesInCells(new[] { firstCell }, data != null ? data.position : OperatorPosition.Melee);
                if (enemiesOnTile != null && enemiesOnTile.Count > 0)
                {
                    if (!enemiesOnTile.Contains(target))
                    {
                        enemiesOnTile.Add(target);
                    }

                    bool hitAny = false;
                    bool stunnedAny = false;
                    for (int i = 0; i < enemiesOnTile.Count; i++)
                    {
                        EnemyBase enemy = enemiesOnTile[i];
                        if (enemy == null || enemy.IsDead) continue;

                        int damage = DamageCalculator.CalculateDamage(CurrentATK, GetTargetMitigation(enemy));
                        if (enemy.TryTakeAttackDamage(damage, dmgType, this))
                        {
                            hitAny = true;
                        }

                        if (Random.value < stunChance && enemy != null && !enemy.IsDead)
                        {
                            enemy.ApplyStun(StunDuration);
                            stunnedAny = true;
                        }
                    }

                    if (hitAny) PlayImpactSound();
                    if (stunnedAny) PlayAbilitySound();
                    return;
                }
            }

            // Deal direct attack damage without firing any projectile
            int singleDamage = DamageCalculator.CalculateDamage(CurrentATK, GetTargetMitigation(target));
            if (target.TryTakeAttackDamage(singleDamage, dmgType, this))
            {
                PlayImpactSound();
            }

            if (Random.value < stunChance && target != null && !target.IsDead)
            {
                target.ApplyStun(StunDuration);
                PlayAbilitySound();
            }
        }
    }
}
