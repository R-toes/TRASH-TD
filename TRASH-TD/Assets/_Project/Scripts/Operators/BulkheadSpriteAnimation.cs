using UnityEngine;

namespace TrashTD.Operators
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class BulkheadSpriteAnimation : MonoBehaviour, IOperatorSpriteFrames
    {
        private const float FrameInterval = 0.25f;

        [SerializeField] private Sprite[] frames;

        public Sprite[] Frames => frames;
        public float FrameDuration => FrameInterval;

        private SpriteRenderer spriteRenderer;
        private float frameTimer;
        private int currentFrame;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (frames == null || frames.Length != 2 || frames[0] == null || frames[1] == null)
            {
                Debug.LogError("BulkheadSpriteAnimation requires exactly two assigned sprites.", this);
                enabled = false;
                return;
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
