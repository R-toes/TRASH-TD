using UnityEngine;
using TrashTD.Audio;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Operators;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Melee air enemy: single-target physical strike on the tile below or an orthogonally adjacent tile.
    /// Hunts ranged operators (Snipers, Medics), then Defenders, then Guards; ignores Casters.
    /// Stats (high DEF, low RES, Grunt speed) live on Enemy_Flying_Melee.asset.
    /// Swoop: the sprite flies 0.75 tiles above its tile (visualOffset), so a strike on the tile below it on screen
    /// looked like a ranged attack. Each attack now dives the sprite onto the target and climbs back up. Range,
    /// damage and attack timing are unchanged: the dive starts just before the attack is due, so it lands as the
    /// damage does. A strike with no warning (first contact) still deals damage at once; only the claw marks wait
    /// for a short dive.
    /// </summary>
    public sealed class MeleeFlyingEnemy : AirCombatEnemy
    {
        // Whole swoop ≈ 0.75 s (dive + hold + rise), inside the 1.15 s attack interval
        private const float DiveTime = 0.24f;
        private const float LateDiveTime = 0.12f;     // dive when the strike comes with no warning
        private const float StrikeHoldTime = 0.09f;
        private const float RiseTime = 0.42f;
        private const float StrikeHeightTiles = 0.3f; // the sprite's centre stops this far above the operator
        // Melee operators can hit it from halfway down the dive until it's 40% of the way back up
        private const float MeleeReachDiveFrom = 0.5f;
        private const float MeleeReachRiseUntil = 0.4f;
        // Hold point against a ground operator: just past the shared tile edge (0.5), so the rat counts as on the
        // operator's tile for melee range instead of sitting exactly on the border
        private const float GroundHoldFraction = 0.52f;

        private enum SwoopPhase { None, Diving, Holding, Rising }

        private SwoopPhase phase;
        private float phaseTimer;
        private float phaseDuration;
        private Vector3 swoopFrom;               // local positions of the EnemyVisual child
        private Vector3 swoopTo;
        private OperatorBase pendingSwipeTarget; // claw marks shown when the dive lands
        private bool strikePending;              // an attack has dealt damage; its claws and sound come on landing
        private Transform visual;

        protected override bool IsTargetable(OperatorBase op)
        {
            return op.OperatorClass != OperatorClass.Caster;
        }

        protected override int GetTargetPriority(OperatorBase op)
        {
            if (op.OperatorClass == OperatorClass.Sniper || op.OperatorClass == OperatorClass.Medic) return 0;
            if (op.OperatorClass == OperatorClass.Defender) return 1;
            return 2;
        }

        protected override bool IsTargetInRange(OperatorBase op)
        {
            return IsOnOrAdjacent(op);
        }

        /// <summary>Low in its swoop, the rat is in reach of ground melee operators.</summary>
        public override bool IsWithinMeleeReach
        {
            get
            {
                if (IsDead || phase == SwoopPhase.None) return false;
                float k = phaseDuration > 0f ? phaseTimer / phaseDuration : 1f;
                return phase == SwoopPhase.Holding ||
                       (phase == SwoopPhase.Diving && k >= MeleeReachDiveFrom) ||
                       (phase == SwoopPhase.Rising && k <= MeleeReachRiseUntil);
            }
        }

        /// <summary>
        /// Fighting a ground operator, the rat holds where a blocked ground enemy would: on the edge between its tile
        /// and the operator's (EnemyBase.OnBlocked), instead of creeping over the operator. It isn't blocked: it
        /// attacks on its own timer and flies on when the target is gone. Against high-ground operators it keeps the
        /// shared hover-and-creep (AirCombatEnemy).
        /// </summary>
        protected override void MoveAlongPath()
        {
            OperatorBase target = CurrentAirTarget;
            if (!TryGetGroundHoldPoint(target, out Vector3 hold))
            {
                base.MoveAlongPath();
                return;
            }

            if ((transform.position - hold).sqrMagnitude > 0.0001f)
                transform.position = Vector3.MoveTowards(transform.position, hold, currentMoveSpeed * Time.deltaTime);
            UpdateFacing(target.transform.position.x - transform.position.x);
        }

        private bool TryGetGroundHoldPoint(OperatorBase target, out Vector3 hold)
        {
            hold = default;
            GridManager grid = GridManagerCache;
            if (target == null || grid == null || path == null || path.Count == 0 || target.DeployedCell == null) return false;
            TileType tileType = target.DeployedCell.TileType;
            if (tileType != TileType.LowGround && tileType != TileType.EnemyPath) return false;

            Vector2Int targetTile = target.DeployedCell.GridPosition;
            Vector3 targetCenter = target.DeployedCell.WorldPosition;

            // On the path: the edge it enters the operator's tile through (nearest occurrence, as OnBlocked picks it)
            int best = -1;
            int last = Mathf.Min(path.Count - 1, currentPathIndex + 2);
            for (int i = Mathf.Max(1, currentPathIndex - 1); i <= last; i++)
            {
                if (grid.WorldToGridPosition(path[i]) != targetTile) continue;
                if (best < 0 || Mathf.Abs(i - currentPathIndex) < Mathf.Abs(best - currentPathIndex)) best = i;
            }

            Vector3 from;
            if (best > 0)
            {
                from = path[best - 1];
                currentPathIndex = best;
            }
            else if (!TryGetAdjacentWaypoint(grid, targetTile, out from))
            {
                return false;
            }

            hold = Vector3.Lerp(from, targetCenter, GroundHoldFraction);
            hold.z = transform.position.z;
            return true;
        }

        /// <summary>Beside the path: the rat's current or previous waypoint, if it's orthogonally next to the operator.</summary>
        private bool TryGetAdjacentWaypoint(GridManager grid, Vector2Int targetTile, out Vector3 waypoint)
        {
            waypoint = default;
            for (int i = Mathf.Min(currentPathIndex, path.Count - 1); i >= Mathf.Max(0, currentPathIndex - 1); i--)
            {
                Vector2Int tile = grid.WorldToGridPosition(path[i]);
                if (Mathf.Abs(tile.x - targetTile.x) + Mathf.Abs(tile.y - targetTile.y) != 1) continue;
                waypoint = path[i];
                return true;
            }
            return false;
        }

        protected override void Update()
        {
            base.Update();
            if (IsDead) return;

            StartDiveIfDue();
            UpdateSwoop();
        }

        protected override void PerformAirAttack(OperatorBase target)
        {
            if (Visual == null)
            {
                PlayStrikeSound();
                PlayAirMeleeAttack(target);
                return;
            }

            // Damage lands now, exactly as before; only the claw marks and their sound wait for the talons to arrive.
            target.TryTakeAttackDamage(CurrentATK, Data.damageType, this);

            if (phase == SwoopPhase.Holding)
            {
                PlayStrikeSound();
                PlayAirMeleeSwipe(target);
                return;
            }

            strikePending = true;
            pendingSwipeTarget = target;
            if (phase != SwoopPhase.Diving) BeginDive(target, LateDiveTime);
        }

        // Every attack plays its sound with the claw marks (PlayStrikeSound), not as the dive starts
        protected override void PlayAirAttackSound() { }

        /// <summary>Talon Rat's own Attack clip if it gets one, else the shared melee hit.</summary>
        private void PlayStrikeSound() => PlayAttackSound(SfxId.GameplayMeleeAttack);

        /// <summary>Starts the dive when the next attack is due within DiveTime and there is a target.</summary>
        private void StartDiveIfDue()
        {
            if (phase != SwoopPhase.None || IsFrozen || IsStunned || Visual == null) return;

            OperatorBase target = CurrentAirTarget;
            if (target == null) return;

            float timeUntilAttack = GetEffectiveAttackInterval() - attackTimer;
            if (timeUntilAttack <= 0f || timeUntilAttack > DiveTime) return;

            BeginDive(target, timeUntilAttack);
        }

        private void BeginDive(OperatorBase target, float duration)
        {
            Vector3 strikePoint = target.transform.position + Vector3.up * (CellSize * StrikeHeightTiles);
            Vector3 local = transform.InverseTransformPoint(strikePoint);
            local.z = HoverPosition.z;

            swoopFrom = Visual.localPosition;
            swoopTo = local;
            SetPhase(SwoopPhase.Diving, Mathf.Max(0.01f, duration));
        }

        private void UpdateSwoop()
        {
            if (phase == SwoopPhase.None) return;
            Transform sprite = Visual;
            if (sprite == null)
            {
                phase = SwoopPhase.None;
                return;
            }

            phaseTimer += Time.deltaTime;
            float k = Mathf.Clamp01(phaseTimer / phaseDuration);

            switch (phase)
            {
                case SwoopPhase.Diving:
                    sprite.localPosition = Vector3.Lerp(swoopFrom, swoopTo, k * k); // speeds up into the strike
                    if (k < 1f) return;
                    if (strikePending)
                    {
                        // The sound plays even if the hit already took the operator out
                        PlayStrikeSound();
                        if (pendingSwipeTarget != null) PlayAirMeleeSwipe(pendingSwipeTarget);
                    }
                    strikePending = false;
                    pendingSwipeTarget = null;
                    SetPhase(SwoopPhase.Holding, StrikeHoldTime);
                    break;

                case SwoopPhase.Holding:
                    if (k < 1f) return;
                    swoopFrom = sprite.localPosition;
                    SetPhase(SwoopPhase.Rising, RiseTime);
                    break;

                case SwoopPhase.Rising:
                    float eased = 1f - (1f - k) * (1f - k);
                    sprite.localPosition = Vector3.Lerp(swoopFrom, HoverPosition, eased);
                    if (k >= 1f) phase = SwoopPhase.None;
                    break;
            }
        }

        private void SetPhase(SwoopPhase next, float duration)
        {
            phase = next;
            phaseTimer = 0f;
            phaseDuration = duration;
        }

        /// <summary>Where the sprite normally flies: visualOffset, mirrored with the facing (as EnemyBase does).</summary>
        private Vector3 HoverPosition
        {
            get
            {
                Vector3 offset = Data != null ? Data.visualOffset : Vector3.zero;
                if (IsFacingLeft) offset.x = -offset.x;
                return offset;
            }
        }

        /// <summary>The raised "EnemyVisual" child that draws the flyer (null without a visualOffset).</summary>
        private Transform Visual
        {
            get
            {
                if (visual == null) visual = transform.Find("EnemyVisual");
                return visual;
            }
        }
    }
}
