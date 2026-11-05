using UnityEngine;

namespace TrashTD.Operators
{
    /// <summary>
    /// Deadeye's sprite: an 8-frame idle loop ("Deadeye-Sheet") and a 10-frame one-shot attack
    /// ("Deadeye Attack Sheet"). DeadeyeOperator starts the attack early so frame 5 lands on the real shot,
    /// leaving combat timing unchanged. A shot with no warning jumps straight to the launch frame.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class DeadeyeSpriteAnimation : MonoBehaviour, IOperatorSpriteFrames
    {
        // 8-frame idle loop (1 s per cycle).
        private const float FrameInterval = 0.125f;

        private static readonly float[] AttackFrameDurations =
        {
            0.12f, 0.07f, 0.06f, 0.10f, 0.11f,
            0.06f, 0.06f, 0.08f, 0.08f, 0.12f
        };

        public const int FireFrame = 5;
        public const int AttackFrameCount = 10;
        public const float WindupTime = 0.46f;
        public const float FirePointOffsetXPixels = -14f;
        public const float FirePointOffsetYPixels = 1f;
        public const float PixelsPerUnit = 32f;

        [SerializeField] private Sprite[] frames;
        [SerializeField] private Sprite[] attackFrames;

        public Sprite[] Frames => frames;
        public Sprite[] AttackFrames => attackFrames;
        public float FrameDuration => FrameInterval;

        public bool HasAttackAnimation => attackFrames != null &&
                                          attackFrames.Length == AttackFrameCount &&
                                          System.Array.TrueForAll(attackFrames, frame => frame != null);
        public bool IsAttacking => attacking;
        public int CurrentAttackFrame => attackFrame;
        public Transform FirePoint
        {
            get
            {
                EnsureFirePoint();
                RefreshFirePointFacing();
                return firePoint;
            }
        }

        private SpriteRenderer spriteRenderer;
        private Transform firePoint;
        private float frameTimer;
        private int currentFrame;

        private bool attacking;
        private int attackFrame;
        private float attackFrameTimer;
        private float windupPlaybackScale = 1f;

        public static float GetAttackFrameDuration(int frameIndex)
        {
            return frameIndex >= 0 && frameIndex < AttackFrameDurations.Length
                ? AttackFrameDurations[frameIndex]
                : 0f;
        }

        public static float AttackDuration
        {
            get
            {
                float duration = 0f;
                for (int i = 0; i < AttackFrameDurations.Length; i++)
                    duration += AttackFrameDurations[i];
                return duration;
            }
        }

        public void RefreshFirePointFacing()
        {
            EnsureFirePoint();
            if (firePoint == null) return;

            float direction = spriteRenderer != null && spriteRenderer.flipX ? -1f : 1f;
            firePoint.localPosition = new Vector3(
                direction * FirePointOffsetXPixels / PixelsPerUnit,
                FirePointOffsetYPixels / PixelsPerUnit,
                0f);
        }

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (frames == null || frames.Length < 2)
            {
                Debug.LogError("DeadeyeSpriteAnimation requires at least two assigned sprites.", this);
                enabled = false;
                return;
            }

            if (!HasAttackAnimation)
            {
                Debug.LogError($"DeadeyeSpriteAnimation requires exactly {AttackFrameCount} assigned attack sprites.", this);
                enabled = false;
                return;
            }

            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i] == null)
                {
                    Debug.LogError($"DeadeyeSpriteAnimation: frame {i} is not assigned.", this);
                    enabled = false;
                    return;
                }
            }

            EnsureFirePoint();
            RefreshFirePointFacing();
            spriteRenderer.sprite = frames[0];
        }

        /// <summary>
        /// Starts the wind-up so the shot frame shows in <paramref name="timeUntilShot"/> seconds. With less time than
        /// the full wind-up, it starts partway in (and paces those frames to fit).
        /// </summary>
        public void BeginAttack(float timeUntilShot)
        {
            if (!enabled || !HasAttackAnimation || attacking || timeUntilShot <= 0f) return;

            attacking = true;
            currentFrame = 0;
            frameTimer = 0f;
            float availableTime = Mathf.Min(timeUntilShot, WindupTime);
            float remainingWindup = WindupTime;
            attackFrame = 0;
            for (int i = 0; i < FireFrame; i++)
            {
                if (remainingWindup < availableTime) break;
                attackFrame = i;
                remainingWindup -= AttackFrameDurations[i];
            }

            float selectedWindupDuration = 0f;
            for (int i = attackFrame; i < FireFrame; i++)
                selectedWindupDuration += AttackFrameDurations[i];
            windupPlaybackScale = selectedWindupDuration / availableTime;
            attackFrameTimer = 0f;
            ShowAttackFrame();
        }

        /// <summary>The shot was fired: shows the shot frame now, unless the wind-up has just reached it.</summary>
        public void ShotFired()
        {
            if (!enabled || !HasAttackAnimation) return;
            if (attacking && attackFrame == FireFrame) return;

            attacking = true;
            attackFrame = FireFrame;
            attackFrameTimer = 0f;
            ShowAttackFrame();
        }

        /// <summary>Back to the idle loop at once (retreat).</summary>
        public void CancelAttack()
        {
            if (!attacking) return;
            attacking = false;
            if (spriteRenderer != null && frames != null && frames.Length > 0) spriteRenderer.sprite = frames[currentFrame];
        }

        private void Update()
        {
            if (attacking)
            {
                UpdateAttack();
                return;
            }

            frameTimer += Time.deltaTime;
            if (frameTimer < FrameInterval) return;

            int elapsedFrames = Mathf.FloorToInt(frameTimer / FrameInterval);
            frameTimer %= FrameInterval;
            currentFrame = (currentFrame + elapsedFrames) % frames.Length;
            spriteRenderer.sprite = frames[currentFrame];
        }

        private void UpdateAttack()
        {
            attackFrameTimer += Time.deltaTime;
            while (attacking)
            {
                float interval = AttackFrameDurations[attackFrame];
                if (attackFrame < FireFrame)
                    interval /= windupPlaybackScale;
                if (attackFrameTimer < interval) return;

                attackFrameTimer -= interval;
                attackFrame++;
                if (attackFrame >= attackFrames.Length || attackFrames[attackFrame] == null)
                {
                    attacking = false;
                    currentFrame = 0;
                    frameTimer = 0f;
                    spriteRenderer.sprite = frames[0];
                    return;
                }

                ShowAttackFrame();
            }
        }

        private void ShowAttackFrame()
        {
            if (spriteRenderer != null) spriteRenderer.sprite = attackFrames[attackFrame];
        }

        private void EnsureFirePoint()
        {
            if (firePoint != null) return;

            firePoint = transform.Find("FirePoint");
            if (firePoint == null)
            {
                var firePointObject = new GameObject("FirePoint");
                firePoint = firePointObject.transform;
                firePoint.SetParent(transform, false);
            }
        }
    }
}
