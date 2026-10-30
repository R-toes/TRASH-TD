using UnityEngine;

namespace TrashTD.Operators
{
    /// <summary>
    /// Deadeye's sprite: an 8-frame idle loop ("Deadeye-Sheet") and a 10-frame attack ("Deadeye Attack Sheet":
    /// frames 0-4 raise and aim the arm, 5 is the shot, 6-9 recover). DeadeyeOperator starts the attack early so
    /// the shot frame lands on the real shot, leaving combat timing unchanged. A shot with no warning (an enemy
    /// walking into range of a charged attack) jumps straight to the shot frame.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class DeadeyeSpriteAnimation : MonoBehaviour, IOperatorSpriteFrames
    {
        // 8-frame idle loop (1 s per cycle).
        private const float FrameInterval = 0.125f;
        // 10 attack frames in 0.7 s, inside Deadeye's 0.75 s attack interval
        private const float AttackFrameInterval = 0.07f;

        /// <summary>The attack frame that shows the shot (recoil, eyes shut).</summary>
        public const int FireFrame = 5;

        /// <summary>How long the wind-up before the shot frame lasts when it can start early.</summary>
        public const float WindupTime = FireFrame * AttackFrameInterval;

        [SerializeField] private Sprite[] frames;
        [SerializeField] private Sprite[] attackFrames;

        public Sprite[] Frames => frames;
        public float FrameDuration => FrameInterval;

        /// <summary>True when the attack sheet is assigned (otherwise Deadeye just idles while shooting).</summary>
        public bool HasAttackAnimation => attackFrames != null && attackFrames.Length > FireFrame && attackFrames[FireFrame] != null;
        public bool IsAttacking => attacking;

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
                Debug.LogError("DeadeyeSpriteAnimation requires at least two assigned sprites.", this);
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

            spriteRenderer.sprite = frames[0];
        }

        /// <summary>
        /// Starts the wind-up so the shot frame shows in <paramref name="timeUntilShot"/> seconds. With less time than
        /// the full wind-up, it starts partway in (and paces those frames to fit).
        /// </summary>
        public void BeginAttack(float timeUntilShot)
        {
            if (!enabled || !HasAttackAnimation || attacking) return;

            int windupFrames = Mathf.Clamp(Mathf.CeilToInt(timeUntilShot / AttackFrameInterval), 1, FireFrame);
            attacking = true;
            attackFrame = FireFrame - windupFrames;
            windupFrameInterval = Mathf.Max(0.01f, timeUntilShot / windupFrames);
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
                float interval = attackFrame < FireFrame ? windupFrameInterval : AttackFrameInterval;
                if (attackFrameTimer < interval) return;

                attackFrameTimer -= interval;
                attackFrame++;
                if (attackFrame >= attackFrames.Length || attackFrames[attackFrame] == null)
                {
                    // Recovered: pick the idle loop back up where it left off
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
