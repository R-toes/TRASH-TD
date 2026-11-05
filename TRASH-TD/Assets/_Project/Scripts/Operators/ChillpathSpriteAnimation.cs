using UnityEngine;

namespace TrashTD.Operators
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class ChillpathSpriteAnimation : MonoBehaviour, IOperatorSpriteFrames
    {
        public const int AttackFrameCount = 12;
        public const int FormFrameIndex = 8;
        public const int CastFrameIndex = 9;
        public const float CastWindupSeconds = 0.74f;

        private static readonly float[] AttackFrameDurations =
        {
            0.10f, 0.09f, 0.08f, 0.08f, 0.08f, 0.08f,
            0.08f, 0.08f, 0.07f, 0.08f, 0.08f, 0.09f
        };

        private const float MuzzleOffsetXPixels = -1.5f;
        private const float MuzzleOffsetYPixels = 14.5f;

        [SerializeField] private Sprite[] idleFrames;
        [SerializeField] private Sprite[] attackFrames;
        [SerializeField, Min(0.01f)] private float idleFrameDuration = 0.15f;

        public Sprite[] Frames => idleFrames;
        public Sprite[] AttackFrames => attackFrames;
        public float FrameDuration => idleFrameDuration;
        public bool HasAttackAnimation => attackFrames != null && attackFrames.Length == AttackFrameCount;
        public bool IsWindingUp => windingUp;
        public bool IsRecovering => recovering;
        public int CurrentAttackFrame => attackFrame;
        public Transform Muzzle
        {
            get
            {
                if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
                SetMuzzleFacing(spriteRenderer != null && spriteRenderer.flipX);
                return muzzle;
            }
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

        private SpriteRenderer spriteRenderer;
        private Transform muzzle;
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
            FindMuzzle();
            idleFrame = 0;
            idleTimer = 0f;
            CancelAttack();
        }

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            FindMuzzle();

            if (!HasValidFrames(idleFrames, 2))
            {
                Debug.LogError("ChillpathSpriteAnimation requires at least two assigned idle sprites.", this);
                enabled = false;
                return;
            }

            if (!HasValidFrames(attackFrames, AttackFrameCount) || attackFrames.Length != AttackFrameCount)
            {
                Debug.LogError($"ChillpathSpriteAnimation requires exactly {AttackFrameCount} assigned attack sprites.", this);
                enabled = false;
                return;
            }

            if (muzzle == null)
            {
                Debug.LogError("ChillpathSpriteAnimation requires a child named Muzzle.", this);
                enabled = false;
                return;
            }

            idleFrameDuration = Mathf.Max(0.01f, idleFrameDuration);
            spriteRenderer.sprite = idleFrames[0];
            SetMuzzleFacing(false);
        }

        public static float GetAttackFrameDuration(int frameIndex) =>
            frameIndex >= 0 && frameIndex < AttackFrameDurations.Length
                ? AttackFrameDurations[frameIndex]
                : 0f;

        public void BeginAttack(float timeUntilCast)
        {
            if (!enabled || !HasAttackAnimation || windingUp || recovering || timeUntilCast <= 0f)
                return;

            windingUp = true;
            attackFrame = 0;
            attackTimer = 0f;
            windupPlaybackScale = CastWindupSeconds / Mathf.Min(timeUntilCast, CastWindupSeconds);
            lastTransitionFrame = Time.frameCount;
            ShowAttackFrame();
        }

        public void Cast()
        {
            if (!enabled || !HasAttackAnimation) return;

            windingUp = false;
            recovering = true;
            attackFrame = CastFrameIndex;
            attackTimer = 0f;
            lastTransitionFrame = Time.frameCount;
            ShowAttackFrame();
        }

        public void SetMuzzleFacing(bool mirrorHorizontally)
        {
            FindMuzzle();
            if (muzzle == null) return;

            float direction = mirrorHorizontally ? -1f : 1f;
            muzzle.localPosition = new Vector3(
                direction * (MuzzleOffsetXPixels / 32f),
                MuzzleOffsetYPixels / 32f,
                0f);
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
            while (windingUp && attackFrame <= FormFrameIndex)
            {
                float duration = AttackFrameDurations[attackFrame];
                if (attackTimer < duration) return;

                attackTimer -= duration;
                if (attackFrame == FormFrameIndex)
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

        private void FindMuzzle()
        {
            if (muzzle == null)
            {
                Transform child = transform.Find("Muzzle");
                if (child != null) muzzle = child;
            }
        }

        private static bool HasValidFrames(Sprite[] sprites, int minimumCount)
        {
            if (sprites == null || sprites.Length < minimumCount) return false;
            for (int i = 0; i < sprites.Length; i++)
                if (sprites[i] == null) return false;
            return true;
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
