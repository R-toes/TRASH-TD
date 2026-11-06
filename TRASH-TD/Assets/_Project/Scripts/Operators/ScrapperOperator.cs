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
    /// - 2nd upgrade (3★): Stun chance increases to 40% and has a second instance (shockwave AoE)
    ///   dealing 50% damage to all enemies on the adjacent tile (including the target). Stun chance rolls on both instances.
    /// Uses 64x64 sledgehammer attack animation with bottom-right 32x32 pivot offset (0.75, 0.25)
    /// to keep Scrapper centered on the deployed tile while the hammer swings through the forward tiles.
    /// Direct melee damage is applied instantly on swing without any projectile.
    /// </summary>
    public class ScrapperOperator : GuardOperator
    {
        public const float BaseStunChance = 0.20f;
        public const float Star2StunChance = 0.30f;
        public const float Star3StunChance = 0.40f;
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
            GridCell[] cells = gm.GetCellsInRange(deployedCell.GridPosition, new[] { new Vector2Int(1, 0) }, Facing);
            return cells != null && cells.Length > 0 ? cells[0] : null;
        }

        public bool IsTargetOnFirstTile(EnemyBase target, GridCell firstCell)
        {
            if (target == null || firstCell == null) return false;
            var gm = GetGridManager();
            if (gm == null) return false;

            Vector2Int targetGridPos = gm.WorldToGridPosition(target.transform.position);
            if (targetGridPos == firstCell.GridPosition) return true;
            if (Vector2.Distance(target.transform.position, firstCell.WorldPosition) <= 0.6f) return true;

            return false;
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
            bool isFirstTileAttack = IsTargetOnFirstTile(target, firstCell);

            float stunChance = CurrentStunChance;
            DamageType dmgType = data != null ? data.damageType : DamageType.Physical;

            // 1. Primary strike: deals direct attack damage to target
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

            // 2. Second instance (2x Upgraded, attacking at the 1 tile close to it):
            // Shockwave AoE dealing 50% damage to all enemies on that tile (including the target), rolling stun on each
            if (HasFirstTileAoeUpgrade && isFirstTileAttack && firstCell != null && EnemyManager.Instance != null)
            {
                List<EnemyBase> enemiesOnTile = EnemyManager.Instance.GetEnemiesInCells(new[] { firstCell }, data != null ? data.position : OperatorPosition.Melee);
                if (enemiesOnTile == null) enemiesOnTile = new List<EnemyBase>();
                if (target != null && !target.IsDead && !enemiesOnTile.Contains(target))
                {
                    enemiesOnTile.Add(target);
                }

                int shockwaveAtk = Mathf.Max(1, Mathf.RoundToInt(CurrentATK * 0.5f));
                bool stunnedAny = false;

                for (int i = 0; i < enemiesOnTile.Count; i++)
                {
                    EnemyBase enemy = enemiesOnTile[i];
                    if (enemy == null || enemy.IsDead) continue;

                    int aoeDamage = DamageCalculator.CalculateDamage(shockwaveAtk, GetTargetMitigation(enemy));
                    enemy.TryTakeAttackDamage(aoeDamage, dmgType, this);

                    if (Random.value < stunChance && enemy != null && !enemy.IsDead)
                    {
                        enemy.ApplyStun(StunDuration);
                        stunnedAny = true;
                    }
                }

                if (stunnedAny)
                {
                    PlayAbilitySound();
                }
            }
        }
    }
}
