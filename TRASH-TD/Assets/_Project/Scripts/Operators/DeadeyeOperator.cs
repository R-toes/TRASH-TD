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
    /// Trait: Increases damage by 20% against flying enemies, but delays attack (increases attack interval by 20%)
    /// when preparing to attack air units. Returns to original attack interval when no aerial enemies are in range.
    /// Attack animation (DeadeyeSpriteAnimation): the wind-up starts just before the next shot is due, so the shot
    /// frame lines up with the real shot; attack timing and damage are unchanged.
    /// </summary>
    public class DeadeyeOperator : SniperOperator
    {
        private const float AirDamageMultiplier = 1.20f;
        private const float AirIntervalMultiplier = 1.20f;

        private GridManager gridManager;
        private GridCell[] cachedRangeCells;
        private DeadeyeSpriteAnimation spriteAnimation;
        private float lastAttackInterval; // from this frame's GetAttackInterval, so the air check isn't run twice

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
            lastAttackInterval = HasAirTargetInRange() ? baseInterval * AirIntervalMultiplier : baseInterval;
            return lastAttackInterval;
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            bool isAir = target.MovementType == EnemyMovementType.Air;

            SpriteAnimation?.ShotFired();
            PlayAttackSound();

            Color projectileColor = isAir ? new Color(1f, 0.5f, 0.1f) : new Color(1f, 0.82f, 0.32f);

            CombatProjectileVisual.Fire(
                ProjectileOrigin,
                target,
                projectileColor,
                14f,
                0.16f,
                0.09f,
                false,
                () => ApplyDeadeyeDamage(target, isAir),
                ProjectileStyle.Bullet);
        }

        private void ApplyDeadeyeDamage(EnemyBase target, bool isAir)
        {
            if (target == null || target.IsDead) return;

            int atk = isAir ? Mathf.RoundToInt(CurrentATK * AirDamageMultiplier) : CurrentATK;
            int damage = DamageCalculator.CalculateDamage(atk, GetTargetMitigation(target));
            target.TryTakeAttackDamage(damage, data.damageType, this);
        }
    }
}
