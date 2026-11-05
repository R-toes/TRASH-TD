using UnityEngine;

namespace TrashTD.Operators
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class QiFuSpriteAnimation : MonoBehaviour, IOperatorSpriteFrames
    {
        public const int AttackFrameCount = 11;
        public const int StrikeFrameIndex = 5;
        public const float WindupDuration = 0.35f;
        public const float StrikePointX = 22f;
        public const float StrikePointY = 20f;

        private static readonly float[] AttackFrameDurations =
        {
            0.10f, 0.10f, 0.05f, 0.05f, 0.05f, 0.05f,
            0.05f, 0.05f, 0.03f, 0.04f, 0.10f
        };

        [SerializeField] private Sprite[] idleFrames;
        [SerializeField] private Sprite[] attackFrames;
        [SerializeField, Min(0.01f)] private float idleFrameDuration = 0.1f;

        public Sprite[] Frames => idleFrames;
        public Sprite[] AttackFrames => attackFrames;
        public float FrameDuration => idleFrameDuration;
        public bool IsWindingUp => windingUp;
        public bool IsRecovering => recovering;
        public int CurrentAttackFrame => attackFrame;
        public bool HasAttackAnimation => attackFrames != null && attackFrames.Length == AttackFrameCount;

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
                Debug.LogError("QiFuSpriteAnimation requires at least two assigned idle sprites.", this);
                enabled = false;
                return;
            }

            if (!HasValidFrames(attackFrames, AttackFrameCount) || attackFrames.Length != AttackFrameCount)
            {
                Debug.LogError($"QiFuSpriteAnimation requires exactly {AttackFrameCount} assigned attack sprites.", this);
                enabled = false;
                return;
            }

            idleFrameDuration = Mathf.Max(0.01f, idleFrameDuration);
            ShowIdleFrame();
        }

        private static bool HasValidFrames(Sprite[] sprites, int minimumCount)
        {
            if (sprites == null || sprites.Length < minimumCount) return false;
            for (int i = 0; i < sprites.Length; i++)
                if (sprites[i] == null) return false;
            return true;
        }

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

        public void BeginAttack(float timeUntilStrike)
        {
            if (!enabled || !HasAttackAnimation || windingUp || recovering || timeUntilStrike <= 0f)
                return;

            windingUp = true;
            attackFrame = 0;
            attackTimer = 0f;
            windupPlaybackScale = WindupDuration / Mathf.Min(timeUntilStrike, WindupDuration);
            lastTransitionFrame = Time.frameCount;
            ShowAttackFrame();
        }

        public void Strike()
        {
            if (!enabled || !HasAttackAnimation) return;

            windingUp = false;
            recovering = true;
            attackFrame = StrikeFrameIndex;
            attackTimer = 0f;
            lastTransitionFrame = Time.frameCount;
            ShowAttackFrame();
        }

        public Vector3 GetStrikePointWorldPosition()
        {
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null || spriteRenderer.sprite == null) return transform.position;

            Sprite strikeSprite = spriteRenderer.sprite;
            Vector2 pivot = strikeSprite.pivot;
            float pixelsPerUnit = strikeSprite.pixelsPerUnit;
            Vector3 localPoint = new Vector3(
                (StrikePointX - pivot.x) / pixelsPerUnit,
                (StrikePointY - pivot.y) / pixelsPerUnit,
                0f);

            if (spriteRenderer.flipX) localPoint.x = -localPoint.x;
            return spriteRenderer.transform.TransformPoint(localPoint);
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
            while (windingUp && attackFrame < StrikeFrameIndex)
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
