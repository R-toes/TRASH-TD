using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Mlem — Defender (blocks 4, ground targets only).
    /// <para>
    /// Attack: he lashes his tongue at an enemy within his own tile and the 2 tiles in front of him, drags it back to his
    /// mouth and swallows it. The swallow is an instant kill whatever the enemy's HP, and counts as a normal kill. If the
    /// target dies or leaves range before the tongue grabs it, the attack whiffs and he can attack again at once.
    /// Targets go by priority Tank, Caster, Rusher, Grunt; within a type, the one closest to the exit goes first.
    /// </para>
    /// <para>
    /// Digestion is his cooldown: after a swallow he can't attack until it finishes (Rusher 2 s, Grunt 3 s, Caster 4 s,
    /// Tank 6 s). While digesting he has bonus DEF and max HP, and he keeps blocking.
    /// </para>
    /// <para>
    /// 1st upgrade: digestion is 20% shorter and he recovers HP while digesting. 2nd upgrade: one tongue attack swallows up
    /// to 3 enemies; they digest together over the average of their times (the 1st upgrade's reduction still applies).
    /// </para>
    /// Every number is on the <see cref="OperatorData"/> asset (the "Devour (Mlem)" fields). The attack follows the
    /// animation: <see cref="MlemSpriteAnimation"/> fires an event on the frame the tongue grabs, the frame his mouth
    /// closes and the frame the attack ends, and this class reacts to those.
    /// </summary>
    [DefaultExecutionOrder(100)] // after GridSpriteSorter's LateUpdate, so caught enemies can be drawn just behind him
    public class MlemOperator : DefenderOperator
    {
        /// <summary>Source name of the digestion DEF / max HP bonus in the stat effects list.</summary>
        public const string DigestSource = "DIGESTING";

        /// <summary>Time between HP recovery ticks while digesting (so the heal numbers don't spam).</summary>
        private const float RegenTickSeconds = 0.5f;

        /// <summary>What he can eat, best target first. Flyers and anything else are never targeted.</summary>
        private static readonly EnemyArchetype[] DevourPriority =
        {
            EnemyArchetype.Tank, EnemyArchetype.Caster, EnemyArchetype.Rusher, EnemyArchetype.Grunt
        };

        private struct GrabbedEnemy
        {
            public EnemyBase Enemy;
            public Vector3 StartPosition;
            public Vector3 StartScale;
            public Vector2 ClusterOffsetPixels;
            public EnemyArchetype Archetype;
        }

        /// <summary>The first part of the drag: the caught enemy is pulled from where it stood onto the tongue.</summary>
        private const float SnapSeconds = 0.045f;

        // --- Rank ---
        /// <summary>1st upgrade: one rank above the card's base rarity.</summary>
        public bool HasFirstUpgrade => data != null && (int)currentRarity >= (int)data.baseRarity + 1;

        /// <summary>2nd upgrade: two ranks above the card's base rarity.</summary>
        public bool HasSecondUpgrade => data != null && (int)currentRarity >= (int)data.baseRarity + 2;

        public int MaxSwallowCount => GetMaxSwallowCount(data, HasSecondUpgrade);

        // --- State ---
        public bool IsDigesting { get; private set; }
        public bool IsLashing => lashing;
        public float DigestRemaining => digestRemaining;
        public float DigestTotal => digestTotal;
        public int EnemiesSwallowed { get; private set; }

        private bool lashing;
        private bool chomped;
        private readonly List<EnemyBase> lashTargets = new List<EnemyBase>();
        private readonly List<GrabbedEnemy> grabbed = new List<GrabbedEnemy>();
        private float grabTime;
        private float frameSeconds = MlemSpriteAnimation.DefaultAttackFrameDuration;

        private float digestRemaining;
        private float digestTotal;
        private int appliedDefBonus;
        private int appliedHpBonus;
        private float regenAccumulator;
        private float regenTickTimer;

        private MlemSpriteAnimation spriteAnimation;
        private GridManager gridManager;

        public MlemSpriteAnimation SpriteAnimation
        {
            get
            {
                if (spriteAnimation == null)
                {
                    spriteAnimation = GetComponentInChildren<MlemSpriteAnimation>(true);
                    if (spriteAnimation != null) SubscribeToAnimation(spriteAnimation);
                }
                return spriteAnimation;
            }
        }

        private GridManager GetGridManager()
        {
            if (gridManager == null) gridManager = FindAnyObjectByType<GridManager>();
            return gridManager;
        }

        // ============================================================
        // Tunable numbers (all read from the OperatorData asset)
        // ============================================================

        /// <summary>Whether Mlem can eat an enemy of this archetype (everything but flyers).</summary>
        public static bool CanDevourArchetype(EnemyArchetype archetype) => GetDevourPriority(archetype) >= 0;

        /// <summary>Position in the priority list (0 = first choice); -1 when it can't be eaten.</summary>
        public static int GetDevourPriority(EnemyArchetype archetype)
        {
            for (int i = 0; i < DevourPriority.Length; i++)
            {
                if (DevourPriority[i] == archetype) return i;
            }
            return -1;
        }

        /// <summary>Seconds one enemy of this archetype takes to digest, before any upgrade.</summary>
        public static float GetBaseDigestSeconds(OperatorData data, EnemyArchetype archetype)
        {
            if (data == null) return 0f;
            switch (archetype)
            {
                case EnemyArchetype.Rusher: return data.digestSecondsRusher;
                case EnemyArchetype.Grunt: return data.digestSecondsGrunt;
                case EnemyArchetype.Caster: return data.digestSecondsCaster;
                case EnemyArchetype.Tank: return data.digestSecondsTank;
                default: return 0f;
            }
        }

        /// <summary>
        /// How long digesting the swallowed enemies takes: the average of their digestion times, shortened by the 1st
        /// upgrade's reduction (which the 2nd upgrade keeps).
        /// </summary>
        public static float GetDigestDuration(OperatorData data, bool hasFirstUpgrade, IReadOnlyList<EnemyArchetype> swallowed)
        {
            if (data == null || swallowed == null || swallowed.Count == 0) return 0f;

            float total = 0f;
            for (int i = 0; i < swallowed.Count; i++) total += GetBaseDigestSeconds(data, swallowed[i]);

            float average = total / swallowed.Count;
            return hasFirstUpgrade ? average * (1f - data.digestTimeReductionUpgrade1) : average;
        }

        public static int GetMaxSwallowCount(OperatorData data, bool hasSecondUpgrade)
        {
            if (data == null) return 1;
            return Mathf.Max(1, hasSecondUpgrade ? data.devourMaxSwallowUpgrade2 : data.devourMaxSwallowBase);
        }

        /// <summary>HP recovered per second while digesting, as a fraction of max HP.</summary>
        public static float GetRegenFractionPerSecond(OperatorData data, bool hasFirstUpgrade)
        {
            if (data == null) return 0f;
            return hasFirstUpgrade ? data.digestRegenFractionPerSecondUpgrade1 : data.digestRegenFractionPerSecond;
        }

        // ============================================================
        // Lifecycle
        // ============================================================

        private void Awake()
        {
            // The generic sheet animation would fight MlemSpriteAnimation over the sprite
            var genericAnim = GetComponent<OperatorSpriteAnimation>();
            if (genericAnim != null && genericAnim.enabled) genericAnim.enabled = false;

            if (SpriteAnimation != null && !spriteAnimation.enabled) spriteAnimation.enabled = true;
        }

        private void OnDestroy()
        {
            if (spriteAnimation != null) UnsubscribeFromAnimation(spriteAnimation);
        }

        public override void Initialize(OperatorData operatorData, OperatorRarity rarity, OperatorFacing operatorFacing = OperatorFacing.Right)
        {
            base.Initialize(operatorData, rarity, operatorFacing);
            ResetState();
        }

        private void ResetState()
        {
            lashing = false;
            chomped = false;
            lashTargets.Clear();
            grabbed.Clear();
            IsDigesting = false;
            digestRemaining = 0f;
            digestTotal = 0f;
            appliedDefBonus = 0;
            appliedHpBonus = 0;
            regenAccumulator = 0f;
            regenTickTimer = 0f;
            EnemiesSwallowed = 0;
        }

        protected override void OnRetreated()
        {
            AbortLash();
            if (IsDigesting) EndDigestion();
            SpriteAnimation?.CancelAttack();
            base.OnRetreated();
        }

        private void SubscribeToAnimation(MlemSpriteAnimation anim)
        {
            anim.GrabFrameReached += OnGrabFrame;
            anim.ChompFrameReached += OnChompFrame;
            anim.AttackFinished += OnAttackFinished;
        }

        private void UnsubscribeFromAnimation(MlemSpriteAnimation anim)
        {
            anim.GrabFrameReached -= OnGrabFrame;
            anim.ChompFrameReached -= OnChompFrame;
            anim.AttackFinished -= OnAttackFinished;
        }

        protected override void Update()
        {
            if (isDeployed)
            {
                TickDigestion();
                WatchLash();
            }

            base.Update();
        }

        private void LateUpdate()
        {
            DragGrabbedEnemies();
        }

        // ============================================================
        // Targeting
        // ============================================================

        /// <summary>His own tile and the tiles his range pattern covers (own tile + 2 in front, facing included).</summary>
        private List<GridCell> GetRangeCells()
        {
            var cells = new List<GridCell>();
            GridManager grid = GetGridManager();
            if (grid == null || deployedCell == null) return cells;

            Vector2Int[] pattern = GetRangePattern();
            if (pattern != null)
            {
                GridCell[] inRange = grid.GetCellsInRange(deployedCell.GridPosition, pattern, Facing);
                if (inRange != null) cells.AddRange(inRange);
            }
            if (!cells.Contains(deployedCell)) cells.Add(deployedCell);
            return cells;
        }

        /// <summary>Enemies in range he could swallow, best target first.</summary>
        private List<EnemyBase> GetDevourCandidates()
        {
            var candidates = new List<EnemyBase>();
            if (EnemyManager.Instance == null || deployedCell == null || data == null) return candidates;

            List<EnemyBase> inRange = EnemyManager.Instance.GetEnemiesInCells(GetRangeCells(), data.position);
            for (int i = 0; i < inRange.Count; i++)
            {
                if (CanDevour(inRange[i])) candidates.Add(inRange[i]);
            }

            candidates.Sort(CompareTargets);
            return candidates;
        }

        /// <summary>Tank > Caster > Rusher > Grunt; within a type, the one furthest along the path (closest to the exit).</summary>
        private static int CompareTargets(EnemyBase a, EnemyBase b)
        {
            int byType = GetDevourPriority(a.Data.archetype).CompareTo(GetDevourPriority(b.Data.archetype));
            if (byType != 0) return byType;
            return a.DistanceToGoal.CompareTo(b.DistanceToGoal);
        }

        /// <summary>Ground enemies of an edible type that the usual attack rules allow (alive, not in a hole, no duel lock).</summary>
        private bool CanDevour(EnemyBase enemy)
        {
            if (enemy == null || enemy.IsDead || enemy.IsBeingDevoured || enemy.IsInsideHole) return false;
            if (enemy.Data == null || enemy.MovementType != EnemyMovementType.Ground) return false;
            if (!CanDevourArchetype(enemy.Data.archetype)) return false;
            return base.CanAttackTarget(enemy);
        }

        protected override EnemyBase FindTarget()
        {
            if (IsDigesting || lashing) return null;

            List<EnemyBase> candidates = GetDevourCandidates();
            return candidates.Count > 0 ? candidates[0] : null;
        }

        public override bool CanAttackTarget(EnemyBase target)
        {
            if (IsDigesting || lashing) return false;
            return CanDevour(target);
        }

        // ============================================================
        // Attack: lash, grab, drag, swallow
        // ============================================================

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed || isStunned || isBound || IsDigesting || lashing) return;

            HasFoughtInWave = true;
            DeployedDuringCurrentPreparation = false;

            // The primary target first, then the next best in range (up to the max swallow count)
            lashTargets.Clear();
            lashTargets.Add(target);
            List<EnemyBase> candidates = GetDevourCandidates();
            for (int i = 0; i < candidates.Count && lashTargets.Count < MaxSwallowCount; i++)
            {
                if (candidates[i] != target) lashTargets.Add(candidates[i]);
            }

            lashing = true;
            chomped = false;
            grabbed.Clear();
            PlayAttackSound();

            MlemSpriteAnimation anim = SpriteAnimation;
            if (anim != null && anim.HasAttackAnimation)
            {
                anim.PlayAttack(); // the animation's events drive the rest
                return;
            }

            // No attack art: run the whole sequence at once
            OnGrabFrame();
            if (grabbed.Count > 0) OnChompFrame();
            OnAttackFinished();
        }

        /// <summary>Animation event: the tongue is fully out and catches whatever is still in range.</summary>
        private void OnGrabFrame()
        {
            if (!lashing || grabbed.Count > 0) return;

            // Anything that died, left range or became untargetable since the lash began is missed
            var inRange = new HashSet<EnemyBase>(GetDevourCandidates());
            for (int i = 0; i < lashTargets.Count; i++)
            {
                EnemyBase enemy = lashTargets[i];
                if (enemy == null || !inRange.Contains(enemy) || !enemy.BeginDevour()) continue;

                var cluster = MlemSpriteAnimation.ClusterPixels;
                grabbed.Add(new GrabbedEnemy
                {
                    Enemy = enemy,
                    StartPosition = enemy.transform.position,
                    StartScale = enemy.transform.localScale,
                    ClusterOffsetPixels = cluster[Mathf.Min(grabbed.Count, cluster.Length - 1)],
                    Archetype = enemy.Data.archetype
                });
            }

            if (grabbed.Count == 0)
            {
                // Whiff: the tongue comes back empty, nothing is swallowed and no digestion starts
                SpriteAnimation?.EndAfterRetract();
                return;
            }

            grabTime = Time.time;
            MlemSpriteAnimation anim = SpriteAnimation;
            frameSeconds = anim != null ? anim.AttackFrameDuration : MlemSpriteAnimation.DefaultAttackFrameDuration;
        }

        /// <summary>
        /// The clenched tongue drags every caught enemy: it is pulled onto the grip point of the grab frame, then follows
        /// the grip points of the reel-in frames (shrinking as it goes) until it reaches his mouth on the chomp frame.
        /// Enemies caught together by the 2nd upgrade cluster a few pixels apart around the same point. They are drawn
        /// one step behind Mlem so his prongs and teeth overlap them.
        /// </summary>
        private void DragGrabbedEnemies()
        {
            if (grabbed.Count == 0 || chomped) return;

            float elapsed = Time.time - grabTime;
            int last = MlemSpriteAnimation.GripPixels.Length - 1;
            float framePosition = Mathf.Clamp(elapsed / frameSeconds, 0f, last);
            int from = Mathf.Min(Mathf.FloorToInt(framePosition), last);
            int to = Mathf.Min(from + 1, last);
            float t = Mathf.SmoothStep(0f, 1f, framePosition - from);
            Vector2 gripPixels = Vector2.Lerp(MlemSpriteAnimation.GripPixels[from], MlemSpriteAnimation.GripPixels[to], t);
            float scale = Mathf.Lerp(MlemSpriteAnimation.GripScales[from], MlemSpriteAnimation.GripScales[to], t);
            float snap = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / SnapSeconds));

            SortingGroup mlemGroup = GetComponent<SortingGroup>();
            for (int i = 0; i < grabbed.Count; i++)
            {
                EnemyBase enemy = grabbed[i].Enemy;
                if (enemy == null || enemy.IsDead) continue;

                Vector3 grip = PixelsToWorld(gripPixels + ClusterOffset(grabbed[i].ClusterOffsetPixels));
                Vector3 position = Vector3.Lerp(grabbed[i].StartPosition, grip, snap);
                position.z = grabbed[i].StartPosition.z;
                enemy.transform.position = position;
                enemy.transform.localScale = grabbed[i].StartScale * Mathf.Lerp(1f, scale, snap);

                if (mlemGroup != null)
                {
                    SortingGroup enemyGroup = enemy.GetComponent<SortingGroup>();
                    if (enemyGroup != null)
                    {
                        enemyGroup.sortingLayerID = mlemGroup.sortingLayerID;
                        enemyGroup.sortingOrder = mlemGroup.sortingOrder - 1;
                    }
                }
            }
        }

        /// <summary>The cluster offsets are written for the left-facing art, so mirror x when he faces right.</summary>
        private Vector2 ClusterOffset(Vector2 pixels)
        {
            return Facing == OperatorFacing.Right ? new Vector2(-pixels.x, pixels.y) : pixels;
        }

        /// <summary>
        /// Converts a point given in pixels from his feet (x negative toward the enemy in the left-facing art, y up from
        /// the bottom of the frame) to a world position. The art is mirrored when he faces right.
        /// </summary>
        private Vector3 PixelsToWorld(Vector2 pixelsFromFeet)
        {
            float side = Facing == OperatorFacing.Right ? -1f : 1f;
            Vector3 scale = transform.lossyScale;
            return transform.position + new Vector3(
                side * pixelsFromFeet.x / MlemSpriteAnimation.PixelsPerUnit * scale.x,
                (pixelsFromFeet.y - MlemSpriteAnimation.PivotHeightPixels) / MlemSpriteAnimation.PixelsPerUnit * scale.y,
                0f);
        }

        /// <summary>Animation event: his mouth closes. Everything on the tongue is swallowed and digestion starts.</summary>
        private void OnChompFrame()
        {
            if (!lashing || chomped || grabbed.Count == 0) return;
            chomped = true;

            Vector3 mouth = GetMouthPosition();
            bool triggerDeathEffects = data != null && data.devourTriggersDeathEffects;
            var swallowed = new List<EnemyArchetype>(grabbed.Count);
            for (int i = 0; i < grabbed.Count; i++)
            {
                EnemyBase enemy = grabbed[i].Enemy;
                if (enemy == null || enemy.IsDead) continue;

                swallowed.Add(grabbed[i].Archetype);
                enemy.CompleteDevour(triggerDeathEffects); // an instant kill, whatever its HP
            }
            grabbed.Clear();
            if (swallowed.Count == 0) return;

            EnemiesSwallowed += swallowed.Count;
            PlayAbilitySound();
            CombatFx.Sparks(mouth, new Color(1f, 0.55f, 0.45f, 1f), 8, 2.2f);
            FloatingCombatNumber.ShowText(mouth + Vector3.up * 0.3f, "GULP!", new Color(1f, 0.62f, 0.5f, 1f), this);

            StartDigestion(swallowed);
        }

        /// <summary>Animation event: the attack animation is over.</summary>
        private void OnAttackFinished()
        {
            lashing = false;
            chomped = false;
            lashTargets.Clear();
            ReleaseGrabbed();

            // A whiff leaves him ready at once; after a swallow, digestion is the cooldown
            if (!IsDigesting) attackTimer = GetAttackInterval();
        }

        /// <summary>Stunned or bound mid-lash: the tongue lets go and the attack is cancelled.</summary>
        private void WatchLash()
        {
            if (lashing && (isStunned || isBound)) AbortLash();
        }

        private void AbortLash()
        {
            if (!lashing && grabbed.Count == 0) return;

            ReleaseGrabbed();
            lashing = false;
            chomped = false;
            lashTargets.Clear();
            SpriteAnimation?.CancelAttack();
        }

        private void ReleaseGrabbed()
        {
            for (int i = 0; i < grabbed.Count; i++)
            {
                EnemyBase enemy = grabbed[i].Enemy;
                if (enemy == null || enemy.IsDead) continue;

                enemy.transform.localScale = grabbed[i].StartScale;
                enemy.CancelDevour();
            }
            grabbed.Clear();
        }

        /// <summary>World position of his mouth: the last grip point (the art faces left; it is flipped when he faces right).</summary>
        public Vector3 GetMouthPosition()
        {
            Vector2[] grips = MlemSpriteAnimation.GripPixels;
            return PixelsToWorld(grips[grips.Length - 1]);
        }

        // ============================================================
        // Digestion
        // ============================================================

        private void StartDigestion(IReadOnlyList<EnemyArchetype> swallowed)
        {
            digestTotal = GetDigestDuration(data, HasFirstUpgrade, swallowed);
            if (digestTotal <= 0f) return;

            digestRemaining = digestTotal;
            IsDigesting = true;
            regenAccumulator = 0f;
            regenTickTimer = 0f;

            // Bonus DEF and max HP for as long as it lasts (tracked by source, so the details panel lists them)
            appliedDefBonus = Mathf.RoundToInt(currentDEF * data.digestDefBonusFraction);
            appliedHpBonus = Mathf.RoundToInt(maxHP * data.digestMaxHpBonusFraction);
            ModifyDEF(appliedDefBonus, DigestSource);
            ModifyMaxHP(appliedHpBonus, DigestSource, healByDelta: false); // a higher cap, not extra HP

            SpriteAnimation?.SetDigesting(true);
        }

        private void TickDigestion()
        {
            if (IsCombatPaused) return;
            AdvanceDigestion(Time.deltaTime);
        }

        /// <summary>Moves digestion forward by <paramref name="dt"/> seconds (Update does this every frame; tests call it directly).</summary>
        public void AdvanceDigestion(float dt)
        {
            if (!IsDigesting) return;

            digestRemaining -= dt;

            float regen = GetRegenFractionPerSecond(data, HasFirstUpgrade);
            if (regen > 0f && currentHP > 0)
            {
                regenAccumulator += maxHP * regen * dt;
                regenTickTimer += dt;
                if (regenTickTimer >= RegenTickSeconds)
                {
                    regenTickTimer = 0f;
                    int heal = Mathf.FloorToInt(regenAccumulator);
                    if (heal > 0)
                    {
                        regenAccumulator -= heal;
                        HealSelf(heal);
                    }
                }
            }

            if (digestRemaining <= 0f) EndDigestion();
        }

        /// <summary>Digestion is over: the bonus goes away (current HP is clamped to the normal max) and he can attack again.</summary>
        private void EndDigestion()
        {
            IsDigesting = false;
            digestRemaining = 0f;
            digestTotal = 0f;
            regenAccumulator = 0f;

            ModifyDEF(-appliedDefBonus, DigestSource);
            ModifyMaxHP(-appliedHpBonus, DigestSource);
            appliedDefBonus = 0;
            appliedHpBonus = 0;

            SpriteAnimation?.SetDigesting(false);
            if (!lashing) attackTimer = GetAttackInterval(); // ready right away
        }

        public override void CollectStatEffects(List<StatEffect> effects)
        {
            base.CollectStatEffects(effects);

            // The DEF and max HP bonus are listed by the base class (they are tracked under DigestSource)
            float regen = GetRegenFractionPerSecond(data, HasFirstUpgrade);
            if (IsDigesting && regen > 0f)
                effects.Add(new StatEffect(DigestSource, $"Regen {regen * 100f:0.#}% HP/s", "HP"));
        }
    }
}
