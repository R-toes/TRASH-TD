using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Enemies
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class JunkernautSpriteAnimation : MonoBehaviour, IEnemySpriteFrames
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
                return;
            }

            currentFrame = 0;
            frameTimer = 0f;
            if (frames[0] != null)
            {
                spriteRenderer.sprite = frames[0];
            }
        }

        private void Update()
        {
            if (enemyBase != null && enemyBase.Data != null &&
                enemyBase.Data.animationFrames != null && enemyBase.Data.animationFrames.Length > 1)
            {
                return;
            }

            if (enemyBase != null && (enemyBase.IsDead || enemyBase.IsFrozen || enemyBase.IsStunned))
            {
                return;
            }

            if (frames == null || frames.Length <= 1) return;

            frameTimer += Time.deltaTime;
            if (frameTimer >= frameDuration)
            {
                frameTimer -= frameDuration;
                currentFrame = (currentFrame + 1) % frames.Length;
                if (frames[currentFrame] != null)
                {
                    spriteRenderer.sprite = frames[currentFrame];
                }
            }
        }
    }
}

