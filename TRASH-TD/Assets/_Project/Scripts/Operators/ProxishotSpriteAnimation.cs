using UnityEngine;

namespace TrashTD.Operators
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class ProxishotSpriteAnimation : MonoBehaviour, IOperatorSpriteFrames
    {
        private static readonly float[] AttackFrameDurations =
        {
            0.13f, 0.11f, 0.11f, 0.12f, 0.07f, 0.10f, 0.11f, 0.14f
        };

        public const int AttackFrameCount = 8;
        public const int FireFrame = 4;
        public const float WindupDuration = 0.47f;
        private const float IdleFrameDuration = 0.125f; // 8 idle frames: a 1 s loop (0.25 s per frame made it a slow 2 s one)

        [SerializeField] private Sprite[] frames;
        [SerializeField] private Sprite[] attackFrames;

        public Sprite[] Frames => frames;
        public Sprite[] AttackFrames => attackFrames;
        public float FrameDuration => IdleFrameDuration;
        public bool HasAttackAnimation => HasValidFrames(attackFrames);
        public bool IsWindingUp => windingUp;
        public bool IsRecovering => recovering;

        private SpriteRenderer spriteRenderer;
        private int idleFrame;
        private float idleTimer;
        private int attackFrame;
        private float attackTimer;
        private float windupPlaybackScale = 1f;
        private bool windingUp;
        private bool recovering;

        public static float GetAttackFrameDuration(int frameIndex) =>
            frameIndex >= 0 && frameIndex < AttackFrameDurations.Length
                ? AttackFrameDurations[frameIndex]
                : 0f;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (!HasIdleFrames(frames))
            {
                Debug.LogError("ProxishotSpriteAnimation requires at least two idle sprites.", this);
                enabled = false;
                return;
            }

            if (!HasAttackAnimation)
            {
                Debug.LogError($"ProxishotSpriteAnimation requires exactly {AttackFrameCount} attack sprites.", this);
                enabled = false;
                return;
            }

            spriteRenderer.sprite = frames[0];
        }

        public void BeginAttack(float timeUntilShot)
        {
            if (!enabled || !HasAttackAnimation || windingUp || recovering || timeUntilShot <= 0f)
                return;

            windingUp = true;
            attackFrame = 0;
            attackTimer = 0f;
            windupPlaybackScale = WindupDuration / Mathf.Min(timeUntilShot, WindupDuration);
            ShowAttackFrame();
        }

        public void ShotFired()
        {
            if (!enabled || !HasAttackAnimation) return;

            if (recovering && attackFrame == FireFrame) return;
            windingUp = false;
            recovering = true;
            attackFrame = FireFrame;
            attackTimer = 0f;
            ShowAttackFrame();
        }

        public void CancelAttack()
        {
            windingUp = false;
            recovering = false;
            attackTimer = 0f;
            idleTimer = 0f;
            ShowIdleFrame();
        }

        private void Update()
        {
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
            if (idleTimer < IdleFrameDuration) return;

            int elapsedFrames = Mathf.FloorToInt(idleTimer / IdleFrameDuration);
            idleTimer %= IdleFrameDuration;
            idleFrame = (idleFrame + elapsedFrames) % frames.Length;
            ShowIdleFrame();
        }

        private void UpdateWindup(float deltaTime)
        {
            attackTimer += deltaTime * windupPlaybackScale;
            while (windingUp && attackFrame < FireFrame)
            {
                float duration = AttackFrameDurations[attackFrame];
                if (attackTimer < duration) return;

                attackTimer -= duration;
                attackFrame++;
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
            if (spriteRenderer != null && attackFrames != null &&
                attackFrame >= 0 && attackFrame < attackFrames.Length)
                spriteRenderer.sprite = attackFrames[attackFrame];
        }

        private void ShowIdleFrame()
        {
            if (spriteRenderer != null && frames != null && frames.Length > 0)
                spriteRenderer.sprite = frames[idleFrame];
        }

        private static bool HasIdleFrames(Sprite[] sprites)
        {
            if (sprites == null || sprites.Length < 2) return false;
            for (int i = 0; i < sprites.Length; i++)
                if (sprites[i] == null) return false;
            return true;
        }

        private static bool HasValidFrames(Sprite[] sprites)
        {
            if (sprites == null || sprites.Length != AttackFrameCount) return false;
            for (int i = 0; i < sprites.Length; i++)
                if (sprites[i] == null) return false;
            return true;
        }
    }
}
