using UnityEngine;
using TrashTD.Combat;
using TrashTD.Data;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Robodrone: a submachine-gun drone. Sprays low-damage physical plasma rounds (one every attackInterval) at the
    /// operator nearest to it within 2.5 tiles, crawling while it fires. A magazine lasts about 5 s of fire, then it
    /// reloads for 2 s at half speed. Rounds left in the magazine carry over to the next target.
    /// The per-round ATK on Enemy_Flyer_Robodrone.asset is low, so DEF soaks most of each round.
    /// </summary>
    public sealed class RobodroneEnemy : AirCombatEnemy
    {
        private const float RangeInTiles = 2.5f;
        // The magazine holds this much fire at the asset's attackInterval (5 s / 0.15 s = 33 rounds).
        private const float BurstSeconds = 5f;
        private const float ReloadSeconds = 2f;
        private const float FiringSpeedMultiplier = 0.2f;
        private const float ReloadSpeedMultiplier = 0.5f;
        private const float RoundSpeed = 12f;

        private int magazineSize;
        private int roundsLeft;
        private float reloadTimer;

        public bool IsReloading => reloadTimer > 0f;

        public override void Initialize(EnemyData enemyData, int difficultyLevel)
        {
            base.Initialize(enemyData, difficultyLevel);
            float interval = enemyData != null ? enemyData.attackInterval : 0f;
            magazineSize = interval > 0f ? Mathf.Max(1, Mathf.RoundToInt(BurstSeconds / interval)) : 1;
            roundsLeft = magazineSize;
            reloadTimer = 0f;
        }

        protected override void Update()
        {
            base.Update();
            if (IsDead || reloadTimer <= 0f || IsFrozen || IsStunned) return;

            reloadTimer = Mathf.Max(0f, reloadTimer - Time.deltaTime);
            if (reloadTimer <= 0f) roundsLeft = magazineSize;
        }

        protected override bool CanAttack => reloadTimer <= 0f && roundsLeft > 0;

        // It never stops for a shot; it crawls at FiringSpeedMultiplier while a target is in range.
        protected override float GetAttackPause(float attackInterval) => 0f;

        protected override float EngagedSpeed => FiringSpeedMultiplier;

        protected override void MoveAlongPath()
        {
            if (reloadTimer > 0f)
            {
                MoveAlongPathBy(currentMoveSpeed * ReloadSpeedMultiplier * Time.deltaTime);
                return;
            }

            base.MoveAlongPath();
        }

        protected override bool IsTargetable(OperatorBase op) => true;

        protected override int GetTargetPriority(OperatorBase op) => 0;

        protected override bool IsTargetInRange(OperatorBase op) => IsWithinCircularRange(op, RangeInTiles);

        /// <summary>
        /// The operator nearest to the drone, re-picked every frame so it swings to whoever is closest.
        /// A taunting operator (Mossmo) next to it still wins, as for every enemy.
        /// </summary>
        protected override OperatorBase SelectTarget()
        {
            if (OperatorManager.Instance == null) return null;

            OperatorBase nearest = null;
            float nearestSqrDistance = float.PositiveInfinity;
            var operators = OperatorManager.Instance.DeployedOperators;
            for (int i = 0; i < operators.Count; i++)
            {
                OperatorBase op = operators[i];
                if (op == null || !op.IsDeployed || !op.CanBeTargetedByEnemy(this) || !IsTargetInRange(op)) continue;
                if (op.ShouldTauntAttacker(transform.position, GridManagerCache)) return op;

                Vector3 offset = op.transform.position - transform.position;
                offset.z = 0f;
                float sqrDistance = offset.sqrMagnitude;
                if (sqrDistance < nearestSqrDistance)
                {
                    nearest = op;
                    nearestSqrDistance = sqrDistance;
                }
            }

            return nearest;
        }

        protected override void PerformAirAttack(OperatorBase target)
        {
            FireRound(target);
            roundsLeft--;
            if (roundsLeft <= 0) reloadTimer = ReloadSeconds;
        }

        private void FireRound(OperatorBase target)
        {
            Color color = Data != null ? Data.attackProjectileColor : new Color(1f, 0.58f, 0.16f, 1f);
            CombatProjectileVisual.Fire(
                ProjectileOrigin,
                target.transform.position,
                color,
                RoundSpeed,
                0.1f,
                0.05f,
                false,
                () =>
                {
                    if (target == null || !target.IsDeployed || Data == null) return;
                    PlayImpactSound();
                    target.TryTakeAttackDamage(CurrentATK, Data.damageType, this);
                },
                ProjectileStyle.Plasma);
        }
    }
}
