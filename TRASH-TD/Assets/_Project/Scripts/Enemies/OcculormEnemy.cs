using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Occuworm (Occulorm) — Enemy Rusher archetype.
    /// Characteristics:
    /// - Aggressive, high-durability rusher with high ATK (around 65) and 0.5s attack interval.
    /// - Leaping ability: Detects deployed ground operators in the 3 tiles in front of it (like Proxishot's range pattern)
    ///   and leaps/pounces onto them to strike and engage.
    /// - Immune to acid pools and acid rain.
    /// - Lifesteal: heals itself for 20% of the damage dealt when attacking operators.
    /// </summary>
    public class OcculormEnemy : RusherEnemy
    {
        public const float DefaultLifestealRatio = 0.2f;
        public const float DefaultLeapCooldown = 60.0f;
        public const float DefaultLeapDuration = 0.35f;
        public const float DefaultLeapArcHeight = 0.5f;

        [Header("Occuworm Mechanics")]
        [SerializeField] private float lifestealRatio = DefaultLifestealRatio;
        [SerializeField] private float leapCooldown = DefaultLeapCooldown;
        [SerializeField] private float leapDuration = DefaultLeapDuration;
        [SerializeField] private float leapArcHeight = DefaultLeapArcHeight;

        private float leapCooldownTimer;
        private bool isLeaping;
        private float leapElapsed;
        private Vector3 leapStartPos;
        private Vector3 leapTargetPos;
        private OperatorBase leapTargetOperator;

        public override bool IsImmuneToAcidRain => true;
        public override bool IsImmuneToAcidTiles => true;

        public float LifestealRatio
        {
            get => lifestealRatio;
            set => lifestealRatio = value;
        }

        public float LeapCooldown
        {
            get => leapCooldown;
            set => leapCooldown = value;
        }

        public float LeapCooldownTimer => leapCooldownTimer;
        public bool IsLeaping => isLeaping;
        public float LeapDuration { get => leapDuration; set => leapDuration = value; }
        public float LeapArcHeight { get => leapArcHeight; set => leapArcHeight = value; }

        public override void Initialize(EnemyData enemyData, int difficultyLevel)
        {
            base.Initialize(enemyData, difficultyLevel);
            leapCooldownTimer = 0f;
            isLeaping = false;
            leapElapsed = 0f;
            leapTargetOperator = null;
        }

        protected override void Update()
        {
            if (isDead) return;

            float deltaTime = Time.deltaTime;

            if (leapCooldownTimer > 0f)
            {
                leapCooldownTimer -= deltaTime;
            }

            if (isLeaping)
            {
                UpdateLeapMotion(deltaTime);
                return;
            }

            if (!isBlocked && !IsFrozen && !IsStunned && !IsPushingBack && leapCooldownTimer <= 0f)
            {
                OperatorBase target = FindGroundOperatorInLeapRange();
                if (target != null)
                {
                    StartLeap(target);
                    return;
                }
            }

            base.Update();
        }

        public OperatorBase FindGroundOperatorInLeapRange()
        {
            GridManager grid = CurrentGrid ?? FindAnyObjectByType<GridManager>();
            if (grid == null || OperatorManager.Instance == null) return null;

            Vector2Int currentCell = grid.WorldToGridPosition(transform.position);
            Vector2Int forward = GetPathForwardDirection();
            if (forward == Vector2Int.zero)
            {
                forward = isFacingLeft ? Vector2Int.left : Vector2Int.right;
            }
            Vector2Int perp = new Vector2Int(-forward.y, forward.x);

            // 3 tiles in front of Occuworm (center, left flank, right flank)
            Vector2Int[] leapCells = new Vector2Int[]
            {
                currentCell + forward,
                currentCell + forward + perp,
                currentCell + forward - perp
            };

            var deployed = OperatorManager.Instance.DeployedOperators;
            OperatorBase bestTarget = null;
            float bestDistSqr = float.MaxValue;

            for (int i = 0; i < deployed.Count; i++)
            {
                var op = deployed[i];
                if (op == null || !op.IsDeployed || op.CurrentHP <= 0 || op.IsCamouflaged) continue;

                // Must be a ground operator (e.g. Guard, Defender, Specialist)
                if (op.Data != null && op.Data.position == OperatorPosition.Ranged) continue;

                Vector2Int opCell = op.DeployedCell != null
                    ? op.DeployedCell.GridPosition
                    : grid.WorldToGridPosition(op.transform.position);

                for (int c = 0; c < leapCells.Length; c++)
                {
                    if (opCell == leapCells[c])
                    {
                        float distSqr = (op.transform.position - transform.position).sqrMagnitude;
                        if (distSqr < bestDistSqr)
                        {
                            bestDistSqr = distSqr;
                            bestTarget = op;
                        }
                        break;
                    }
                }
            }

            return bestTarget;
        }

        public void StartLeap(OperatorBase target)
        {
            if (target == null) return;
            isLeaping = true;
            leapElapsed = 0f;
            leapStartPos = transform.position;
            leapTargetPos = target.transform.position;
            leapTargetOperator = target;
            PlayAttackSound();
            UpdateFacing(leapTargetPos.x - leapStartPos.x);
        }

        public void StepLeapMotion(float deltaTime) => UpdateLeapMotion(deltaTime);

        private void UpdateLeapMotion(float deltaTime)
        {
            leapElapsed += deltaTime;
            float t = leapDuration > 0f ? Mathf.Clamp01(leapElapsed / leapDuration) : 1f;

            Vector3 horizontalPos = Vector3.Lerp(leapStartPos, leapTargetPos, t);
            float arc = Mathf.Sin(t * Mathf.PI) * leapArcHeight;
            transform.position = horizontalPos + new Vector3(0f, arc, 0f);
            UpdateFacing(leapTargetPos.x - leapStartPos.x);

            if (t >= 1f)
            {
                CompleteLeap();
            }
        }

        private void CompleteLeap()
        {
            isLeaping = false;
            transform.position = leapTargetPos;
            leapCooldownTimer = leapCooldown;

            if (leapTargetOperator != null && leapTargetOperator.IsDeployed && leapTargetOperator.CurrentHP > 0)
            {
                PlayImpactSound();
                // Attempt to engage block with the landed operator so it starts attacking
                BlockingSystem.TryEngageBlock(leapTargetOperator, this);
                // When it leaps it starts attacking immediately
                attackTimer = GetEffectiveAttackInterval();
            }

            SyncPathIndexToPosition();
        }

        private void SyncPathIndexToPosition()
        {
            if (path == null || path.Count == 0) return;
            float bestDist = float.MaxValue;
            int bestIdx = currentPathIndex;
            for (int i = currentPathIndex; i < path.Count; i++)
            {
                float d = Vector3.Distance(transform.position, path[i]);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIdx = i;
                }
            }
            currentPathIndex = bestIdx;
        }

        protected override void MoveAlongPath()
        {
            if (isLeaping) return;
            base.MoveAlongPath();
        }

        protected override void AttackBlocker()
        {
            if (blockingOperator == null) return;

            attackTimer += Time.deltaTime;
            if (attackTimer >= GetEffectiveAttackInterval())
            {
                attackTimer = 0f;
                PlayAttackSound();
                MeleeSwipeVisual.Play(
                    VisualPosition,
                    blockingOperator.transform.position,
                    new Color(0.85f, 0.12f, 0.28f, 1f),
                    1.2f,
                    MeleeStyle.Claw);

                int hpBefore = blockingOperator.CurrentHP;
                bool hit = blockingOperator.TryTakeAttackDamage(currentATK, data != null ? data.damageType : DamageType.Physical, this);
                if (hit)
                {
                    int actualDamage = Mathf.Max(0, hpBefore - blockingOperator.CurrentHP);
                    int healAmount = Mathf.Max(1, Mathf.FloorToInt(actualDamage * lifestealRatio));
                    Heal(healAmount);
                }
            }
        }
    }
}
