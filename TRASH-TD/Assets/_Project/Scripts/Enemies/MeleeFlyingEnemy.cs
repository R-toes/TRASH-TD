using UnityEngine;
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
        private const float DiveTime = 0.16f;
        private const float LateDiveTime = 0.08f;     // dive when the strike comes with no warning
        private const float StrikeHoldTime = 0.06f;
        private const float RiseTime = 0.3f;
        private const float StrikeHeightTiles = 0.3f; // the sprite's centre stops this far above the operator

        private enum SwoopPhase { None, Diving, Holding, Rising }

        private SwoopPhase phase;
        private float phaseTimer;
        private float phaseDuration;
        private Vector3 swoopFrom;               // local positions of the EnemyVisual child
        private Vector3 swoopTo;
        private OperatorBase pendingSwipeTarget; // claw marks shown when the dive lands
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
                PlayAirMeleeAttack(target);
                return;
            }

            // Damage lands now, exactly as before; only the claw marks wait for the talons to arrive.
            target.TryTakeAttackDamage(CurrentATK, Data.damageType, this);

            if (phase == SwoopPhase.Holding)
            {
                PlayAirMeleeSwipe(target);
                return;
            }

            pendingSwipeTarget = target;
            if (phase != SwoopPhase.Diving) BeginDive(target, LateDiveTime);
        }

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
                    if (pendingSwipeTarget != null && pendingSwipeTarget.IsDeployed) PlayAirMeleeSwipe(pendingSwipeTarget);
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
