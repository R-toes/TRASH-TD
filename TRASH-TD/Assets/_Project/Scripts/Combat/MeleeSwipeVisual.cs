using UnityEngine;

namespace TrashTD.Combat
{
    /// <summary>How a melee hit looks.</summary>
    public enum MeleeStyle
    {
        Slash,      // Blade arc sweeping across the target (Guards, Specialists, Stag-ger)
        FlameSlash, // Ignisaur: double arc trailing fire, a small fireball on the hit and a spray of embers
        ScrapSlash, // Scrapper: double arc, an impact ring, a shower of falling sparks and junk knocked loose
        Claw,       // Three raking claw marks (enemy melee, Talon Rat)
        Bash,       // Blunt blow: impact ring, sparks and dust (Defenders, tank enemies)
        Spikes,     // Spines bursting out of the target (Thornchin, Spoilcupine)
        Smash,      // THE ERADICATOR's slam: big flash, shockwave, flying debris
        Impact      // Something landing on a spot (Stage 2 falling items)
    }

    /// <summary>
    /// Pixel-art melee hits, drawn with PixelParticles at the stage-art scale so they match the projectiles
    /// and blasts: 1-texel dots snapped to the 1/32 grid, pooled (no GameObject per hit), on scaled time.
    /// Each style sweeps or bursts over a few frames: a bright cutting edge leads, the attacker's colour follows,
    /// and everything fades to a darker shade. The effect is centred on the target; the attacker position only
    /// sets the direction of the blow. A size above 1 draws a bigger, heavier version (a double arc for slashes).
    /// </summary>
    public static class MeleeSwipeVisual
    {
        private const int SortingOrder = CombatFx.ImpactSortingOrder;
        private const float Px = CombatFxArt.Pixel;

        public static void Play(Vector3 attackerPosition, Vector3 targetPosition, Color color,
            float sizeMultiplier = 1f, MeleeStyle style = MeleeStyle.Slash)
        {
            Vector2 target = targetPosition;
            Vector2 forward = target - (Vector2)attackerPosition;
            forward = forward.sqrMagnitude < 0.0001f ? Vector2.right : forward.normalized;
            float size = Mathf.Clamp(sizeMultiplier, 0.5f, 3f);
            color.a = 1f;

            switch (style)
            {
                case MeleeStyle.Slash:
                case MeleeStyle.FlameSlash:
                case MeleeStyle.ScrapSlash:
                    PlaySlash(target, forward, color, size, style);
                    break;
                case MeleeStyle.Claw:
                    PlayClaw(target, forward, color, size);
                    break;
                case MeleeStyle.Bash:
                    PlayBash(target, forward, color, size);
                    break;
                case MeleeStyle.Spikes:
                    PlaySpikes(target, color, size);
                    break;
                case MeleeStyle.Smash:
                    PlaySmash(target, color, size);
                    break;
                case MeleeStyle.Impact:
                    PlayImpact(target, color, size);
                    break;
            }
        }

        /// <summary>Shadeslice's thrust, drawn by <see cref="MeleeStabVisual"/> (its original line-and-glow look).</summary>
        public static void PlayStab(Vector3 attackerPosition, Vector3 targetPosition, Color color)
        {
            MeleeStabVisual.Play(attackerPosition, targetPosition, color);
        }

        // ─────────────────────────────────────────────────────
        //  Styles
        // ─────────────────────────────────────────────────────

        private static void PlaySlash(Vector2 target, Vector2 forward, Color color, float size, MeleeStyle style)
        {
            bool clockwise = Random.value < 0.5f;
            float radius = 0.34f * size;
            bool signature = style != MeleeStyle.Slash;
            bool heavy = size >= 1.5f || signature;
            float lifeScale = signature ? 1.4f : 1f; // the signature slashes linger a little longer
            Arc(target, forward, radius, 70f, 3f * size, 0.06f, 0f, color, clockwise, lifeScale);
            if (heavy)
            {
                // A second, thinner echo just inside the first
                Arc(target, forward, radius * 0.78f, 60f, 2f, 0.06f, 0.035f, Color.Lerp(color, Color.white, 0.35f), clockwise, lifeScale);
            }

            Color edge = Edge(color);
            CombatFx.Flash(target, edge, heavy, 0.05f);
            CombatFx.Sparks(target, edge, Mathf.RoundToInt(4 * size), 2.4f * size, 0.16f, 0f, forward, 100f, false, 0.05f);

            if (style == MeleeStyle.FlameSlash) PlayFlameExtras(target, forward, radius, size, clockwise);
            else if (style == MeleeStyle.ScrapSlash) PlayScrapExtras(target, forward, size);
        }

        /// <summary>Ignisaur: fire licking up off the blade along the arc, a small fireball on the hit and a spray of embers.</summary>
        private static void PlayFlameExtras(Vector2 target, Vector2 forward, float radius, float size, bool clockwise)
        {
            const int flames = 14;
            for (int i = 0; i <= flames; i++)
            {
                float t = i / (float)flames;
                Dot(ArcPoint(target, forward, radius, 70f, t, clockwise) + Random.insideUnitCircle * Px, t * 0.06f,
                    Random.Range(0.25f, 0.4f), new Color(1f, 0.92f, 0.5f), new Color(0.8f, 0.12f, 0.04f, 0f),
                    Random.value < 0.4f ? CombatFxArt.Dot2() : CombatFxArt.Dot1(),
                    new Vector2(Random.Range(-0.15f, 0.15f), Random.Range(0.45f, 0.85f)));
            }

            CombatFx.Explosion(target + forward * 0.05f, 0.16f * size, 0.05f, true);

            for (int i = 0; i < 12; i++)
            {
                Dot(target + Random.insideUnitCircle * radius * 0.7f, Random.Range(0.02f, 0.14f), Random.Range(0.35f, 0.6f),
                    new Color(1f, 0.88f, 0.4f), new Color(0.75f, 0.12f, 0.05f, 0f),
                    i % 3 == 0 ? CombatFxArt.Dot2() : CombatFxArt.Dot1(),
                    new Vector2(Random.Range(-0.3f, 0.3f), Random.Range(0.5f, 1f)));
            }
        }

        /// <summary>Scrapper: an impact ring, a shower of hot sparks that fall, and junk (bolts, rust, shards) knocked loose.</summary>
        private static void PlayScrapExtras(Vector2 target, Vector2 forward, float size)
        {
            CombatFx.Pop(target, Color.white, 0.05f);
            CombatFx.Sparks(target, new Color(1f, 0.85f, 0.45f), 8, 3f * size, 0.3f, 6f, forward, 130f, true, 0.05f);

            Color[] scrap =
            {
                new Color(0.72f, 0.38f, 0.2f),   // rust
                new Color(0.66f, 0.7f, 0.76f),   // steel
                new Color(0.36f, 0.37f, 0.42f),  // dark metal
                new Color(0.95f, 0.8f, 0.3f),    // yellow paint
                new Color(0.5f, 0.7f, 0.35f)     // green plastic
            };
            for (int i = 0; i < 9; i++)
            {
                Vector2 dir = ((Vector2)(Quaternion.Euler(0f, 0f, Random.Range(-70f, 70f)) * forward) + Vector2.up * 0.5f).normalized;
                Color c = scrap[i % scrap.Length];
                Sprite piece = i % 3 == 0 ? CombatFxArt.GlassFragment() : i % 3 == 1 ? CombatFxArt.Dot2() : CombatFxArt.Dot1();
                Dot(target, 0.05f, Random.Range(0.45f, 0.7f), c, new Color(c.r, c.g, c.b, 0f), piece,
                    dir * Random.Range(1.4f, 2.4f) * size, 8f, 1f, Random.Range(0, 4) * 90f);
            }
        }

        private static void PlayClaw(Vector2 target, Vector2 forward, Color color, float size)
        {
            Vector2 side = Perpendicular(forward);
            Vector2 rake = (side + forward * 0.5f).normalized; // diagonal across the blow
            Vector2 spacing = Perpendicular(rake) * 4f * Px * size;
            float half = 6f * Px * size;
            int steps = Mathf.CeilToInt(half * 2f / Px);
            Color edge = Edge(color);
            Color fade = Fade(color);

            for (int claw = -1; claw <= 1; claw++)
            {
                Vector2 center = target + spacing * claw;
                float clawDelay = (claw + 1) * 0.02f;
                for (int i = 0; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    Vector2 point = center + rake * Mathf.Lerp(half, -half, t);
                    float delay = clawDelay + t * 0.04f;
                    Dot(point, delay, 0.14f, edge, fade);
                    if (Mathf.Sin(Mathf.PI * t) > 0.5f)
                        Dot(point - Perpendicular(rake) * Px, delay, 0.12f, color, fade);
                }
            }

            CombatFx.Flash(target, edge, false, 0.04f);
            CombatFx.Sparks(target, edge, 3, 1.8f, 0.14f, 0f, forward, 90f, false, 0.04f);
        }

        private static void PlayBash(Vector2 target, Vector2 forward, Color color, float size)
        {
            Vector2 contact = target - forward * 0.12f;
            Color edge = Edge(color);

            CombatFx.Flash(contact, Color.white, true);
            CombatFx.Pop(contact, edge);
            CombatFx.Sparks(contact, edge, Mathf.RoundToInt(5 * size), 2.6f * size, 0.18f, 0f, forward, 140f, true);
            CombatFx.Smoke(contact, new Color(0.78f, 0.74f, 0.68f, 0.55f), 2, 0.06f, 0.35f, 0.2f,
                sortingOrder: SortingOrder - 1);

            // Short impact streaks thrown back from the blow
            Vector2 side = Perpendicular(forward);
            for (int s = -1; s <= 1; s++)
            {
                Vector2 dir = (forward + side * s * 0.6f).normalized;
                for (int i = 2; i <= 5; i++)
                    Dot(contact + dir * i * Px * size, i * 0.012f, 0.1f, edge, Fade(color));
            }
        }

        private static void PlaySpikes(Vector2 target, Color color, float size)
        {
            Color edge = Edge(color);
            Color fade = Fade(color);
            const int spikes = 7;
            float start = Random.Range(0f, 360f);
            for (int s = 0; s < spikes; s++)
            {
                float angle = (start + s * 360f / spikes + Random.Range(-12f, 12f)) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                int length = Mathf.RoundToInt(Random.Range(5f, 8f) * size);
                for (int i = 2; i <= length; i++)
                {
                    bool tip = i == length;
                    Dot(target + dir * i * Px, i * 0.006f, tip ? 0.16f : 0.13f, tip ? Color.white : (i > length - 2 ? edge : color), fade);
                }
            }
            CombatFx.Flash(target, edge);
        }

        private static void PlaySmash(Vector2 target, Color color, float size)
        {
            Color edge = Edge(color);
            int radiusPx = Mathf.RoundToInt(14f * size);

            CombatFx.Flash(target, Color.white, true);
            PixelParticles.Emit(new PixelParticle
            {
                Position = target,
                Life = 0.3f,
                StartColor = edge,
                EndColor = Fade(color),
                Frames = CombatFxArt.ShockwaveFrames(radiusPx),
                SortingOrder = SortingOrder
            });
            CombatFx.Sparks(target, edge, Mathf.RoundToInt(8 * size), 3.2f * size, 0.25f, 3f, big: true);

            // Debris kicked up by the slam
            Color[] debris = { new Color(0.42f, 0.44f, 0.48f), new Color(0.6f, 0.58f, 0.55f), new Color(0.3f, 0.3f, 0.32f) };
            for (int i = 0; i < 8; i++)
            {
                Vector2 dir = new Vector2(Random.Range(-1f, 1f), Random.Range(0.3f, 1f)).normalized;
                Color c = debris[i % debris.Length];
                Dot(target, 0f, Random.Range(0.45f, 0.7f), c, new Color(c.r, c.g, c.b, 0f),
                    i % 2 == 0 ? CombatFxArt.Dot2() : CombatFxArt.Dot1(),
                    dir * size * Random.Range(1.6f, 2.6f), 9f, 1.2f);
            }
            CombatFx.Smoke(target, new Color(0.62f, 0.6f, 0.58f, 0.6f), 3, 0.15f * size, 0.55f, 0.25f,
                sortingOrder: SortingOrder - 1);
        }

        private static void PlayImpact(Vector2 position, Color color, float size)
        {
            Color edge = Edge(color);
            CombatFx.Flash(position, edge, true);
            CombatFx.Sparks(position, edge, Mathf.RoundToInt(6 * size), 2.2f * size, 0.2f, 4f);
            CombatFx.Smoke(position, new Color(0.75f, 0.72f, 0.66f, 0.5f), 1, 0.05f, 0.35f, 0.15f,
                sortingOrder: SortingOrder - 1);
        }

        // ─────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Crescent swept around a pivot behind the target (its middle just past the target): thick in the
        /// middle, tapering to points, drawn from one end to the other over <paramref name="sweepTime"/>.
        /// The outer row is the bright cutting edge.
        /// </summary>
        private static void Arc(Vector2 target, Vector2 forward, float radius, float halfAngleDeg, float thicknessPx,
            float sweepTime, float delay, Color color, bool clockwise, float lifeScale = 1f)
        {
            Vector2 side = Perpendicular(forward);
            Vector2 pivot = target - forward * radius * 0.8f;
            float arcLength = radius * halfAngleDeg * 2f * Mathf.Deg2Rad;
            int steps = Mathf.Max(6, Mathf.CeilToInt(arcLength / Px));
            Color edge = Edge(color);
            Color inner = Color.Lerp(color, edge, 0.3f);
            Color fade = Fade(color);

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float angle = Mathf.Lerp(-halfAngleDeg, halfAngleDeg, clockwise ? 1f - t : t) * Mathf.Deg2Rad;
                Vector2 radial = forward * Mathf.Cos(angle) + side * Mathf.Sin(angle);
                int layers = Mathf.Max(1, Mathf.RoundToInt(thicknessPx * Mathf.Sin(Mathf.PI * t)));
                float pointDelay = delay + t * sweepTime;
                for (int k = 0; k < layers; k++)
                {
                    Color start = k == 0 ? edge : k == 1 ? inner : color;
                    Dot(pivot + radial * (radius - k * Px), pointDelay, (k == 0 ? 0.13f : 0.11f) * lifeScale, start, fade);
                }
            }
        }

        /// <summary>The point at <paramref name="t"/> (0..1, in sweep order) along the outer edge of the arc <see cref="Arc"/> draws.</summary>
        private static Vector2 ArcPoint(Vector2 target, Vector2 forward, float radius, float halfAngleDeg, float t, bool clockwise)
        {
            Vector2 pivot = target - forward * radius * 0.8f;
            float angle = Mathf.Lerp(-halfAngleDeg, halfAngleDeg, clockwise ? 1f - t : t) * Mathf.Deg2Rad;
            return pivot + (forward * Mathf.Cos(angle) + Perpendicular(forward) * Mathf.Sin(angle)) * radius;
        }

        private static void Dot(Vector2 position, float delay, float life, Color start, Color end,
            Sprite sprite = null, Vector2 velocity = default, float gravity = 0f, float drag = 0f, float rotation = 0f)
        {
            PixelParticles.Emit(new PixelParticle
            {
                Position = position,
                Velocity = velocity,
                Delay = delay,
                Life = life,
                Gravity = gravity,
                Drag = drag,
                StartColor = start,
                EndColor = end,
                Sprite = sprite != null ? sprite : CombatFxArt.Dot1(),
                Rotation = rotation,
                SortingOrder = SortingOrder
            });
        }

        private static Vector2 Perpendicular(Vector2 v) => new Vector2(-v.y, v.x);

        /// <summary>
        /// The colour at full brightness (and at least moderate saturation unless it is a grey), lightened:
        /// a visible cutting edge even for near-black colours such as Shadeslice's.
        /// </summary>
        private static Color Edge(Color color)
        {
            Color.RGBToHSV(color, out float h, out float s, out _);
            Color vivid = Color.HSVToRGB(h, s < 0.05f ? 0f : Mathf.Max(s, 0.6f), 1f);
            return Color.Lerp(vivid, Color.white, 0.45f);
        }

        private static Color Fade(Color color) => new Color(color.r * 0.45f, color.g * 0.45f, color.b * 0.45f, 0f);
    }
}
