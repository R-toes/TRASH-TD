using UnityEngine;

namespace TrashTD.Enemies
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class ExplodingBugSpriteAnimation : MonoBehaviour, IEnemySpriteFrames
    {
        [SerializeField] private Sprite[] frames;
        [SerializeField] private float frameDuration = 0.12f;

        private SpriteRenderer spriteRenderer;
        private float frameTimer;
        private int currentFrame;

        public Sprite[] Frames => frames;
        public float FrameDuration => frameDuration;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();

            if (frames == null || frames.Length != 8 ||
                frameDuration <= 0f)
            {
                Debug.LogError($"{nameof(ExplodingBugSpriteAnimation)} requires exactly 8 sprites and a positive frame duration.", this);
                enabled = false;
                return;
            }

            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i] == null)
                {
                    Debug.LogError($"{nameof(ExplodingBugSpriteAnimation)}: frame {i} is null.", this);
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
            frameTimer += Time.deltaTime;
            if (frameTimer < frameDuration)
                return;

            int elapsedFrames = Mathf.FloorToInt(frameTimer / frameDuration);
            frameTimer %= frameDuration;
            currentFrame = (currentFrame + elapsedFrames) % frames.Length;
            spriteRenderer.sprite = frames[currentFrame];
        }
    }
}
