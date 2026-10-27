using UnityEngine;

namespace TrashTD.Operators
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class DeadeyeSpriteAnimation : MonoBehaviour, IOperatorSpriteFrames
    {
        // 8-frame idle loop (1 s per cycle).
        private const float FrameInterval = 0.125f;

        [SerializeField] private Sprite[] frames;

        public Sprite[] Frames => frames;
        public float FrameDuration => FrameInterval;

        private SpriteRenderer spriteRenderer;
        private float frameTimer;
        private int currentFrame;

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

        private void Update()
        {
            frameTimer += Time.deltaTime;
            if (frameTimer < FrameInterval) return;

            int elapsedFrames = Mathf.FloorToInt(frameTimer / FrameInterval);
            frameTimer %= FrameInterval;
            currentFrame = (currentFrame + elapsedFrames) % frames.Length;
            spriteRenderer.sprite = frames[currentFrame];
        }
    }
}
