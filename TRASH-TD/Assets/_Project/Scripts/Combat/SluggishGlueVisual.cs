using System.Collections.Generic;
using UnityEngine;
using TrashTD.Enemies;
using TrashTD.Operators;

namespace TrashTD.Combat
{
    public struct GluePalette
    {
        public Color Highlight; // Glossy specular highlight (top curve and bulb glints)
        public Color Body;      // Vivid primary glue body
        public Color Shadow;    // Saturated mid-dark shade for viscous volume
        public Color Outline;   // Dark border for crisp pixel art silhouette separation
        public Color Droplet;   // Falling drip pixel particle color
    }

    /// <summary>
    /// Pixel-art Bloons TD style "glue effect" for the Sluggish status effect (applied by Resicochet and sluggish effects).
    /// Displays a thick, viscous glue splatter cap covering the top of the enemy's silhouette with hanging gooey
    /// teardrop drips, swollen bulbous tips, glossy specular liquid highlights, and falling pixel droplets.
    /// The glue color adapts dynamically based on Resicochet's charge level:
    /// - Charge 1 (Orange / Default): Classic yellow-orange Bloons glue.
    /// - Charge 2 (Green): Corrosive/acid lime-green glue.
    /// - Charge 3 (Blue): Super glue / frozen cyan-cobalt glue.
    /// Built procedurally from the enemy sprite's silhouette (following IceStatusVisual), matching pixel resolution,
    /// sorting order, walk animation, and mirroring (flipX).
    /// </summary>
    public sealed class SluggishGlueVisual : MonoBehaviour
    {
        public const string ObjectName = "SluggishGlueVisual";

        private const int SortingOffset = 11; // Above enemy body (+0), below stun stars (+15)
        private const float WobbleInterval = 0.35f;
        private const int Pad = 2; // Padding around sprite for glue dome and teardrop bulbs

        private static readonly Dictionary<(Sprite source, int charge, int wobble), Sprite> SpriteCache =
            new Dictionary<(Sprite source, int charge, int wobble), Sprite>();

        private static readonly Dictionary<Sprite, Vector2> DripTipCache =
            new Dictionary<Sprite, Vector2>();

        private SpriteRenderer overlay;
        private SpriteRenderer targetRenderer;
        private bool isSluggish;
        private int currentCharge = 1;
        private Sprite builtForSprite;
        private int builtForCharge = -1;
        private int builtForWobble = -1;
        private Sprite overlaySprite;

        private float stateTime;
        private float wobbleTimer;
        private int wobbleFrame;
        private float dripTimer;

        public bool IsSluggish => isSluggish;
        public int CurrentCharge => currentCharge;

        public static SluggishGlueVisual SetState(
            GameObject target,
            bool sluggish,
            int charge,
            SpriteRenderer renderer,
            SluggishGlueVisual existing = null)
        {
            if (target == null) return existing;

            SluggishGlueVisual visual = existing;
            if (visual == null)
            {
                if (!sluggish) return null;

                var go = new GameObject(ObjectName);
                go.transform.SetParent(target.transform, false);
                visual = go.AddComponent<SluggishGlueVisual>();
                visual.overlay = go.AddComponent<SpriteRenderer>();
            }

            visual.Apply(sluggish, charge, renderer);
            return visual;
        }

        public void Apply(bool sluggish, int charge, SpriteRenderer renderer)
        {
            if (renderer != null) targetRenderer = renderer;

            bool wasSluggish = isSluggish;
            int prevCharge = currentCharge;

            isSluggish = sluggish;
            currentCharge = Mathf.Clamp(charge, 1, 3);

            if (!wasSluggish && isSluggish)
            {
                stateTime = 0f;
                wobbleTimer = 0f;
                wobbleFrame = 0;
                dripTimer = Random.Range(0.15f, 0.35f);
                builtForSprite = null;
                builtForCharge = -1;
                builtForWobble = -1;
                PlaySplatBurst();
            }
            else if (prevCharge != currentCharge)
            {
                builtForCharge = -1;
            }

            if (overlay != null)
            {
                overlay.enabled = isSluggish;
                if (isSluggish) Refresh();
            }
        }

        public void PlaySplatBurst()
        {
            GluePalette palette = GetPalette(currentCharge);
            Vector2 center = BodyCenter;
            CombatFx.Droplets(center + Vector2.up * 0.15f, palette.Droplet, 6, 2.2f);
            CombatFx.Pop(center + Vector2.up * 0.20f, palette.Highlight);
        }

        private Vector2 BodyCenter => targetRenderer != null ? (Vector2)targetRenderer.bounds.center : (Vector2)transform.position;

        private void LateUpdate()
        {
            if (!isSluggish || overlay == null) return;

            float dt = Time.deltaTime;
            stateTime += dt;

            // Wobble drip stretch animation
            wobbleTimer += dt;
            if (wobbleTimer >= WobbleInterval)
            {
                wobbleTimer -= WobbleInterval;
                wobbleFrame = (wobbleFrame + 1) % 2;
                builtForWobble = -1;
            }

            // Dripping pixel particles
            dripTimer -= dt;
            if (dripTimer <= 0f && overlay.enabled && targetRenderer != null)
            {
                dripTimer = Random.Range(0.28f, 0.45f);
                EmitFallingDroplet();
            }

            Refresh();
        }

        private void EmitFallingDroplet()
        {
            Sprite sprite = targetRenderer != null ? targetRenderer.sprite : null;
            if (sprite == null) return;

            Vector2 localTip = GetDripTip(sprite);
            float lx = localTip.x * (targetRenderer.flipX ? -1f : 1f);
            float ly = localTip.y;
            Vector3 worldPos = targetRenderer.transform.TransformPoint(new Vector3(lx, ly, 0f));

            GluePalette palette = GetPalette(currentCharge);

            PixelParticles.Emit(new PixelParticle
            {
                Position = worldPos,
                Velocity = new Vector2(Random.Range(-0.12f, 0.12f), -Random.Range(0.6f, 1.2f)),
                Gravity = 6f,
                Drag = 0.5f,
                Life = Random.Range(0.24f, 0.38f),
                StartColor = palette.Droplet,
                EndColor = new Color(palette.Shadow.r, palette.Shadow.g, palette.Shadow.b, 0f),
                Sprite = CombatFxArt.Dot2(),
                SortingOrder = targetRenderer.sortingOrder + SortingOffset + 1
            });
        }

        private void Refresh()
        {
            Sprite sprite = targetRenderer != null ? targetRenderer.sprite : null;
            bool visible = isSluggish && sprite != null && targetRenderer.enabled && targetRenderer.gameObject.activeInHierarchy;
            overlay.enabled = visible;
            if (!visible) return;

            if (sprite != builtForSprite || currentCharge != builtForCharge || wobbleFrame != builtForWobble || overlaySprite == null)
            {
                builtForSprite = sprite;
                builtForCharge = currentCharge;
                builtForWobble = wobbleFrame;
                overlaySprite = GetGlue(sprite, currentCharge, wobbleFrame);
            }

            if (overlay.sprite != overlaySprite) overlay.sprite = overlaySprite;

            Transform target = targetRenderer.transform;
            transform.SetPositionAndRotation(target.position, target.rotation);

            Vector3 lossy = target.lossyScale;
            Vector3 parent = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            Vector3 scale = new Vector3(
                parent.x != 0f ? lossy.x / parent.x : 1f,
                parent.y != 0f ? lossy.y / parent.y : 1f,
                parent.z != 0f ? lossy.z / parent.z : 1f);

            transform.localScale = scale;
            overlay.flipX = targetRenderer.flipX;
            overlay.flipY = targetRenderer.flipY;
            overlay.sortingLayerID = targetRenderer.sortingLayerID;
            overlay.sortingOrder = targetRenderer.sortingOrder + SortingOffset;
            overlay.color = Color.white;
        }

        // =====================================================
        // Glue Palette Definitions (Charge 1 -> 2 -> 3)
        // =====================================================

        public static GluePalette GetPalette(int charge)
        {
            return charge switch
            {
                // Charge 2: Corrosive Lime Green (Bloons corrosive glue)
                2 => new GluePalette
                {
                    Highlight = new Color(0.78f, 1.00f, 0.68f, 0.95f),
                    Body = ResicochetOperator.Charge2ProjectileColor, // (0.2f, 0.95f, 0.35f)
                    Shadow = new Color(0.08f, 0.60f, 0.18f, 0.95f),
                    Outline = new Color(0.03f, 0.32f, 0.08f, 0.95f),
                    Droplet = new Color(0.25f, 0.95f, 0.40f)
                },
                // Charge 3: Super Glue Cyan-Blue (Bloons super glue)
                3 => new GluePalette
                {
                    Highlight = new Color(0.80f, 0.96f, 1.00f, 0.95f),
                    Body = ResicochetOperator.Charge3ProjectileColor, // (0.2f, 0.65f, 1f)
                    Shadow = new Color(0.08f, 0.36f, 0.72f, 0.95f),
                    Outline = new Color(0.03f, 0.18f, 0.45f, 0.95f),
                    Droplet = new Color(0.25f, 0.70f, 1.00f)
                },
                // Charge 1 (Default): Classic Orange (Bloons glue)
                _ => new GluePalette
                {
                    Highlight = new Color(1.00f, 0.94f, 0.65f, 0.95f),
                    Body = ResicochetOperator.Charge1ProjectileColor, // (1f, 0.55f, 0.12f)
                    Shadow = new Color(0.72f, 0.32f, 0.05f, 0.95f),
                    Outline = new Color(0.40f, 0.15f, 0.02f, 0.95f),
                    Droplet = new Color(1.00f, 0.65f, 0.15f)
                }
            };
        }

        // =====================================================
        // Procedural Pixel Art Glue Generation
        // =====================================================

        private static Sprite GetGlue(Sprite sprite, int charge, int wobble)
        {
            var key = (sprite, charge, wobble);
            if (SpriteCache.TryGetValue(key, out Sprite cached) && cached != null) return cached;
            Sprite created = BuildGlueSprite(sprite, charge, wobble);
            SpriteCache[key] = created;
            return created;
        }

        private static Vector2 GetDripTip(Sprite sprite)
        {
            if (DripTipCache.TryGetValue(sprite, out Vector2 tip)) return tip;
            return Vector2.zero;
        }

        private static Sprite BuildGlueSprite(Sprite sprite, int charge, int wobble)
        {
            IceStatusVisual.Silhouette sil = IceStatusVisual.GetSilhouette(sprite);
            if (sil == null || sil.Solid == null) return null;

            int pad = Pad;
            int w = sil.Width + pad * 2;
            int h = sil.Height + pad * 2;

            bool[] body = new bool[w * h];
            int minX = w, maxX = 0, minY = h, maxY = 0;
            bool anySolid = false;

            for (int y = 0; y < sil.Height; y++)
            {
                for (int x = 0; x < sil.Width; x++)
                {
                    if (sil.Solid[y * sil.Width + x])
                    {
                        int bx = x + pad;
                        int by = y + pad;
                        body[by * w + bx] = true;
                        if (bx < minX) minX = bx;
                        if (bx > maxX) maxX = bx;
                        if (by < minY) minY = by;
                        if (by > maxY) maxY = by;
                        anySolid = true;
                    }
                }
            }

            if (!anySolid) return null;

            int bodyWidth = maxX - minX + 1;
            int bodyHeight = maxY - minY + 1;

            int[] topY = new int[w];
            int[] botY = new int[w];
            bool[] colHasSolid = new bool[w];

            for (int x = minX; x <= maxX; x++)
            {
                int top = -1;
                int bot = -1;
                for (int y = maxY; y >= minY; y--)
                {
                    if (body[y * w + x])
                    {
                        if (top == -1) top = y;
                        bot = y;
                    }
                }
                if (top != -1)
                {
                    topY[x] = top;
                    botY[x] = bot;
                    colHasSolid[x] = true;
                }
            }

            int seed = StableHash(sprite.name) * 37 + charge * 7919;
            var rng = new System.Random(seed);

            // Number of vertical drips based on enemy width
            int numDrips = bodyWidth < 12 ? 2 : (bodyWidth < 22 ? 3 : 4);
            int[] dripCols = new int[numDrips];
            int[] dripLengths = new int[numDrips];

            for (int d = 0; d < numDrips; d++)
            {
                float frac = (d + 1f) / (numDrips + 1f);
                int baseCol = minX + Mathf.RoundToInt(bodyWidth * frac) + rng.Next(-1, 2);
                dripCols[d] = Mathf.Clamp(baseCol, minX + 1, maxX - 1);

                // Drip length varies, with a subtle 1-pixel breathing shift in wobble frame 1
                int baseLen = Mathf.Max(3, Mathf.RoundToInt(bodyHeight * (0.35f + 0.22f * (float)rng.NextDouble())));
                int wobbleDelta = (wobble == 1) ? ((d % 2 == 0) ? 1 : -1) : 0;
                dripLengths[d] = Mathf.Max(2, baseLen + wobbleDelta);
            }

            bool[] glue = new bool[w * h];
            bool[] isBulb = new bool[w * h];
            int lowestDripY = maxY;
            int lowestDripX = (minX + maxX) / 2;

            // 1. Fill the Glue Cap (thick dollop doming over the top)
            for (int x = minX; x <= maxX; x++)
            {
                if (!colHasSolid[x]) continue;

                // Dome puff up by 1 pixel in the central columns of the head
                int dome = (x > minX + 1 && x < maxX - 1) ? 1 : 0;
                int colTop = topY[x] + dome;

                // Wavy cap bottom edge
                int capDepth = Mathf.Max(3, Mathf.RoundToInt(bodyHeight * 0.42f) + Mathf.RoundToInt(Mathf.Sin(x * 0.75f + seed) * 1.5f));
                int colBot = Mathf.Max(botY[x], topY[x] - capDepth);

                for (int y = colBot; y <= colTop; y++)
                {
                    if (y >= 0 && y < h)
                    {
                        glue[y * w + x] = true;
                    }
                }
            }

            // 2. Extrude the Hanging Viscous Drips with Bulbous Tips
            for (int d = 0; d < numDrips; d++)
            {
                int dx = dripCols[d];
                if (!colHasSolid[dx]) continue;

                int capDepth = Mathf.Max(3, Mathf.RoundToInt(bodyHeight * 0.42f) + Mathf.RoundToInt(Mathf.Sin(dx * 0.75f + seed) * 1.5f));
                int startY = topY[dx] - capDepth;
                int endY = Mathf.Max(botY[dx] - 1, startY - dripLengths[d]);

                // Track lowest drip tip for falling droplet particles
                if (endY < lowestDripY)
                {
                    lowestDripY = endY;
                    lowestDripX = dx;
                }

                // Drip Stem
                for (int y = endY; y <= startY; y++)
                {
                    if (y >= 0 && y < h)
                    {
                        glue[y * w + dx] = true;
                        // Stem 2-pixels wide for larger enemies
                        if (bodyWidth >= 16 && dx + 1 < w && colHasSolid[Mathf.Min(dx + 1, maxX)])
                        {
                            glue[y * w + dx + 1] = true;
                        }
                    }
                }

                // Bulbous Droplet Tip (rounded surface tension bulb)
                int bulbHeight = Mathf.Min(3, startY - endY + 1);
                for (int by = endY; by < endY + bulbHeight; by++)
                {
                    if (by >= 0 && by < h)
                    {
                        glue[by * w + dx] = true;
                        isBulb[by * w + dx] = true;
                        if (dx - 1 >= 0)
                        {
                            glue[by * w + dx - 1] = true;
                            isBulb[by * w + dx - 1] = true;
                        }
                        if (dx + 1 < w)
                        {
                            glue[by * w + dx + 1] = true;
                            isBulb[by * w + dx + 1] = true;
                        }
                    }
                }
            }

            // Cache the lowest drip tip in local sprite coordinates for droplet particle emission
            float tipLx = (lowestDripX + 0.5f - (sil.Pivot.x + pad)) / sil.PixelsPerUnit;
            float tipLy = (lowestDripY + 0.5f - (sil.Pivot.y + pad)) / sil.PixelsPerUnit;
            DripTipCache[sprite] = new Vector2(tipLx, tipLy);

            // 3. Shading the Viscous Glue (Highlight, Body, Shadow, Outline)
            GluePalette palette = GetPalette(charge);
            Color[] pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

            float cx = (minX + maxX) * 0.5f;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!glue[i]) continue;

                    bool topAir = (y + 1 >= h) || !glue[(y + 1) * w + x];
                    bool botAir = (y - 1 < 0) || !glue[(y - 1) * w + x];
                    bool leftAir = (x - 1 < 0) || !glue[y * w + (x - 1)];
                    bool rightAir = (x + 1 >= w) || !glue[y * w + (x + 1)];

                    // Specular glossy highlight:
                    // - On the upper-left curve of the main cap (matching stage top-left light source)
                    // - A bright glossy dot on the upper-left of each drip bulb
                    bool isCapHighlight = (topAir || leftAir) && (x <= cx + 1 && y >= maxY - 2);
                    bool isBulbHighlight = isBulb[i] && (leftAir || topAir) && !botAir && (x <= dxOrBulb(x, isBulb, w, h));

                    if (isCapHighlight || isBulbHighlight)
                    {
                        pixels[i] = palette.Highlight;
                    }
                    // Dark viscous border / outline on bottom edges and shaded outer contours
                    else if (botAir || (rightAir && y < maxY - 1))
                    {
                        pixels[i] = palette.Outline;
                    }
                    // Mid-dark shadow in lower drip bulbs and lower body of cap
                    else if ((y - 2 >= 0 && !glue[(y - 2) * w + x]) || isBulb[i])
                    {
                        pixels[i] = palette.Shadow;
                    }
                    // Main vivid glue body
                    else
                    {
                        pixels[i] = palette.Body;
                    }
                }
            }

            return MakeSprite(
                $"Glue_{sprite.name}_c{charge}_w{wobble}",
                pixels,
                w,
                h,
                sil.Pivot + new Vector2(pad, pad),
                sil.PixelsPerUnit);
        }

        private static int dxOrBulb(int x, bool[] isBulb, int w, int h)
        {
            return x;
        }

        private static int StableHash(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < text.Length; i++) hash = hash * 31 + text[i];
                return hash;
            }
        }

        private static Sprite MakeSprite(string name, Color[] pixels, int w, int h, Vector2 pivotPixels, float pixelsPerUnit)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, w, h),
                new Vector2(pivotPixels.x / w, pivotPixels.y / h),
                pixelsPerUnit);
        }
    }
}
