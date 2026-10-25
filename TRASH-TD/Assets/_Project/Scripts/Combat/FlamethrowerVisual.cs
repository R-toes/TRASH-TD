using UnityEngine;

namespace TrashTD.Combat
{
    /// <summary>
    /// Pixel flame jet for flamethrower attacks (Roboflame). Each frame while the jet is on (Emit):
    /// - a stream of hot puffs that swell and cool from white-yellow through orange to red,
    /// - big licking tongues of red-orange flame mixed in,
    /// - a flickering glow at the nozzle,
    /// - flames curling up off the stream, embers and smoke rising from its far end.
    /// Ignite adds a burst of flame on whatever the jet hits. Runs on scaled time (PixelParticles).
    /// </summary>
    public static class FlamethrowerVisual
    {
        private const float ParticlesPerSecond = 110f;
        private const float Life = 0.4f;
        private const float Drag = 2.5f;
        private const float SpreadDegrees = 8f;
        private const float TongueChance = 0.25f;
        private const int SortingOrder = CombatFx.ExplosionSortingOrder - 1;

        private static readonly Color WhiteHot = new Color(1f, 0.97f, 0.8f, 1f);
        private static readonly Color Yellow = new Color(1f, 0.82f, 0.3f, 1f);
        private static readonly Color Orange = new Color(1f, 0.5f, 0.12f, 1f);
        private static readonly Color DyingRed = new Color(0.75f, 0.12f, 0.04f, 0f);

        /// <param name="range">How far (world units) the flame reaches before it dies out.</param>
        /// <param name="emitCarry">Caller-owned fraction of a particle, so the rate is steady at any frame rate.</param>
        public static void Emit(Vector2 origin, Vector2 direction, float range, float deltaTime, ref float emitCarry)
        {
            if (deltaTime <= 0f || range <= 0f || direction.sqrMagnitude < 0.0001f) return;
            direction.Normalize();
            Vector2 side = new Vector2(-direction.y, direction.x);

            // Launch speed that carries a puff `range` units before drag stops it at the end of its life
            float speed = range * Drag / (1f - Mathf.Exp(-Drag * Life));

            emitCarry += ParticlesPerSecond * deltaTime;
            int count = Mathf.FloorToInt(emitCarry);
            emitCarry -= count;

            for (int i = 0; i < count; i++)
            {
                Vector2 dir = Quaternion.Euler(0f, 0f, Random.Range(-SpreadDegrees, SpreadDegrees)) * direction;
                if (Random.value < TongueChance)
                {
                    // A big licking tongue: slower, longer-lived, swelling wide and red
                    PixelParticles.Emit(new PixelParticle
                    {
                        Position = origin + dir * range * 0.15f,
                        Velocity = dir * speed * Random.Range(0.7f, 0.9f),
                        Life = Life * Random.Range(1f, 1.25f),
                        Drag = Drag,
                        Gravity = -1.4f,
                        StartColor = Orange,
                        EndColor = DyingRed,
                        StartScale = 1.5f,
                        EndScale = 3.5f,
                        Sprite = CombatFxArt.Dot2(),
                        SortingOrder = SortingOrder - 1
                    });
                    continue;
                }

                bool core = Random.value < 0.35f;
                PixelParticles.Emit(new PixelParticle
                {
                    Position = origin + Random.insideUnitCircle * CombatFxArt.Pixel * 1.5f,
                    Velocity = dir * speed * Random.Range(0.85f, 1.05f),
                    Life = Life * Random.Range(0.85f, 1.1f),
                    Drag = Drag,
                    Gravity = -0.8f, // Flames lift as they slow
                    StartColor = core ? WhiteHot : Yellow,
                    EndColor = DyingRed,
                    StartScale = 1f,
                    EndScale = 2.6f,
                    Sprite = core ? CombatFxArt.Dot1() : CombatFxArt.Dot2(),
                    SortingOrder = SortingOrder
                });
            }

            // Nozzle glow flickering on and off
            if (Random.value < deltaTime * 16f)
                CombatFx.Flash(origin, Orange);

            // Flames curling up off the stream
            if (Random.value < deltaTime * 22f)
            {
                Vector2 along = origin + direction * range * Random.Range(0.3f, 0.95f) +
                                side * Random.Range(-0.15f, 0.15f) * range;
                PixelParticles.Emit(new PixelParticle
                {
                    Position = along,
                    Velocity = new Vector2(Random.Range(-0.2f, 0.2f), Random.Range(0.8f, 1.4f)),
                    Life = Random.Range(0.3f, 0.5f),
                    Drag = 1.5f,
                    StartColor = Yellow,
                    EndColor = DyingRed,
                    StartScale = 1.5f,
                    EndScale = 0.6f,
                    Sprite = CombatFxArt.Dot2(),
                    SortingOrder = SortingOrder
                });
            }

            Vector2 tip = origin + direction * range;
            if (Random.value < deltaTime * 18f)
            {
                PixelParticles.Emit(new PixelParticle
                {
                    Position = tip + Random.insideUnitCircle * 0.2f,
                    Velocity = new Vector2(Random.Range(-0.3f, 0.3f), Random.Range(0.5f, 1.1f)),
                    Life = Random.Range(0.4f, 0.7f),
                    Drag = 1f,
                    StartColor = new Color(1f, 0.85f, 0.4f, 1f),
                    EndColor = new Color(1f, 0.3f, 0.1f, 0f),
                    Sprite = CombatFxArt.Dot1(),
                    SortingOrder = SortingOrder
                });
            }

            if (Random.value < deltaTime * 8f)
            {
                CombatFx.Smoke(tip, new Color(0.28f, 0.26f, 0.25f, 0.5f), 1, 0.15f, 0.65f, 0.5f,
                    sortingOrder: SortingOrder - 2);
            }
        }

        /// <summary>A burst of flame licking up off something the jet just hit.</summary>
        public static void Ignite(Vector2 position)
        {
            for (int i = 0; i < 7; i++)
            {
                PixelParticles.Emit(new PixelParticle
                {
                    Position = position + new Vector2(Random.Range(-0.2f, 0.2f), Random.Range(-0.2f, 0.05f)),
                    Velocity = new Vector2(Random.Range(-0.15f, 0.15f), Random.Range(0.7f, 1.3f)),
                    Life = Random.Range(0.35f, 0.55f),
                    Delay = Random.Range(0f, 0.12f),
                    Drag = 1.5f,
                    StartColor = i % 3 == 0 ? WhiteHot : Yellow,
                    EndColor = DyingRed,
                    StartScale = 2f,
                    EndScale = 0.6f,
                    Sprite = CombatFxArt.Dot2(),
                    SortingOrder = SortingOrder
                });
            }

            CombatFx.Sparks(position, Orange, 4, 1.6f, 0.3f, -1f);
        }
    }
}
