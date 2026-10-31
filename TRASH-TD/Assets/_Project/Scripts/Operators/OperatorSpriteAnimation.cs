using UnityEngine;

namespace TrashTD.Operators
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class OperatorSpriteAnimation : MonoBehaviour, IOperatorSpriteFrames
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

            if (spriteRenderer != null)
            {
                if (spriteRenderer.sprite == null && frames != null && frames.Length > 0 && frames[0] != null)
                {
                    spriteRenderer.sprite = frames[0];
                }

                frameDuration = ResolveFrameDuration(spriteRenderer.sprite, frameDuration);
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

        private Sprite GetSampleSprite()
        {
            if (spriteRenderer != null && spriteRenderer.sprite != null)
            {
                return spriteRenderer.sprite;
            }

            if (frames != null)
            {
                for (int i = 0; i < frames.Length; i++)
                {
                    if (frames[i] != null) return frames[i];
                }
            }

            if (TryGetComponent<OperatorBase>(out var op) && op != null && op.Data != null && op.Data.portrait != null)
            {
                return op.Data.portrait;
            }

            return null;
        }

        private bool ShouldResolveFramesFromTexture()
        {
            if (isCustomConfigured) return false;
            if (GetComponent<ResicochetOperator>() != null) return false;

            Sprite sample = GetSampleSprite();
            if (sample == null || sample.texture == null)
            {
                return false;
            }

            string texName = sample.texture.name.ToLowerInvariant();
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
            Sprite sample = GetSampleSprite();
            return sample != null && sample.texture != null ? GetSheetFrameCount(sample.texture) : 2;
        }

        /// <summary>
        /// Frames in an operator sheet's top row: one per square frame, so every frame the sheet has is played.
        /// Resicochet's sheet stacks one 4-frame row per charge count.
        /// </summary>
        public static int GetSheetFrameCount(Texture2D texture)
        {
            if (texture == null) return 2;

            string texName = texture.name.ToLowerInvariant();
            if (texName.Contains("resicochet"))
            {
                return 4;
            }

            int frameCount = Mathf.RoundToInt((float)texture.width / texture.height);
            return Mathf.Max(2, frameCount);
        }

        /// <summary>
        /// Slices the top row of the sheet <paramref name="sample"/> comes from into its animation frames,
        /// as gameplay does. Entries past the texture's edge stay null.
        /// </summary>
        public static Sprite[] SliceSheet(Sprite sample)
        {
            if (sample == null || sample.texture == null) return new Sprite[0];

            Texture2D texture = sample.texture;
            float ppu = sample.pixelsPerUnit > 0f ? sample.pixelsPerUnit : 32f;
            Vector2 pivot = new Vector2(0.5f, 0.5f);

            int frameCount = GetSheetFrameCount(texture);
            float frameWidth = (texture.height > 0 && texture.height * frameCount <= texture.width)
                ? texture.height
                : (texture.width / (float)frameCount);
            float frameHeight = Mathf.Min(texture.height, frameWidth > 0 ? frameWidth : texture.height);

            var sliced = new Sprite[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                float x = i * frameWidth;
                if (x + frameWidth > texture.width || frameHeight > texture.height) break;
                sliced[i] = Sprite.Create(texture, new Rect(x, texture.height - frameHeight, frameWidth, frameHeight), pivot, ppu);
            }
            return sliced;
        }

        /// <summary>Per-sheet playback speed overrides (Castiel, Stag-ger, Drawgoo) applied to the default 0.25 s frame.</summary>
        public static float ResolveFrameDuration(Sprite sample, float frameDuration)
        {
            if (sample == null || sample.texture == null || !Mathf.Approximately(frameDuration, 0.25f)) return frameDuration;

            string texName = sample.texture.name.ToLowerInvariant();
            if (texName.Contains("castiel")) return 0.1f;
            if (texName.Contains("stag-ger") || texName.Contains("stagger")) return 0.125f;
            if (texName.Contains("drawgoo")) return 0.125f;
            return frameDuration;
        }

        private void TryResolveFramesFromTexture()
        {
            Sprite sample = GetSampleSprite();
            if (sample == null || sample.texture == null)
            {
                return;
            }

            frames = SliceSheet(sample);

            if (spriteRenderer != null && (spriteRenderer.sprite == null || spriteRenderer.sprite == sample))
            {
                if (frames.Length > 0 && frames[0] != null)
                {
                    spriteRenderer.sprite = frames[0];
                }
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
