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

            // If frames are missing or incomplete, auto-resolve both 32x32 frames from the sprite's texture sheet
            if (frames == null || frames.Length != 2 || frames[0] == null || frames[1] == null)
            {
                TryResolveFramesFromTexture();
            }

            if (frames == null || frames.Length != 2 || frames[0] == null || frames[1] == null)
            {
                Debug.LogError($"{nameof(OperatorSpriteAnimation)} requires exactly two assigned sprites.", this);
                enabled = false;
                return;
            }

            spriteRenderer.sprite = frames[0];
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

            // 2-frame horizontal sprite sheet (64x32 or two equal halves)
            float frameWidth = texture.width / 2f;
            float frameHeight = texture.height;

            Sprite frame0 = Sprite.Create(texture, new Rect(0, 0, frameWidth, frameHeight), pivot, ppu);
            Sprite frame1 = Sprite.Create(texture, new Rect(frameWidth, 0, frameWidth, frameHeight), pivot, ppu);

            frames = new[] { frame0, frame1 };
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
