using System.Collections.Generic;
using UnityEngine;
using TrashTD.Audio;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Core.Pathfinding;
using TrashTD.Data;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Abstract base class for all enemies (GDD 1.5).
    /// Handles movement along path, taking damage, being blocked, and reaching exits.
    /// Subclasses override behavior for archetype-specific logic.
    /// 
    /// Parallel design to OperatorBase — demonstrates software reuse through
    /// shared enemy lifecycle with archetype-specific overrides.
    /// </summary>
    public abstract class EnemyBase : MonoBehaviour
    {
        private struct FrontAttackTarget
        {
            public OperatorBase Operator;
            public int Distance;

            public FrontAttackTarget(OperatorBase target, int distance)
            {
                Operator = target;
                Distance = distance;
            }
        }

        private const float RangedAttackStopDurationSeconds = 0.75f;

        [Header("Enemy Data")]
        [SerializeField] protected EnemyData data;

        // --- Runtime Stats (scaled by difficulty) ---
        protected int currentHP;
        protected int maxHP;
        protected int currentATK;
        protected int currentDEF;
        protected int currentRES;
        protected float currentMoveSpeed;
        private GridManager gridManager;
        private float trapDamageTimer;
        private bool isPushingBack;
        private Vector3 pushbackStartPosition;
        private Vector3 pushbackTargetPosition;
        private float pushbackElapsed;
        private float pushbackDuration;
        private int pushbackTargetPathIndex;

        // --- Pathfinding ---
        protected List<Vector3> path;
        protected int currentPathIndex;
        protected int difficultyLevel;

        public IReadOnlyList<Vector3> Path => path;
        public int CurrentPathIndex => currentPathIndex;
        public int DifficultyLevel => difficultyLevel;
        public virtual bool IsImmuneToAcidRain => data != null && data.isAcidRainImmune;
        public virtual bool IsImmuneToAcidTiles => data != null && data.isAcidTileImmune;

        // --- State ---
        protected bool isBlocked;
        protected OperatorBase blockingOperator;
        protected Vector3 blockedPosition;
        protected Vector2Int blockedAdjacentGridPos;
        protected bool isDead;
        protected bool isFacingLeft;
        protected float attackTimer;
        private float rangedAttackStopTimer;

        private float chillAmount;
        private float slowTimer;
        private float freezeTimer;
        private float stunTimer;
        private float speedBeforeChill;
        private float chillSlowMultiplier = 1f;
        private bool isFrozen;
        private bool isStunned;
        private float sluggishTimer;
        private float sluggishMoveSpeedMultiplier = 1f;
        private float sluggishAttackIntervalMultiplier = 1f;
        private bool isSluggish;
        protected SpriteRenderer[] animationRenderers;
        protected int animationFrameIndex;
        protected float animationFrameTimer;
        private SpriteRenderer[] chillRenderers;
        private Color[] originalRendererColors;

        private bool isInsideHole;
        private float holeStayTimer;
        private Vector3 destinationHolePosition;
        private const float DefaultHoleStayDuration = 1.5f;

        // --- Properties ---
        public EnemyData Data => data;
        public int CurrentHP => currentHP;
        public int MaxHP => maxHP;
        public int CurrentATK => currentATK;
        public virtual int CurrentDEF => currentDEF;
        public int CurrentRES => currentRES;
        public float CurrentMoveSpeed => currentMoveSpeed;
        public bool IsBlocked => isBlocked;
        public OperatorBase BlockingOperator => blockingOperator;
        public Vector3 BlockedPosition => blockedPosition;
        public Vector2Int BlockedAdjacentGridPos => blockedAdjacentGridPos;
        public bool IsPushingBack => isPushingBack;
        public bool IsDead => isDead;
        public bool IsFacingLeft => isFacingLeft;
        public bool IsFrozen => isFrozen;
        public bool IsStunned => isStunned;
        public bool IsSluggish => isSluggish;
        public float SluggishTimer => sluggishTimer;
        public float SluggishMoveSpeedMultiplier => sluggishMoveSpeedMultiplier;
        public float SluggishAttackIntervalMultiplier => sluggishAttackIntervalMultiplier;
        public bool IsInsideHole => isInsideHole;
        public virtual bool CanBeTargetedBy(OperatorPosition opPosition) => true;

        /// <summary>
        /// Where this enemy's shots and strikes leave its sprite: <see cref="EnemyData.projectileOrigin"/> from the
        /// visible sprite's centre (raised by visualOffset for flyers), mirrored like the sprite (the art faces right).
        /// </summary>
        public Vector3 ProjectileOrigin
        {
            get
            {
                Transform visual = transform.Find("EnemyVisual");
                Vector3 center = visual != null ? visual.position : transform.position;
                Vector2 offset = data != null ? data.projectileOrigin : Vector2.zero;
                if (isFacingLeft) offset.x = -offset.x;
                Vector3 scale = transform.lossyScale;
                return center + new Vector3(offset.x * scale.x, offset.y * scale.y, 0f);
            }
        }

        public Vector3 VisualPosition
        {
            get
            {
                if (animationRenderers == null)
                    animationRenderers = GetComponentsInChildren<SpriteRenderer>(true);

                for (int i = 0; i < animationRenderers.Length; i++)
                {
                    SpriteRenderer renderer = animationRenderers[i];
                    if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy &&
                        renderer.sprite != null)
                    {
                        return renderer.bounds.center;
                    }
                }

                return transform.position;
            }
        }
        public float ChillAmount => chillAmount;
        public EnemyMovementType MovementType => data.movementType;
        public float DistanceToGoal
        {
            get
            {
                if (path == null || currentPathIndex >= path.Count) return 0f;

                float distance = Vector3.Distance(transform.position, path[currentPathIndex]);
                for (int i = currentPathIndex; i < path.Count - 1; i++)
                {
                    distance += Vector3.Distance(path[i], path[i + 1]);
                }

                return distance;
            }
        }

        // --- Events ---
        /// <summary>Fired when this enemy reaches an exit point.</summary>
        public System.Action<EnemyBase> OnReachedExit;

        /// <summary>Fired when this enemy dies.</summary>
        public System.Action<EnemyBase> OnDied;

        /// <summary>
        /// Initialize this enemy with data and difficulty scaling.
        /// Call after instantiation, before path assignment.
        /// </summary>
        public virtual void Initialize(EnemyData enemyData, int difficultyLevel)
        {
            data = enemyData;
            this.difficultyLevel = difficultyLevel;
            animationRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            animationFrameIndex = 0;
            animationFrameTimer = 0f;
            ApplyAnimationFrame();
            maxHP = data.GetScaledHP(difficultyLevel);
            currentHP = maxHP;
            currentATK = data.GetScaledATK(difficultyLevel);
            currentDEF = data.GetScaledDEF(difficultyLevel);
            currentRES = data.baseRES;
            currentMoveSpeed = data.moveSpeed;
            chillAmount = 0f;
            slowTimer = 0f;
            freezeTimer = 0f;
            stunTimer = 0f;
            speedBeforeChill = currentMoveSpeed;
            chillSlowMultiplier = 1f;
            isFrozen = false;
            isStunned = false;
            isSluggish = false;
            sluggishTimer = 0f;
            sluggishMoveSpeedMultiplier = 1f;
            sluggishAttackIntervalMultiplier = 1f;
            isSluggish = false;
            sluggishTimer = 0f;
            sluggishMoveSpeedMultiplier = 1f;
            sluggishAttackIntervalMultiplier = 1f;
            CacheChillRenderers();
            UpdateChillVisual();

            isBlocked = false;
            blockingOperator = null;
            isPushingBack = false;
            pushbackElapsed = 0f;
            isDead = false;
            isFacingLeft = false;
            ApplyFacingVisuals();
            // Ground melee enemies are ready to strike on first contact with a blocker
            // (casters keep their wind-up before the first ranged shot).
            attackTimer = data != null && data.movementType == EnemyMovementType.Ground &&
                          data.archetype != EnemyArchetype.Caster
                ? GetEffectiveAttackInterval()
                : 0f;
            rangedAttackStopTimer = 0f;
            currentPathIndex = 0;
            trapDamageTimer = 0f;
            isInsideHole = false;
            holeStayTimer = 0f;
            destinationHolePosition = Vector3.zero;
        }

        public void SetGridManager(GridManager manager)
        {
            gridManager = manager;
        }

        /// <summary>
        /// Assign a movement path (from A* or predefined waypoints).
        /// </summary>
        public void SetPath(List<Vector3> newPath)
        {
            path = newPath;
            currentPathIndex = 0;

            if (path != null && path.Count > 0)
            {
                transform.position = path[0];
                if (path.Count > 1)
                {
                    UpdateFacing(path[1].x - path[0].x);
                }
            }
        }

        // ========================
        // MonoBehaviour Lifecycle
        // ========================

        protected virtual void Update()
        {
            if (isDead) return;

            if (isInsideHole)
            {
                UpdateHoleStay(Time.deltaTime);
                return;
            }

            UpdateSpriteAnimation(Time.deltaTime);
            UpdateTrapDamage(Time.deltaTime);
            if (isDead) return;

            UpdateChillStatus(Time.deltaTime);
            if (isFrozen || isStunned) return;

            if (isBlocked)
            {
                if (Vector3.Distance(transform.position, blockedPosition) > 0.02f)
                {
                    UpdateFacing(blockedPosition.x - transform.position.x);
                    transform.position = Vector3.MoveTowards(transform.position, blockedPosition, currentMoveSpeed * Time.deltaTime);
                }
                else
                {
                    transform.position = blockedPosition;
                }

                // Attack the blocking operator while blocked
                AttackBlocker();
            }
            else
            {
                // The melee attack keeps charging while walking (capped at one interval), so the first
                // hit lands as soon as an operator blocks this enemy instead of a full interval later.
                ChargeMeleeAttack(Time.deltaTime);

                if (isPushingBack)
                {
                    UpdatePushback();
                }
                else
                {
                    // Move along path
                    MoveAlongPath();
                }
            }
        }

        /// <summary>
        /// Per-frame upkeep for enemies with their own Update: hole stay, animation, trap damage, chill/stun
        /// timers and pushback. Returns false when the enemy can't act this frame.
        /// </summary>
        protected bool TickStatusEffects(float deltaTime)
        {
            if (isDead) return false;

            if (isInsideHole)
            {
                UpdateHoleStay(deltaTime);
                return false;
            }

            UpdateSpriteAnimation(deltaTime);
            UpdateTrapDamage(deltaTime);
            if (isDead) return false;

            UpdateChillStatus(deltaTime);
            if (isFrozen || isStunned) return false;

            if (isPushingBack)
            {
                UpdatePushback();
                return false;
            }

            return true;
        }

        /// <summary>Walks into (and then holds) the stopping point in front of the blocker.</summary>
        protected void HoldBlockedPosition(float deltaTime)
        {
            if (Vector3.Distance(transform.position, blockedPosition) > 0.02f)
            {
                UpdateFacing(blockedPosition.x - transform.position.x);
                transform.position = Vector3.MoveTowards(transform.position, blockedPosition, currentMoveSpeed * deltaTime);
            }
            else
            {
                transform.position = blockedPosition;
            }
        }

        protected GridManager CurrentGrid => gridManager;

        private void ChargeMeleeAttack(float deltaTime)
        {
            if (data == null) return;

            float attackInterval = GetEffectiveAttackInterval();
            attackTimer = Mathf.Min(attackTimer + deltaTime, attackInterval);
        }

        protected void UpdateRangedBehavior(bool prioritizeRangedOperators)
        {
            if (isDead) return;

            float deltaTime = Time.deltaTime;
            if (isInsideHole)
            {
                UpdateHoleStay(deltaTime);
                return;
            }

            UpdateSpriteAnimation(deltaTime);
            UpdateTrapDamage(deltaTime);
            if (isDead) return;

            UpdateChillStatus(deltaTime);
            if (isFrozen || isStunned) return;

            if (isPushingBack)
            {
                UpdatePushback();
                return;
            }

            if (rangedAttackStopTimer > 0f)
            {
                rangedAttackStopTimer = Mathf.Max(0f, rangedAttackStopTimer - deltaTime);
                return;
            }

            attackTimer += deltaTime;
            float attackInterval = GetEffectiveAttackInterval();

            if (attackTimer >= attackInterval)
            {
                OperatorBase target = FindOperatorTarget(prioritizeRangedOperators);
                if (target != null)
                {
                    attackTimer = 0f;
                    rangedAttackStopTimer = RangedAttackStopDurationSeconds;
                    AttackOperator(target);
                    return;
                }
            }

            // A blocked caster holds in front of its blocker and keeps shooting its usual targets.
            if (isBlocked)
                HoldBlockedPosition(deltaTime);
            else
                MoveAlongPath();
        }

        protected void UpdateFrontalAreaAttackBehavior(int tileRange)
        {
            if (isDead) return;

            float deltaTime = Time.deltaTime;
            if (isInsideHole)
            {
                UpdateHoleStay(deltaTime);
                return;
            }

            UpdateSpriteAnimation(deltaTime);
            UpdateTrapDamage(deltaTime);
            if (isDead) return;

            UpdateChillStatus(deltaTime);
            if (isFrozen) return;

            if (isPushingBack)
            {
                UpdatePushback();
                return;
            }

            attackTimer += deltaTime;
            float attackInterval = GetEffectiveAttackInterval();

            if (attackTimer >= attackInterval)
            {
                Vector2Int direction = GetPathForwardDirection();
                bool includeBlockingTile = isBlocked && blockingOperator != null;
                List<FrontAttackTarget> targets =
                    FindOperatorsInFront(tileRange, direction, includeBlockingTile);
                if (includeBlockingTile && !ContainsOperator(targets, blockingOperator))
                    targets.Add(new FrontAttackTarget(blockingOperator, 1));

                if (targets.Count > 0)
                {
                    attackTimer = 0f;
                    PlayAttackSound();
                    PlayFrontalSlamVisual(direction, tileRange, includeBlockingTile);
                    for (int i = 0; i < targets.Count; i++)
                    {
                        FrontAttackTarget frontTarget = targets[i];
                        OperatorBase target = frontTarget.Operator;
                        if (target != null && target.IsDeployed)
                        {
                            int damage = frontTarget.Distance == 2
                                ? Mathf.RoundToInt(currentATK * 0.5f)
                                : currentATK;
                            target.TakeDamage(damage, data.damageType, this);
                        }
                    }

                    return;
                }
            }

            if (isBlocked)
            {
                if (Vector3.Distance(transform.position, blockedPosition) > 0.02f)
                {
                    UpdateFacing(blockedPosition.x - transform.position.x);
                    transform.position = Vector3.MoveTowards(transform.position, blockedPosition, currentMoveSpeed * deltaTime);
                }
                else
                {
                    transform.position = blockedPosition;
                }
            }
            else
            {
                MoveAlongPath();
            }
        }

        private List<FrontAttackTarget> FindOperatorsInFront(
            int tileRange,
            Vector2Int direction,
            bool includeBlockingTile)
        {
            var targets = new List<FrontAttackTarget>();
            if (tileRange <= 0 || path == null || gridManager == null ||
                OperatorManager.Instance == null || direction == Vector2Int.zero)
                return targets;

            Vector2Int enemyCell = gridManager.WorldToGridPosition(transform.position);
            var deployedOperators = OperatorManager.Instance.DeployedOperators;
            for (int i = 0; i < deployedOperators.Count; i++)
            {
                OperatorBase op = deployedOperators[i];
                if (op == null || !op.IsDeployed || op.DeployedCell == null)
                    continue;

                Vector2Int offset = op.DeployedCell.GridPosition - enemyCell;
                int tileDistance = offset.x * direction.x + offset.y * direction.y;
                int minimumDistance = includeBlockingTile ? 0 : 1;
                if (tileDistance >= minimumDistance &&
                    tileDistance < minimumDistance + tileRange &&
                    offset == direction * tileDistance)
                {
                    targets.Add(new FrontAttackTarget(op, tileDistance - minimumDistance + 1));
                }
            }

            return targets;
        }

        protected Vector2Int GetPathForwardDirection()
        {
            if (path == null) return Vector2Int.zero;

            for (int i = currentPathIndex; i < path.Count; i++)
            {
                Vector3 waypointOffset = path[i] - transform.position;
                if (waypointOffset.sqrMagnitude <= 0.0001f)
                    continue;

                if (Mathf.Abs(waypointOffset.x) >= Mathf.Abs(waypointOffset.y))
                    return new Vector2Int(waypointOffset.x >= 0f ? 1 : -1, 0);
                return new Vector2Int(0, waypointOffset.y >= 0f ? 1 : -1);
            }

            return Vector2Int.zero;
        }

        private static bool ContainsOperator(List<FrontAttackTarget> targets, OperatorBase target)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].Operator == target)
                    return true;
            }

            return false;
        }

        private void PlayFrontalSlamVisual(
            Vector2Int direction,
            int tileRange,
            bool includeBlockingTile)
        {
            if (gridManager == null || direction == Vector2Int.zero || tileRange <= 0)
                return;

            Vector2Int enemyCell = gridManager.WorldToGridPosition(transform.position);
            Vector3 center = gridManager.GridToWorldPosition(enemyCell) +
                new Vector3(direction.x, direction.y, 0f) *
                (gridManager.CellSize *
                    (includeBlockingTile ? tileRange - 1 : tileRange + 1) * 0.5f);

            Combat.SandHulkSlamVisual.Play(center, direction, gridManager.CellSize, tileRange);
        }

        protected virtual void UpdateSpriteAnimation(float deltaTime)
        {
            if (data == null || data.animationFrames == null || data.animationFrames.Length <= 1)
                return;

            float frameDuration = 1f / Mathf.Max(0.01f, data.animationFrameRate);
            animationFrameTimer += deltaTime;
            while (animationFrameTimer >= frameDuration)
            {
                animationFrameTimer -= frameDuration;
                animationFrameIndex = (animationFrameIndex + 1) % data.animationFrames.Length;
                ApplyAnimationFrame();
            }
        }

        protected virtual void ApplyAnimationFrame()
        {
            if (data == null || data.animationFrames == null ||
                animationFrameIndex >= data.animationFrames.Length ||
                data.animationFrames[animationFrameIndex] == null)
                return;

            if (animationRenderers == null)
                animationRenderers = GetComponentsInChildren<SpriteRenderer>(true);

            for (int i = 0; i < animationRenderers.Length; i++)
            {
                // The flyer's ground shadow keeps its own ellipse sprite.
                if (animationRenderers[i] != null && animationRenderers[i].gameObject.name != "AirShadow")
                {
                    animationRenderers[i].sprite = data.animationFrames[animationFrameIndex];
                    animationRenderers[i].flipX = isFacingLeft;
                }
            }
        }

        private OperatorBase FindOperatorTarget(bool prioritizeRangedOperators)
        {
            if (OperatorManager.Instance == null || data == null) return null;

            float range = data.attackRange > 0 ? data.attackRange : 2.5f;
            float sqrRange = range * range;
            OperatorBase tauntTarget = null;
            float tauntSqrDistance = float.MaxValue;
            OperatorBase preferredTarget = null;
            OperatorBase fallbackTarget = null;
            float preferredSqrDistance = float.MaxValue;
            float fallbackSqrDistance = float.MaxValue;
            var deployed = OperatorManager.Instance.DeployedOperators;

            for (int i = 0; i < deployed.Count; i++)
            {
                OperatorBase op = deployed[i];
                if (op == null || !op.IsDeployed || !op.IsTargetable) continue;

                float sqrDistance = (op.transform.position - transform.position).sqrMagnitude;
                if (sqrDistance > sqrRange) continue;

                // Taunt mechanic: Ranged enemies prioritize taunting operators (Mossmo)
                // if this enemy (the attacker) is in a surrounding tile.
                if (op.ShouldTauntAttacker(transform.position, gridManager))
                {
                    if (sqrDistance < tauntSqrDistance)
                    {
                        tauntSqrDistance = sqrDistance;
                        tauntTarget = op;
                    }
                    continue;
                }

                bool isPreferred = op.Data != null &&
                    (op.Data.position == OperatorPosition.Ranged) == prioritizeRangedOperators;
                if (isPreferred)
                {
                    if (sqrDistance < preferredSqrDistance)
                    {
                        preferredSqrDistance = sqrDistance;
                        preferredTarget = op;
                    }
                }
                else if (sqrDistance < fallbackSqrDistance)
                {
                    fallbackSqrDistance = sqrDistance;
                    fallbackTarget = op;
                }
            }

            if (tauntTarget != null) return tauntTarget;
            return preferredTarget != null ? preferredTarget : fallbackTarget;
        }

        // Enemy sounds sit a little under operator sounds: there are many more enemies on screen.
        private const float EnemySfxVolume = 0.7f;

        // Melee hits land in the same moment the swing starts; an attack sound younger than this is left alone.
        private const float MinAttackSoundBeforeCut = 0.1f;

        private AudioSource attackVoice;
        private AudioClip attackVoiceClip;
        private float attackVoiceStartTime;

        /// <summary>The enemy's own Attack clip (swing, shot, cast), if it has one.</summary>
        protected void PlayAttackSound()
        {
            UnitSfx.Play(data, UnitSfxEvent.Attack, out attackVoice, EnemySfxVolume);
            attackVoiceClip = attackVoice != null ? attackVoice.clip : null;
            attackVoiceStartTime = Time.unscaledTime;
        }

        /// <summary>
        /// The enemy's own Impact clip (its projectile landing), if it has one. When it plays, the attack sound
        /// still running is cut so the two don't overlap.
        /// </summary>
        protected void PlayImpactSound()
        {
            if (!UnitSfx.Play(data, UnitSfxEvent.Impact, out AudioSource impactVoice, EnemySfxVolume) || impactVoice == null) return;
            if (attackVoice == null) return;

            if (Time.unscaledTime - attackVoiceStartTime >= MinAttackSoundBeforeCut)
                AudioManager.Instance?.StopSfx(attackVoice, attackVoiceClip);
            attackVoice = null;
            attackVoiceClip = null;
        }

        private void AttackOperator(OperatorBase target)
        {
            PlayAttackSound();
            System.Action onImpact = () =>
            {
                if (target == null || !target.IsDeployed || data == null) return;
                PlayImpactSound();
                target.TryTakeAttackDamage(currentATK, data.damageType, this);
            };

            Color projectileColor = data != null
                ? data.attackProjectileColor
                : new Color(0.55f, 0.9f, 0.3f);
            if (data != null && data.usesSandProjectileVisual)
            {
                CombatProjectileVisual.FireSand(
                    ProjectileOrigin,
                    target.transform.position,
                    projectileColor,
                    8f,
                    onImpact);
                return;
            }

            CombatProjectileVisual.Fire(
                ProjectileOrigin,
                target.transform.position,
                projectileColor,
                8f,
                0.1f,
                0.05f,
                false,
                onImpact,
                ProjectileStyle.Sludge);
        }

        private void UpdateTrapDamage(float deltaTime)
        {
            if (isDead || isInsideHole || gridManager == null || IsImmuneToAcidTiles) return;

            Vector2Int gridPosition = gridManager.WorldToGridPosition(transform.position);
            GridCell cell = gridManager.GetCell(gridPosition);
            if (cell == null || cell.TileType != TileType.Trap)
            {
                trapDamageTimer = 0f;
                return;
            }

            trapDamageTimer += deltaTime;
            while (trapDamageTimer >= TrapTileRules.DamageIntervalSeconds && !isDead)
            {
                trapDamageTimer -= TrapTileRules.DamageIntervalSeconds;
                AcidDamageFlash.Flash(gameObject);
                AudioManager.Instance?.PlaySfx(SfxId.TileTrapTick, 0.6f);
                TakeTrueDamage(TrapTileRules.DamagePerTick);
            }
        }

        // ========================
        // Movement
        // ========================

        /// <summary>
        /// Move along the assigned path toward the exit.
        /// </summary>
        protected virtual void MoveAlongPath()
        {
            MoveAlongPathBy(currentMoveSpeed * Time.deltaTime);
        }

        /// <summary>Moves up to <paramref name="step"/> world units along the path (for enemies that set their own pace).</summary>
        protected void MoveAlongPathBy(float step)
        {
            if (path == null || currentPathIndex >= path.Count)
                return;

            Vector3 targetPos = path[currentPathIndex];
            UpdateFacing(targetPos.x - transform.position.x);

            transform.position = Vector3.MoveTowards(transform.position, targetPos, step);

            if (Vector3.Distance(transform.position, targetPos) < 0.01f)
            {
                // Check if ground enemy reached a Hole tile
                if (data != null && data.movementType == EnemyMovementType.Ground && gridManager != null)
                {
                    Vector2Int gridPos = gridManager.WorldToGridPosition(transform.position);
                    GridCell currentCell = gridManager.GetCell(gridPos);
                    if (currentCell != null && currentCell.TileType == TileType.Hole)
                    {
                        Vector3 destWorldPos = transform.position;
                        if (currentPathIndex + 1 < path.Count)
                        {
                            Vector2Int nextPos = gridManager.WorldToGridPosition(path[currentPathIndex + 1]);
                            GridCell nextCell = gridManager.GetCell(nextPos);
                            if (nextCell != null && nextCell.TileType == TileType.Hole)
                            {
                                destWorldPos = path[currentPathIndex + 1];
                            }
                            else
                            {
                                GridCell linked = gridManager.GetLinkedHoleCell(gridPos);
                                destWorldPos = linked != null ? linked.WorldPosition : transform.position;
                            }
                        }
                        else
                        {
                            GridCell linked = gridManager.GetLinkedHoleCell(gridPos);
                            destWorldPos = linked != null ? linked.WorldPosition : transform.position;
                        }

                        if (destWorldPos != transform.position)
                        {
                            currentPathIndex++;
                if (currentPathIndex < path.Count)
                {
                    UpdateFacing(path[currentPathIndex].x - transform.position.x);
                }
                            EnterHole(destWorldPos, DefaultHoleStayDuration);
                            return;
                        }
                    }
                }

                currentPathIndex++;
                if (currentPathIndex < path.Count)
                {
                    UpdateFacing(path[currentPathIndex].x - transform.position.x);
                }

                if (currentPathIndex >= path.Count)
                {
                    ReachExit();
                }
            }
        }

        /// <summary>
        /// Updates the facing direction based on horizontal movement.
        /// Walking left mirrors the sprite (flipX = true).
        /// Walking right unmirrors the sprite (flipX = false).
        /// Moving purely up or down maintains the previous mirrored state.
        /// </summary>
        public virtual void UpdateFacing(float deltaX)
        {
            const float threshold = 0.001f;
            if (deltaX < -threshold)
            {
                SetFacingLeft(true);
            }
            else if (deltaX > threshold)
            {
                SetFacingLeft(false);
            }
        }

        public void SetFacingLeft(bool facingLeft)
        {
            isFacingLeft = facingLeft;
            ApplyFacingVisuals();
        }

        public virtual void ApplyFacingVisuals()
        {
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer sr = renderers[i];
                if (sr != null)
                {
                    if (sr.gameObject.name == "AirShadow") continue;
                    sr.flipX = isFacingLeft;
                }
            }

            Transform visualObj = transform.Find("EnemyVisual");
            if (visualObj != null && data != null && data.visualOffset.x != 0f)
            {
                visualObj.localPosition = new Vector3(
                    isFacingLeft ? -data.visualOffset.x : data.visualOffset.x,
                    data.visualOffset.y,
                    data.visualOffset.z);
            }
        }

        /// <summary>
        /// Enter a hole/ladder tile: becomes untargetable, invulnerable, hides visuals,
        /// and teleports to the destination hole after the duration.
        /// </summary>
        public void EnterHole(Vector3 destinationPos, float duration = DefaultHoleStayDuration)
        {
            if (isDead || isInsideHole) return;

            isInsideHole = true;
            holeStayTimer = duration;
            destinationHolePosition = destinationPos;

            if (isBlocked && blockingOperator != null)
            {
                blockingOperator.ReleaseBlock(this);
            }

            SetVisualsActive(false);
            AudioManager.Instance?.PlaySfx(SfxId.TileHoleEnter, 0.7f);
        }

        /// <summary>
        /// Exits the hole tile and reappears at the destination.
        /// </summary>
        public void ExitHole()
        {
            if (!isInsideHole) return;

            transform.position = destinationHolePosition;
            isInsideHole = false;
            SetVisualsActive(true);
            AudioManager.Instance?.PlaySfx(SfxId.TileHoleExit, 0.7f);

            if (path != null && currentPathIndex < path.Count)
            {
                if (Vector3.Distance(transform.position, path[currentPathIndex]) < 0.1f)
                {
                    currentPathIndex++;
                if (currentPathIndex < path.Count)
                {
                    UpdateFacing(path[currentPathIndex].x - transform.position.x);
                }
                }
            }

            if (path != null && currentPathIndex >= path.Count)
            {
                ReachExit();
            }
        }

        private void UpdateHoleStay(float deltaTime)
        {
            if (!isInsideHole) return;

            holeStayTimer -= deltaTime;
            if (holeStayTimer <= 0f)
            {
                ExitHole();
            }
        }

        private void SetVisualsActive(bool active)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = active;
            }

            var healthBar = GetComponent<Combat.WorldHealthBar>();
            if (healthBar != null)
            {
                healthBar.SetVisible(active);
            }
        }

        /// <summary>
        /// Push this enemy backward along its route by the requested number of grid cells.
        /// </summary>
        public bool PushBack(int cells, float duration = 0.45f)
        {
            if (isDead || isInsideHole || isPushingBack || gridManager == null || path == null || cells <= 0 || currentPathIndex <= 0)
                return false;

            float remainingDistance = gridManager.CellSize * cells;
            if (remainingDistance <= 0f)
                return false;

            Vector3 startPosition = transform.position;
            Vector3 pushedPosition;
            int nextPathIndex;
            bool moved;
            Vector3 previousTargetPosition = startPosition;
            bool hasPreviousTarget = false;
            do
            {
                CalculatePushbackTarget(startPosition, remainingDistance, out pushedPosition, out nextPathIndex, out moved);
                if (!moved)
                    return false;

                if (hasPreviousTarget && (pushedPosition - previousTargetPosition).sqrMagnitude <= Mathf.Epsilon)
                    return false;

                GridCell destinationCell = gridManager.GetCell(gridManager.WorldToGridPosition(pushedPosition));
                if (destinationCell == null || !destinationCell.IsOccupied || destinationCell.OccupantOperator == null)
                    break;

                previousTargetPosition = pushedPosition;
                hasPreviousTarget = true;
                remainingDistance += gridManager.CellSize;
            }
            while (remainingDistance > 0f);

            if (isBlocked && blockingOperator != null)
                blockingOperator.ReleaseBlock(this);

            pushbackStartPosition = transform.position;
            pushbackTargetPosition = pushedPosition;
            pushbackTargetPathIndex = nextPathIndex;
            pushbackElapsed = 0f;
            pushbackDuration = Mathf.Max(0.01f, duration);
            isPushingBack = true;
            return true;
        }

        private void CalculatePushbackTarget(
            Vector3 startPosition,
            float distance,
            out Vector3 targetPosition,
            out int targetPathIndex,
            out bool moved)
        {
            targetPosition = startPosition;
            targetPathIndex = currentPathIndex;
            moved = false;
            int waypointIndex = Mathf.Min(currentPathIndex - 1, path.Count - 1);

            while (waypointIndex >= 0 && distance > 0f)
            {
                Vector3 waypoint = path[waypointIndex];
                float segmentDistance = Vector3.Distance(targetPosition, waypoint);
                if (segmentDistance <= Mathf.Epsilon)
                {
                    targetPathIndex = waypointIndex;
                    waypointIndex--;
                    continue;
                }

                if (segmentDistance >= distance)
                {
                    targetPosition = Vector3.MoveTowards(targetPosition, waypoint, distance);
                    moved = true;
                    distance = 0f;
                }
                else
                {
                    targetPosition = waypoint;
                    distance -= segmentDistance;
                    targetPathIndex = waypointIndex;
                    waypointIndex--;
                    moved = true;
                }
            }
        }

        private void UpdatePushback()
        {
            pushbackElapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(pushbackElapsed / pushbackDuration);
            float easedProgress = Mathf.SmoothStep(0f, 1f, progress);
            transform.position = Vector3.Lerp(pushbackStartPosition, pushbackTargetPosition, easedProgress);

            if (progress >= 1f)
            {
                currentPathIndex = pushbackTargetPathIndex;
                isPushingBack = false;
            }
        }

        /// <summary>
        /// Called when the enemy reaches the exit point.
        /// Costs the player life points (defined in EnemyData.lifePointCost).
        /// </summary>
        protected virtual void ReachExit()
        {
            OnReachedExit?.Invoke(this);
            Destroy(gameObject);
        }

        // ========================
        // Damage & Death
        // ========================

        /// <summary>
        /// Take damage from an operator or effect.
        /// Damage formula: max(ATK - DEF, ATK * 0.05) — applied by DamageCalculator before calling this.
        /// </summary>
        public virtual void TakeDamage(int damage, DamageType damageType)
        {
            if (isDead || isInsideHole || damage <= 0) return;

            int previousHP = currentHP;
            currentHP = Mathf.Max(0, currentHP - damage);
            int actualDamage = previousHP - currentHP;
            if (actualDamage > 0)
            {
                FloatingCombatNumber.Show(VisualPosition, actualDamage, damageType);
                WorldHealthBar.UpdateFor(gameObject, currentHP, maxHP);
            }

            if (currentHP <= 0)
            {
                Die();
            }
        }

        /// <summary>
        /// Take true damage directly (ignoring DEF and RES).
        /// </summary>
        public virtual void TakeTrueDamage(int damage)
        {
            TakeDamage(damage, DamageType.True);
        }

        /// <summary>
        /// Check if this enemy can receive damage from the specified attacker.
        /// Overridden by specialized enemies like Gooelist (only receives damage from its blocker when blocked).
        /// </summary>
        public virtual bool CanReceiveDamageFrom(OperatorBase attacker)
        {
            return true;
        }

        public virtual bool TryTakeAttackDamage(int damage, DamageType damageType, OperatorBase attacker = null, bool canHitAir = false)
        {
            if (isDead || isInsideHole) return false;

            if (!CanReceiveDamageFrom(attacker))
            {
                FloatingCombatNumber.ShowText(VisualPosition, "DUEL!", new Color(1f, 0.85f, 0.3f));
                return false;
            }
            bool bypassAirImmunity = canHitAir || (attacker != null && attacker.CanHitAir);
            if (!bypassAirImmunity &&
                attacker != null && attacker.Data != null &&
                attacker.Data.position == OperatorPosition.Melee &&
                data != null && data.movementType == EnemyMovementType.Air)
                return false;

            bool ignoresMiss = attacker != null && attacker.IgnoresMissMechanic;
            if (!ignoresMiss)
            {
                if (!Combat.StageCombatModifiers.TryAttackHit(transform.position))
                    return false;

                if (data != null && data.operatorAttackMissChance > 0f &&
                    !Combat.StageCombatModifiers.TryAttackHit(data.operatorAttackMissChance))
                {
                    FloatingCombatNumber.ShowMiss(VisualPosition);
                    AudioManager.Instance?.PlaySfx(SfxId.CombatMiss, 0.6f);
                    return false;
                }
            }

            TakeDamage(damage, damageType);
            attacker?.PlayImpactSound();
            return true;
        }

        /// <summary>
        /// Kill immediately without going through damage (sandbox wave clears).
        /// </summary>
        public virtual void ForceKill()
        {
            if (!isDead) Die();
        }

        /// <summary>
        /// Handle enemy death.
        /// </summary>
        protected virtual void Die()
        {
            isDead = true;

            // Release from blocker if blocked
            if (isBlocked && blockingOperator != null)
            {
                blockingOperator.ReleaseBlock(this);
            }

            OnDied?.Invoke(this);

            // TODO: Death animation, loot drops
            Destroy(gameObject);
        }

        // ========================
        // Blocking (GDD 1.4.4)
        // ========================

        /// <summary>
        /// Called when this enemy is blocked by an operator.
        /// </summary>
        public virtual void OnBlocked(OperatorBase blocker)
        {
            isBlocked = true;
            blockingOperator = blocker;

            Vector2Int blockerGridPos = blocker != null && blocker.DeployedCell != null
                ? blocker.DeployedCell.GridPosition
                : (gridManager != null && blocker != null ? gridManager.WorldToGridPosition(blocker.transform.position) : Vector2Int.zero);

            blockedAdjacentGridPos = blockerGridPos;
            bool foundInPath = false;

            if (path != null && path.Count > 0)
            {
                int bestIdx = -1;
                if (gridManager != null)
                {
                    for (int i = 0; i < path.Count; i++)
                    {
                        if (gridManager.WorldToGridPosition(path[i]) == blockerGridPos)
                        {
                            if (bestIdx == -1 || Mathf.Abs(i - currentPathIndex) < Mathf.Abs(bestIdx - currentPathIndex))
                            {
                                bestIdx = i;
                            }
                        }
                    }
                }
                else if (blocker != null)
                {
                    float minDist = float.MaxValue;
                    for (int i = 0; i < path.Count; i++)
                    {
                        float d = Vector3.Distance(path[i], blocker.transform.position);
                        if (d < minDist && d <= 1.0f)
                        {
                            minDist = d;
                            bestIdx = i;
                        }
                    }
                }

                if (bestIdx != -1)
                {
                    foundInPath = true;
                    // The enemy approaches the blocker from path[bestIdx - 1] towards path[bestIdx].
                    // When blocked, the enemy is stopped in front of the blocker (on the incoming/entrance boundary).
                    if (bestIdx > 0)
                    {
                        blockedAdjacentGridPos = gridManager != null
                            ? gridManager.WorldToGridPosition(path[bestIdx - 1])
                            : blockerGridPos;
                        blockedPosition = (path[bestIdx - 1] + path[bestIdx]) * 0.5f;
                        currentPathIndex = bestIdx;
                    }
                    else
                    {
                        // Blocker is on the very first waypoint (spawn)
                        blockedAdjacentGridPos = blockerGridPos;
                        blockedPosition = path[bestIdx];
                        currentPathIndex = bestIdx;
                    }
                }
            }

            if (!foundInPath)
            {
                if (blocker != null)
                {
                    Vector2Int forwardOffset = blocker.Facing.ToOffset();
                    blockedAdjacentGridPos = blockerGridPos + forwardOffset;
                    if (gridManager != null)
                    {
                        Vector3 forwardWorld = gridManager.GridToWorldPosition(blockedAdjacentGridPos);
                        blockedPosition = (blocker.transform.position + forwardWorld) * 0.5f;
                    }
                    else
                    {
                        blockedPosition = blocker.transform.position + (Vector3)(Vector2)forwardOffset * 0.5f;
                    }
                }
                else
                {
                    blockedPosition = transform.position;
                }
            }

            if (!Application.isPlaying)
            {
                transform.position = blockedPosition;
            }
        }

        /// <summary>
        /// Called when this enemy is released from blocking.
        /// </summary>
        public virtual void OnUnblocked()
        {
            isBlocked = false;
            blockingOperator = null;
        }

        /// <summary>
        /// Attack the blocking operator while blocked.
        /// </summary>
        protected virtual void AttackBlocker()
        {
            if (blockingOperator == null) return;

            // Casters never melee their blocker; they keep their own ranged attacks (Askalaser, Hextension).
            if (data != null && data.archetype == EnemyArchetype.Caster) return;

            attackTimer += Time.deltaTime;
            if (attackTimer >= GetEffectiveAttackInterval())
            {
                attackTimer = 0f;
                PlayAttackSound();
                MeleeSwipeVisual.Play(VisualPosition, blockingOperator.transform.position, new Color(1f, 0.35f, 0.25f, 1f), 1f,
                    data.archetype == EnemyArchetype.Tank ? MeleeStyle.Bash : MeleeStyle.Claw);
                blockingOperator.TryTakeAttackDamage(currentATK, data.damageType, this);
            }
        }

        // ========================
        // Speed Modifiers
        // ========================

        /// <summary>
        /// Apply a speed modifier (for slows, stuns, etc).
        /// </summary>
        public void ApplySpeedModifier(float multiplier)
        {
            currentMoveSpeed = data.moveSpeed * multiplier;
        }

        /// <summary>
        /// Reset speed to base value.
        /// </summary>
        public void ResetSpeed()
        {
            currentMoveSpeed = data.moveSpeed;
        }

        /// <summary>
        /// Applies a timed movement slow and accumulates chill toward a temporary freeze.
        /// </summary>
        public bool ApplyChill(
            float amount,
            float slowMultiplier,
            float slowDuration,
            float freezeThreshold,
            float freezeDuration)
        {
            if (isDead || isInsideHole || data == null || amount <= 0f || slowDuration <= 0f || freezeThreshold <= 0f)
                return false;

            if (slowTimer <= 0f && !isFrozen)
            {
                speedBeforeChill = currentMoveSpeed;
            }

            chillSlowMultiplier = Mathf.Min(chillSlowMultiplier, Mathf.Clamp01(slowMultiplier));
            slowTimer = Mathf.Max(slowTimer, slowDuration);
            if (!isFrozen)
            {
                currentMoveSpeed = speedBeforeChill * chillSlowMultiplier;
            }

            bool triggeredFreeze = false;
            chillAmount += amount;
            if (chillAmount >= freezeThreshold && freezeDuration > 0f)
            {
                chillAmount = 0f;
                isFrozen = true;
                freezeTimer = Mathf.Max(freezeTimer, freezeDuration);
                currentMoveSpeed = 0f;
                triggeredFreeze = true;
            }

            UpdateChillVisual();
            return triggeredFreeze;
        }

        /// <summary>
        /// Applies a stun to this enemy, completely stopping its movement and attacks for the given duration.
        /// </summary>
        public void ApplyStun(float duration)
        {
            if (isDead || isInsideHole || duration <= 0f) return;

            stunTimer = Mathf.Max(stunTimer, duration);
            if (!isStunned)
            {
                isStunned = true;
                Combat.FloatingCombatNumber.ShowText(VisualPosition, "STUNNED!", new Color(1f, 0.9f, 0.2f));
            }

            UpdateChillVisual();
        }

        public virtual float GetEffectiveAttackInterval()
        {
            if (data == null) return float.PositiveInfinity;
            float interval = Combat.StageCombatModifiers.GetAttackInterval(data.attackInterval);
            if (isSluggish)
            {
                interval *= sluggishAttackIntervalMultiplier;
            }
            return interval;
        }

        public void ApplySluggish(float duration = 3.0f, float speedMultiplier = 0.6f, float attackIntervalMultiplier = 1.5f)
        {
            if (isDead || isInsideHole) return;

            if (slowTimer <= 0f && sluggishTimer <= 0f && !isFrozen)
            {
                speedBeforeChill = currentMoveSpeed;
            }

            sluggishMoveSpeedMultiplier = Mathf.Min(sluggishMoveSpeedMultiplier, Mathf.Clamp01(speedMultiplier));
            sluggishAttackIntervalMultiplier = Mathf.Max(sluggishAttackIntervalMultiplier, Mathf.Max(1f, attackIntervalMultiplier));
            sluggishTimer = Mathf.Max(sluggishTimer, duration);

            if (!isSluggish)
            {
                isSluggish = true;
                Combat.FloatingCombatNumber.ShowText(VisualPosition, "SLUGGISH!", new Color(0.75f, 0.45f, 1f));
            }

            if (!isFrozen && !isStunned)
            {
                float mult = (slowTimer > 0f ? chillSlowMultiplier : 1f) * sluggishMoveSpeedMultiplier;
                currentMoveSpeed = speedBeforeChill * mult;
            }
        }

        protected void UpdateChillStatus(float deltaTime)
        {
            bool wasSlowed = slowTimer > 0f;
            bool wasFrozen = isFrozen;

            if (slowTimer > 0f)
            {
                slowTimer = Mathf.Max(0f, slowTimer - deltaTime);
            }

            if (freezeTimer > 0f)
            {
                freezeTimer = Mathf.Max(0f, freezeTimer - deltaTime);
                if (freezeTimer <= 0f)
                {
                    isFrozen = false;
                }
            }

            if (stunTimer > 0f)
            {
                stunTimer = Mathf.Max(0f, stunTimer - deltaTime);
                if (stunTimer <= 0f)
                {
                    isStunned = false;
                }
            }

            bool wasSluggish = isSluggish;
            if (sluggishTimer > 0f)
            {
                sluggishTimer = Mathf.Max(0f, sluggishTimer - deltaTime);
                if (sluggishTimer <= 0f)
                {
                    isSluggish = false;
                    sluggishMoveSpeedMultiplier = 1f;
                    sluggishAttackIntervalMultiplier = 1f;
                }
            }

            if (!isFrozen && !isStunned)
            {
                float speedMultiplier = 1f;
                if (slowTimer > 0f) speedMultiplier *= chillSlowMultiplier;
                if (sluggishTimer > 0f) speedMultiplier *= sluggishMoveSpeedMultiplier;

                if (slowTimer > 0f || sluggishTimer > 0f)
                {
                    currentMoveSpeed = speedBeforeChill * speedMultiplier;
                }
                else if (wasSlowed || wasFrozen || wasSluggish)
                {
                    currentMoveSpeed = speedBeforeChill;
                    chillSlowMultiplier = 1f;
                    sluggishMoveSpeedMultiplier = 1f;
                    sluggishAttackIntervalMultiplier = 1f;
                }
            }

            UpdateChillVisual();
        }

        private void CacheChillRenderers()
        {
            chillRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            originalRendererColors = new Color[chillRenderers.Length];
            for (int i = 0; i < chillRenderers.Length; i++)
            {
                originalRendererColors[i] = chillRenderers[i].color;
            }
        }

        private void UpdateChillVisual()
        {
            if (chillRenderers == null || originalRendererColors == null) return;

            float tintAmount = isFrozen ? 0.95f : isStunned ? 0.85f : slowTimer > 0f ? 0.8f : 0f;
            for (int i = 0; i < chillRenderers.Length; i++)
            {
                if (chillRenderers[i] == null) continue;

                Color original = originalRendererColors[i];
                Color effectColor = isStunned
                    ? new Color(1f, 0.9f, 0.15f, original.a)
                    : new Color(0.1f, 0.6f, 1f, original.a);
                chillRenderers[i].color = Color.Lerp(original, effectColor, tintAmount);
            }
        }
    }
}
