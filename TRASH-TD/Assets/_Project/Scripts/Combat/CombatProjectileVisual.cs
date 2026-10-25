using System;
using UnityEngine;
using TrashTD.Enemies;
using Random = UnityEngine.Random;

namespace TrashTD.Combat
{
    /// <summary>How a projectile looks: its pixel sprite, trail, muzzle flash and impact.</summary>
    public enum ProjectileStyle
    {
        Bolt,     // Basic ranged attack: a glowing dart
        Bullet,   // Sniper round: hot slug with a tracer streak
        Pellet,   // Shotgun pellet
        ArtsOrb,  // Caster magic: shaded orb with a flickering halo and sparkles
        FireOrb,  // Fire magic: orb shedding flames, bursts into a small fireball
        Frost,    // Ice shard trailing frost, shatters on impact
        Rocket,   // Junk rocket with a smoke trail (its blast is drawn by the AoE)
        HeavyRocket, // Eradicator's every-4th warhead: bigger, glowing, trailing fire (blast drawn by the AoE)
        Quill,    // Spine/needle
        Heal,     // Healing cross lobbed to an ally
        Bubble,   // Wobbling bubble lobbed to an ally, pops on arrival
        Sludge,   // Lobbed toxic glob that splats (enemy shots; purple for acid)
        Sand      // Lobbed clod of sand that bursts into dust
    }

    /// <summary>
    /// Pixel-art projectile flying from an attacker to a target, then calling onImpact (where the caller
    /// applies damage or healing, so timing stays with the visual). A projectile aimed at an EnemyBase homes
    /// on its visible sprite. The style picks the sprite (CombatFxArt), trail, muzzle flash and impact
    /// (CombatFx); lobbed styles arc, but only visually, so arrival timing is the same as a straight shot.
    /// The size/trailWidth/burstOnImpact parameters are kept for existing callers; the style sets the look.
    /// Runs on scaled time.
    /// </summary>
    public sealed class CombatProjectileVisual : MonoBehaviour
    {
        private const int BodySortingOrder = 40;
        private const int HaloSortingOrder = 39;

        private Vector3 targetPosition;
        private EnemyBase targetEnemy;
        private Vector3 targetOffset;
        private float speed;
        private Action onImpact;
        private ProjectileStyle style;
        private Color color;

        private Vector3 groundPosition; // Logical position (straight line); the sprite adds the arc on top
        private float traveled;
        private float arcHeight;
        private Vector2 lastVisual;
        private float trailDistance;
        private float age;
        private SpriteRenderer body;
        private SpriteRenderer halo;

        public static void Fire(
            Vector3 start,
            Vector3 target,
            Color color,
            float speed,
            float size,
            float trailWidth,
            bool burstOnImpact = false,
            Action onImpact = null,
            ProjectileStyle style = ProjectileStyle.Bolt)
        {
            Spawn(start, target, color, speed, style, onImpact);
        }

        public static void Fire(
            Vector3 start,
            EnemyBase target,
            Color color,
            float speed,
            float size,
            float trailWidth,
            bool burstOnImpact = false,
            Action onImpact = null,
            ProjectileStyle style = ProjectileStyle.Bolt)
        {
            if (target == null) return;

            Spawn(start, target.VisualPosition, color, speed, style, onImpact, target);
        }

        public static void FireSand(
            Vector3 start,
            Vector3 target,
            Color trailColor,
            float speed,
            Action onImpact = null)
        {
            Spawn(start, target, trailColor, speed, ProjectileStyle.Sand, onImpact);
        }

        public static void FireShotgunSpread(
            Vector3 start,
            Vector3 direction,
            float range,
            float minSpreadOffset,
            float maxSpreadOffset,
            Color color,
            float speed,
            float size,
            float trailWidth,
            int pelletCount,
            Action onImpact = null,
            EnemyBase aimTarget = null)
        {
            if (pelletCount <= 0 || range <= 0f || direction.sqrMagnitude <= Mathf.Epsilon) return;

            direction.Normalize();
            Vector3 perpendicular = Vector3.Cross(direction, Vector3.forward).normalized;

            // One muzzle blast for the whole spread
            Vector2 muzzle = start + direction * 0.25f;
            CombatFx.Flash(muzzle, color, true);
            CombatFx.Sparks(muzzle, color, 5, 2.5f, 0.12f, direction: direction, coneDegrees: 70f);
            CombatFx.Smoke(muzzle, new Color(0.7f, 0.68f, 0.65f, 0.5f), 2, 0.05f, 0.35f, 0.2f, sortingOrder: CombatFx.TrailSortingOrder);

            for (int i = 0; i < pelletCount; i++)
            {
                float normalizedPosition = pelletCount == 1
                    ? 0.5f
                    : i / (float)(pelletCount - 1);
                float lateralOffset = Mathf.Lerp(minSpreadOffset, maxSpreadOffset, normalizedPosition);
                Action pelletImpact = i == pelletCount - 1 ? onImpact : null;
                if (aimTarget != null)
                {
                    Spawn(start, aimTarget.VisualPosition, color, speed, ProjectileStyle.Pellet, pelletImpact,
                        aimTarget, perpendicular * lateralOffset, false);
                }
                else
                {
                    Vector3 pelletTarget = start + direction * range + perpendicular * lateralOffset;
                    Spawn(start, pelletTarget, color, speed, ProjectileStyle.Pellet, pelletImpact, muzzleFlash: false);
                }
            }
        }

        private static void Spawn(
            Vector3 start,
            Vector3 target,
            Color color,
            float speed,
            ProjectileStyle style,
            Action onImpact,
            EnemyBase targetEnemy = null,
            Vector3 targetOffset = default,
            bool muzzleFlash = true)
        {
            color.a = 1f;
            var projectile = new GameObject("CombatProjectileVisual");
            projectile.transform.position = PixelParticles.Snap(start);

            var visual = projectile.AddComponent<CombatProjectileVisual>();
            visual.targetPosition = target + targetOffset;
            visual.targetEnemy = targetEnemy;
            visual.targetOffset = targetOffset;
            visual.speed = speed;
            visual.onImpact = onImpact;
            visual.style = style;
            visual.color = color;
            visual.groundPosition = start;
            visual.lastVisual = start;

            visual.body = projectile.AddComponent<SpriteRenderer>();
            visual.body.sprite = GetBodySprite(style, color);
            visual.body.sortingOrder = BodySortingOrder;

            if (style == ProjectileStyle.ArtsOrb || style == ProjectileStyle.FireOrb || style == ProjectileStyle.HeavyRocket)
            {
                var haloObject = new GameObject("Halo");
                haloObject.transform.SetParent(projectile.transform, false);
                visual.halo = haloObject.AddComponent<SpriteRenderer>();
                visual.halo.sprite = CombatFxArt.PopRingFrames()[3];
                visual.halo.color = new Color(color.r, color.g, color.b, 0.45f);
                visual.halo.sortingOrder = HaloSortingOrder;
            }

            float distance = Vector2.Distance(start, visual.targetPosition);
            visual.arcHeight = GetArcFactor(style) * Mathf.Clamp(distance, 0.5f, 3f) * 0.5f;

            Vector2 direction = ((Vector2)(visual.targetPosition - start)).normalized;
            visual.ApplyOrientation(direction);
            if (muzzleFlash) PlayMuzzle(style, start, direction, color);
        }

        private static Sprite GetBodySprite(ProjectileStyle style, Color color)
        {
            return style switch
            {
                ProjectileStyle.Bullet => CombatFxArt.Bullet(color),
                ProjectileStyle.Pellet => CombatFxArt.Pellet(color),
                ProjectileStyle.ArtsOrb => CombatFxArt.Orb(color),
                ProjectileStyle.FireOrb => CombatFxArt.Orb(color),
                ProjectileStyle.Frost => CombatFxArt.Shard(color),
                ProjectileStyle.Rocket => CombatFxArt.Rocket(),
                ProjectileStyle.HeavyRocket => CombatFxArt.HeavyRocket(),
                ProjectileStyle.Quill => CombatFxArt.Quill(color),
                ProjectileStyle.Heal => CombatFxArt.HealCross(color),
                ProjectileStyle.Bubble => CombatFxArt.Bubble(color),
                ProjectileStyle.Sludge => CombatFxArt.Glob(color),
                ProjectileStyle.Sand => CombatFxArt.SandClod(),
                _ => CombatFxArt.Bolt(color)
            };
        }

        /// <summary>Peak arc height per unit of distance for lobbed styles; 0 flies straight.</summary>
        private static float GetArcFactor(ProjectileStyle style)
        {
            return style switch
            {
                ProjectileStyle.Sludge => 0.45f,
                ProjectileStyle.Sand => 0.4f,
                ProjectileStyle.Heal => 0.3f,
                ProjectileStyle.Bubble => 0.2f,
                _ => 0f
            };
        }

        private static bool IsOriented(ProjectileStyle style)
        {
            return style == ProjectileStyle.Bolt || style == ProjectileStyle.Bullet || style == ProjectileStyle.Frost ||
                   style == ProjectileStyle.Rocket || style == ProjectileStyle.HeavyRocket || style == ProjectileStyle.Quill;
        }

        private void ApplyOrientation(Vector2 direction)
        {
            if (!IsOriented(style) || direction.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        }

        private static void PlayMuzzle(ProjectileStyle style, Vector2 start, Vector2 direction, Color color)
        {
            Vector2 muzzle = start + direction * 0.2f;
            switch (style)
            {
                case ProjectileStyle.Bullet:
                    CombatFx.Flash(muzzle, color, true);
                    CombatFx.Sparks(muzzle, color, 3, 2f, 0.1f, direction: direction, coneDegrees: 50f);
                    CombatFx.Smoke(muzzle, new Color(0.7f, 0.68f, 0.65f, 0.45f), 1, 0.03f, 0.3f, 0.2f, sortingOrder: CombatFx.TrailSortingOrder);
                    break;
                case ProjectileStyle.Rocket:
                    CombatFx.Flash(muzzle, new Color(1f, 0.6f, 0.2f), true);
                    CombatFx.Smoke(start, new Color(0.62f, 0.6f, 0.58f, 0.6f), 3, 0.12f, 0.5f, 0.25f, sortingOrder: CombatFx.TrailSortingOrder);
                    break;
                case ProjectileStyle.HeavyRocket:
                    CombatFx.Flash(muzzle, color, true);
                    CombatFx.Sparks(muzzle, new Color(1f, 0.6f, 0.2f), 8, 2.6f, 0.25f, direction: -direction, coneDegrees: 120f, big: true);
                    CombatFx.Smoke(start, new Color(0.5f, 0.48f, 0.46f, 0.7f), 6, 0.2f, 0.7f, 0.3f, sortingOrder: CombatFx.TrailSortingOrder);
                    break;
                case ProjectileStyle.ArtsOrb:
                case ProjectileStyle.FireOrb:
                case ProjectileStyle.Frost:
                    CombatFx.Pop(muzzle, color);
                    break;
                case ProjectileStyle.Bolt:
                    CombatFx.Flash(muzzle, color);
                    break;
            }
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;
            age += deltaTime;

            if (targetEnemy != null)
                targetPosition = targetEnemy.VisualPosition + targetOffset;

            Vector3 toTarget = targetPosition - groundPosition;
            float step = speed * deltaTime;
            bool arrived = toTarget.sqrMagnitude <= step * step;
            if (arrived)
            {
                traveled += toTarget.magnitude;
                groundPosition = targetPosition;
            }
            else
            {
                groundPosition += toTarget.normalized * step;
                traveled += step;
            }

            float remaining = arrived ? 0f : Vector2.Distance(groundPosition, targetPosition);
            float progress = traveled / Mathf.Max(0.0001f, traveled + remaining);
            Vector2 visual = (Vector2)groundPosition + Vector2.up * (arcHeight * 4f * progress * (1f - progress));

            Vector2 moved = visual - lastVisual;
            if (moved.sqrMagnitude > 0.000001f) ApplyOrientation(moved);
            EmitTrail(lastVisual, visual);
            lastVisual = visual;

            transform.position = PixelParticles.Snap(visual);
            AnimateBody(deltaTime);

            if (!arrived) return;

            onImpact?.Invoke();
            onImpact = null;
            PlayImpact(visual, moved.sqrMagnitude > 0.000001f ? moved.normalized : Vector2.right);
            Destroy(gameObject);
        }

        private void AnimateBody(float deltaTime)
        {
            switch (style)
            {
                case ProjectileStyle.Sand:
                    transform.Rotate(0f, 0f, -540f * deltaTime);
                    break;
                case ProjectileStyle.Bubble:
                    float wobble = Mathf.Sin(age * 18f) * 0.08f;
                    transform.localScale = new Vector3(1f + wobble, 1f - wobble, 1f);
                    break;
            }

            if (halo != null)
            {
                // Flicker between two ring sizes, like an unstable glow
                Sprite[] rings = CombatFxArt.PopRingFrames();
                halo.sprite = rings[Mathf.FloorToInt(age / 0.06f) % 2 == 0 ? 3 : 2];
            }
        }

        // ─────────────────────────────────────────────────────
        //  Trails
        // ─────────────────────────────────────────────────────

        private float GetTrailSpacingPx()
        {
            return style switch
            {
                ProjectileStyle.Bullet => 1f,
                ProjectileStyle.FireOrb => 2f,
                ProjectileStyle.HeavyRocket => 2f,
                ProjectileStyle.Bolt => 2f,
                ProjectileStyle.Pellet => 2f,
                ProjectileStyle.Heal => 6f,
                ProjectileStyle.Bubble => 7f,
                ProjectileStyle.Sludge => 5f,
                ProjectileStyle.Quill => 0f,
                _ => 3f
            };
        }

        private void EmitTrail(Vector2 from, Vector2 to)
        {
            float spacing = GetTrailSpacingPx() * CombatFxArt.Pixel;
            if (spacing <= 0f) return;

            float length = Vector2.Distance(from, to);
            if (length <= 0f) return;
            Vector2 direction = (to - from) / length;

            trailDistance += length;
            while (trailDistance >= spacing)
            {
                trailDistance -= spacing;
                EmitTrailParticle(to - direction * trailDistance, direction);
            }
        }

        private void EmitTrailParticle(Vector2 position, Vector2 direction)
        {
            Color faded = new Color(color.r, color.g, color.b, 0f);
            switch (style)
            {
                case ProjectileStyle.Bullet:
                case ProjectileStyle.Pellet:
                case ProjectileStyle.Bolt:
                    PixelParticles.Emit(new PixelParticle
                    {
                        Position = position,
                        Life = style == ProjectileStyle.Bolt ? 0.12f : style == ProjectileStyle.Bullet ? 0.08f : 0.05f,
                        StartColor = Color.Lerp(color, Color.white, 0.4f),
                        EndColor = faded,
                        Sprite = CombatFxArt.Dot1(),
                        SortingOrder = CombatFx.TrailSortingOrder
                    });
                    break;

                case ProjectileStyle.ArtsOrb:
                    bool twinkle = Random.value < 0.25f;
                    PixelParticles.Emit(new PixelParticle
                    {
                        Position = position + Random.insideUnitCircle * CombatFxArt.Pixel * 2f,
                        Velocity = Random.insideUnitCircle * 0.4f,
                        Life = Random.Range(0.25f, 0.4f),
                        Drag = 2f,
                        StartColor = Color.Lerp(color, Color.white, 0.5f),
                        EndColor = faded,
                        Sprite = twinkle ? CombatFxArt.Plus3() : CombatFxArt.Dot1(),
                        SortingOrder = CombatFx.TrailSortingOrder
                    });
                    break;

                case ProjectileStyle.FireOrb:
                    PixelParticles.Emit(new PixelParticle
                    {
                        Position = position + Random.insideUnitCircle * CombatFxArt.Pixel * 2f,
                        Velocity = new Vector2(Random.Range(-0.2f, 0.2f), Random.Range(0.4f, 0.8f)),
                        Life = Random.Range(0.2f, 0.32f),
                        StartColor = new Color(1f, 0.88f, 0.4f, 1f),
                        EndColor = new Color(0.7f, 0.12f, 0.05f, 0f),
                        Sprite = Random.value < 0.4f ? CombatFxArt.Dot2() : CombatFxArt.Dot1(),
                        SortingOrder = CombatFx.TrailSortingOrder
                    });
                    break;

                case ProjectileStyle.Frost:
                    PixelParticles.Emit(new PixelParticle
                    {
                        Position = position + Random.insideUnitCircle * CombatFxArt.Pixel * 2f,
                        Velocity = new Vector2(Random.Range(-0.1f, 0.1f), -0.25f),
                        Life = Random.Range(0.3f, 0.45f),
                        StartColor = Color.Lerp(color, Color.white, 0.7f),
                        EndColor = faded,
                        Sprite = CombatFxArt.Dot1(),
                        SortingOrder = CombatFx.TrailSortingOrder
                    });
                    break;

                case ProjectileStyle.Rocket:
                    Vector2 tail = position - direction * CombatFxArt.Pixel * 5f;
                    PixelParticles.Emit(new PixelParticle
                    {
                        Position = tail,
                        Velocity = new Vector2(Random.Range(-0.1f, 0.1f), Random.Range(0.05f, 0.2f)),
                        Life = Random.Range(0.4f, 0.6f),
                        Drag = 1f,
                        StartColor = new Color(0.66f, 0.64f, 0.62f, 0.7f),
                        EndColor = new Color(0.4f, 0.4f, 0.4f, 0f),
                        Frames = CombatFxArt.SmokeFrames(),
                        SortingOrder = CombatFx.TrailSortingOrder - 1
                    });
                    PixelParticles.Emit(new PixelParticle
                    {
                        Position = tail,
                        Velocity = -direction * 0.6f + Random.insideUnitCircle * 0.2f,
                        Life = 0.08f,
                        StartColor = new Color(1f, 0.85f, 0.35f, 1f),
                        EndColor = new Color(1f, 0.3f, 0.1f, 0f),
                        Sprite = CombatFxArt.Dot2(),
                        SortingOrder = CombatFx.TrailSortingOrder
                    });
                    break;

                case ProjectileStyle.HeavyRocket:
                    Vector2 exhaust = position - direction * CombatFxArt.Pixel * 7f;
                    PixelParticles.Emit(new PixelParticle
                    {
                        Position = exhaust + Random.insideUnitCircle * CombatFxArt.Pixel * 2f,
                        Velocity = new Vector2(Random.Range(-0.15f, 0.15f), Random.Range(0.05f, 0.25f)),
                        Life = Random.Range(0.6f, 0.85f),
                        Drag = 1f,
                        StartColor = new Color(0.42f, 0.4f, 0.4f, 0.8f),
                        EndColor = new Color(0.25f, 0.24f, 0.24f, 0f),
                        Frames = CombatFxArt.SmokeFrames(),
                        SortingOrder = CombatFx.TrailSortingOrder - 1
                    });
                    PixelParticles.Emit(new PixelParticle
                    {
                        Position = exhaust,
                        Velocity = -direction * 1.2f + Random.insideUnitCircle * 0.4f,
                        Life = Random.Range(0.12f, 0.2f),
                        StartColor = new Color(1f, 0.95f, 0.6f, 1f),
                        EndColor = new Color(1f, 0.25f, 0.05f, 0f),
                        StartScale = 1f,
                        EndScale = 2f,
                        Sprite = CombatFxArt.Dot2(),
                        SortingOrder = CombatFx.TrailSortingOrder
                    });
                    break;

                case ProjectileStyle.Heal:
                    PixelParticles.Emit(new PixelParticle
                    {
                        Position = position,
                        Velocity = new Vector2(0f, 0.3f),
                        Life = 0.45f,
                        StartColor = Color.Lerp(color, Color.white, 0.3f),
                        EndColor = faded,
                        Sprite = CombatFxArt.Plus3(),
                        SortingOrder = CombatFx.TrailSortingOrder
                    });
                    break;

                case ProjectileStyle.Bubble:
                    PixelParticles.Emit(new PixelParticle
                    {
                        Position = position + Random.insideUnitCircle * CombatFxArt.Pixel * 3f,
                        Velocity = new Vector2(0f, 0.2f),
                        Life = 0.3f,
                        StartColor = Color.white,
                        EndColor = faded,
                        Sprite = CombatFxArt.Dot1(),
                        SortingOrder = CombatFx.TrailSortingOrder
                    });
                    break;

                case ProjectileStyle.Sludge:
                    PixelParticles.Emit(new PixelParticle
                    {
                        Position = position,
                        Velocity = new Vector2(Random.Range(-0.1f, 0.1f), 0f),
                        Life = 0.35f,
                        Gravity = 4f,
                        StartColor = color,
                        EndColor = new Color(color.r * 0.6f, color.g * 0.6f, color.b * 0.6f, 0f),
                        Sprite = CombatFxArt.Dot2(),
                        SortingOrder = CombatFx.TrailSortingOrder
                    });
                    break;

                case ProjectileStyle.Sand:
                    PixelParticles.Emit(new PixelParticle
                    {
                        Position = position + Random.insideUnitCircle * CombatFxArt.Pixel * 2f,
                        Velocity = new Vector2(Random.Range(-0.15f, 0.15f), 0f),
                        Life = 0.3f,
                        Gravity = 2f,
                        StartColor = new Color(0.93f, 0.74f, 0.45f, 0.9f),
                        EndColor = new Color(0.82f, 0.6f, 0.32f, 0f),
                        Sprite = CombatFxArt.Dot1(),
                        SortingOrder = CombatFx.TrailSortingOrder
                    });
                    break;
            }
        }

        // ─────────────────────────────────────────────────────
        //  Impacts
        // ─────────────────────────────────────────────────────

        private void PlayImpact(Vector2 position, Vector2 direction)
        {
            switch (style)
            {
                case ProjectileStyle.Bolt:
                    CombatFx.Flash(position, color);
                    CombatFx.Sparks(position, color, 4, 2.2f);
                    break;
                case ProjectileStyle.Bullet:
                    CombatFx.Flash(position, color);
                    CombatFx.Sparks(position, color, 5, 3f, 0.18f, direction: direction, coneDegrees: 140f);
                    break;
                case ProjectileStyle.Pellet:
                    CombatFx.Sparks(position, color, 2, 1.8f, 0.12f, direction: direction, coneDegrees: 120f);
                    break;
                case ProjectileStyle.Quill:
                    CombatFx.Sparks(position, color, 3, 1.5f, 0.15f);
                    break;
                case ProjectileStyle.ArtsOrb:
                    CombatFx.Pop(position, color);
                    CombatFx.Sparks(position, color, 6, 2f, 0.25f);
                    CombatFx.Crosses(position, color, 2, 0.1f, false, 0.35f);
                    break;
                case ProjectileStyle.FireOrb:
                    CombatFx.Explosion(position, 0.22f);
                    break;
                case ProjectileStyle.Frost:
                    Color ice = Color.Lerp(color, Color.white, 0.5f);
                    CombatFx.Flash(position, ice);
                    CombatFx.Sparks(position, ice, 7, 2.4f, 0.35f, 5f, big: true);
                    break;
                case ProjectileStyle.Rocket:
                    CombatFx.Flash(position, new Color(1f, 0.6f, 0.2f), true);
                    break;
                case ProjectileStyle.HeavyRocket:
                    CombatFx.Flash(position, color, true);
                    CombatFx.Sparks(position, new Color(1f, 0.55f, 0.15f), 10, 3.2f, 0.35f, 3f, big: true);
                    break;
                case ProjectileStyle.Heal:
                    CombatFx.Crosses(position, color, 3, 0.12f, true);
                    CombatFx.Sparks(position, color, 4, 1.2f, 0.3f, -0.5f);
                    break;
                case ProjectileStyle.Bubble:
                    CombatFx.Pop(position, color);
                    CombatFx.Droplets(position, Color.Lerp(color, Color.white, 0.3f), 5, 1.6f);
                    break;
                case ProjectileStyle.Sludge:
                    CombatFx.Droplets(position, color, 7, 2f);
                    CombatFx.Smoke(position, new Color(color.r * 0.7f, color.g * 0.7f, color.b * 0.7f, 0.5f), 1, 0.05f, 0.4f, 0.15f,
                        sortingOrder: CombatFx.ImpactSortingOrder - 1);
                    break;
                case ProjectileStyle.Sand:
                    CombatFx.Smoke(position, new Color(0.9f, 0.72f, 0.45f, 0.75f), 3, 0.08f, 0.45f, 0.2f,
                        sortingOrder: CombatFx.ImpactSortingOrder - 1);
                    CombatFx.Droplets(position, new Color(0.93f, 0.74f, 0.45f), 5, 1.4f);
                    break;
            }
        }
    }
}
