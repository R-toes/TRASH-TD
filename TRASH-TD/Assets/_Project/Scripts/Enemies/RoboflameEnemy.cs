using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.GameLoop;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Roboflame — Enemy Caster with a flamethrower. Blockable (weight 1): a blocked Roboflame holds in front of its blocker and keeps flaming.
    /// - Walks the path until an operator is inside its flame cone: the tile it stands on, 1 tile ahead,
    ///   and 3 tiles across at 2 tiles ahead ("ahead" = its path direction).
    /// - Then it sprays fire for 3.5 s, sweeping side to side, creeping forward at 15% speed. Each sweep is one
    ///   damage tick of ATK Arts damage to every operator in the cone (5 ticks per burst). A 1.5 s cooldown
    ///   follows, when it walks at 75% speed; then it flames again if anyone is in the cone.
    /// - Stage 4: every 2nd tick that lands on the same operator adds 1 heat stack to it.
    /// </summary>
    public class RoboflameEnemy : EnemyCaster
    {
        public const float SweepDuration = 0.7f;   // One pass across the cone = one damage tick
        public const int TicksPerBurst = 5;
        public const float BurstDuration = TicksPerBurst * SweepDuration; // 3.5 s
        public const float CooldownDuration = 1.5f;
        public const float SweepHalfAngle = 28f;
        public const int TicksPerHeatStack = 2;
        public const float BurstMoveSpeedMultiplier = 0.15f;
        public const float CooldownMoveSpeedMultiplier = 0.75f;
        private const float FlameReachTiles = 2.4f;

        /// <summary>Cone tiles as (tiles ahead, tiles to the side).</summary>
        private static readonly Vector2Int[] ConeOffsets =
        {
            new Vector2Int(0, 0),
            new Vector2Int(1, 0),
            new Vector2Int(2, 0),
            new Vector2Int(2, 1),
            new Vector2Int(2, -1),
        };

        [SerializeField] private Vector2 flameOriginOffset = new Vector2(0.3f, -0.05f);

        private readonly Dictionary<OperatorBase, int> ticksTaken = new Dictionary<OperatorBase, int>();
        private float burstElapsed = -1f;
        private int ticksDealt;
        private float cooldownTimer;
        private Vector2Int flameDirection;
        private float emitCarry;

        public bool IsFlaming => burstElapsed >= 0f;
        public float CooldownRemaining => cooldownTimer;
        public int TicksDealtThisBurst => ticksDealt;

        protected override void Update()
        {
            float deltaTime = Time.deltaTime;
            if (!TickStatusEffects(deltaTime)) return;

            Step(deltaTime);
        }

        /// <summary>One frame of Roboflame's behaviour after status effects (public for tests).</summary>
        public void Step(float deltaTime)
        {
            if (IsDead) return;

            if (cooldownTimer > 0f)
                cooldownTimer = Mathf.Max(0f, cooldownTimer - deltaTime);

            if (IsFlaming)
            {
                // Creeps forward while it sprays (or holds in front of its blocker).
                if (isBlocked) HoldBlockedPosition(deltaTime);
                else MoveAlongPathBy(currentMoveSpeed * BurstMoveSpeedMultiplier * deltaTime);
                if (!IsDead) UpdateBurst(deltaTime);
                return;
            }

            Vector2Int direction = GetPathForwardDirection();
            if (cooldownTimer <= 0f && direction != Vector2Int.zero && GetOperatorsInCone(direction).Count > 0)
            {
                StartBurst(direction);
                return;
            }

            // Between bursts it walks on, a little slower than its normal pace.
            float pace = cooldownTimer > 0f ? CooldownMoveSpeedMultiplier : 1f;
            if (isBlocked) HoldBlockedPosition(deltaTime);
            else MoveAlongPathBy(currentMoveSpeed * pace * deltaTime);
        }

        public List<Vector2Int> GetConeCells(Vector2Int direction)
        {
            var cells = new List<Vector2Int>(ConeOffsets.Length);
            if (CurrentGrid == null || direction == Vector2Int.zero) return cells;

            Vector2Int origin = CurrentGrid.WorldToGridPosition(transform.position);
            Vector2Int side = new Vector2Int(direction.y, -direction.x);
            for (int i = 0; i < ConeOffsets.Length; i++)
            {
                Vector2Int cell = origin + direction * ConeOffsets[i].x + side * ConeOffsets[i].y;
                if (CurrentGrid.GetCell(cell) != null)
                    cells.Add(cell);
            }

            return cells;
        }

        public List<OperatorBase> GetOperatorsInCone(Vector2Int direction)
        {
            var targets = new List<OperatorBase>();
            if (OperatorManager.Instance == null) return targets;

            List<Vector2Int> cells = GetConeCells(direction);
            if (cells.Count == 0) return targets;

            var deployed = OperatorManager.Instance.DeployedOperators;
            for (int i = 0; i < deployed.Count; i++)
            {
                OperatorBase op = deployed[i];
                if (op == null || !op.IsDeployed || op.CurrentHP <= 0 || !op.IsTargetable || op.DeployedCell == null)
                    continue;

                if (cells.Contains(op.DeployedCell.GridPosition))
                    targets.Add(op);
            }

            return targets;
        }

        private void StartBurst(Vector2Int direction)
        {
            flameDirection = direction;
            burstElapsed = 0f;
            ticksDealt = 0;
            emitCarry = 0f;
            if (direction.x != 0)
                UpdateFacing(direction.x);

            PlayAttackSound();
            DealTick(); // The first sweep starts on the centre line
        }

        private void UpdateBurst(float deltaTime)
        {
            burstElapsed += deltaTime;

            // It creeps along the path while flaming, so keep the flame pointed the way it's going.
            Vector2Int direction = GetPathForwardDirection();
            if (direction != Vector2Int.zero)
                flameDirection = direction;

            while (ticksDealt < TicksPerBurst && ticksDealt * SweepDuration <= burstElapsed)
            {
                if (!DealTick())
                {
                    EndBurst();
                    return;
                }
            }

            if (burstElapsed >= BurstDuration)
            {
                EndBurst();
                return;
            }

            EmitFlame(deltaTime);
        }

        /// <summary>One sweep's damage to everyone in the cone. Returns false when the cone is empty.</summary>
        private bool DealTick()
        {
            List<OperatorBase> targets = GetOperatorsInCone(flameDirection);
            ticksDealt++;
            if (targets.Count == 0) return false;

            for (int i = 0; i < targets.Count; i++)
            {
                OperatorBase op = targets[i];
                if (!op.TryTakeAttackDamage(currentATK, data.damageType, this)) continue;
                PlayImpactSound();
                FlamethrowerVisual.Ignite(op.transform.position);
                RegisterHeatTick(op);
            }

            return true;
        }

        private void RegisterHeatTick(OperatorBase op)
        {
            if (op == null || !op.IsDeployed || op.CurrentHP <= 0) return;

            ticksTaken.TryGetValue(op, out int ticks);
            ticks++;
            ticksTaken[op] = ticks;

            if (StageCombatModifiers.IsHeatHazardActive && ticks % TicksPerHeatStack == 0)
                Stage4HeatHazard.AddHeatStack(op);
        }

        private void EndBurst()
        {
            burstElapsed = -1f;
            cooldownTimer = CooldownDuration;
        }

        private void EmitFlame(float deltaTime)
        {
            float cellSize = CurrentGrid != null ? CurrentGrid.CellSize : 1f;
            Vector2 forward = new Vector2(flameDirection.x, flameDirection.y);
            float angle = SweepHalfAngle * Mathf.Sin(Mathf.PI * burstElapsed / SweepDuration);
            Vector2 aim = Quaternion.Euler(0f, 0f, angle) * forward;
            FlamethrowerVisual.Emit(GetFlameOrigin(), aim, FlameReachTiles * cellSize, deltaTime, ref emitCarry);
        }

        private Vector2 GetFlameOrigin()
        {
            Vector2 offset = flameDirection.x != 0
                ? new Vector2(flameDirection.x * flameOriginOffset.x, flameOriginOffset.y)
                : new Vector2(0f, flameDirection.y * flameOriginOffset.x);
            return (Vector2)transform.position + offset;
        }
    }
}
