using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.GameLoop;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Enemies
{
    /// <summary>
    /// THE ERADICATOR — heavy Tank (block weight 2, drawn at 2x like Sand Hulk).
    /// - Melee: when blocked, or when a ground (non-High Ground) operator stands on its tile or one of the
    ///   4 adjacent tiles, it swings at it every attack interval. The first hit on each operator stuns it
    ///   for 2 s. Melee replaces rockets.
    /// - Rockets: otherwise, every 1.5 attack intervals it stops briefly and fires a rocket at the
    ///   lowest-HP operator within 3 tiles (taunt first). The rocket blasts a 1.75-tile circle around the target.
    /// - Every 4th rocket is a heavy warhead: its own projectile, a "HEAVY!" label and 1.5x damage. On Stage 4
    ///   it also adds 1 heat stack to every operator it hits.
    /// </summary>
    public class EradicatorEnemy : TankEnemy
    {
        public const float RocketIntervalMultiplier = 1.5f;
        public const int RocketRangeTiles = 3;
        public const float BlastRadiusTiles = 1.75f; // Same as Basurocket, a bit bigger than a caster splash (1.5)
        public const int HeavyRocketEvery = 4;
        public const float HeavyRocketDamageMultiplier = 1.5f;
        public const float MeleeStunDuration = 2f;
        public const float AttackStopDuration = 0.75f;
        private const float RocketSpeed = 6.5f;
        private const float HeavyRocketSpeed = 5f;

        private static readonly Vector2Int[] MeleeOffsets =
        {
            new Vector2Int(0, 0),
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
        };

        // Crimson and molten red, so its blasts never read as Basurocket's yellow-orange junk rockets
        private static readonly Color RocketBlastColor = new Color(0.8f, 0.08f, 0.14f, 1f);
        private static readonly Color HeavyRocketColor = new Color(1f, 0.32f, 0.05f, 1f);

        private readonly HashSet<OperatorBase> stunnedOperators = new HashSet<OperatorBase>();
        private float meleeTimer;
        private float rocketTimer;
        private float attackStopTimer;
        private int rocketsFired;

        public int RocketsFired => rocketsFired;
        public bool NextRocketIsHeavy => (rocketsFired + 1) % HeavyRocketEvery == 0;

        public override void Initialize(EnemyData enemyData, int difficultyLevel)
        {
            base.Initialize(enemyData, difficultyLevel);
            stunnedOperators.Clear();
            meleeTimer = GetMeleeInterval();   // Ready to swing on first contact, like other ground melee
            rocketTimer = 0f;                  // Rockets keep their wind-up, like enemy casters
            attackStopTimer = 0f;
            rocketsFired = 0;
        }

        protected override void Update()
        {
            float deltaTime = Time.deltaTime;
            if (!TickStatusEffects(deltaTime)) return;

            Step(deltaTime);
        }

        /// <summary>One frame of the Eradicator's behaviour after status effects (public for tests).</summary>
        public void Step(float deltaTime)
        {
            if (IsDead) return;

            float meleeInterval = GetMeleeInterval();
            float rocketInterval = GetRocketInterval();
            meleeTimer = Mathf.Min(meleeTimer + deltaTime, meleeInterval);
            rocketTimer = Mathf.Min(rocketTimer + deltaTime, rocketInterval);
            if (attackStopTimer > 0f)
                attackStopTimer = Mathf.Max(0f, attackStopTimer - deltaTime);

            OperatorBase meleeTarget = FindMeleeTarget();
            if (meleeTarget != null)
            {
                if (meleeTimer >= meleeInterval)
                {
                    meleeTimer = 0f;
                    attackStopTimer = AttackStopDuration;
                    MeleeAttack(meleeTarget);
                }
            }
            else if (rocketTimer >= rocketInterval)
            {
                OperatorBase rocketTarget = FindRocketTarget();
                if (rocketTarget != null)
                {
                    rocketTimer = 0f;
                    attackStopTimer = AttackStopDuration;
                    FireRocket(rocketTarget);
                }
            }

            if (isBlocked)
                HoldBlockedPosition(deltaTime);
            else if (attackStopTimer <= 0f)
                MoveAlongPathBy(currentMoveSpeed * deltaTime);
        }

        private float GetMeleeInterval() => GetEffectiveAttackInterval();

        private float GetRocketInterval() => GetEffectiveAttackInterval() * RocketIntervalMultiplier;

        // ========================
        // Melee
        // ========================

        public OperatorBase FindMeleeTarget()
        {
            if (isBlocked && blockingOperator != null && blockingOperator.IsDeployed && blockingOperator.CurrentHP > 0)
                return blockingOperator;

            if (CurrentGrid == null || OperatorManager.Instance == null) return null;

            Vector2Int selfCell = CurrentGrid.WorldToGridPosition(transform.position);
            OperatorBase tauntTarget = null;
            OperatorBase weakest = null;
            var deployed = OperatorManager.Instance.DeployedOperators;
            for (int i = 0; i < deployed.Count; i++)
            {
                OperatorBase op = deployed[i];
                if (!IsGroundOperator(op) || !op.IsTargetable) continue;

                Vector2Int offset = op.DeployedCell.GridPosition - selfCell;
                if (System.Array.IndexOf(MeleeOffsets, offset) < 0) continue;

                if (tauntTarget == null && op.ShouldTauntAttacker(transform.position, CurrentGrid))
                    tauntTarget = op;
                if (weakest == null || op.CurrentHP < weakest.CurrentHP)
                    weakest = op;
            }

            return tauntTarget != null ? tauntTarget : weakest;
        }

        private static bool IsGroundOperator(OperatorBase op)
        {
            return op != null && op.IsDeployed && op.CurrentHP > 0 && op.DeployedCell != null &&
                   op.DeployedCell.TileType != TileType.HighGround;
        }

        private void MeleeAttack(OperatorBase target)
        {
            PlayAttackSound();
            MeleeSwipeVisual.Play(VisualPosition, target.transform.position, new Color(1f, 0.45f, 0.2f, 1f), 1.4f, MeleeStyle.Smash);
            if (!target.TryTakeAttackDamage(currentATK, data.damageType, this)) return;

            CombatFx.Sparks(target.transform.position, new Color(1f, 0.75f, 0.35f), 6, 2.4f, 0.2f, big: true);
            if (stunnedOperators.Add(target) && target.IsDeployed && target.CurrentHP > 0)
                target.ApplyStun(MeleeStunDuration);
        }

        // ========================
        // Rockets
        // ========================

        public OperatorBase FindRocketTarget()
        {
            if (OperatorManager.Instance == null) return null;

            float cellSize = CurrentGrid != null ? CurrentGrid.CellSize : 1f;
            float range = RocketRangeTiles * cellSize;
            float sqrRange = range * range;
            OperatorBase tauntTarget = null;
            OperatorBase weakest = null;
            var deployed = OperatorManager.Instance.DeployedOperators;
            for (int i = 0; i < deployed.Count; i++)
            {
                OperatorBase op = deployed[i];
                if (op == null || !op.IsDeployed || op.CurrentHP <= 0 || !op.IsTargetable || op.DeployedCell == null)
                    continue;
                if ((op.transform.position - transform.position).sqrMagnitude > sqrRange) continue;

                if (tauntTarget == null && op.ShouldTauntAttacker(transform.position, CurrentGrid))
                    tauntTarget = op;
                if (weakest == null || op.CurrentHP < weakest.CurrentHP)
                    weakest = op;
            }

            return tauntTarget != null ? tauntTarget : weakest;
        }

        private void FireRocket(OperatorBase target)
        {
            rocketsFired++;
            bool heavy = rocketsFired % HeavyRocketEvery == 0;
            Vector2Int targetCell = target.DeployedCell.GridPosition;

            // The front of its arm cannon (EnemyData.projectileOrigin), scaled and raised with the sprite.
            Vector3 launcher = ProjectileOrigin;

            Vector3 impact = target.transform.position;
            float speed = heavy ? HeavyRocketSpeed : RocketSpeed;
            float cellSize = CurrentGrid != null ? CurrentGrid.CellSize : 1f;
            BlastWarningVisual.Play(impact, BlastRadiusTiles * cellSize, heavy ? HeavyRocketColor : RocketBlastColor,
                Vector2.Distance(launcher, impact) / speed);

            PlayAttackSound();
            CombatProjectileVisual.Fire(
                launcher,
                impact,
                heavy ? HeavyRocketColor : RocketBlastColor,
                speed,
                0.1f,
                0.05f,
                false,
                () => DetonateRocket(targetCell, heavy),
                heavy ? ProjectileStyle.HeavyRocket : ProjectileStyle.Rocket);
        }

        /// <summary>Blasts a circle of BlastRadiusTiles around a cell's centre (public for tests).</summary>
        public void DetonateRocket(Vector2Int centerCell, bool heavy)
        {
            if (this == null || data == null || CurrentGrid == null) return;
            PlayImpactSound();

            Vector3 center = CurrentGrid.GridToWorldPosition(centerCell);
            float radius = BlastRadiusTiles * CurrentGrid.CellSize;
            AoeBlastVisual.PlayCircle(center, radius, heavy ? HeavyRocketColor : RocketBlastColor, AoeStyle.HeavyOrdnance);
            if (heavy)
                FloatingCombatNumber.ShowText(center, "HEAVY!", HeavyRocketColor);

            if (OperatorManager.Instance == null) return;

            int damage = heavy ? Mathf.RoundToInt(currentATK * HeavyRocketDamageMultiplier) : currentATK;
            bool addHeat = heavy && StageCombatModifiers.IsHeatHazardActive;
            var deployed = new List<OperatorBase>(OperatorManager.Instance.DeployedOperators);
            for (int i = 0; i < deployed.Count; i++)
            {
                OperatorBase op = deployed[i];
                if (op == null || !op.IsDeployed || op.CurrentHP <= 0 || op.DeployedCell == null) continue;
                if ((op.transform.position - center).sqrMagnitude > radius * radius) continue;

                bool landed = op.TryTakeAttackDamage(damage, data.damageType, this);
                if (landed && addHeat && op.IsDeployed && op.CurrentHP > 0)
                    Stage4HeatHazard.AddHeatStack(op);
            }
        }
    }
}
