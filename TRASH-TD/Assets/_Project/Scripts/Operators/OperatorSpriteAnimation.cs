using UnityEngine;

namespace TrashTD.Operators
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class OperatorSpriteAnimation : MonoBehaviour
    {
        [SerializeField] private Sprite[] frames;
        [Min(0.01f)]
        [SerializeField] private float frameDuration = 0.25f;

        private SpriteRenderer spriteRenderer;
        private float frameTimer;
        private int currentFrame;

        public void Configure(Sprite[] animationFrames)
        {
            frames = animationFrames;
        }

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            frameDuration = Mathf.Max(0.01f, frameDuration);

            if (ShouldResolveFramesFromTexture())
            {
                TryResolveFramesFromTexture();
            }

            if (!HasValidFrames())
            {
                Debug.LogError($"{nameof(OperatorSpriteAnimation)} requires at least two assigned sprites.", this);
                enabled = false;
                return;
            }

            spriteRenderer.sprite = frames[0];
        }

        private bool HasValidFrames()
        {
            if (frames == null || frames.Length < 2) return false;
            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i] == null) return false;
            }
            return true;
        }

        private bool ShouldResolveFramesFromTexture()
        {
            if (spriteRenderer == null || spriteRenderer.sprite == null || spriteRenderer.sprite.texture == null)
            {
                return false;
            }

            int expectedFrameCount = GetExpectedFrameCount();
            if (frames == null || frames.Length != expectedFrameCount)
            {
                return true;
            }

            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i] == null)
                {
                    return true;
                }
            }

            return false;
        }

        private int GetExpectedFrameCount()
        {
            if (spriteRenderer == null || spriteRenderer.sprite == null || spriteRenderer.sprite.texture == null)
            {
                return 2;
            }

            Texture2D texture = spriteRenderer.sprite.texture;
            int frameCount = Mathf.RoundToInt((float)texture.width / texture.height);
            if (texture.name.ToLowerInvariant().Contains("basurocket"))
            {
                frameCount = 4;
            }

            return Mathf.Max(2, frameCount);
        }

        private void TryResolveFramesFromTexture()
        {
            if (spriteRenderer == null || spriteRenderer.sprite == null || spriteRenderer.sprite.texture == null)
            {
                return;
            }

            Texture2D texture = spriteRenderer.sprite.texture;
            float ppu = spriteRenderer.sprite.pixelsPerUnit > 0f ? spriteRenderer.sprite.pixelsPerUnit : 32f;
            Vector2 pivot = new Vector2(0.5f, 0.5f);

            int frameCount = GetExpectedFrameCount();
            float frameWidth = texture.height > 0 ? texture.height : (texture.width / (float)frameCount);
            float frameHeight = texture.height;

            frames = new Sprite[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                frames[i] = Sprite.Create(texture, new Rect(i * frameWidth, 0, frameWidth, frameHeight), pivot, ppu);
            }
        }

        private void Update()
        {
            frameTimer += Time.deltaTime;
            if (frameTimer < frameDuration) return;

            int elapsedFrames = Mathf.FloorToInt(frameTimer / frameDuration);
            frameTimer %= frameDuration;
            currentFrame = (currentFrame + elapsedFrames) % frames.Length;
            spriteRenderer.sprite = frames[currentFrame];
        }
    }
}
