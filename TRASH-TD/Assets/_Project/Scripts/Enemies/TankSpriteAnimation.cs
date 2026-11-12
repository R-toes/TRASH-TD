using System;
using UnityEngine;

namespace TrashTD.Enemies
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class TankSpriteAnimation : MonoBehaviour, IEnemySpriteFrames
    {
        [SerializeField] private Sprite[] frames;
        [SerializeField] private float frameDuration = 0.3f;

        [Header("Attack (optional)")]
        [Tooltip("Attack animation frames. Leave empty for a tank with no attack animation.")]
        [SerializeField] private Sprite[] attackFrames;
        [SerializeField, Min(0.01f)] private float attackFrameDuration = 0.08f;
        [Tooltip("Zero-based index of the attack frame on which the hit lands (4 = the 5th frame).")]
        [SerializeField, Min(0)] private int attackHitFrame = 4;

        private float frameTimer;
        private int currentFrame;

        private bool isAttacking;
        private int attackFrame;
        private float attackTimer;
        private Action onAttackHit;

        public Sprite[] Frames => frames;
        public float FrameDuration => frameDuration;
        public bool HasAttackAnimation => attackFrames != null && attackFrames.Length > 0 &&
                                          !Array.Exists(attackFrames, frame => frame == null);
        public bool IsAttacking => isAttacking;

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
            ApplyFrame(frames[currentFrame]);
        }

        /// <summary>
        /// Plays the attack animation once. <paramref name="onHit"/> runs the moment the hit frame is shown,
        /// so damage lands exactly on that frame. Returns false if there is no attack animation or one is already playing.
        /// </summary>
        public bool PlayAttack(Action onHit)
        {
            if (!enabled || isAttacking || !HasAttackAnimation) return false;

            isAttacking = true;
            attackFrame = 0;
            attackTimer = 0f;
            onAttackHit = onHit;
            ApplyFrame(attackFrames[0]);
            if (attackHitFrame == 0) FireHit();
            return true;
        }

        /// <summary>Stops an attack in progress without landing its hit (stun, freeze, unblock).</summary>
        public void CancelAttack()
        {
            if (!isAttacking) return;

            isAttacking = false;
            onAttackHit = null;
            frameTimer = 0f;
            ApplyFrame(frames[currentFrame]);
        }

        private void Update()
        {
            if (isAttacking)
            {
                UpdateAttack();
                return;
            }

            frameTimer += Time.deltaTime;
            if (frameTimer < frameDuration)
                return;

            int elapsedFrames = Mathf.FloorToInt(frameTimer / frameDuration);
            frameTimer %= frameDuration;
            currentFrame = (currentFrame + elapsedFrames) % frames.Length;
            ApplyFrame(frames[currentFrame]);
        }

        private void UpdateAttack()
        {
            attackTimer += Time.deltaTime;
            while (isAttacking && attackTimer >= attackFrameDuration)
            {
                attackTimer -= attackFrameDuration;
                attackFrame++;

                if (attackFrame >= attackFrames.Length)
                {
                    // Back to idle where the idle loop left off
                    isAttacking = false;
                    onAttackHit = null;
                    frameTimer = 0f;
                    ApplyFrame(frames[currentFrame]);
                    return;
                }

                ApplyFrame(attackFrames[attackFrame]);
                if (attackFrame == attackHitFrame) FireHit();
            }
        }

        private void FireHit()
        {
            Action hit = onAttackHit;
            onAttackHit = null;
            hit?.Invoke();
        }

        private void ApplyFrame(Sprite sprite)
        {
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].gameObject.name != "AirShadow")
                    renderers[i].sprite = sprite;
            }
        }
    }
}
