using UnityEngine;

namespace TrashTD.Enemies
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class TankSpriteAnimation : MonoBehaviour, IEnemySpriteFrames
    {
        [SerializeField] private Sprite[] frames;
        [SerializeField] private float frameDuration = 0.3f;

        private float frameTimer;
        private int currentFrame;

        public Sprite[] Frames => frames;
        public float FrameDuration => frameDuration;

        private void Awake()
        {
            if (frames == null || frames.Length < 2 ||
                System.Array.Exists(frames, frame => frame == null) ||
                frameDuration <= 0f)
            {
                Debug.LogError($"{nameof(TankSpriteAnimation)} requires at least two sprites and a positive frame duration.", this);
                enabled = false;
                return;
            }

            currentFrame = 0;
            frameTimer = 0f;
            ApplyFrame();
        }

        private void Update()
        {
            frameTimer += Time.deltaTime;
            if (frameTimer < frameDuration)
                return;

            int elapsedFrames = Mathf.FloorToInt(frameTimer / frameDuration);
            frameTimer %= frameDuration;
            currentFrame = (currentFrame + elapsedFrames) % frames.Length;
            ApplyFrame();
        }

        private void ApplyFrame()
        {
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].gameObject.name != "AirShadow")
                    renderers[i].sprite = frames[currentFrame];
            }
        }
    }
}
