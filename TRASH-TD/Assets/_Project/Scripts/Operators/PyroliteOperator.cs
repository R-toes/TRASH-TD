using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Pyrolite operator — Caster.
    /// Trait: Increase damage by 3% per enemy in range (stacks up to 5 times).
    /// </summary>
    public class PyroliteOperator : CasterOperator
    {
        private const float DamageBonusPerEnemy = 0.03f;
        private const int MaxBonusStacks = 5;
        // Relative to the centered 64x32 attack frame pivot.
        private const float MuzzleOffsetXPixels = 25f;
        private const float MuzzleOffsetYPixels = -2f;

        private PyroliteSpriteAnimation spriteAnimation;
        private float lastAttackInterval;

        private PyroliteSpriteAnimation SpriteAnimation
        {
            get
            {
                if (spriteAnimation == null) spriteAnimation = GetComponentInChildren<PyroliteSpriteAnimation>(true);
                return spriteAnimation;
            }
        }

        public override float GetAttackInterval()
        {
            lastAttackInterval = base.GetAttackInterval();
            return lastAttackInterval;
        }

        protected override void OnRetreated()
        {
            SpriteAnimation?.CancelAttack();
            base.OnRetreated();
        }

        protected override void Update()
        {
            base.Update();
            UpdateAttackWindup();
        }

        private void UpdateAttackWindup()
        {
            PyroliteSpriteAnimation anim = SpriteAnimation;
            if (anim == null || !anim.HasAttackAnimation) return;

            if (!isDeployed)
            {
                anim.CancelAttack();
                return;
            }

            if (IsCombatPaused || isStunned || isBound)
            {
                anim.CancelWindup();
                return;
            }

            if (anim.IsWindingUp)
            {
                EnemyBase windupTarget = FindTarget();
                if (windupTarget == null || !CanAttackTarget(windupTarget))
                    anim.CancelWindup();
                return;
            }

            if (anim.IsRecovering) return;

            float timeUntilShot = lastAttackInterval - attackTimer;
            if (timeUntilShot <= 0f || timeUntilShot > PyroliteSpriteAnimation.AttackWindupSeconds)
                return;

            EnemyBase target = FindTarget();
            if (target != null && CanAttackTarget(target))
                anim.BeginAttack(timeUntilShot);
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            SpriteAnimation?.Fire();
            base.Attack(target);
        }

        public Vector3 GetAttackMuzzlePosition()
        {
            Vector3 scale = transform.lossyScale;
            float direction = facing == OperatorFacing.Right ? 1f : -1f;
            return transform.position + new Vector3(
                direction * (MuzzleOffsetXPixels / 32f) * scale.x,
                (MuzzleOffsetYPixels / 32f) * scale.y,
                0f);
        }

        protected override void FireAttackProjectile(EnemyBase target, Vector3 startOffset = default)
        {
            base.FireAttackProjectile(target, GetAttackMuzzlePosition() - ProjectileOrigin);
        }

        public int GetEnemiesInRangeCount()
        {
            if (EnemyManager.Instance == null || deployedCell == null || data == null || data.rangePattern == null)
                return 0;

            var gridManager = FindAnyObjectByType<GridManager>();
            if (gridManager == null) return 0;

            var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
            var enemies = EnemyManager.Instance.GetEnemiesInCells(rangeCells, data.position);
            if (enemies == null) return 0;

            int count = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i];
                if (enemy != null && !enemy.IsDead)
                {
                    count++;
                }
            }
            return count;
        }

        protected override float GetDamageMultiplier()
        {
            int enemyCount = GetEnemiesInRangeCount();
            int stacks = Mathf.Clamp(enemyCount, 0, MaxBonusStacks);
            return 1f + (stacks * DamageBonusPerEnemy);
        }
    }
}
