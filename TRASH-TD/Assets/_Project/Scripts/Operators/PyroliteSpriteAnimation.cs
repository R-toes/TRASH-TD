using UnityEngine;

namespace TrashTD.Operators
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class PyroliteSpriteAnimation : MonoBehaviour, IOperatorSpriteFrames
    {
        public const int AttackFrameCount = 13;
        public const int FiringFrameIndex = 8;
        public const float AttackWindupSeconds = 0.64f;

        private static readonly float[] AttackFrameDurations =
        {
            0.10f, 0.09f, 0.06f, 0.05f, 0.08f, 0.09f, 0.09f,
            0.08f, 0.06f, 0.08f, 0.08f, 0.09f, 0.07f
        };

        [SerializeField] private Sprite[] idleFrames;
        [SerializeField] private Sprite[] attackFrames;
        [SerializeField, Min(0.01f)] private float idleFrameDuration = 0.25f;

        public Sprite[] Frames => idleFrames;
        public Sprite[] AttackFrames => attackFrames;
        public float FrameDuration => idleFrameDuration;
        public bool HasAttackAnimation => attackFrames != null && attackFrames.Length == AttackFrameCount;
        public bool IsWindingUp => windingUp;
        public bool IsRecovering => recovering;
        public int CurrentAttackFrame => attackFrame;
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

        private SpriteRenderer spriteRenderer;
        private int idleFrame;
        private float idleTimer;
        private bool windingUp;
        private bool recovering;
        private int attackFrame;
        private float attackTimer;
        private float windupPlaybackScale = 1f;
        private int lastTransitionFrame = -1;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (idleFrames == null || idleFrames.Length < 2)
            {
                Debug.LogError("PyroliteSpriteAnimation requires at least two idle sprites.", this);
                enabled = false;
                return;
            }

            for (int i = 0; i < idleFrames.Length; i++)
            {
                if (idleFrames[i] == null)
                {
                    Debug.LogError($"PyroliteSpriteAnimation: idle frame {i} is not assigned.", this);
                    enabled = false;
                    return;
                }
            }

            if (!HasAttackAnimation)
            {
                Debug.LogError($"PyroliteSpriteAnimation requires exactly {AttackFrameCount} attack sprites.", this);
                enabled = false;
                return;
            }

            for (int i = 0; i < attackFrames.Length; i++)
            {
                if (attackFrames[i] == null)
                {
                    Debug.LogError($"PyroliteSpriteAnimation: attack frame {i} is not assigned.", this);
                    enabled = false;
                    return;
                }
            }

            idleFrameDuration = Mathf.Max(0.01f, idleFrameDuration);
            spriteRenderer.sprite = idleFrames[0];
        }

        public static float GetAttackFrameDuration(int frameIndex)
        {
            return frameIndex >= 0 && frameIndex < AttackFrameDurations.Length
                ? AttackFrameDurations[frameIndex]
                : 0f;
        }

        public void BeginAttack(float timeUntilShot)
        {
            if (!enabled || !HasAttackAnimation || windingUp || recovering || timeUntilShot <= 0f)
                return;

            windingUp = true;
            attackFrame = 0;
            attackTimer = 0f;
            windupPlaybackScale = AttackWindupSeconds / Mathf.Min(timeUntilShot, AttackWindupSeconds);
            lastTransitionFrame = Time.frameCount;
            ShowAttackFrame();
        }

        public void Fire()
        {
            if (!enabled || !HasAttackAnimation) return;

            windingUp = false;
            recovering = true;
            attackFrame = FiringFrameIndex;
            attackTimer = 0f;
            lastTransitionFrame = Time.frameCount;
            ShowAttackFrame();
        }

        public void CancelWindup()
        {
            if (!windingUp) return;

            windingUp = false;
            attackTimer = 0f;
            lastTransitionFrame = Time.frameCount;
            ShowIdleFrame();
        }

        public void CancelAttack()
        {
            windingUp = false;
            recovering = false;
            attackTimer = 0f;
            lastTransitionFrame = Time.frameCount;
            ShowIdleFrame();
        }

        private void Update()
        {
            if (Time.frameCount == lastTransitionFrame) return;

            if (windingUp)
            {
                UpdateWindup(Time.deltaTime);
                return;
            }

            if (recovering)
            {
                UpdateRecovery(Time.deltaTime);
                return;
            }

            idleTimer += Time.deltaTime;
            if (idleTimer < idleFrameDuration) return;

            int elapsedFrames = Mathf.FloorToInt(idleTimer / idleFrameDuration);
            idleTimer %= idleFrameDuration;
            idleFrame = (idleFrame + elapsedFrames) % idleFrames.Length;
            ShowIdleFrame();
        }

        private void UpdateWindup(float deltaTime)
        {
            attackTimer += deltaTime * windupPlaybackScale;
            while (windingUp && attackFrame < FiringFrameIndex)
            {
                float duration = AttackFrameDurations[attackFrame];
                if (attackTimer < duration) return;

                attackTimer -= duration;
                if (attackFrame == FiringFrameIndex - 1)
                {
                    attackTimer = 0f;
                    return;
                }

                attackFrame++;
                ShowAttackFrame();
            }
        }

        private void UpdateRecovery(float deltaTime)
        {
            attackTimer += deltaTime;
            while (recovering)
            {
                float duration = AttackFrameDurations[attackFrame];
                if (attackTimer < duration) return;

                attackTimer -= duration;
                attackFrame++;
                if (attackFrame >= AttackFrameCount)
                {
                    recovering = false;
                    idleTimer = 0f;
                    ShowIdleFrame();
                    return;
                }

                ShowAttackFrame();
            }
        }

        private void ShowAttackFrame()
        {
            if (spriteRenderer != null) spriteRenderer.sprite = attackFrames[attackFrame];
        }

        private void ShowIdleFrame()
        {
            if (spriteRenderer != null && idleFrames != null && idleFrames.Length > 0)
                spriteRenderer.sprite = idleFrames[idleFrame];
        }
    }
}
