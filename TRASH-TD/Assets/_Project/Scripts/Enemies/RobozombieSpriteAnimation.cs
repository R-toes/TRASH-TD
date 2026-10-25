using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Enemies
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class RobozombieSpriteAnimation : MonoBehaviour, IEnemySpriteFrames
    {
        [SerializeField] private Sprite[] frames;
        [SerializeField] private float frameDuration = 0.125f;

        private SpriteRenderer spriteRenderer;
        private EnemyBase enemyBase;
        private float frameTimer;
        private int currentFrame;

        public Sprite[] Frames => frames;
        public float FrameDuration => frameDuration;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            enemyBase = GetComponent<EnemyBase>();

            if (frames == null || frames.Length == 0 || frameDuration <= 0f)
            {
                Debug.LogError($"{nameof(RobozombieSpriteAnimation)} requires animation frames and a positive frame duration.", this);
                enabled = false;
                return;
            }

            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i] == null)
                {
                    Debug.LogError($"{nameof(RobozombieSpriteAnimation)}: frame {i} is null.", this);
                    enabled = false;
                    return;
                }
            }

            currentFrame = 0;
            frameTimer = 0f;
            spriteRenderer.sprite = frames[currentFrame];
        }

        private void Update()
        {
            // If EnemyBase is already driving sprite animation from EnemyData.animationFrames,
            // don't run a second competing animation loop.
            if (enemyBase != null && enemyBase.Data != null &&
                enemyBase.Data.animationFrames != null && enemyBase.Data.animationFrames.Length > 1)
            {
                return;
            }

            // Pause walk animation when dead, frozen, or stunned
            if (enemyBase != null && (enemyBase.IsDead || enemyBase.IsFrozen || enemyBase.IsStunned))
            {
                return;
            }

            frameTimer += Time.deltaTime;
            if (frameTimer < frameDuration)
                return;

            int elapsedFrames = Mathf.FloorToInt(frameTimer / frameDuration);
            frameTimer -= elapsedFrames * frameDuration;
            currentFrame = (currentFrame + elapsedFrames) % frames.Length;

            SpriteRenderer targetRenderer = GetActiveRenderer();
            if (targetRenderer != null)
            {
                targetRenderer.sprite = frames[currentFrame];
            }
        }

        private SpriteRenderer GetActiveRenderer()
        {
            if (spriteRenderer != null && spriteRenderer.enabled)
                return spriteRenderer;

            Transform visualChild = transform.Find("EnemyVisual");
            if (visualChild != null)
            {
                var childRenderer = visualChild.GetComponent<SpriteRenderer>();
                if (childRenderer != null && childRenderer.enabled)
                {
                    spriteRenderer = childRenderer;
                    return spriteRenderer;
                }
            }

            return spriteRenderer;
        }
    }
}
