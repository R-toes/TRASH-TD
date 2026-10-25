using System;
using UnityEngine;
using TrashTD.Enemies;
using Random = UnityEngine.Random;

namespace TrashTD.Combat
{
    /// <summary>Colour scheme of a ramping laser.</summary>
    public enum LaserPalette
    {
        Holy,   // Castiel: white core, pale yellow body, gold glow, sparkles
        Crimson // Askalaser: pink-white core, red body, dark crimson glow, crackling arcs
    }

    /// <summary>
    /// Pixel-art ramping laser (Castiel, Askalaser): a hot core, a body and an outer glow, each a stretched
    /// 1-texel sprite snapped to whole art pixels. The owner calls SetStage as its damage ramps (stage 0-5,
    /// matching the 5 → 15 → 30 → 80 → 100 → 150 steps) and OnDamageTick on every hit. Each stage widens and
    /// brightens the beam and adds effects:
    /// - 1: outer glow, sparks at the impact;
    /// - 2: the beam pulses, glow at the source;
    /// - 3: motes stream along the beam; Holy sheds sparkles, Crimson crackles with arcs;
    /// - 4: the beam flickers in width, a halo ring (Holy) or heat smoke (Crimson) at the impact;
    /// - 5: everything at full strength, with a burst on every tick.
    /// A stage-up flashes both ends and pops a ring at the target. Six pips above the source show the stage.
    /// Runs on scaled time.
    /// </summary>
    public sealed class LightLaserVisual : MonoBehaviour
    {
        public const int MaxStage = 5;

        private const int GlowSortingOrder = 39;
        private const int BodySortingOrder = 40;
        private const int CoreSortingOrder = 41;
        private const int PipSortingOrder = 42;
        private const float PipHeight = 0.45f;
        private const int PipSpacingPx = 3;
        private const float StageUpFlashDuration = 0.15f;

        // Widths in art pixels per stage
        private static readonly int[] CoreWidthPx = { 1, 1, 2, 2, 3, 3 };
        private static readonly int[] BodyWidthPx = { 2, 3, 4, 5, 6, 7 };
        private static readonly int[] GlowWidthPx = { 0, 5, 7, 9, 11, 14 };
        private static readonly float[] GlowAlpha = { 0f, 0.22f, 0.28f, 0.36f, 0.44f, 0.55f };

        private sealed class Palette
        {
            public Color Core;
            public Color Body;
            public Color Glow;
            public Color Spark;
            public Color Accent;
        }

        private static readonly Palette HolyPalette = new Palette
        {
            Core = new Color(1f, 1f, 1f, 1f),
            Body = new Color(1f, 0.93f, 0.55f, 1f),
            Glow = new Color(1f, 0.74f, 0.18f, 1f),
            Spark = new Color(1f, 0.86f, 0.35f, 1f),
            Accent = new Color(1f, 0.98f, 0.85f, 1f)
        };

        private static readonly Palette CrimsonPalette = new Palette
        {
            Core = new Color(1f, 0.86f, 0.84f, 1f),
            Body = new Color(1f, 0.16f, 0.14f, 1f),
            Glow = new Color(0.62f, 0.02f, 0.07f, 1f),
            Spark = new Color(1f, 0.36f, 0.26f, 1f),
            Accent = new Color(1f, 0.55f, 0.42f, 1f)
        };

        private static Sprite pixelSprite;

        private Transform source;
        private Transform target;
        private EnemyBase targetEnemy;
        private float tickInterval;
        private float tickTimer;
        private Action onTick;

        private LaserPalette paletteKind;
        private Palette palette;
        private SpriteRenderer core;
        private SpriteRenderer body;
        private SpriteRenderer glow;
        private SpriteRenderer[] pips;
        private int stage;
        private float age;
        private float stageUpFlash;
        private float moteCarry;
        private float effectCarry;

        public int Stage => stage;

        public static LightLaserVisual Fire(Transform source, Transform target, LaserPalette palette, float tickInterval, Action tick)
        {
            var laserObject = new GameObject("LightLaserVisual");
            var visual = laserObject.AddComponent<LightLaserVisual>();
            visual.source = source;
            visual.target = target;
            visual.tickInterval = tickInterval;
            visual.onTick = tick;
            visual.paletteKind = palette;
            visual.palette = palette == LaserPalette.Crimson ? CrimsonPalette : HolyPalette;
            visual.Build();
            visual.UpdateBeam();
            return visual;
        }

        public static LightLaserVisual Fire(Transform source, EnemyBase target, LaserPalette palette, float tickInterval, Action tick)
        {
            if (target == null)
                return null;

            var laser = Fire(source, target.transform, palette, tickInterval, tick);
            laser.targetEnemy = target;
            laser.UpdateBeam();
            return laser;
        }

        /// <summary>Power stage 0-5. Raising it plays the stage-up flare.</summary>
        public void SetStage(int newStage)
        {
            newStage = Mathf.Clamp(newStage, 0, MaxStage);
            if (newStage == stage) return;

            bool rising = newStage > stage;
            stage = newStage;
            UpdatePips();
            if (rising) PlayStageUp();
        }

        /// <summary>Impact effect for one damage tick, scaled to the current stage.</summary>
        public void OnDamageTick()
        {
            if (source == null || target == null) return;

            Vector2 end = GetEnd();
            CombatFx.Flash(end, palette.Spark, stage >= 3);
            CombatFx.Sparks(end, palette.Spark, 2 + stage * 2, 1.4f + stage * 0.45f, 0.18f + stage * 0.03f, big: stage >= 3);

            if (stage >= MaxStage)
            {
                CombatFx.Pop(end, palette.Accent);
                if (paletteKind == LaserPalette.Holy)
                    CombatFx.Crosses(end, palette.Accent, 2, 0.15f, true, 0.5f);
                else
                    CombatFx.Sparks(end, palette.Glow, 6, 2.6f, 0.3f, 2f, big: true);
            }
        }

        private void Update()
        {
            if (source == null || target == null)
            {
                Stop();
                return;
            }

            float deltaTime = Time.deltaTime;
            age += deltaTime;
            stageUpFlash = Mathf.Max(0f, stageUpFlash - deltaTime);

            UpdateBeam();
            EmitStageEffects(deltaTime);

            if (onTick == null) return;
            tickTimer += deltaTime;
            while (tickTimer >= tickInterval && onTick != null)
            {
                tickTimer -= tickInterval;
                onTick.Invoke();
            }
        }

        public void Stop()
        {
            onTick = null;
            if (this != null) Destroy(gameObject);
        }

        // ---------- Beam ----------

        private void Build()
        {
            glow = AddLayer("Glow", GlowSortingOrder);
            body = AddLayer("Body", BodySortingOrder);
            core = AddLayer("Core", CoreSortingOrder);

            pips = new SpriteRenderer[MaxStage + 1];
            for (int i = 0; i < pips.Length; i++)
            {
                pips[i] = AddLayer("Pip" + i, PipSortingOrder);
                pips[i].transform.localScale = new Vector3(CombatFxArt.Pixel * 2f, CombatFxArt.Pixel * 2f, 1f);
            }
            UpdatePips();
        }

        private SpriteRenderer AddLayer(string layerName, int sortingOrder)
        {
            var layerObject = new GameObject(layerName);
            layerObject.transform.SetParent(transform, false);
            var spriteRenderer = layerObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = GetPixelSprite();
            spriteRenderer.sortingOrder = sortingOrder;
            return spriteRenderer;
        }

        private Vector2 GetEnd() => targetEnemy != null ? (Vector2)targetEnemy.VisualPosition : (Vector2)target.position;

        private void UpdateBeam()
        {
            if (source == null || target == null) return;

            Vector2 start = source.position;
            Vector2 end = GetEnd();
            Vector2 delta = end - start;
            float length = delta.magnitude;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            Vector3 middle = PixelParticles.Snap((start + end) * 0.5f);

            // Stage 2+: the beam pulses; stage 4+: it also flickers a pixel wider now and then
            float pulse = stage >= 2 ? 0.5f + 0.5f * Mathf.Sin(age * (8f + stage * 2f)) : 0f;
            int flicker = stage >= 4 && Mathf.Repeat(age * 14f, 1f) < 0.5f ? 1 : 0;
            float flash = stageUpFlash / StageUpFlashDuration;

            SetLayer(core, middle, angle, length, CoreWidthPx[stage] + (flash > 0f ? 1 : 0),
                Color.Lerp(palette.Core, Color.white, flash));
            SetLayer(body, middle, angle, length, BodyWidthPx[stage] + flicker,
                Color.Lerp(palette.Body, palette.Accent, pulse * 0.35f + flash * 0.6f));

            Color glowColor = palette.Glow;
            glowColor.a = Mathf.Clamp01(GlowAlpha[stage] * (0.8f + 0.4f * pulse) + flash * 0.3f);
            SetLayer(glow, middle, angle, length, GlowWidthPx[stage] + flicker * 2, glowColor);
            glow.enabled = GlowWidthPx[stage] > 0 || flash > 0f;

            // Pips above the source
            Vector2 pipOrigin = start + new Vector2(-(pips.Length - 1) * PipSpacingPx * 0.5f * CombatFxArt.Pixel, PipHeight);
            for (int i = 0; i < pips.Length; i++)
                pips[i].transform.position = PixelParticles.Snap(pipOrigin + new Vector2(i * PipSpacingPx * CombatFxArt.Pixel, 0f));
        }

        private static void SetLayer(SpriteRenderer layer, Vector3 middle, float angle, float length, int widthPx, Color color)
        {
            layer.transform.position = middle;
            layer.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            layer.transform.localScale = new Vector3(length, Mathf.Max(1, widthPx) * CombatFxArt.Pixel, 1f);
            layer.color = color;
        }

        private void UpdatePips()
        {
            if (pips == null) return;
            for (int i = 0; i < pips.Length; i++)
            {
                bool lit = i <= stage;
                Color color = lit ? Color.Lerp(palette.Body, palette.Core, i == stage ? 0.4f : 0f) : palette.Glow;
                color.a = lit ? 1f : 0.35f;
                pips[i].color = color;
            }
        }

        // ---------- Effects ----------

        private void PlayStageUp()
        {
            stageUpFlash = StageUpFlashDuration;
            Vector2 start = source.position;
            Vector2 end = GetEnd();

            CombatFx.Flash(start, palette.Accent, true);
            CombatFx.Flash(end, palette.Accent, true);
            CombatFx.Pop(end, palette.Body);
            CombatFx.Sparks(end, palette.Spark, 4 + stage * 2, 2f + stage * 0.4f, 0.3f, big: true);
            if (paletteKind == LaserPalette.Holy)
                CombatFx.Crosses(start, palette.Accent, 1 + stage / 2, 0.12f, false, 0.5f);
            else
                CombatFx.Sparks(start, palette.Glow, 3 + stage, 1.5f, 0.25f);
        }

        private void EmitStageEffects(float deltaTime)
        {
            if (deltaTime <= 0f) return;

            Vector2 start = source.position;
            Vector2 end = GetEnd();
            Vector2 delta = end - start;
            float length = delta.magnitude;
            if (length < 0.01f) return;
            Vector2 direction = delta / length;
            Vector2 side = new Vector2(-direction.y, direction.x);

            // Stage 1+: a steady sizzle at the impact
            effectCarry += deltaTime * (stage >= 1 ? 6f + stage * 4f : 3f);
            while (effectCarry >= 1f)
            {
                effectCarry -= 1f;
                CombatFx.Sparks(end, palette.Spark, 1, 1.2f + stage * 0.3f, 0.15f + stage * 0.02f);
                if (stage >= 2 && Random.value < 0.3f)
                    CombatFx.Flash(start, palette.Body);
            }

            if (stage < 3) return;

            // Stage 3+: motes stream down the beam
            const float MoteSpeed = 9f;
            moteCarry += deltaTime * (10f + (stage - 3) * 8f);
            while (moteCarry >= 1f)
            {
                moteCarry -= 1f;
                PixelParticles.Emit(new PixelParticle
                {
                    Position = start + side * Random.Range(-1f, 1f) * CombatFxArt.Pixel * BodyWidthPx[stage] * 0.4f,
                    Velocity = direction * MoteSpeed,
                    Life = length / MoteSpeed,
                    StartColor = palette.Accent,
                    EndColor = new Color(palette.Spark.r, palette.Spark.g, palette.Spark.b, 0.6f),
                    Sprite = CombatFxArt.Dot1(),
                    SortingOrder = CoreSortingOrder
                });

                if (paletteKind == LaserPalette.Holy)
                {
                    // Sparkles shed off the beam, drifting up
                    if (Random.value < 0.35f)
                    {
                        PixelParticles.Emit(new PixelParticle
                        {
                            Position = start + delta * Random.value + side * Random.Range(-1f, 1f) * CombatFxArt.Pixel * GlowWidthPx[stage] * 0.5f,
                            Velocity = new Vector2(Random.Range(-0.1f, 0.1f), Random.Range(0.2f, 0.5f)),
                            Life = Random.Range(0.35f, 0.6f),
                            StartColor = palette.Accent,
                            EndColor = new Color(palette.Glow.r, palette.Glow.g, palette.Glow.b, 0f),
                            Sprite = Random.value < 0.4f ? CombatFxArt.Plus3() : CombatFxArt.Dot1(),
                            SortingOrder = CoreSortingOrder
                        });
                    }
                }
                else
                {
                    // Crackling arcs: short-lived bright pixels jumping off the beam
                    if (Random.value < 0.5f)
                    {
                        Vector2 at = start + delta * Random.value;
                        float offset = Random.Range(2f, 3f + stage) * CombatFxArt.Pixel * (Random.value < 0.5f ? -1f : 1f);
                        for (int i = 0; i < 3; i++)
                        {
                            PixelParticles.Emit(new PixelParticle
                            {
                                Position = at + side * offset * (i / 2f) + direction * i * CombatFxArt.Pixel * 2f,
                                Life = 0.06f,
                                StartColor = i == 1 ? palette.Core : palette.Spark,
                                EndColor = new Color(palette.Body.r, palette.Body.g, palette.Body.b, 0f),
                                Sprite = CombatFxArt.Dot1(),
                                SortingOrder = CoreSortingOrder
                            });
                        }
                    }
                }
            }

            if (stage < 4) return;

            // Stage 4+: a halo ring (Holy) or heat smoke (Crimson) at the impact
            if (Random.value < deltaTime * (stage >= MaxStage ? 6f : 3f))
            {
                if (paletteKind == LaserPalette.Holy)
                    CombatFx.Pop(end, palette.Glow);
                else
                    CombatFx.Smoke(end, new Color(0.25f, 0.04f, 0.05f, 0.55f), 1, 0.08f, 0.5f, 0.4f);
            }
        }

        private static Sprite GetPixelSprite()
        {
            if (pixelSprite == null)
            {
                pixelSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            }
            return pixelSprite;
        }
    }
}
