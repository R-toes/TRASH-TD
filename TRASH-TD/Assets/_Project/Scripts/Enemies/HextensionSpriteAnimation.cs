using UnityEngine;

namespace TrashTD.Enemies
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class HextensionSpriteAnimation : MonoBehaviour, IEnemySpriteFrames
    {
        [SerializeField] private Sprite[] frames;
        [SerializeField] private float frameDuration = 0.125f;

        private SpriteRenderer spriteRenderer;
        private float frameTimer;
        private int currentFrame;

        public Sprite[] Frames => frames;
        public float FrameDuration => frameDuration;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();

            if (frames == null || frames.Length == 0)
            {
                var enemy = GetComponent<EnemyBase>();
                if (enemy != null && enemy.Data != null && enemy.Data.animationFrames != null && enemy.Data.animationFrames.Length > 0)
                {
                    frames = enemy.Data.animationFrames;
                    if (enemy.Data.animationFrameRate > 0)
                    {
                        frameDuration = 1f / enemy.Data.animationFrameRate;
                    }
                }
            }

            if (frames != null && frames.Length > 0 && frames[0] != null)
            {
                currentFrame = 0;
                frameTimer = 0f;
                spriteRenderer.sprite = frames[currentFrame];
            }
        }

        private void Update()
        {
            if (frames == null || frames.Length <= 1 || frameDuration <= 0f)
                return;

            frameTimer += Time.deltaTime;
            if (frameTimer < frameDuration)
                return;

            int elapsedFrames = Mathf.FloorToInt(frameTimer / frameDuration);
            frameTimer %= frameDuration;
            currentFrame = (currentFrame + elapsedFrames) % frames.Length;

            if (frames[currentFrame] != null)
            {
                spriteRenderer.sprite = frames[currentFrame];
            }
        }
    }
}

