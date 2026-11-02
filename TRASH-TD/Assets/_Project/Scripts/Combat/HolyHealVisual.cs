using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;

namespace TrashTD.Combat
{
    /// <summary>
    /// Panacea's heal, drawn without a projectile:
    /// - the caster glows white (a white silhouette over its sprite, the same SpriteFlash shader as hit flashes)
    ///   with a halo of light opening on the ground beneath it;
    /// - the healed operator gets the same ground halo, a soft column of light from above and motes rising.
    /// Halos are pixel-art ellipses (a bright rim, an inner ring and dithered glow) on the Ground layer so they
    /// sit under every unit; a few sparkles run around each rim. Runs on scaled time.
    /// </summary>
    public sealed class HolyHealVisual : MonoBehaviour
    {
        public static readonly Color HolyColor = new Color(1f, 0.96f, 0.82f);

        private const float Duration = 1.1f;
        private const float OpenSeconds = 0.22f;
        private const float FadeStart = 0.65f;
        private const int GroundOrder = 12;
        private const int BeamOrder = 30;
        private const int RimSparkles = 4;
        private const float GlowPeak = 0.85f;
        private const float GlowRise = 0.12f;
        private const float GlowLength = 0.75f;

        private float age;
        private float ringRadiusX;
        private SpriteRenderer halo;
        private SpriteRenderer glow;
        private SpriteRenderer beam;
        private readonly List<SpriteRenderer> sparkles = new List<SpriteRenderer>(RimSparkles);
        private SpriteRenderer glowSource;
        private SpriteRenderer glowOverlay;
        private float moteTimer;

        /// <summary>The healer: glows white, with a halo under it.</summary>
        public static void PlayOnCaster(Component caster) => Spawn(caster, glowWhite: true, beamOfLight: false);

        /// <summary>The healed operator: a halo under it and a column of light.</summary>
        public static void PlayOnTarget(Component target) => Spawn(target, glowWhite: false, beamOfLight: true);

        private static void Spawn(Component unit, bool glowWhite, bool beamOfLight)
        {
            if (unit == null) return;

            SpriteRenderer body = unit.GetComponent<SpriteRenderer>();
            Bounds bounds = body != null && body.sprite != null
                ? body.bounds
                : new Bounds(unit.transform.position, new Vector3(1f, 1f, 0f));
            Vector3 feet = new Vector3(bounds.center.x, bounds.min.y + 3f * CombatFxArt.Pixel, 0f);

            // Standalone, not a child: a child would be drawn inside the unit's SortingGroup, over the tiles but
            // never under neighbouring units.
            var root = new GameObject(glowWhite ? "HolyHeal_Caster" : "HolyHeal_Target");
            root.transform.position = PixelParticles.Snap(feet);
            var visual = root.AddComponent<HolyHealVisual>();
            visual.ringRadiusX = HaloWidthPx * 0.5f * CombatFxArt.Pixel;

            visual.glow = visual.MakeGroundSprite("Glow", GetHaloGlow(), 0);
            visual.halo = visual.MakeGroundSprite("Halo", GetHaloRing(), 1);
            for (int i = 0; i < RimSparkles; i++)
                visual.sparkles.Add(visual.MakeGroundSprite("Sparkle", CombatFxArt.Dot2(), 2));

            if (beamOfLight)
            {
                var beamObject = new GameObject("Beam");
                beamObject.transform.SetParent(root.transform, false);
                visual.beam = beamObject.AddComponent<SpriteRenderer>();
                visual.beam.sprite = GetBeam();
                visual.beam.sortingLayerID = GridSorting.DefaultLayerId;
                visual.beam.sortingOrder = BeamOrder;
                visual.beam.color = Clear(HolyColor);
                CombatFx.Crosses(bounds.center, HolyColor, 3, 0.25f, true, 0.7f, 0.08f);
            }

            if (glowWhite && body != null) visual.StartGlow(body);
        }

        private SpriteRenderer MakeGroundSprite(string name, Sprite sprite, int orderOffset)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            SpriteRenderer renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = Clear(HolyColor);
            GridSorting.PlaceOnGround(renderer);
            renderer.sortingOrder = GroundOrder + orderOffset;
            return renderer;
        }

        private void StartGlow(SpriteRenderer source)
        {
            Material material = GetFlashMaterial();
            if (material == null) return; // Shader missing: the halo still plays

            glowSource = source;
            var overlayObject = new GameObject("HolyGlowOverlay");
            overlayObject.transform.SetParent(source.transform, false);
            glowOverlay = overlayObject.AddComponent<SpriteRenderer>();
            glowOverlay.sharedMaterial = material;
            glowOverlay.color = Clear(Color.white);
        }

        private void Update()
        {
            age += Time.deltaTime;
            float t = age / Duration;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            float open = Mathf.Clamp01(age / OpenSeconds);
            float openEased = 1f - (1f - open) * (1f - open);
            float fade = t < FadeStart ? 1f : 1f - (t - FadeStart) / (1f - FadeStart);
            float breathe = 0.85f + 0.15f * Mathf.Sin(age * 14f);

            // Halo opens outward from the middle, the rim brightest
            float scale = Mathf.Lerp(0.4f, 1f, openEased);
            halo.transform.localScale = new Vector3(scale, scale, 1f);
            halo.color = WithAlpha(HolyColor, fade * breathe);
            glow.transform.localScale = new Vector3(scale * 1.05f, scale * 1.05f, 1f);
            glow.color = WithAlpha(HolyColor, 0.55f * fade);

            // Sparkles chase each other around the rim
            for (int i = 0; i < sparkles.Count; i++)
            {
                float angle = age * 3.2f + i * Mathf.PI * 2f / sparkles.Count;
                Vector3 rim = new Vector3(Mathf.Cos(angle) * ringRadiusX * scale,
                    Mathf.Sin(angle) * ringRadiusX * HaloFlatten * scale, 0f);
                sparkles[i].transform.localPosition = PixelParticles.Snap(rim);
                sparkles[i].color = WithAlpha(Color.white, fade * (0.6f + 0.4f * Mathf.Sin(age * 20f + i)));
            }

            if (beam != null)
            {
                // A column of light: fades in fast, narrows as it fades
                float beamIn = Mathf.Clamp01(age / 0.1f);
                beam.transform.localScale = new Vector3(Mathf.Lerp(1.2f, 0.6f, t), 1f, 1f);
                beam.color = WithAlpha(HolyColor, 0.5f * beamIn * fade);
            }

            UpdateGlow();
            EmitMotes(fade);
        }

        /// <summary>The caster's white silhouette: rises fast, holds a moment, eases off.</summary>
        private void UpdateGlow()
        {
            if (glowOverlay == null || glowSource == null) return;

            float alpha = age < GlowRise
                ? Mathf.Lerp(0f, GlowPeak, age / GlowRise)
                : Mathf.Lerp(GlowPeak, 0f, Mathf.Clamp01((age - GlowRise) / GlowLength));
            bool visible = alpha > 0.01f && glowSource.enabled && glowSource.sprite != null;
            glowOverlay.enabled = visible;
            if (!visible) return;

            glowOverlay.sprite = glowSource.sprite;
            glowOverlay.flipX = glowSource.flipX;
            glowOverlay.flipY = glowSource.flipY;
            glowOverlay.sortingLayerID = glowSource.sortingLayerID;
            glowOverlay.sortingOrder = glowSource.sortingOrder + 1;
            glowOverlay.color = WithAlpha(Color.white, alpha * Mathf.Min(1f, glowSource.color.a + 0.2f));
        }

        /// <summary>Motes of light rising from the halo.</summary>
        private void EmitMotes(float fade)
        {
            moteTimer -= Time.deltaTime;
            if (moteTimer > 0f || fade < 0.3f) return;
            moteTimer = 0.06f;

            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector2 from = (Vector2)transform.position + new Vector2(Mathf.Cos(angle) * ringRadiusX, Mathf.Sin(angle) * ringRadiusX * HaloFlatten) * 0.8f;
            PixelParticles.Emit(new PixelParticle
            {
                Position = from,
                Velocity = new Vector2(0f, Random.Range(0.5f, 0.9f)),
                Life = Random.Range(0.45f, 0.7f),
                Drag = 0.5f,
                StartColor = Color.white,
                EndColor = Clear(HolyColor),
                Sprite = CombatFxArt.Dot1(),
                SortingOrder = CombatFx.ImpactSortingOrder
            });
        }

        private void OnDestroy()
        {
            if (glowOverlay != null) Destroy(glowOverlay.gameObject);
        }

        private static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
        private static Color Clear(Color color) => WithAlpha(color, 0f);

        // ============================
        // Art (generated once, white; tinted by the renderer)
        // ============================

        private const int HaloWidthPx = 31;
        private const float HaloFlatten = 0.4f;

        private static Sprite haloRing;
        private static Sprite haloGlow;
        private static Sprite beamSprite;
        private static Material flashMaterial;

        /// <summary>A flat ellipse: a 1 px bright rim, an inner ring at 70% and four small rays poking out.</summary>
        private static Sprite GetHaloRing()
        {
            if (haloRing != null) return haloRing;

            int width = HaloWidthPx + 4, height = Mathf.RoundToInt(HaloWidthPx * HaloFlatten) + 4;
            Texture2D texture = NewTexture(width, height, "HolyHaloRing");
            float cx = (width - 1) * 0.5f, cy = (height - 1) * 0.5f;
            float rx = HaloWidthPx * 0.5f, ry = HaloWidthPx * 0.5f * HaloFlatten;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float d = Ellipse(x - cx, y - cy, rx, ry);
                    float alpha = 0f;
                    if (d <= 1f && d > 1f - 1.1f / ry) alpha = 1f;                        // rim
                    else if (d <= 0.72f && d > 0.72f - 1f / ry) alpha = 0.7f;            // inner ring
                    bool ray = (Mathf.Abs(x - cx) < 0.6f || Mathf.Abs(y - cy) < 0.6f) && d > 1f && d <= 1f + 2.2f / ry;
                    if (ray) alpha = 0.8f;                                                 // rays at N/E/S/W
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply();
            haloRing = ToSprite(texture);
            return haloRing;
        }

        /// <summary>The halo's soft fill: solid middle fading through dither bands to the rim.</summary>
        private static Sprite GetHaloGlow()
        {
            if (haloGlow != null) return haloGlow;

            int width = HaloWidthPx + 4, height = Mathf.RoundToInt(HaloWidthPx * HaloFlatten) + 4;
            Texture2D texture = NewTexture(width, height, "HolyHaloGlow");
            float cx = (width - 1) * 0.5f, cy = (height - 1) * 0.5f;
            float rx = HaloWidthPx * 0.5f, ry = HaloWidthPx * 0.5f * HaloFlatten;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float d = Ellipse(x - cx, y - cy, rx, ry);
                    float alpha = d > 1f ? 0f : d <= 0.45f ? 0.6f : d <= 0.75f ? 0.4f : (x + y) % 2 == 0 ? 0.3f : 0f;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply();
            haloGlow = ToSprite(texture);
            return haloGlow;
        }

        /// <summary>A column of light standing on the halo: bright core, dithered edges, fading out toward the top.</summary>
        private static Sprite GetBeam()
        {
            if (beamSprite != null) return beamSprite;

            const int width = 15, height = 44;
            Texture2D texture = NewTexture(width, height, "HolyBeam");
            float cx = (width - 1) * 0.5f;
            for (int y = 0; y < height; y++)
            {
                float rise = 1f - (float)y / (height - 1);
                float vertical = Mathf.Min(1f, y / 3f) * rise * rise;    // soft foot, long fade upward
                for (int x = 0; x < width; x++)
                {
                    float across = Mathf.Abs(x - cx) / (cx + 0.5f);
                    float band = across < 0.25f ? 1f : across < 0.55f ? 0.6f : across < 0.85f ? ((x + y) % 2 == 0 ? 0.4f : 0f) : 0f;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, band * vertical));
                }
            }
            texture.Apply();
            // Pivot at the bottom so the column stands on the halo
            beamSprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0f), CombatFxArt.PixelsPerUnit);
            beamSprite.name = "HolyBeam";
            return beamSprite;
        }

        private static float Ellipse(float dx, float dy, float rx, float ry) =>
            Mathf.Sqrt((dx * dx) / (rx * rx) + (dy * dy) / (ry * ry));

        private static Texture2D NewTexture(int width, int height, string name)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name
            };
        }

        private static Sprite ToSprite(Texture2D texture)
        {
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f),
                CombatFxArt.PixelsPerUnit);
            sprite.name = texture.name;
            return sprite;
        }

        private static Material GetFlashMaterial()
        {
            if (flashMaterial != null) return flashMaterial;

            Shader shader = Shader.Find("TrashTD/SpriteFlash");
            if (shader == null) shader = Resources.Load<Shader>("Shaders/SpriteFlash");
            if (shader == null) return null;

            flashMaterial = new Material(shader) { name = "HolyGlow" };
            return flashMaterial;
        }
    }
}
