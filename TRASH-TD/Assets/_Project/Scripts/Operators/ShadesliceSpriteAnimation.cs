using UnityEngine;

namespace TrashTD.Operators
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class ShadesliceSpriteAnimation : MonoBehaviour, IOperatorSpriteFrames
    {
        public const int AttackFrameCount = 14;
        public const int ImpactFrameIndex = 8;
        public const float WindupSeconds = 0.45f;

        private static readonly float[] AttackFrameDurations =
        {
            0.08f, 0.05f, 0.05f, 0.07f, 0.10f, 0.03f, 0.03f,
            0.04f, 0.05f, 0.09f, 0.07f, 0.04f, 0.06f, 0.09f
        };

        [SerializeField] private Sprite[] idleFrames;
        [SerializeField] private Sprite[] attackFrames;
        [SerializeField, Min(0.01f)] private float idleFrameDuration = 0.1f;

        public Sprite[] Frames => idleFrames;
        public Sprite[] AttackFrames => attackFrames;
        public float FrameDuration => idleFrameDuration;
        public bool HasAttackAnimation => HasValidFrames(attackFrames, AttackFrameCount) &&
                                          attackFrames.Length == AttackFrameCount;
        public bool IsWindingUp => windingUp;
        public bool IsRecovering => recovering;
        public int CurrentAttackFrame => attackFrame;
        public static float AttackDuration => SumDurations(0, AttackFrameCount);

        private SpriteRenderer spriteRenderer;
        private int idleFrame;
        private float idleTimer;
        private bool windingUp;
        private bool recovering;
        private int attackFrame;
        private float attackTimer;
        private float windupPlaybackScale = 1f;
        private int lastTransitionFrame = -1;

        public void Configure(Sprite[] idle, Sprite[] attack)
        {
            idleFrames = idle;
            attackFrames = attack;
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            idleFrame = 0;
            idleTimer = 0f;
            CancelAttack();
        }

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (!HasValidFrames(idleFrames, 2))
            {
                Debug.LogError("ShadesliceSpriteAnimation requires at least two assigned idle sprites.", this);
                enabled = false;
                return;
            }

            if (!HasAttackAnimation)
            {
                Debug.LogError($"ShadesliceSpriteAnimation requires exactly {AttackFrameCount} assigned attack sprites.", this);
                enabled = false;
                return;
            }

            idleFrameDuration = Mathf.Max(0.01f, idleFrameDuration);
            spriteRenderer.sprite = idleFrames[0];
        }

        public static float GetAttackFrameDuration(int frameIndex) =>
            frameIndex >= 0 && frameIndex < AttackFrameDurations.Length
                ? AttackFrameDurations[frameIndex]
                : 0f;

        public void BeginAttack(float timeUntilImpact)
        {
            if (!enabled || !HasAttackAnimation || windingUp || recovering || timeUntilImpact <= 0f)
                return;

            windingUp = true;
            attackFrame = 0;
            attackTimer = 0f;
            windupPlaybackScale = WindupSeconds / Mathf.Min(timeUntilImpact, WindupSeconds);
            lastTransitionFrame = Time.frameCount;
            ShowAttackFrame();
        }

        public void BeginImpact()
        {
            if (!enabled || !windingUp) return;

            windingUp = false;
            recovering = true;
            attackFrame = ImpactFrameIndex;
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
            while (windingUp && attackFrame < ImpactFrameIndex)
            {
                float duration = AttackFrameDurations[attackFrame];
                if (attackTimer < duration) return;

                attackTimer -= duration;
                attackFrame++;
                if (attackFrame < ImpactFrameIndex)
                    ShowAttackFrame();
            }
        }

        private void UpdateRecovery(float deltaTime)
        {
            attackTimer += deltaTime;
            while (recovering)
            {
                if (attackTimer < AttackFrameDurations[attackFrame]) return;

                attackTimer -= AttackFrameDurations[attackFrame];
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

        private static float SumDurations(int start, int end)
        {
            float duration = 0f;
            for (int i = start; i < end; i++)
                duration += AttackFrameDurations[i];
            return duration;
        }

        private static bool HasValidFrames(Sprite[] sprites, int minimumCount)
        {
            if (sprites == null || sprites.Length < minimumCount) return false;
            for (int i = 0; i < sprites.Length; i++)
                if (sprites[i] == null) return false;
            return true;
        }
    }
}
