using UnityEngine;

namespace TrashTD.Operators
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class OperatorSpriteAnimation : MonoBehaviour
    {
        [SerializeField] private Sprite[] frames;
        [Min(0.01f)]
        [SerializeField] private float frameDuration = 0.25f;

        public float FrameDuration
        {
            get => frameDuration;
            set => frameDuration = Mathf.Max(0.01f, value);
        }

        public Sprite[] Frames => frames;

        private SpriteRenderer spriteRenderer;
        private float frameTimer;
        private int currentFrame;
        private bool isCustomConfigured;

        public void Configure(Sprite[] animationFrames)
        {
            frames = animationFrames;
            isCustomConfigured = true;
            currentFrame = 0;
            frameTimer = 0f;
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }
            if (spriteRenderer != null && frames != null && frames.Length > 0 && frames[0] != null)
            {
                spriteRenderer.sprite = frames[0];
            }
        }

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            frameDuration = Mathf.Max(0.01f, frameDuration);

            if (spriteRenderer != null && spriteRenderer.sprite != null && spriteRenderer.sprite.texture != null)
            {
                string texName = spriteRenderer.sprite.texture.name.ToLowerInvariant();
                if (texName.Contains("castiel") && Mathf.Approximately(frameDuration, 0.25f))
                {
                    frameDuration = 0.1f;
                }
            }

            if (ShouldResolveFramesFromTexture())
            {
                TryResolveFramesFromTexture();
            }

            if (HasValidFrames() && spriteRenderer != null)
            {
                spriteRenderer.sprite = frames[0];
            }
        }

        private void Start()
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (isCustomConfigured || GetComponent<ResicochetOperator>() != null) return;

            if (!HasValidFrames() || (spriteRenderer != null && spriteRenderer.sprite == null))
            {
                Sprite fallback = null;
                if (TryGetComponent<OperatorBase>(out var op) && op != null && op.Data != null)
                {
                    fallback = op.Data.portrait;
                }
                RefreshFromOperatorDataOrRenderer(fallback);
            }
        }

        public void RefreshFromOperatorDataOrRenderer(Sprite fallbackSprite = null)
        {
            if (isCustomConfigured || GetComponent<ResicochetOperator>() != null) return;

            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (spriteRenderer == null) return;

            if (spriteRenderer.sprite == null && fallbackSprite != null)
            {
                spriteRenderer.sprite = fallbackSprite;
            }

            if (ShouldResolveFramesFromTexture())
            {
                TryResolveFramesFromTexture();
            }

            if (HasValidFrames())
            {
                if (spriteRenderer.sprite == null)
                {
                    spriteRenderer.sprite = frames[0];
                }
                enabled = true;
            }
            else if (fallbackSprite != null)
            {
                frames = new Sprite[] { fallbackSprite, fallbackSprite };
                spriteRenderer.sprite = fallbackSprite;
                enabled = true;
            }
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
            if (isCustomConfigured) return false;
            if (GetComponent<ResicochetOperator>() != null) return false;

            if (spriteRenderer == null || spriteRenderer.sprite == null || spriteRenderer.sprite.texture == null)
            {
                return false;
            }

            string texName = spriteRenderer.sprite.texture.name.ToLowerInvariant();
            if (texName.Contains("resicochet")) return false;

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
            string texName = texture.name.ToLowerInvariant();
            if (texName.Contains("resicochet"))
            {
                return 4;
            }

            int frameCount = Mathf.RoundToInt((float)texture.width / texture.height);
            if (texName.Contains("basurocket") || texName.Contains("progeny") || texName.Contains("stag-ger") || texName.Contains("stagger") || texName.Contains("thornchin") || texName.Contains("coalesce") || texName.Contains("ignisaur"))
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
            float frameWidth = (texture.height > 0 && texture.height * frameCount <= texture.width)
                ? texture.height
                : (texture.width / (float)frameCount);
            float frameHeight = Mathf.Min(texture.height, frameWidth > 0 ? frameWidth : texture.height);

            frames = new Sprite[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                float x = i * frameWidth;
                if (x + frameWidth > texture.width || frameHeight > texture.height) break;
                frames[i] = Sprite.Create(texture, new Rect(x, texture.height - frameHeight, frameWidth, frameHeight), pivot, ppu);
            }
        }

        private void Update()
        {
            if (frames == null || frames.Length == 0) return;

            frameTimer += Time.deltaTime;
            if (frameTimer < frameDuration) return;

            int elapsedFrames = Mathf.FloorToInt(frameTimer / frameDuration);
            frameTimer %= frameDuration;
            currentFrame = (currentFrame + elapsedFrames) % frames.Length;
            if (spriteRenderer != null && frames[currentFrame] != null)
            {
                spriteRenderer.sprite = frames[currentFrame];
            }
        }
    }
}
