using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Deadeye operator — Sniper.
    /// Trait: Increases damage by 20% against flying enemies. At base rarity, attacking air also increases the
    /// attack interval by 20%; the first upgrade removes that penalty and adds a stacking single-target damage bonus.
    /// Attack animation (DeadeyeSpriteAnimation): the wind-up starts just before the next shot is due, so the shot
    /// frame lines up with the real shot; attack timing and damage are unchanged.
    /// </summary>
    public class DeadeyeOperator : SniperOperator
    {
        private const float AirDamageMultiplier = 1.20f;
        private const float AirIntervalMultiplier = 1.20f;
        public const float TargetHitDamageBonus = 0.02f;
        public const float MaximumTargetDamageBonus = 1f;
        public const float KillShotDamageBonus = 1f;
        private const int TargetDamageBonusHitCap = 50;

        private GridManager gridManager;
        private GridCell[] cachedRangeCells;
        private DeadeyeSpriteAnimation spriteAnimation;
        private float lastAttackInterval; // from this frame's GetAttackInterval, so the air check isn't run twice
        private EnemyBase rampTarget;
        private int landedHitsOnRampTarget;
        private bool hasKillShotBonus;

        public bool HasTargetDamageUpgrade => currentRarity >= OperatorRarity.Star2;
        public bool HasKillShotUpgrade => currentRarity >= OperatorRarity.Star3;
        public int LandedHitsOnRampTarget => landedHitsOnRampTarget;
        public bool HasPendingKillShotBonus => hasKillShotBonus;

        public static float GetAirAttackIntervalMultiplier(bool hasAirTarget, OperatorRarity rarity) =>
            hasAirTarget && rarity < OperatorRarity.Star2 ? AirIntervalMultiplier : 1f;

        public override void Initialize(OperatorData operatorData, OperatorRarity rarity, OperatorFacing operatorFacing = OperatorFacing.Right)
        {
            base.Initialize(operatorData, rarity, operatorFacing);
            rampTarget = null;
            landedHitsOnRampTarget = 0;
            hasKillShotBonus = false;
        }

        private GridManager GetGridManager()
        {
            if (gridManager == null)
            {
                gridManager = FindAnyObjectByType<GridManager>();
            }
            return gridManager;
        }

        private DeadeyeSpriteAnimation SpriteAnimation
        {
            get
            {
                if (spriteAnimation == null) spriteAnimation = GetComponentInChildren<DeadeyeSpriteAnimation>(true);
                return spriteAnimation;
            }
        }

        protected override void OnDeployed()
        {
            base.OnDeployed();
            RefreshRangeCells();
        }

        protected override void OnRetreated()
        {
            cachedRangeCells = null;
            rampTarget = null;
            landedHitsOnRampTarget = 0;
            hasKillShotBonus = false;
            SpriteAnimation?.CancelAttack();
            base.OnRetreated();
        }

        protected override void Update()
        {
            base.Update();
            StartAttackWindup();
        }

        /// <summary>
        /// Starts the attack animation when the next shot is due within the wind-up time and the last target can
        /// still be shot, so the shot frame shows as the shot fires.
        /// </summary>
        private void StartAttackWindup()
        {
            DeadeyeSpriteAnimation anim = SpriteAnimation;
            if (anim == null || !anim.HasAttackAnimation || anim.IsAttacking) return;
            if (!isDeployed || IsCombatPaused) return;

            float timeUntilShot = lastAttackInterval - attackTimer;
            if (timeUntilShot <= 0f || timeUntilShot > DeadeyeSpriteAnimation.WindupTime) return;
            if (currentTarget == null || !CanAttackTarget(currentTarget) || !IsOnRangeTile(currentTarget)) return;

            anim.BeginAttack(timeUntilShot);
        }

        /// <summary>
        /// Whether the enemy stands on one of Deadeye's range tiles. Only checked during the short wind-up window;
        /// a miss (e.g. an enemy held between two tiles) just means the animation starts at the shot frame instead.
        /// </summary>
        private bool IsOnRangeTile(EnemyBase enemy)
        {
            GridManager gm = GetGridManager();
            if (gm == null || cachedRangeCells == null) return false;

            Vector2Int position = gm.WorldToGridPosition(enemy.transform.position);
            for (int i = 0; i < cachedRangeCells.Length; i++)
            {
                if (cachedRangeCells[i] != null && cachedRangeCells[i].GridPosition == position) return true;
            }
            return false;
        }

        public override void ApplyFacingVisuals()
        {
            base.ApplyFacingVisuals();
            RefreshRangeCells();
        }

        private void RefreshRangeCells()
        {
            var gm = GetGridManager();
            if (gm != null && deployedCell != null && data != null && data.rangePattern != null)
            {
                cachedRangeCells = gm.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
            }
            else
            {
                cachedRangeCells = null;
            }
        }

        public bool HasAirTargetInRange()
        {
            if (!isDeployed || EnemyManager.Instance == null || data == null)
                return false;

            if (cachedRangeCells == null || cachedRangeCells.Length == 0)
            {
                RefreshRangeCells();
                if (cachedRangeCells == null || cachedRangeCells.Length == 0)
                    return false;
            }

            var candidates = EnemyManager.Instance.GetEnemiesInCells(cachedRangeCells, data.position);
            if (candidates == null || candidates.Count == 0) return false;

            for (int i = 0; i < candidates.Count; i++)
            {
                var enemy = candidates[i];
                if (enemy != null && !enemy.IsDead && enemy.MovementType == EnemyMovementType.Air)
                {
                    return true;
                }
            }

            return false;
        }

        public override float GetAttackInterval()
        {
            float baseInterval = base.GetAttackInterval();
            lastAttackInterval = baseInterval * GetAirAttackIntervalMultiplier(HasAirTargetInRange(), currentRarity);
            return lastAttackInterval;
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            SelectRampTarget(target);

            bool isAir = target.MovementType == EnemyMovementType.Air;
            int hitCountForShot = landedHitsOnRampTarget;
            bool usesKillShotBonus = ConsumeKillShotBonus();

            SpriteAnimation?.ShotFired();
            PlayAttackSound();
            FireLightning(target, isAir, hitCountForShot, usesKillShotBonus);
        }

        private void SelectRampTarget(EnemyBase target)
        {
            if (rampTarget == target) return;

            rampTarget = target;
            landedHitsOnRampTarget = 0;
        }

        private bool ConsumeKillShotBonus()
        {
            bool usesBonus = HasKillShotUpgrade && hasKillShotBonus;
            if (usesBonus)
                hasKillShotBonus = false;
            return usesBonus;
        }

        private void FireLightning(EnemyBase target, bool isAir, int hitCountForShot, bool usesKillShotBonus)
        {
            Color projectileColor = isAir ? new Color(1f, 0.5f, 0.1f) : new Color(1f, 0.82f, 0.32f);

            CombatProjectileVisual.Fire(
                SpriteAnimation != null ? SpriteAnimation.FirePoint.position : ProjectileOrigin,
                target,
                projectileColor,
                14f,
                0.16f,
                0.09f,
                false,
                () => ApplyDeadeyeDamage(target, isAir, hitCountForShot, usesKillShotBonus),
                ProjectileStyle.Pea);
        }

        private void ApplyDeadeyeDamage(EnemyBase target, bool isAir, int hitCountForShot, bool usesKillShotBonus)
        {
            if (target == null || target.IsDead) return;

            int atk = CalculateAttackPower(CurrentATK, hitCountForShot, HasTargetDamageUpgrade, isAir, usesKillShotBonus);
            int damage = DamageCalculator.CalculateDamage(atk, GetTargetMitigation(target));
            int previousHP = target.CurrentHP;
            if (!target.TryTakeAttackDamage(damage, data.damageType, this) || target.CurrentHP >= previousHP)
                return;

            if (usesKillShotBonus)
                FloatingCombatNumber.ShowText(target.VisualPosition, "CRIT!", new Color(1f, 0.82f, 0.25f));

            if (target.IsDead)
            {
                if (HasKillShotUpgrade)
                    hasKillShotBonus = true;
            }
            else if (HasTargetDamageUpgrade && rampTarget == target)
            {
                landedHitsOnRampTarget = Mathf.Max(
                    landedHitsOnRampTarget,
                    Mathf.Min(hitCountForShot + 1, TargetDamageBonusHitCap));
            }
        }

        public static int CalculateAttackPower(int attack, int landedHitsOnTarget, bool hasTargetDamageUpgrade,
            bool isAir, bool usesKillShotBonus)
        {
            float bonus = isAir ? AirDamageMultiplier - 1f : 0f;
            if (hasTargetDamageUpgrade)
                bonus += Mathf.Min(
                    Mathf.Clamp(landedHitsOnTarget + 1, 1, TargetDamageBonusHitCap) * TargetHitDamageBonus,
                    MaximumTargetDamageBonus);
            if (usesKillShotBonus)
                bonus += KillShotDamageBonus;

            return Mathf.RoundToInt(attack * (1f + bonus));
        }
    }
}
