using UnityEngine;
using System.Collections.Generic;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Chillpath operator — Caster.
    /// Trait: Decrease attack interval after applying freeze.
    /// Ability: Every 4th attack, attacks an additional target in range (or the current target if no additional target is in range).
    /// </summary>
    public class ChillpathOperator : CasterOperator
    {
        public const int AttacksPerBonusTarget = 4;
        public const float FrozenEnemyDamageMultiplier = 1.5f;
        public const int UpgradedShardCount = 3;
        private const float FreezeHasteDuration = 3.5f;
        private const float AttackIntervalMultiplier = 0.75f; // 25% faster attack rate

        private float hasteTimer;
        private int attackCount;
        private float lastAttackInterval;
        private ChillpathSpriteAnimation spriteAnimation;

        private ChillpathSpriteAnimation SpriteAnimation
        {
            get
            {
                if (spriteAnimation == null) spriteAnimation = GetComponentInChildren<ChillpathSpriteAnimation>(true);
                return spriteAnimation;
            }
        }

        public bool IsHasteActive => hasteTimer > 0f;
        public bool HasFrozenDamageUpgrade => currentRarity >= OperatorRarity.Star2;
        public bool HasTripleShardUpgrade => currentRarity >= OperatorRarity.Star3;
        public int AttackCount => attackCount;
        public int BonusAttackCount { get; private set; }
        public EnemyBase LastBonusTarget { get; private set; }
        public EnemyBase LastUpgradeShardTarget { get; private set; }

        public static bool IsFrozenDamageBonusActive
        {
            get
            {
                OperatorManager manager = OperatorManager.Instance;
                if (manager == null) return false;

                var deployedOperators = manager.DeployedOperators;
                for (int i = 0; i < deployedOperators.Count; i++)
                {
                    if (deployedOperators[i] != null &&
                        deployedOperators[i] is ChillpathOperator chillpath &&
                        chillpath.IsDeployed &&
                        chillpath.HasFrozenDamageUpgrade)
                        return true;
                }

                return false;
            }
        }

        protected override void Update()
        {
            if (hasteTimer > 0f)
            {
                hasteTimer -= Time.deltaTime;
            }
            base.Update();
            UpdateAttackWindup();
        }

        public override void ApplyFacingVisuals()
        {
            base.ApplyFacingVisuals();
            SpriteAnimation?.SetMuzzleFacing(facing == OperatorFacing.Right);
        }

        private void UpdateAttackWindup()
        {
            ChillpathSpriteAnimation anim = SpriteAnimation;
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

            float timeUntilCast = lastAttackInterval - attackTimer;
            if (timeUntilCast <= 0f || timeUntilCast > ChillpathSpriteAnimation.CastWindupSeconds)
                return;

            EnemyBase target = FindTarget();
            if (target != null && CanAttackTarget(target))
                anim.BeginAttack(timeUntilCast);
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            attackCount++;
            bool isBonusAttack = (attackCount >= AttacksPerBonusTarget);
            if (isBonusAttack)
            {
                attackCount = 0;
            }

            SpriteAnimation?.Cast();
            base.Attack(target);

            if (HasTripleShardUpgrade)
            {
                List<EnemyBase> shardTargets = GetUpgradeShardTargets(target);
                LastUpgradeShardTarget = shardTargets.Count > 1 ? shardTargets[1] : target;
                for (int shardIndex = 1; shardIndex < UpgradedShardCount; shardIndex++)
                {
                    EnemyBase shardTarget = shardTargets[shardIndex % shardTargets.Count];
                    Vector3 shardOffset = shardTarget == target
                        ? new Vector3(0.12f * shardIndex, 0.12f * shardIndex, 0f)
                        : Vector3.zero;
                    FireAttackProjectile(shardTarget, shardOffset);
                }
            }
            else
            {
                LastUpgradeShardTarget = null;
            }

            if (isBonusAttack)
            {
                EnemyBase additionalTarget = FindAdditionalTarget(target);
                EnemyBase extraTarget = additionalTarget != null ? additionalTarget : target;

                BonusAttackCount++;
                LastBonusTarget = extraTarget;

                Vector3 visualOffset = (extraTarget == target)
                    ? new Vector3(0.12f, 0.12f, 0f)
                    : Vector3.zero;

                PlayAbilitySound("ExtraHit");
                FireAttackProjectile(extraTarget, visualOffset);
            }
        }

        private List<EnemyBase> GetUpgradeShardTargets(EnemyBase primaryTarget)
        {
            var targets = new List<EnemyBase> { primaryTarget };
            if (EnemyManager.Instance == null || DeployedCell == null || Data == null || Data.rangePattern == null)
                return targets;

            GridManager gridManager = FindAnyObjectByType<GridManager>();
            if (gridManager == null) return targets;

            GridCell[] rangeCells = gridManager.GetCellsInRange(DeployedCell.GridPosition, GetRangePattern(), Facing);
            var candidates = EnemyManager.Instance.GetEnemiesInCells(rangeCells, Data.position);
            if (candidates == null) return targets;

            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                EnemyBase candidate = candidates[candidateIndex];
                if (candidate == null || candidate.IsDead || !CanAttackTarget(candidate) || targets.Contains(candidate))
                    continue;

                targets.Add(candidate);
                if (targets.Count >= UpgradedShardCount) break;
            }

            return targets;
        }

        protected override void OnFreezeApplied(EnemyBase target)
        {
            base.OnFreezeApplied(target);
            PlayAbilitySound("Freeze");
            bool wasActive = IsHasteActive;
            hasteTimer = FreezeHasteDuration;
            if (!wasActive)
            {
                FloatingCombatNumber.ShowText(transform.position + Vector3.up * 0.6f, "HASTE!", Color.cyan);
            }
        }

        public override float GetAttackInterval()
        {
            float baseInterval = base.GetAttackInterval();
            lastAttackInterval = IsHasteActive ? baseInterval * AttackIntervalMultiplier : baseInterval;
            return lastAttackInterval;
        }

        protected override void FireAttackProjectile(EnemyBase target, Vector3 startOffset = default)
        {
            Transform muzzle = SpriteAnimation != null ? SpriteAnimation.Muzzle : null;
            Vector3 start = muzzle != null ? muzzle.position : transform.position;
            base.FireAttackProjectile(target, start - ProjectileOrigin + startOffset);
        }

        protected override void OnDeployed()
        {
            base.OnDeployed();
            attackCount = 0;
            BonusAttackCount = 0;
            LastBonusTarget = null;
            LastUpgradeShardTarget = null;
            hasteTimer = 0f;
        }

        protected override void OnRetreated()
        {
            SpriteAnimation?.CancelAttack();
            hasteTimer = 0f;
            attackCount = 0;
            BonusAttackCount = 0;
            LastBonusTarget = null;
            LastUpgradeShardTarget = null;
            base.OnRetreated();
        }

        public override void CollectStatEffects(System.Collections.Generic.List<StatEffect> effects)
        {
            base.CollectStatEffects(effects);

            if (IsHasteActive)
                effects.Add(new StatEffect("HASTE", $"Interval x{AttackIntervalMultiplier:0.##}  ({hasteTimer:0.#}s)", "ATK SPD"));
        }
    }
}
