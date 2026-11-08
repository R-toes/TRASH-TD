using UnityEngine;

namespace TrashTD.Operators
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class PompothSpriteAnimation : MonoBehaviour, IOperatorSpriteFrames
    {
        // 6-frame idle loop (~0.75 s per cycle — slightly faster to feel snappy).
        private const float FrameInterval = 0.125f;

        [SerializeField] private Sprite[] frames;

        public Sprite[] Frames => frames;
        public float FrameDuration => FrameInterval;

        private SpriteRenderer spriteRenderer;
        private float frameTimer;
        private int currentFrame;

        public int CurrentFrameIndex => currentFrame;
        public float NormalizedFrameTime => frameTimer / FrameInterval;

        public bool SetFramesPreservingPhase(Sprite[] replacementFrames)
        {
            if (frames == null || replacementFrames == null || replacementFrames.Length != frames.Length)
            {
                Debug.LogError("Pompoth animation sheets must have the same number of frames.", this);
                return false;
            }

            for (int i = 0; i < replacementFrames.Length; i++)
            {
                if (replacementFrames[i] == null)
                {
                    Debug.LogError($"Pompoth animation replacement frame {i} is not assigned.", this);
                    return false;
                }
            }

            frames = replacementFrames;
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
                spriteRenderer.sprite = frames[currentFrame % frames.Length];
            return true;
        }

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (frames == null || frames.Length < 2)
            {
                Debug.LogError("PompothSpriteAnimation requires at least two assigned sprites.", this);
                enabled = false;
                return;
            }

            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i] == null)
                {
                    Debug.LogError($"PompothSpriteAnimation: frame {i} is not assigned.", this);
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
