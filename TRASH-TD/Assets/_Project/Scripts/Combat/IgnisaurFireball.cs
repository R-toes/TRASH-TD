using System;
using UnityEngine;
using TrashTD.Enemies;
using TrashTD.Operators;

namespace TrashTD.Combat
{
    /// <summary>
    /// Ignisaur's thrown fireball: a looping 16x16 fly strip that homes on its target, then a one-shot impact strip.
    /// The art faces LEFT, so it is mirrored and tilted to face its travel direction. The caller's onImpact
    /// (which applies damage) runs on the frame the fireball arrives, as with CombatProjectileVisual.
    /// Runs on scaled time.
    /// </summary>
    public sealed class IgnisaurFireball : MonoBehaviour
    {
        private const int BodySortingOrder = 40; // Same as every other projectile: above operators, enemies and tiles
        private const float MaxLifetime = 6f;

        private EnemyBase target;
        private Vector3 targetPosition;
        private float speed;
        private Action onImpact;
        private Sprite[] flyFrames;
        private Sprite[] impactFrames;
        private SpriteRenderer body;

        private Vector3 position;
        private float frameTimer;
        private int frame;
        private float age;
        private bool impacting;

        public static void Fire(Vector3 start, EnemyBase target, Sprite[] flyFrames, Sprite[] impactFrames, float speed, Action onImpact)
        {
            if (target == null || flyFrames == null || flyFrames.Length == 0 || impactFrames == null || impactFrames.Length == 0) return;

            var projectile = new GameObject("IgnisaurFireball");
            projectile.transform.position = PixelParticles.Snap(start);

            var fireball = projectile.AddComponent<IgnisaurFireball>();
            fireball.target = target;
            fireball.targetPosition = target.VisualPosition;
            fireball.speed = speed;
            fireball.onImpact = onImpact;
            fireball.flyFrames = flyFrames;
            fireball.impactFrames = impactFrames;
            fireball.position = start;

            fireball.body = projectile.AddComponent<SpriteRenderer>();
            fireball.body.sprite = flyFrames[0];
            fireball.body.sortingOrder = BodySortingOrder;
            fireball.Orient(fireball.targetPosition - start);
        }

        private void Orient(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.0001f) return;

            // Art faces left: travelling left it is upright as drawn, travelling right it is mirrored.
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            bool goingRight = direction.x > 0f;
            body.flipX = goingRight;
            transform.rotation = Quaternion.Euler(0f, 0f, goingRight ? angle : angle - 180f);
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            age += deltaTime;
            if (age > MaxLifetime)
            {
                Destroy(gameObject);
                return;
            }

            if (impacting)
            {
                if (!Animate(deltaTime, impactFrames, IgnisaurSpriteAnimation.FireballImpactDurations, loop: false))
                    Destroy(gameObject);
                return;
            }

            if (target != null && !target.IsDead) targetPosition = target.VisualPosition;

            Vector3 toTarget = targetPosition - position;
            float step = speed * deltaTime;
            bool arrived = toTarget.sqrMagnitude <= step * step;
            if (arrived)
            {
                position = targetPosition;
            }
            else
            {
                Orient(toTarget);
                position += toTarget.normalized * step;
            }

            transform.position = PixelParticles.Snap(position);
            Animate(deltaTime, flyFrames, IgnisaurSpriteAnimation.FireballFlyDurations, loop: true);

            if (!arrived) return;

            onImpact?.Invoke();
            onImpact = null;

            // The impact art is not rotated: it stays upright where the fireball hit
            impacting = true;
            frame = 0;
            frameTimer = 0f;
            transform.rotation = Quaternion.identity;
            body.sprite = impactFrames[0];
        }

        /// <summary>Advances a strip by its per-frame durations. Returns false once a non-looping strip has finished.</summary>
        private bool Animate(float deltaTime, Sprite[] frames, float[] durations, bool loop)
        {
            frameTimer += deltaTime;
            while (frameTimer >= durations[frame])
            {
                frameTimer -= durations[frame];
                frame++;
                if (frame >= frames.Length)
                {
                    if (!loop) return false;
                    frame = 0;
                }
                body.sprite = frames[frame];
            }
            return true;
        }
    }
}
