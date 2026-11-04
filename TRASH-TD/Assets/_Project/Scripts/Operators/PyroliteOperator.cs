using System.Collections.Generic;
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
        private OperatorData cachedRangeData;
        private OperatorRarity cachedRangeRarity;
        private Vector2Int[] cachedUpgradedRangePattern;

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

        public override Vector2Int[] GetRangePattern()
        {
            if (data == null || data.rangePattern == null || currentRarity < OperatorRarity.Star2)
                return data != null ? data.rangePattern : null;

            if (cachedRangeData != data || cachedRangeRarity != currentRarity || cachedUpgradedRangePattern == null)
            {
                cachedRangeData = data;
                cachedRangeRarity = currentRarity;
                cachedUpgradedRangePattern = BuildUpgradedRangePattern(data.rangePattern);
            }

            return cachedUpgradedRangePattern;
        }

        public static Vector2Int[] GetRangePatternForRarity(OperatorData operatorData, OperatorRarity rarity)
        {
            if (operatorData == null || operatorData.rangePattern == null || rarity < OperatorRarity.Star2)
                return operatorData != null ? operatorData.rangePattern : null;

            return BuildUpgradedRangePattern(operatorData.rangePattern);
        }

        private static Vector2Int[] BuildUpgradedRangePattern(Vector2Int[] basePattern)
        {
            int furthestForward = int.MinValue;
            for (int i = 0; i < basePattern.Length; i++)
            {
                Vector2Int offset = basePattern[i];
                if (offset.y == 0) furthestForward = Mathf.Max(furthestForward, offset.x);
            }

            if (furthestForward == int.MinValue)
            {
                for (int i = 0; i < basePattern.Length; i++)
                    furthestForward = Mathf.Max(furthestForward, basePattern[i].x);
            }

            var extendedPattern = new List<Vector2Int>(basePattern);
            AddRangeTileIfMissing(extendedPattern, new Vector2Int(furthestForward, 1));
            AddRangeTileIfMissing(extendedPattern, new Vector2Int(furthestForward, -1));

            return extendedPattern.ToArray();
        }

        private static void AddRangeTileIfMissing(List<Vector2Int> pattern, Vector2Int tile)
        {
            for (int i = 0; i < pattern.Count; i++)
                if (pattern[i] == tile) return;
            pattern.Add(tile);
        }

        protected override int ArtsImpactCount => currentRarity >= OperatorRarity.Star3 ? 2 : 1;

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
            Vector2Int[] rangePattern = GetRangePattern();
            if (EnemyManager.Instance == null || deployedCell == null || data == null || rangePattern == null)
                return 0;

            var gridManager = FindAnyObjectByType<GridManager>();
            if (gridManager == null) return 0;

            var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, rangePattern, Facing);
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
