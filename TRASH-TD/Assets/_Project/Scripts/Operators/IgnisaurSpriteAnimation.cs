using System;
using UnityEngine;

namespace TrashTD.Operators
{
    /// <summary>
    /// Ignisaur's sprite playback: a looping idle (32x32 frames), a one-shot ranged throw and a one-shot
    /// close-range claw swipe (both 64x32 frames). Every sheet has its own hand-tuned per-frame timings.
    ///
    /// Alignment: the attack frames are 64 px wide with the body drawn exactly where it is in the 32 px idle
    /// frames (idle pixel x = attack pixel x - 24). The attack sheets use pivot (0.625, 0.5) against the idle
    /// pivot (0.5, 0.5), so the body stays put when switching animations. SpriteRenderer.flipX mirrors around the
    /// pivot, so the same holds when facing right. Frames are never blended: an attack starts on its frame 0
    /// and ends back on idle frame 0.
    ///
    /// The strike moments are frame events: the throw releases the fireball when frame ThrowEventFrame
    /// is shown, and the swipe lands its damage when frame SwipeEventFrame is shown.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class IgnisaurSpriteAnimation : MonoBehaviour, IOperatorSpriteFrames
    {
        public const int ThrowEventFrame = 4;
        public const int SwipeEventFrame = 5;

        // Seconds per frame, in order. The holds and quick frames are intentional wind-ups and fast swings.
        public static readonly float[] IdleDurations = { 0.150f, 0.120f, 0.120f, 0.140f, 0.160f, 0.120f, 0.110f, 0.120f };
        public static readonly float[] ThrowDurations = { 0.140f, 0.130f, 0.220f, 0.070f, 0.160f, 0.120f, 0.110f, 0.140f };
        public static readonly float[] SwipeDurations = { 0.130f, 0.190f, 0.060f, 0.060f, 0.060f, 0.170f, 0.110f, 0.110f, 0.140f };
        public static readonly float[] FireballFlyDurations = { 0.08f, 0.08f, 0.08f, 0.08f };
        public static readonly float[] FireballImpactDurations = { 0.07f, 0.08f, 0.09f, 0.14f };

        /// <summary>
        /// Where the fireball leaves the throw frame, in world units from the transform (the sprite's centre-height
        /// pivot), with the art facing left as drawn: the fireball is drawn at pixel (12, 18) of the 64x32 frame and
        /// the body pivot sits at (40, 16). Mirror x when facing right.
        /// </summary>
        public static readonly Vector2 FireballSpawnOffset = new Vector2(-0.875f, -0.0625f);

        private enum Mode { Idle, Throw, Swipe }

        [SerializeField] private Sprite[] idleFrames;
        [SerializeField] private Sprite[] throwFrames;
        [SerializeField] private Sprite[] swipeFrames;
        [SerializeField] private Sprite[] fireballFlyFrames;
        [SerializeField] private Sprite[] fireballImpactFrames;

        public Sprite[] Frames => idleFrames;
        /// <summary>Average idle frame time, for code that plays the idle loop at one fixed rate (Reclaimers' Intel).</summary>
        public float FrameDuration => 1.04f / 8f;
        public Sprite[] FireballFlyFrames => fireballFlyFrames;
        public Sprite[] FireballImpactFrames => fireballImpactFrames;
        public bool IsAttacking => mode != Mode.Idle;
        public bool HasThrowAnimation => IsComplete(throwFrames, ThrowDurations.Length);
        public bool HasSwipeAnimation => IsComplete(swipeFrames, SwipeDurations.Length);
        public bool HasFireballFrames => IsComplete(fireballFlyFrames, FireballFlyDurations.Length) &&
                                         IsComplete(fireballImpactFrames, FireballImpactDurations.Length);
        public int CurrentFrame => frame;

        private SpriteRenderer spriteRenderer;
        private Mode mode = Mode.Idle;
        private int frame;
        private float timer;
        private Action onEvent;

        private static bool IsComplete(Sprite[] frames, int expected)
        {
            if (frames == null || frames.Length != expected) return false;
            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i] == null) return false;
            }
            return true;
        }

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (IsComplete(idleFrames, IdleDurations.Length))
            {
                spriteRenderer.sprite = idleFrames[0];
            }
            else
            {
                Debug.LogError($"{nameof(IgnisaurSpriteAnimation)} needs {IdleDurations.Length} idle frames.", this);
                enabled = false;
            }
        }

        /// <summary>Plays the throw once. <paramref name="onReleaseFrame"/> runs when the release frame is shown.</summary>
        public bool PlayThrow(Action onReleaseFrame) => Play(Mode.Throw, throwFrames, onReleaseFrame);

        /// <summary>Plays the claw swipe once. <paramref name="onStrikeFrame"/> runs when the strike frame is shown.</summary>
        public bool PlaySwipe(Action onStrikeFrame) => Play(Mode.Swipe, swipeFrames, onStrikeFrame);

        private bool Play(Mode newMode, Sprite[] frames, Action eventCallback)
        {
            if (!enabled || mode != Mode.Idle) return false;
            if (newMode == Mode.Throw ? !HasThrowAnimation : !HasSwipeAnimation) return false;

            mode = newMode;
            frame = 0;
            timer = 0f;
            onEvent = eventCallback;
            spriteRenderer.sprite = frames[0];
            return true;
        }

        /// <summary>Drops an attack in progress without firing its event and returns to idle frame 0 (retreat, stun).</summary>
        public void CancelAttack()
        {
            if (mode == Mode.Idle) return;
            ReturnToIdle();
        }

        private void ReturnToIdle()
        {
            mode = Mode.Idle;
            onEvent = null;
            frame = 0;
            timer = 0f;
            spriteRenderer.sprite = idleFrames[0];
        }

        private void Update()
        {
            timer += Time.deltaTime;

            if (mode == Mode.Idle)
            {
                while (timer >= IdleDurations[frame])
                {
                    timer -= IdleDurations[frame];
                    frame = (frame + 1) % idleFrames.Length;
                    spriteRenderer.sprite = idleFrames[frame];
                }
                return;
            }

            Sprite[] frames = mode == Mode.Throw ? throwFrames : swipeFrames;
            float[] durations = mode == Mode.Throw ? ThrowDurations : SwipeDurations;
            int eventFrame = mode == Mode.Throw ? ThrowEventFrame : SwipeEventFrame;

            while (mode != Mode.Idle && timer >= durations[frame])
            {
                timer -= durations[frame];
                frame++;

                if (frame >= frames.Length)
                {
                    // Every animation starts and ends on the same ready pose, so this is an instant cut to idle frame 0.
                    ReturnToIdle();
                    return;
                }

                spriteRenderer.sprite = frames[frame];
                if (frame == eventFrame)
                {
                    Action fire = onEvent;
                    onEvent = null;
                    fire?.Invoke();
                }
            }
        }
    }
}
