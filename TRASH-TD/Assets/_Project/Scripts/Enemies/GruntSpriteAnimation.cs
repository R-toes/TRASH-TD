using UnityEngine;

namespace TrashTD.Enemies
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class GruntSpriteAnimation : MonoBehaviour, IEnemySpriteFrames
    {
        [SerializeField] private Sprite[] frames;
        [SerializeField] private float frameDuration = 0.3f;

        private SpriteRenderer spriteRenderer;
        private EnemyBase enemyBase;
        private float frameTimer;
        private int currentFrame;

        public Sprite[] Frames => frames;
        public float FrameDuration => frameDuration;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            enemyBase = GetComponent<EnemyBase>();

            if (frames == null || frames.Length != 3 ||
                frames[0] == null || frames[1] == null || frames[2] == null ||
                frameDuration <= 0f)
            {
                Debug.LogError($"{nameof(GruntSpriteAnimation)} requires three sprites and a positive frame duration.", this);
                enabled = false;
                return;
            }

            currentFrame = 0;
            frameTimer = 0f;
            spriteRenderer.sprite = frames[currentFrame];
        }

        private void Update()
        {
            // Grunts with their own sheet (EnemyData.animationFrames, e.g. Tick-Torque) are animated by EnemyBase
            if (enemyBase != null && enemyBase.Data != null &&
                enemyBase.Data.animationFrames != null && enemyBase.Data.animationFrames.Length > 1)
            {
                return;
            }

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
