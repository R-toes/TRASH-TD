using UnityEngine;

namespace TrashTD.Enemies
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class RusherSpriteAnimation : MonoBehaviour
    {
        [SerializeField] private Sprite[] frames;
        [SerializeField] private float frameDuration = 0.25f;

        private SpriteRenderer spriteRenderer;
        private float frameTimer;
        private int currentFrame;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();

            if (frames == null || frames.Length != 3 ||
                frames[0] == null || frames[1] == null || frames[2] == null ||
                frameDuration <= 0f)
            {
                Debug.LogError($"{nameof(RusherSpriteAnimation)} requires three sprites and a positive frame duration.", this);
                enabled = false;
                return;
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
