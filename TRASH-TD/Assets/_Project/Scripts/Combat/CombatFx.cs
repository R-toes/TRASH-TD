using UnityEngine;

namespace TrashTD.Combat
{
    /// <summary>
    /// Reusable one-shot pixel effects built on PixelParticles: sparks, flashes, smoke, fireballs, heal
    /// crosses, droplets and pops. Shared by CombatProjectileVisual (trails, muzzles, impacts) and
    /// AoeBlastVisual (area effects), so a hit looks the same whoever deals it.
    /// </summary>
    public static class CombatFx
    {
        public const int TrailSortingOrder = 38;
        public const int ImpactSortingOrder = 41;
        public const int ExplosionSortingOrder = 30; // Above units (4-12) and weather rain (24)

        private static Color Fade(Color color) => new Color(color.r, color.g, color.b, 0f);

        private static Vector2 RandomDirection() => Random.insideUnitCircle.normalized;

        /// <summary>A 1-frame star flash.</summary>
        public static void Flash(Vector2 position, Color color, bool large = false, float delay = 0f)
        {
            PixelParticles.Emit(new PixelParticle
            {
                Position = position,
                Life = large ? 0.09f : 0.06f,
                Delay = delay,
                StartColor = Color.Lerp(color, Color.white, 0.6f),
                EndColor = Fade(color),
                Sprite = large ? CombatFxArt.Flash7() : CombatFxArt.Flash5(),
                SortingOrder = ImpactSortingOrder + 1
            });
        }

        /// <summary>Pixels thrown out in all directions (or in a cone around a direction), slowing as they fly.</summary>
        public static void Sparks(Vector2 position, Color color, int count, float speed, float life = 0.2f,
            float gravity = 0f, Vector2 direction = default, float coneDegrees = 360f, bool big = false, float delay = 0f)
        {
            for (int i = 0; i < count; i++)
            {
                Vector2 dir = direction == Vector2.zero || coneDegrees >= 360f
                    ? RandomDirection()
                    : (Vector2)(Quaternion.Euler(0f, 0f, Random.Range(-coneDegrees, coneDegrees) * 0.5f) * direction.normalized);
                PixelParticles.Emit(new PixelParticle
                {
                    Position = position,
                    Velocity = dir * speed * Random.Range(0.55f, 1.1f),
                    Life = life * Random.Range(0.7f, 1.2f),
                    Delay = delay,
                    Gravity = gravity,
                    Drag = 5f,
                    StartColor = Color.Lerp(color, Color.white, 0.45f),
                    EndColor = Fade(color),
                    Sprite = big && i % 2 == 0 ? CombatFxArt.Dot2() : CombatFxArt.Dot1(),
                    SortingOrder = ImpactSortingOrder
                });
            }
        }

        /// <summary>Puffs of smoke that swell, drift and fade.</summary>
        public static void Smoke(Vector2 position, Color color, int count, float spread, float life = 0.6f,
            float rise = 0.35f, float delay = 0f, int sortingOrder = ExplosionSortingOrder - 1)
        {
            for (int i = 0; i < count; i++)
            {
                PixelParticles.Emit(new PixelParticle
                {
                    Position = position + Random.insideUnitCircle * spread,
                    Velocity = new Vector2(Random.Range(-0.25f, 0.25f), rise * Random.Range(0.6f, 1.2f)),
                    Life = life * Random.Range(0.8f, 1.25f),
                    Delay = delay + Random.Range(0f, 0.08f),
                    Drag = 1.5f,
                    StartColor = color,
                    EndColor = Fade(color),
                    Frames = CombatFxArt.SmokeFrames(),
                    SortingOrder = sortingOrder
                });
            }
        }

        /// <summary>
        /// Fireball flipbook with sparks, embers and a smoke cloud left behind. Lite keeps the fireball but
        /// throws far fewer particles, for secondary blasts that go off alongside a full one.
        /// </summary>
        public static void Explosion(Vector2 position, float radius, float delay = 0f, bool lite = false)
        {
            int sizePx = Mathf.RoundToInt(radius * 2.2f * CombatFxArt.PixelsPerUnit);
            PixelParticles.Emit(new PixelParticle
            {
                Position = position,
                Life = 0.42f,
                Delay = delay,
                StartColor = Color.white,
                EndColor = new Color(1f, 1f, 1f, 0.85f),
                Frames = CombatFxArt.ExplosionFrames(sizePx),
                SortingOrder = ExplosionSortingOrder
            });

            Color fire = new Color(1f, 0.6f, 0.18f);
            if (lite)
            {
                Sparks(position, fire, 3, radius * 4f, 0.3f, 3f, big: true, delay: delay);
                Smoke(position, new Color(0.38f, 0.36f, 0.35f, 0.7f), 1, radius * 0.3f, 0.8f, 0.4f, delay + 0.18f);
                return;
            }

            Flash(position, fire, true, delay);
            Sparks(position, fire, Mathf.Clamp(Mathf.RoundToInt(radius * 12f), 6, 22), radius * 5f, 0.35f, 3f, big: true, delay: delay);
            Sparks(position, new Color(1f, 0.85f, 0.4f), Mathf.Clamp(Mathf.RoundToInt(radius * 6f), 3, 10), radius * 2.2f, 0.7f, -0.6f, delay: delay + 0.05f);
            Smoke(position, new Color(0.38f, 0.36f, 0.35f, 0.75f), Mathf.Clamp(Mathf.RoundToInt(radius * 4f), 2, 8), radius * 0.45f, 0.9f, 0.4f, delay + 0.18f);
        }

        private static readonly Color[] JunkColors =
        {
            new Color(0.86f, 0.2f, 0.16f),  // soda-can red
            new Color(0.25f, 0.7f, 0.3f),   // bottle green
            new Color(0.3f, 0.55f, 0.95f),  // can blue
            new Color(0.8f, 0.82f, 0.86f),  // tin
            new Color(1f, 0.85f, 0.3f)      // label yellow
        };

        /// <summary>
        /// Basurocket's junk rocket: one bright yellow-orange fireball with a burst of colourful scrap (can and
        /// bottle bits) flung out and tumbling down. Clean and bright, no smoke column.
        /// </summary>
        public static void JunkRocketBlast(Vector2 position, float radius)
        {
            Explosion(position, radius * 0.85f);
            Flash(position, new Color(1f, 0.9f, 0.4f), true);
            Sparks(position, new Color(1f, 0.85f, 0.3f), 10, radius * 5f, 0.3f, 2f, big: true);

            int scraps = Mathf.Clamp(Mathf.RoundToInt(radius * 8f), 8, 16);
            for (int i = 0; i < scraps; i++)
            {
                Vector2 dir = new Vector2(Random.Range(-1f, 1f), Random.Range(0.2f, 1f)).normalized;
                Color color = JunkColors[i % JunkColors.Length];
                PixelParticles.Emit(new PixelParticle
                {
                    Position = position,
                    Velocity = dir * radius * Random.Range(3f, 5f),
                    Life = Random.Range(0.55f, 0.8f),
                    Gravity = 9f,
                    Drag = 1.2f,
                    StartColor = color,
                    EndColor = new Color(color.r, color.g, color.b, 0f),
                    Sprite = i % 3 == 0 ? CombatFxArt.Dot1() : CombatFxArt.Dot2(),
                    Rotation = Random.Range(0f, 90f),
                    SortingOrder = ImpactSortingOrder
                });
            }
        }

        /// <summary>
        /// THE ERADICATOR's heavy ordnance: a crimson-tinted fireball, dark shrapnel thrown wide, a column of
        /// black smoke, a second trailing shockwave and a scorch mark left on the ground for a moment.
        /// </summary>
        public static void HeavyOrdnanceBlast(Vector2 position, float radius)
        {
            int sizePx = Mathf.RoundToInt(radius * 0.85f * 2.2f * CombatFxArt.PixelsPerUnit);
            PixelParticles.Emit(new PixelParticle
            {
                Position = position,
                Life = 0.5f,
                StartColor = new Color(1f, 0.6f, 0.55f),
                EndColor = new Color(0.75f, 0.35f, 0.35f, 0.85f),
                Frames = CombatFxArt.ExplosionFrames(sizePx),
                SortingOrder = ExplosionSortingOrder
            });
            Flash(position, new Color(1f, 0.3f, 0.25f), true);

            // Shrapnel: dark metal chunks thrown wide and falling back, plus red-hot fragments
            Sparks(position, new Color(0.42f, 0.42f, 0.48f), 14, radius * 6f, 0.55f, 6f, big: true);
            Sparks(position, new Color(1f, 0.32f, 0.18f), 10, radius * 4.5f, 0.4f, 3f);

            // A rising column of thick black smoke
            for (int i = 0; i < 8; i++)
            {
                PixelParticles.Emit(new PixelParticle
                {
                    Position = position + Random.insideUnitCircle * radius * 0.35f,
                    Velocity = new Vector2(Random.Range(-0.15f, 0.15f), Random.Range(0.5f, 0.9f)) * radius,
                    Life = Random.Range(1.1f, 1.5f),
                    Delay = 0.12f + Random.Range(0f, 0.15f),
                    Drag = 0.8f,
                    StartColor = new Color(0.13f, 0.1f, 0.1f, 0.85f),
                    EndColor = new Color(0.22f, 0.2f, 0.2f, 0f),
                    StartScale = 2f,
                    EndScale = 3f,
                    Frames = CombatFxArt.SmokeFrames(),
                    SortingOrder = ExplosionSortingOrder + 1
                });
            }

            // Second shockwave ring, trailing the area's own
            int radiusPx = Mathf.Max(3, Mathf.RoundToInt(radius * CombatFxArt.PixelsPerUnit));
            PixelParticles.Emit(new PixelParticle
            {
                Position = position,
                Life = 0.28f,
                Delay = 0.12f,
                StartColor = new Color(1f, 0.25f, 0.2f, 0.9f),
                EndColor = new Color(0.6f, 0.05f, 0.05f, 0f),
                Frames = CombatFxArt.ShockwaveFrames(radiusPx),
                SortingOrder = ExplosionSortingOrder - 4
            });

            // Scorch mark on the ground, beneath the units
            PixelParticles.Emit(new PixelParticle
            {
                Position = position,
                Life = 1.8f,
                StartColor = new Color(0.08f, 0.05f, 0.04f, 0.6f),
                EndColor = new Color(0.08f, 0.05f, 0.04f, 0f),
                Sprite = CombatFxArt.Scorch(Mathf.RoundToInt(radiusPx * 0.75f)),
                SortingOrder = 3
            });
        }

        /// <summary>A small 1 px ring popping open.</summary>
        public static void Pop(Vector2 position, Color color, float delay = 0f)
        {
            PixelParticles.Emit(new PixelParticle
            {
                Position = position,
                Life = 0.18f,
                Delay = delay,
                StartColor = Color.Lerp(color, Color.white, 0.4f),
                EndColor = Fade(color),
                Frames = CombatFxArt.PopRingFrames(),
                SortingOrder = ImpactSortingOrder
            });
        }

        /// <summary>Little crosses floating up and fading: twinkles (small) or heal symbols (large).</summary>
        public static void Crosses(Vector2 position, Color color, int count, float spread, bool large,
            float life = 0.6f, float delay = 0f)
        {
            for (int i = 0; i < count; i++)
            {
                PixelParticles.Emit(new PixelParticle
                {
                    Position = position + Random.insideUnitCircle * spread,
                    Velocity = new Vector2(Random.Range(-0.1f, 0.1f), Random.Range(0.35f, 0.7f)),
                    Life = life * Random.Range(0.8f, 1.2f),
                    Delay = delay + Random.Range(0f, 0.15f),
                    Drag = 1f,
                    StartColor = Color.Lerp(color, Color.white, 0.3f),
                    EndColor = Fade(color),
                    Sprite = large ? CombatFxArt.Plus5() : CombatFxArt.Plus3(),
                    SortingOrder = ImpactSortingOrder
                });
            }
        }

        /// <summary>Droplets flung up and out, falling back under gravity (sludge, acid, bubble water).</summary>
        public static void Droplets(Vector2 position, Color color, int count, float speed, float delay = 0f)
        {
            for (int i = 0; i < count; i++)
            {
                Vector2 dir = new Vector2(Random.Range(-1f, 1f), Random.Range(0.3f, 1f)).normalized;
                PixelParticles.Emit(new PixelParticle
                {
                    Position = position,
                    Velocity = dir * speed * Random.Range(0.6f, 1.1f),
                    Life = Random.Range(0.3f, 0.45f),
                    Delay = delay,
                    Gravity = 7f,
                    Drag = 1f,
                    StartColor = color,
                    EndColor = new Color(color.r * 0.7f, color.g * 0.7f, color.b * 0.7f, 0f),
                    Sprite = i % 3 == 0 ? CombatFxArt.Dot2() : CombatFxArt.Dot1(),
                    SortingOrder = ImpactSortingOrder
                });
            }
        }
    }
}
