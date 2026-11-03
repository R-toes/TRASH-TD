using UnityEngine;

namespace TrashTD.Operators
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class BulkheadSpriteAnimation : MonoBehaviour, IOperatorSpriteFrames
    {
        private const float FrameInterval = 0.125f;
        private const float AttackFrameInterval = 0.07f;

        public const int StrikeFrame = 5;
        public const float WindupTime = StrikeFrame * AttackFrameInterval;

        [SerializeField] private Sprite[] frames;
        [SerializeField] private Sprite[] attackFrames;

        public Sprite[] Frames => frames;
        public Sprite[] AttackFrames => attackFrames;
        public float FrameDuration => FrameInterval;
        public bool HasAttackAnimation => attackFrames != null && attackFrames.Length > StrikeFrame && attackFrames[StrikeFrame] != null;
        public bool IsAttacking => attacking;
        public bool IsBeforeStrike => attacking && attackFrame < StrikeFrame;

        private SpriteRenderer spriteRenderer;
        private float frameTimer;
        private int currentFrame;
        private bool attacking;
        private int attackFrame;
        private float attackFrameTimer;
        private float windupFrameInterval = AttackFrameInterval;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (frames == null || frames.Length < 2)
            {
                Debug.LogError("BulkheadSpriteAnimation requires at least two assigned sprites.", this);
                enabled = false;
                return;
            }

            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i] == null)
                {
                    Debug.LogError($"BulkheadSpriteAnimation: frame {i} is not assigned.", this);
                    enabled = false;
                    return;
                }
            }

            spriteRenderer.sprite = frames[0];
        }

        public void BeginAttack(float timeUntilStrike)
        {
            if (!enabled || !HasAttackAnimation || attacking) return;

            int windupFrames = Mathf.Clamp(Mathf.CeilToInt(timeUntilStrike / AttackFrameInterval), 1, StrikeFrame);
            attacking = true;
            attackFrame = StrikeFrame - windupFrames;
            windupFrameInterval = Mathf.Max(0.01f, timeUntilStrike / windupFrames);
            attackFrameTimer = 0f;
            ShowAttackFrame();
        }

        public void Strike()
        {
            if (!enabled || !HasAttackAnimation) return;
            if (attacking && attackFrame == StrikeFrame) return;

            attacking = true;
            attackFrame = StrikeFrame;
            attackFrameTimer = 0f;
            ShowAttackFrame();
        }

        public void CancelAttack()
        {
            if (!attacking) return;
            attacking = false;
            if (spriteRenderer != null && frames != null && frames.Length > 0)
                spriteRenderer.sprite = frames[currentFrame];
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
                float interval = attackFrame < StrikeFrame ? windupFrameInterval : AttackFrameInterval;
                if (attackFrameTimer < interval) return;

                attackFrameTimer -= interval;
                attackFrame++;
                if (attackFrame >= attackFrames.Length || attackFrames[attackFrame] == null)
                {
                    attacking = false;
                    frameTimer = 0f;
                    spriteRenderer.sprite = frames[currentFrame];
                    return;
                }

                ShowAttackFrame();
            }
        }

        private void ShowAttackFrame()
        {
            if (spriteRenderer != null) spriteRenderer.sprite = attackFrames[attackFrame];
        }
    }
}
