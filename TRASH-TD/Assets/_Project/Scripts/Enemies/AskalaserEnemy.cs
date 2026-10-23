using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Enemies
{
    public enum AskalaserState
    {
        State1_Global,
        State2_Laser
    }

    /// <summary>
    /// Askalaser — Caster enemy archetype.
    /// Characteristics:
    /// - Arts damage type.
    /// - Low DEF, high RES.
    /// - Moves at Grunt speed (1.0 tiles/sec).
    /// - Unblockable / immune to blocking (Caster archetype).
    /// 
    /// Two Combat States:
    /// - State 1 (Global):
    ///   - Global range.
    ///   - Moves along the path, stops moving during attack.
    ///   - Attacks every 5.0 seconds.
    ///   - Deals 20 Arts damage.
    ///   - Prioritizes deployed operator with the lowest DEF.
    /// 
    /// - State 2 (Laser):
    ///   - Engages when a valid operator is within the State 2 pattern centered on Askalaser:
    ///     - Yellow tiles (Ground & Ranged units): (0, 2), (-1, 1), (0, 1), (1, 1), (-2, 0), (-1, 0), (1, 0), (2, 0), (-1, -1), (0, -1), (1, -1), (0, -2)
    ///     - Green tiles (Ranged units ONLY): (-1, 2), (1, 2), (-2, 1), (2, 1), (-2, -1), (2, -1), (-1, -2), (1, -2)
    ///   - Continuous light magic laser locking onto one target.
    ///   - Completely stops moving while lasering.
    ///   - Ticks every 0.375 seconds.
    ///   - Damage ramps every 1.5 seconds: 5 -> 15 -> 30 -> 80 -> 100 -> 150.
    ///   - Continuously attacks the operator until it dies, becomes camouflaged (Proxyshot), or leaves range.
    ///   - Prioritizes target with lowest DEF.
    ///   - When lock breaks: switches to another target in range (ramp resets to 5) or transitions back to State 1.
    /// </summary>
    public class AskalaserEnemy : EnemyBase
    {
        public const float State1AttackInterval = 5.0f;
        public const int State1Damage = 20;
        public const float State1StopDuration = 0.75f;

        public const float LaserTickInterval = 0.375f;
        public const float LaserStepDuration = 1.5f;

        public static readonly Color LaserColor = new Color(1f, 0.95f, 0.5f, 1f);

        [Header("Askalaser State")]
        [SerializeField] private float state1Timer = 0f;
        [SerializeField] private float state1StopTimer = 0f;

        [Header("State 2 Laser")]
        [SerializeField] private OperatorBase lockedTarget;
        [SerializeField] private float lockDuration = 0f;
        [SerializeField] private float laserTickTimer = 0f;

        private LightLaserVisual laserVisual;
        private GridManager cachedGridManager;

        public AskalaserState CurrentState => lockedTarget != null ? AskalaserState.State2_Laser : AskalaserState.State1_Global;
        public OperatorBase CurrentLockedTarget => lockedTarget;
        public float LockDuration => lockDuration;
        public float State1Timer => state1Timer;

        public GridManager EffectiveGridManager
        {
            get
            {
                if (cachedGridManager == null)
                    cachedGridManager = FindAnyObjectByType<GridManager>();
                return cachedGridManager;
            }
            set => cachedGridManager = value;
        }

        // ==========================================
        // Range Pattern Offsets (Relative to Askalaser at 0, 0)
        // ==========================================

        public static readonly Vector2Int[] YellowOffsets = new Vector2Int[]
        {
            new Vector2Int(0, 2),
            new Vector2Int(-1, 1), new Vector2Int(0, 1), new Vector2Int(1, 1),
            new Vector2Int(-2, 0), new Vector2Int(-1, 0), new Vector2Int(1, 0), new Vector2Int(2, 0),
            new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
            new Vector2Int(0, -2)
        };

        public static readonly Vector2Int[] GreenOffsets = new Vector2Int[]
        {
            new Vector2Int(-1, 2), new Vector2Int(1, 2),
            new Vector2Int(-2, 1), new Vector2Int(2, 1),
            new Vector2Int(-2, -1), new Vector2Int(2, -1),
            new Vector2Int(-1, -2), new Vector2Int(1, -2)
        };

        public static bool IsInYellowPattern(Vector2Int offset)
        {
            if (offset.y == 2 && offset.x == 0) return true;
            if (offset.y == 1 && offset.x >= -1 && offset.x <= 1) return true;
            if (offset.y == 0 && ((offset.x >= -2 && offset.x <= -1) || (offset.x >= 1 && offset.x <= 2))) return true;
            if (offset.y == -1 && offset.x >= -1 && offset.x <= 1) return true;
            if (offset.y == -2 && offset.x == 0) return true;
            return false;
        }

        public static bool IsInGreenPattern(Vector2Int offset)
        {
            if (offset.y == 2 && (offset.x == -1 || offset.x == 1)) return true;
            if (offset.y == 1 && (offset.x == -2 || offset.x == 2)) return true;
            if (offset.y == -1 && (offset.x == -2 || offset.x == 2)) return true;
            if (offset.y == -2 && (offset.x == -1 || offset.x == 1)) return true;
            return false;
        }

        public static int GetLaserDamageForDuration(float duration)
        {
            if (duration < LaserStepDuration) return 5;
            if (duration < LaserStepDuration * 2f) return 15;
            if (duration < LaserStepDuration * 3f) return 30;
            if (duration < LaserStepDuration * 4f) return 80;
            if (duration < LaserStepDuration * 5f) return 100;
            return 150;
        }

        public int GetCurrentLaserDamage() => GetLaserDamageForDuration(lockDuration);

        public void SetGridManagerInstance(GridManager gm)
        {
            SetGridManager(gm);
            cachedGridManager = gm;
        }

        public Vector2Int GetOperatorGridPos(OperatorBase op)
        {
            if (op == null) return Vector2Int.zero;
            if (op.DeployedCell != null)
                return op.DeployedCell.GridPosition;

            var gm = EffectiveGridManager;
            if (gm != null)
                return gm.WorldToGridPosition(op.transform.position);

            return new Vector2Int(Mathf.RoundToInt(op.transform.position.x), Mathf.RoundToInt(op.transform.position.y));
        }

        public Vector2Int GetMyGridPos()
        {
            var gm = EffectiveGridManager;
            if (gm != null)
                return gm.WorldToGridPosition(transform.position);

            return new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.y));
        }

        public bool IsTargetInState2Range(OperatorBase op)
        {
            if (op == null || !op.IsDeployed || op.CurrentHP <= 0 || !op.IsTargetable || op.IsCamouflaged)
                return false;

            Vector2Int opPos = GetOperatorGridPos(op);
            Vector2Int myPos = GetMyGridPos();
            Vector2Int offset = opPos - myPos;

            if (IsInYellowPattern(offset))
                return true;

            if (IsInGreenPattern(offset))
                return op.Data != null && op.Data.position == OperatorPosition.Ranged;

            return false;
        }

        private IReadOnlyList<OperatorBase> GetDeployedOperators()
        {
            if (OperatorManager.Instance != null)
                return OperatorManager.Instance.DeployedOperators;

            return FindObjectsByType<OperatorBase>();
        }

        public OperatorBase FindState2Target()
        {
            var deployed = GetDeployedOperators();
            if (deployed == null || deployed.Count == 0) return null;

            OperatorBase bestTarget = null;
            int lowestDef = int.MaxValue;
            float closestDist = float.MaxValue;

            OperatorBase tauntTarget = null;
            float tauntDist = float.MaxValue;

            for (int i = 0; i < deployed.Count; i++)
            {
                OperatorBase op = deployed[i];
                if (!IsTargetInState2Range(op)) continue;

                float dist = (op.transform.position - transform.position).sqrMagnitude;

                if (op.ShouldTauntAttacker(transform.position, EffectiveGridManager))
                {
                    if (dist < tauntDist)
                    {
                        tauntDist = dist;
                        tauntTarget = op;
                    }
                    continue;
                }

                int opDef = op.CurrentDEF;
                if (opDef < lowestDef || (opDef == lowestDef && dist < closestDist))
                {
                    lowestDef = opDef;
                    closestDist = dist;
                    bestTarget = op;
                }
            }

            return tauntTarget != null ? tauntTarget : bestTarget;
        }

        public OperatorBase FindState1Target()
        {
            var deployed = GetDeployedOperators();
            if (deployed == null || deployed.Count == 0) return null;

            OperatorBase bestTarget = null;
            int lowestDef = int.MaxValue;
            float closestDist = float.MaxValue;

            OperatorBase tauntTarget = null;
            float tauntDist = float.MaxValue;

            for (int i = 0; i < deployed.Count; i++)
            {
                OperatorBase op = deployed[i];
                if (op == null || !op.IsDeployed || op.CurrentHP <= 0 || !op.IsTargetable || op.IsCamouflaged)
                    continue;

                float dist = (op.transform.position - transform.position).sqrMagnitude;

                if (op.ShouldTauntAttacker(transform.position, EffectiveGridManager))
                {
                    if (dist < tauntDist)
                    {
                        tauntDist = dist;
                        tauntTarget = op;
                    }
                    continue;
                }

                int opDef = op.CurrentDEF;
                if (opDef < lowestDef || (opDef == lowestDef && dist < closestDist))
                {
                    lowestDef = opDef;
                    closestDist = dist;
                    bestTarget = op;
                }
            }

            return tauntTarget != null ? tauntTarget : bestTarget;
        }

        // ==========================================
        // Lifecycle & Movement Overrides
        // ==========================================

        protected override void MoveAlongPath()
        {
            // If currently in State 2 or stopped for State 1 attack, remain stationary
            if (lockedTarget != null || state1StopTimer > 0f)
                return;

            // If an operator is in State 2 range, lock on immediately instead of advancing
            OperatorBase state2Candidate = FindState2Target();
            if (state2Candidate != null)
            {
                LockOnState2Target(state2Candidate);
                return;
            }

            base.MoveAlongPath();
        }

        protected override void Update()
        {
            base.Update();

            if (IsDead) return;

            if (IsInsideHole || IsFrozen || IsStunned || IsPushingBack)
            {
                ResetLock();
                return;
            }

            UpdateAskalaser(Time.deltaTime);
        }

        public void UpdateAskalaser(float deltaTime)
        {
            if (IsDead) return;

            // 1. If currently in State 2 (laser lock)
            if (lockedTarget != null)
            {
                if (!IsTargetInState2Range(lockedTarget))
                {
                    ResetLock();
                    OperatorBase next = FindState2Target();
                    if (next != null)
                    {
                        LockOnState2Target(next);
                    }
                    return;
                }

                lockDuration += deltaTime;
                laserTickTimer += deltaTime;

                while (laserTickTimer >= LaserTickInterval)
                {
                    laserTickTimer -= LaserTickInterval;
                    ApplyLaserTick();
                    if (lockedTarget == null) break;
                }

                // Stationary while channeling laser
                return;
            }

            // 2. Not locked: Check if an operator is in State 2 range
            OperatorBase state2Candidate = FindState2Target();
            if (state2Candidate != null)
            {
                LockOnState2Target(state2Candidate);
                return;
            }

            // 3. State 1: Global Poke / March
            UpdateState1(deltaTime);
        }

        private void UpdateState1(float deltaTime)
        {
            if (state1StopTimer > 0f)
            {
                state1StopTimer = Mathf.Max(0f, state1StopTimer - deltaTime);
            }

            state1Timer += deltaTime;
            if (state1Timer >= State1AttackInterval)
            {
                OperatorBase target = FindState1Target();
                if (target != null)
                {
                    state1Timer = 0f;
                    state1StopTimer = State1StopDuration;
                    PerformState1Attack(target);
                }
            }
        }

        public void LockOnState2Target(OperatorBase target)
        {
            if (target == null) return;

            if (lockedTarget != target)
            {
                ResetLock();
                lockedTarget = target;
                lockDuration = 0f;
                laserTickTimer = 0f;
            }

            if (laserVisual == null)
            {
                PlayAttackSound();
                laserVisual = LightLaserVisual.Fire(
                    transform,
                    lockedTarget.transform,
                    LaserColor,
                    LaserTickInterval,
                    null);
            }
        }

        public void ResetLock()
        {
            if (laserVisual != null)
            {
                laserVisual.Stop();
                laserVisual = null;
            }

            lockedTarget = null;
            lockDuration = 0f;
            laserTickTimer = 0f;
        }

        public void ManualLaserTick()
        {
            ApplyLaserTick();
        }

        private void ApplyLaserTick()
        {
            if (lockedTarget == null || !IsTargetInState2Range(lockedTarget))
            {
                ResetLock();
                return;
            }

            int damage = GetCurrentLaserDamage();
            PlayImpactSound();
            lockedTarget.TryTakeAttackDamage(damage, DamageType.Arts, this);

            // Check if target died, retreated, or activated Camouflage (e.g. Proxyshot)
            if (lockedTarget == null || !lockedTarget.IsDeployed || lockedTarget.CurrentHP <= 0 ||
                !lockedTarget.IsTargetable || lockedTarget.IsCamouflaged)
            {
                ResetLock();
                OperatorBase next = FindState2Target();
                if (next != null)
                {
                    LockOnState2Target(next);
                }
            }
        }

        public void PerformState1Attack(OperatorBase target, bool applyImmediately = false)
        {
            if (target == null || !target.IsDeployed || !target.IsTargetable || target.IsCamouflaged)
                return;

            PlayAttackSound();

            void DealDamage()
            {
                if (target != null && target.IsDeployed && target.CurrentHP > 0)
                {
                    PlayImpactSound();
                    target.TryTakeAttackDamage(State1Damage, DamageType.Arts, this);
                }
            }

            if (applyImmediately)
            {
                DealDamage();
                return;
            }

            Color projectileColor = data != null ? data.attackProjectileColor : LaserColor;
            CombatProjectileVisual.Fire(
                transform.position,
                target.transform.position,
                projectileColor,
                8f,
                0.1f,
                0.05f,
                false,
                DealDamage);
        }

        public void SetState1Timer(float timer) => state1Timer = timer;
        public void SetLockDuration(float duration) => lockDuration = duration;

        // ==========================================
        // Enemy Base Overrides
        // ==========================================

        public override void OnBlocked(OperatorBase blocker)
        {
            // Enemy casters are immune to blocking per GDD 1.4.4 & 1.5
        }

        public override void ForceKill()
        {
            ResetLock();
            base.ForceKill();
        }

        protected override void Die()
        {
            ResetLock();
            base.Die();
        }

        private void OnDisable()
        {
            ResetLock();
        }

        private void OnDestroy()
        {
            ResetLock();
        }
    }
}

