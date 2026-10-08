using UnityEngine;

namespace TrashTD.Operators
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class OperatorSpriteAnimation : MonoBehaviour
    {
        private const float FrameDuration = 0.25f;

        [SerializeField] private Sprite[] frames;

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

            // If frames are missing or incomplete, auto-resolve frames from the sprite's texture sheet
            if (!HasValidFrames())
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

        private void TryResolveFramesFromTexture()
        {
            if (spriteRenderer == null || spriteRenderer.sprite == null || spriteRenderer.sprite.texture == null)
            {
                return;
            }

            Texture2D texture = spriteRenderer.sprite.texture;
            float ppu = spriteRenderer.sprite.pixelsPerUnit > 0f ? spriteRenderer.sprite.pixelsPerUnit : 32f;
            Vector2 pivot = new Vector2(0.5f, 0.5f);

            int frameCount = Mathf.Max(2, Mathf.FloorToInt((float)texture.width / texture.height));
            if (texture.name.ToLowerInvariant().Contains("basurocket"))
            {
                frameCount = 4;
            }

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
            if (frameTimer < FrameDuration) return;

            int elapsedFrames = Mathf.FloorToInt(frameTimer / FrameDuration);
            frameTimer %= FrameDuration;
            currentFrame = (currentFrame + elapsedFrames) % frames.Length;
            spriteRenderer.sprite = frames[currentFrame];
        }
    }
}
