using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Combat
{
    /// <summary>
    /// Procedural pixel art for projectiles, impacts and AoE effects. Everything is drawn at the stage art's
    /// scale (32 texels per unit, so 1 texel = 1 map pixel on a 1-unit tile) with point filtering.
    /// Shapes are written as ASCII rows (top row first) and shaded from one colour, so each projectile gets a
    /// hot core, highlight, base, shadow and dark outline in its own colour. Sprites are cached by shape and
    /// colour; the cache checks for destroyed sprites because domain reload is off (they are destroyed when
    /// play mode ends but the static cache survives).
    /// </summary>
    public static class CombatFxArt
    {
        public const float PixelsPerUnit = 32f;
        public const float Pixel = 1f / PixelsPerUnit;
        public const int ExplosionFrameCount = 7;

        private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Sprite[]> FrameCache = new Dictionary<string, Sprite[]>();

        // ─────────────────────────────────────────────────────
        //  Projectile bodies (pointing right, +x)
        // ─────────────────────────────────────────────────────

        public static Sprite Bolt(Color color) => Shaded("bolt", color,
            "..DCLW.",
            "DCLWWWW",
            "..DCLW.");

        // Energy bolt: white-hot nose fading into a tapered glowing tail
        public static Sprite PlasmaBolt(Color color) => Shaded("plasmabolt", color,
            "...DCCLL.",
            ".DCLLWWWL",
            "DCLWWWWWW",
            ".DCLLWWWL",
            "...DCCLL.");

        public static Sprite Bullet(Color color) => Shaded("bullet", color,
            "OCLLWWW",
            "OCLLWWW");

        public static Sprite Pellet(Color color) => Shaded("pellet", color,
            "LW",
            "WL");

        public static Sprite Pea(Color color) => Shaded("pea", color,
            ".OOO.",
            "OCLCO",
            "OCLWO",
            "OCLCO",
            ".OOO.");

        public static Sprite Orb(Color color) => Shaded("orb", color,
            "...OOO...",
            ".OOCCCOO.",
            ".OCLLLCO.",
            "OCLWWLCDO",
            "OCLWWLCDO",
            "OCLLLCCDO",
            ".OCCCCDO.",
            ".OODDDOO.",
            "...OOO...");

        public static Sprite Shard(Color color) => Shaded("shard", color,
            "....OO...",
            "..ODCLLO.",
            "ODCLLWWWO",
            "..ODCLLO.",
            "....OO...");

        // Jagged pane of glass: see-through body, bright edge highlights, sharp point forward
        public static Sprite GlassShard(Color color) => Shaded("glassshard", color,
            "..OO.....",
            ".OWLOO...",
            "OLbbLWOO.",
            ".OLbbbLWO",
            "..ODbDOO.",
            "...OO....");

        public static Sprite Quill(Color color) => Shaded("quill", color,
            "DD.......",
            "DCCCCLLWW",
            "DD.......");

        public static Sprite HealCross(Color color) => Shaded("healcross", color,
            "..OOO..",
            "..OWO..",
            "OOOWOOO",
            "OWWWWWO",
            "OOOWOOO",
            "..OWO..",
            "..OOO..");

        public static Sprite Glob(Color color) => Shaded("glob", color,
            "..OOOO..",
            ".OCLLCO.",
            "OCLWLCCO",
            "OCLLCCDO",
            "OCCCCDDO",
            ".OCDDDO.",
            "..OOOO..");

        public static Sprite Bubble(Color color) => Shaded("bubble", color,
            "...CCC...",
            ".CCbbbCC.",
            ".CbwwbbC.",
            "CbbwbbbbC",
            "CbbbbbbbC",
            "CbbbbbbDC",
            ".CbbbbDC.",
            ".CCbbbCC.",
            "...CCC...");

        /// <summary>A junk rocket: a soda-can body with a red nose, dark fins and a flame at the back. Untinted.</summary>
        public static Sprite Rocket() => Shaded("rocket", Color.white,
            "...nn......",
            "..nmmmmmr..",
            "fymmlmmmrr.",
            "..nnnnnnr..",
            "...nn......");

        /// <summary>
        /// The Eradicator's heavy warhead: a long, fat rocket with hazard stripes, a big red nose and a twin
        /// exhaust flame. Untinted.
        /// </summary>
        public static Sprite HeavyRocket() => Shaded("heavyrocket", Color.white,
            "..nn..........",
            ".nnnmmmmmmrr..",
            "fynmymmymmrrr.",
            "fynmlymmymrrrr",
            "fynmymmymmrrr.",
            ".nnnmmmmmmrr..",
            "..nn..........");

        /// <summary>A lumpy clod of sand in four tones. Untinted.</summary>
        public static Sprite SandClod()
        {
            return Cached("sandclod", () =>
            {
                const int size = 8;
                Color[] tones =
                {
                    new Color(0.62f, 0.41f, 0.20f),
                    new Color(0.82f, 0.57f, 0.28f),
                    new Color(0.93f, 0.71f, 0.40f),
                    new Color(1f, 0.87f, 0.61f)
                };
                Texture2D texture = NewTexture(size, size, "Fx SandClod");
                Vector2 center = new Vector2(3.5f, 3.5f);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float d = (new Vector2(x, y) - center).magnitude + (Hash(x, y, 3) - 0.5f) * 1.2f;
                        Color color = Color.clear;
                        if (d <= 2.6f)
                        {
                            float grain = Hash(x + 11, y + 5, 7) + (x - y) * 0.06f;
                            color = tones[grain < 0.25f ? 1 : grain < 0.75f ? 2 : 3];
                        }
                        else if (d <= 3.6f)
                        {
                            color = tones[0];
                        }
                        texture.SetPixel(x, y, color);
                    }
                }
                texture.Apply();
                return ToSprite(texture);
            });
        }

        // ─────────────────────────────────────────────────────
        //  Particles (white, tinted by the particle colour)
        // ─────────────────────────────────────────────────────

        public static Sprite Dot1() => White("dot1", "W");

        public static Sprite Dot2() => White("dot2", "WW", "WW");

        public static Sprite Plus3() => White("plus3", ".W.", "WWW", ".W.");

        /// <summary>3x3 hollow mini-bubble foam pixel bit: .W. / W.W / .W.</summary>
        public static Sprite FoamBit() => White("foambit",
            ".W.",
            "W.W",
            ".W.");

        public static Sprite GlassFragment() => White("glassfrag", "WW.", ".WW");

        public static Sprite Plus5() => White("plus5",
            "..W..",
            "..W..",
            "WWWWW",
            "..W..",
            "..W..");

        public static Sprite Flash5() => White("flash5",
            "..W..",
            ".WWW.",
            "WWWWW",
            ".WWW.",
            "..W..");

        public static Sprite Flash7() => White("flash7",
            "...W...",
            "...W...",
            "..WWW..",
            "WWWWWWW",
            "..WWW..",
            "...W...",
            "...W...");

        /// <summary>Smoke puff, growing over 4 frames, with dithered edges.</summary>
        public static Sprite[] SmokeFrames()
        {
            return CachedFrames("smoke", () =>
            {
                var frames = new Sprite[4];
                for (int f = 0; f < frames.Length; f++)
                {
                    float radius = 1.6f + f * 0.9f;
                    int size = Mathf.CeilToInt(radius * 2f) + 2;
                    Texture2D texture = NewTexture(size, size, "Fx Smoke " + f);
                    Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            float d = (new Vector2(x, y) - center).magnitude;
                            bool solid = d <= radius - 0.8f;
                            bool dither = !solid && d <= radius + 0.3f && (x + y) % 2 == 0;
                            float shade = y > center.y ? 1f : 0.82f; // lit from above
                            texture.SetPixel(x, y, solid || dither ? new Color(shade, shade, shade, 1f) : Color.clear);
                        }
                    }
                    texture.Apply();
                    frames[f] = ToSprite(texture);
                }
                return frames;
            });
        }

        /// <summary>A 1 px ring opening from radius 2 to 5 over 4 frames, for small impact pops.</summary>
        public static Sprite[] PopRingFrames()
        {
            return CachedFrames("popring", () =>
            {
                var frames = new Sprite[4];
                for (int f = 0; f < frames.Length; f++) frames[f] = BuildRing("Fx PopRing " + f, 2 + f, 1f, false);
                return frames;
            });
        }

        /// <summary>
        /// 6 frames of Bubblets' 40x40 protective bubble popping: wobble & tension flare, perimeter rupture,
        /// burst into 8 droplet beads, droplet expansion, fine mist, and final evaporation.
        /// Matches Bubble-pop.png.
        /// </summary>
        public static Sprite[] BubblePopFrames()
        {
            return CachedFrames("bubblepop", () =>
            {
#if UNITY_EDITOR
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Operators/Bubble-pop.png");
                if (assets != null)
                {
                    var list = new System.Collections.Generic.List<Sprite>();
                    foreach (var a in assets)
                    {
                        if (a is Sprite s) list.Add(s);
                    }
                    if (list.Count >= 6)
                    {
                        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                        return list.ToArray();
                    }
                }
#endif
                return BuildProceduralBubblePopFrames();
            });
        }

        private static Sprite[] BuildProceduralBubblePopFrames()
        {
            string path = System.IO.Path.Combine(Application.dataPath, "_Project/Art/Operators/Bubble-pop.png");
            if (System.IO.File.Exists(path))
            {
                try
                {
                    byte[] raw = System.IO.File.ReadAllBytes(path);
                    var tex = new Texture2D(288, 48, TextureFormat.RGBA32, false);
                    tex.filterMode = FilterMode.Point;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.LoadImage(raw);
                    var loadedFrames = new Sprite[6];
                    for (int i = 0; i < 6; i++)
                    {
                        loadedFrames[i] = Sprite.Create(
                            tex,
                            new Rect(i * 48, 0, 48, 48),
                            new Vector2(0.5f, 0.5f),
                            PixelsPerUnit);
                        loadedFrames[i].name = $"Bubble-pop_{i}";
                    }
                    return loadedFrames;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[CombatFxArt] Failed to load Bubble-pop.png: {ex.Message}");
                }
            }

            // Procedural fallback matching the exact 6-frame pixel art sequence
            const int size = 48;
            const float cx = 23.5f;
            const float cy = 23.5f;

            Color cDark = new Color(9f / 255f, 30f / 255f, 60f / 255f, 1f);
            Color cRim = new Color(129f / 255f, 186f / 255f, 228f / 255f, 1f);
            Color cLight = new Color(209f / 255f, 237f / 255f, 255f / 255f, 1f);
            Color cWhite = Color.white;
            Color cBody = new Color(116f / 255f, 151f / 255f, 177f / 255f, 0.33f);
            Color cShadow = new Color(76f / 255f, 122f / 255f, 178f / 255f, 0.33f);

            var frames = new Sprite[6];
            float[] angles8 = { 0f, 45f, 90f, 135f, 180f, 225f, 270f, 315f };

            for (int f = 0; f < 6; f++)
            {
                Texture2D tex = NewTexture(size, size, $"Fx BubblePop {f}");
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }

                if (f == 0)
                {
                    // Frame 0: Wobble & Rim flare
                    float r = 16.5f;
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            float dx = x - cx;
                            float dy = y - cy;
                            float dist = Mathf.Sqrt(dx * dx + dy * dy);
                            if (dist > r + 1.2f) continue;

                            if (dist >= r - 0.9f && dist <= r + 0.9f)
                            {
                                if (dy > 0 && dx < 0) tex.SetPixel(x, y, cWhite);
                                else if (dy > 0 || dx < 0) tex.SetPixel(x, y, cLight);
                                else tex.SetPixel(x, y, cRim);
                            }
                            else if (dist > r + 0.5f && dist <= r + 1.2f)
                            {
                                tex.SetPixel(x, y, cDark);
                            }
                            else if (dist < r - 0.9f)
                            {
                                if (dx < -4 && dy > 4 && dist > 7f) tex.SetPixel(x, y, cLight);
                                else if (dy < -2) tex.SetPixel(x, y, cShadow);
                                else tex.SetPixel(x, y, cBody);
                            }
                        }
                    }
                }
                else if (f == 1)
                {
                    // Frame 1: Rupture & 8 Puncture gaps
                    float r = 17.2f;
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            float dx = x - cx;
                            float dy = y - cy;
                            float dist = Mathf.Sqrt(dx * dx + dy * dy);
                            if (dist > r + 1.2f) continue;

                            float angleDeg = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                            if (angleDeg < 0f) angleDeg += 360f;

                            float minGapDist = 999f;
                            for (int i = 0; i < 8; i++)
                            {
                                float gapAngle = i * 45f + 22.5f;
                                float diff = Mathf.Abs(angleDeg - gapAngle);
                                if (diff > 180f) diff = 360f - diff;
                                if (diff < minGapDist) minGapDist = diff;
                            }

                            if (minGapDist < 11f) continue;

                            if (dist >= r - 1.2f && dist <= r + 0.9f)
                            {
                                tex.SetPixel(x, y, minGapDist < 16f ? cLight : cRim);
                            }
                            else if (dist > r + 0.6f && dist <= r + 1.2f)
                            {
                                tex.SetPixel(x, y, cDark);
                            }
                            else if (dist >= 10f && dist < r - 1.2f && (x + y) % 2 == 0)
                            {
                                tex.SetPixel(x, y, cBody);
                            }
                        }
                    }
                }
                else if (f == 2)
                {
                    // Frame 2: Radial burst into 8 droplets at radius 19.5
                    for (int i = 0; i < 8; i++)
                    {
                        float rad = angles8[i] * Mathf.Deg2Rad;
                        int bx = Mathf.RoundToInt(cx + 19.5f * Mathf.Cos(rad));
                        int by = Mathf.RoundToInt(cy + 19.5f * Mathf.Sin(rad));
                        if (bx >= 1 && bx < size - 1 && by >= 1 && by < size - 1)
                        {
                            tex.SetPixel(bx, by, cWhite);
                            tex.SetPixel(bx - 1, by, cLight);
                            tex.SetPixel(bx + 1, by, cRim);
                            tex.SetPixel(bx, by - 1, cRim);
                            tex.SetPixel(bx, by + 1, cLight);
                            tex.SetPixel(bx + 1, by - 1, cDark);
                        }

                        int sx = Mathf.RoundToInt(cx + 14.5f * Mathf.Cos(rad + 0.15f));
                        int sy = Mathf.RoundToInt(cy + 14.5f * Mathf.Sin(rad + 0.15f));
                        if (sx >= 0 && sx < size && sy >= 0 && sy < size) tex.SetPixel(sx, sy, cLight);
                    }
                }
                else if (f == 3)
                {
                    // Frame 3: Droplets at radius 22.5
                    for (int i = 0; i < 8; i++)
                    {
                        float rad = angles8[i] * Mathf.Deg2Rad;
                        int bx = Mathf.RoundToInt(cx + 22.5f * Mathf.Cos(rad));
                        int by = Mathf.RoundToInt(cy + 22.5f * Mathf.Sin(rad));
                        if (bx >= 1 && bx < size - 1 && by >= 1 && by < size - 1)
                        {
                            tex.SetPixel(bx, by, cLight);
                            tex.SetPixel(bx + (int)Mathf.Sign(Mathf.Cos(rad)), by, cRim);
                            tex.SetPixel(bx, by + (int)Mathf.Sign(Mathf.Sin(rad)), cDark);
                        }

                        int tx = Mathf.RoundToInt(cx + 17.5f * Mathf.Cos(rad));
                        int ty = Mathf.RoundToInt(cy + 17.5f * Mathf.Sin(rad));
                        if (tx >= 0 && tx < size && ty >= 0 && ty < size) tex.SetPixel(tx, ty, cLight);
                    }
                }
                else if (f == 4)
                {
                    // Frame 4: Dispersing 1px mist dots at radius 24.5
                    Color cSoft = new Color(cRim.r, cRim.g, cRim.b, 0.65f);
                    for (int i = 0; i < 8; i++)
                    {
                        float rad = angles8[i] * Mathf.Deg2Rad;
                        int bx = Mathf.RoundToInt(cx + 24.5f * Mathf.Cos(rad));
                        int by = Mathf.RoundToInt(cy + 24.5f * Mathf.Sin(rad));
                        if (bx >= 0 && bx < size && by >= 0 && by < size) tex.SetPixel(bx, by, cSoft);
                    }
                }
                else if (f == 5)
                {
                    // Frame 5: Faint evaporating mist dots
                    Color cFaint = new Color(cRim.r, cRim.g, cRim.b, 0.3f);
                    for (int i = 0; i < 8; i += 2)
                    {
                        float rad = (i * 45f + 15f) * Mathf.Deg2Rad;
                        int bx = Mathf.RoundToInt(cx + 25.5f * Mathf.Cos(rad));
                        int by = Mathf.RoundToInt(cy + 25.5f * Mathf.Sin(rad));
                        if (bx >= 0 && bx < size && by >= 0 && by < size) tex.SetPixel(bx, by, cFaint);
                    }
                }

                tex.Apply();
                frames[f] = ToSprite(tex);
                frames[f].name = $"Bubble-pop_{f}";
            }

            return frames;
        }

        // ─────────────────────────────────────────────────────
        //  AoE
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// The exact area of a circular AoE: a 1 px rim at the radius and a 50% checker dither inside.
        /// White; the effect tints it.
        /// </summary>
        public static Sprite AreaCircle(int radiusPx)
        {
            return Cached("areacircle" + radiusPx, () =>
            {
                int size = radiusPx * 2 + 1;
                Texture2D texture = NewTexture(size, size, "Fx AreaCircle " + radiusPx);
                float c = radiusPx;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float d = new Vector2(x - c, y - c).magnitude;
                        Color color = Color.clear;
                        if (d <= radiusPx + 0.5f && d > radiusPx - 0.9f) color = Color.white;
                        else if (d <= radiusPx - 0.9f && (x + y) % 2 == 0) color = new Color(1f, 1f, 1f, 0.55f);
                        texture.SetPixel(x, y, color);
                    }
                }
                texture.Apply();
                return ToSprite(texture);
            });
        }

        /// <summary>Solid white disc (tinted by the renderer): orbs and hot cores.</summary>
        public static Sprite Disc(int radiusPx)
        {
            radiusPx = Mathf.Max(0, radiusPx);
            return Cached("disc" + radiusPx, () => RadialSprite("Fx Disc " + radiusPx, radiusPx,
                (d, x, y) => d <= radiusPx + 0.5f ? 1f : 0f));
        }

        /// <summary>
        /// Pixel-art glow: a solid middle, then two dithered bands fading out to the radius (tinted by the renderer).
        /// </summary>
        public static Sprite GlowDisc(int radiusPx)
        {
            radiusPx = Mathf.Max(1, radiusPx);
            return Cached("glowdisc" + radiusPx, () => RadialSprite("Fx GlowDisc " + radiusPx, radiusPx, (d, x, y) =>
            {
                float t = d / (radiusPx + 0.5f);
                if (t > 1f) return 0f;
                if (t <= 0.45f) return 1f;
                if (t <= 0.75f) return 0.6f;
                return (x + y) % 2 == 0 ? 0.35f : 0f;
            }));
        }

        /// <summary>Star flare: long 1 px rays up, down, left and right, shorter diagonals, a bright centre.</summary>
        public static Sprite Flare(int radiusPx)
        {
            radiusPx = Mathf.Max(2, radiusPx);
            return Cached("flare" + radiusPx, () => RadialSprite("Fx Flare " + radiusPx, radiusPx, (d, x, y) =>
            {
                int dx = Mathf.Abs(x - radiusPx), dy = Mathf.Abs(y - radiusPx);
                if (d <= 1.5f) return 1f;
                if (dx == 0 || dy == 0) return d <= radiusPx + 0.5f ? 1f - 0.5f * (d / radiusPx) : 0f;
                if (dx == dy) return d <= radiusPx * 0.6f ? 0.7f : 0f;
                return 0f;
            }));
        }

        /// <summary>Square sprite of (2r+1) px whose alpha comes from <paramref name="alphaAt"/>(distance, x, y).</summary>
        private static Sprite RadialSprite(string name, int radiusPx, Func<float, int, int, float> alphaAt)
        {
            int size = radiusPx * 2 + 1;
            Texture2D texture = NewTexture(size, size, name);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x - radiusPx, y - radiusPx).magnitude;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alphaAt(d, x, y)));
                }
            }
            texture.Apply();
            return ToSprite(texture);
        }

        /// <summary>Shockwave: a 2 px ring expanding to the exact radius over 6 frames.</summary>
        public static Sprite[] ShockwaveFrames(int radiusPx)
        {
            return CachedFrames("shockwave" + radiusPx, () =>
            {
                float[] steps = { 0.25f, 0.45f, 0.65f, 0.8f, 0.92f, 1f };
                var frames = new Sprite[steps.Length];
                for (int i = 0; i < steps.Length; i++)
                {
                    int r = Mathf.Max(2, Mathf.RoundToInt(radiusPx * steps[i]));
                    // Frames share the full size so they stay centred
                    frames[i] = BuildRing($"Fx Shockwave {radiusPx}/{i}", r, 2f, false, radiusPx);
                }
                return frames;
            });
        }

        /// <summary>
        /// A scorch mark left on the ground: a ragged disc, solid in the middle and breaking into a dither at
        /// its noisy edge. White; the effect tints it (dark).
        /// </summary>
        public static Sprite Scorch(int radiusPx)
        {
            return Cached("scorch" + radiusPx, () =>
            {
                int size = radiusPx * 2 + 1;
                Texture2D texture = NewTexture(size, size, "Fx Scorch " + radiusPx);
                float c = radiusPx;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float d = new Vector2(x - c, y - c).magnitude / radiusPx;
                        float edge = 0.7f + 0.3f * ValueNoise(x / 3f, y / 3f, 29);
                        Color color = Color.clear;
                        if (d <= edge * 0.75f) color = Color.white;
                        else if (d <= edge && (x + y) % 2 == 0) color = new Color(1f, 1f, 1f, 0.7f);
                        else if (d <= edge * 1.15f && x % 2 == 0 && y % 2 == 0) color = new Color(1f, 1f, 1f, 0.45f);
                        texture.SetPixel(x, y, color);
                    }
                }
                texture.Apply();
                return ToSprite(texture);
            });
        }

        /// <summary>A 1 px target ring for blast warnings, sized to the blast radius.</summary>
        public static Sprite TargetRing(int radiusPx) => Cached("targetring" + radiusPx,
            () => BuildRing("Fx TargetRing " + radiusPx, radiusPx, 1f, false));

        /// <summary>One grid cell of a cell-based AoE: dithered fill, 1 px rim and 4 px corner brackets.</summary>
        public static Sprite AreaCell()
        {
            return Cached("areacell", () =>
            {
                const int size = 32;
                const int bracket = 5;
                Texture2D texture = NewTexture(size, size, "Fx AreaCell");
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int dx = Mathf.Min(x, size - 1 - x);
                        int dy = Mathf.Min(y, size - 1 - y);
                        bool rim = dx == 0 || dy == 0;
                        bool corner = (dx <= 1 && dy < bracket) || (dy <= 1 && dx < bracket);
                        Color color = Color.clear;
                        if (corner) color = Color.white;
                        else if (rim) color = new Color(1f, 1f, 1f, 0.8f);
                        else if ((x + y) % 2 == 0) color = new Color(1f, 1f, 1f, 0.45f);
                        texture.SetPixel(x, y, color);
                    }
                }
                texture.Apply();
                return ToSprite(texture);
            });
        }

        /// <summary>
        /// Fireball flipbook, sizePx across: a white-hot core blooming through yellow, orange and red into a
        /// hollowing ring of dithered smoke. Untinted.
        /// </summary>
        public static Sprite[] ExplosionFrames(int sizePx)
        {
            sizePx = Mathf.Clamp(sizePx / 4 * 4, 12, 128);
            return CachedFrames("explosion" + sizePx, () =>
            {
                Color[] palette =
                {
                    new Color(0.24f, 0.22f, 0.22f), // 0 dark smoke
                    new Color(0.45f, 0.42f, 0.40f), // 1 smoke
                    new Color(0.55f, 0.16f, 0.10f), // 2 deep red
                    new Color(0.90f, 0.28f, 0.10f), // 3 red-orange
                    new Color(1f, 0.58f, 0.15f),    // 4 orange
                    new Color(1f, 0.86f, 0.35f),    // 5 yellow
                    new Color(1f, 0.98f, 0.85f)     // 6 white-hot
                };

                var frames = new Sprite[ExplosionFrameCount];
                float half = sizePx * 0.5f;
                for (int f = 0; f < ExplosionFrameCount; f++)
                {
                    float t = f / (float)(ExplosionFrameCount - 1);
                    float grow = 1f - (1f - t) * (1f - t) * (1f - t);
                    float radius = Mathf.Lerp(0.3f, 0.92f, grow) * half;
                    float hollow = t > 0.45f ? (t - 0.45f) / 0.55f * radius * 0.85f : -1f;

                    Texture2D texture = NewTexture(sizePx, sizePx, $"Fx Explosion {sizePx}/{f}");
                    for (int y = 0; y < sizePx; y++)
                    {
                        for (int x = 0; x < sizePx; x++)
                        {
                            float dx = x + 0.5f - half;
                            float dy = y + 0.5f - half;
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            float noise = ValueNoise(x / 4f, y / 4f, 17 + f / 3);
                            float edge = radius * (0.78f + 0.4f * noise);
                            if (d > edge || d < hollow * (0.8f + 0.4f * noise))
                            {
                                texture.SetPixel(x, y, Color.clear);
                                continue;
                            }

                            float inner = 1f - d / edge;
                            float heat = inner * 1.1f + (1f - t) * 0.9f - t * 0.35f + (noise - 0.5f) * 0.5f + dy / half * 0.12f;
                            int index = heat > 1.45f ? 6 : heat > 1.15f ? 5 : heat > 0.9f ? 4 : heat > 0.65f ? 3 : heat > 0.45f ? 2 : heat > 0.2f ? 1 : 0;

                            // Late frames: smoke thins into a dither at the edges
                            bool thin = index <= 1 && t > 0.6f && (x + y) % 2 == 0 && inner < 0.35f;
                            texture.SetPixel(x, y, thin ? Color.clear : palette[index]);
                        }
                    }
                    texture.Apply();
                    frames[f] = ToSprite(texture);
                }
                return frames;
            });
        }

        // ─────────────────────────────────────────────────────
        //  Builders
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// Shape shaded from one colour. W core (near white), L highlight, C base, D shadow, O outline,
        /// b translucent fill, w white. Fixed colours: m metal, l metal shine, n dark metal, r red, f flame, y yellow.
        /// </summary>
        private static Sprite Shaded(string shape, Color color, params string[] rows)
        {
            string key = shape + ColorUtility.ToHtmlStringRGBA(color);
            return Cached(key, () =>
            {
                Color core = Color.Lerp(color, Color.white, 0.75f);
                Color light = Color.Lerp(color, Color.white, 0.35f);
                Color shadow = new Color(color.r * 0.62f, color.g * 0.62f, color.b * 0.62f, 1f);
                Color outline = new Color(color.r * 0.22f, color.g * 0.22f, color.b * 0.22f, 0.9f);
                Color fill = new Color(color.r, color.g, color.b, 0.28f);
                Color solid = new Color(color.r, color.g, color.b, 1f);

                return FromRows("Fx " + key, rows, ch => ch switch
                {
                    'W' => core,
                    'L' => light,
                    'C' => solid,
                    'D' => shadow,
                    'O' => outline,
                    'b' => fill,
                    'w' => Color.white,
                    'm' => new Color(0.66f, 0.68f, 0.72f),
                    'l' => new Color(0.88f, 0.9f, 0.94f),
                    'n' => new Color(0.32f, 0.34f, 0.40f),
                    'r' => new Color(0.86f, 0.2f, 0.16f),
                    'f' => new Color(1f, 0.5f, 0.12f),
                    'y' => new Color(1f, 0.9f, 0.4f),
                    _ => Color.clear
                });
            });
        }

        private static Sprite White(string key, params string[] rows) =>
            Cached(key, () => FromRows("Fx " + key, rows, ch => ch == 'W' ? Color.white : Color.clear));

        private static Sprite FromRows(string name, string[] rows, Func<char, Color> palette)
        {
            int height = rows.Length;
            int width = rows[0].Length;
            Texture2D texture = NewTexture(width, height, name);
            for (int row = 0; row < height; row++)
            {
                for (int x = 0; x < width; x++)
                {
                    texture.SetPixel(x, height - 1 - row, palette(rows[row][x]));
                }
            }
            texture.Apply();
            return ToSprite(texture);
        }

        /// <summary>A ring of the given radius and thickness, in a texture sized for canvasRadius (defaults to radius).</summary>
        private static Sprite BuildRing(string name, int radius, float thickness, bool dithered, int canvasRadius = -1)
        {
            if (canvasRadius < radius) canvasRadius = radius;
            int size = canvasRadius * 2 + 1;
            Texture2D texture = NewTexture(size, size, name);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x - canvasRadius, y - canvasRadius).magnitude;
                    bool on = d <= radius + 0.5f && d > radius + 0.5f - thickness;
                    if (dithered && (x + y) % 2 == 1) on = false;
                    texture.SetPixel(x, y, on ? Color.white : Color.clear);
                }
            }
            texture.Apply();
            return ToSprite(texture);
        }

        private static Texture2D NewTexture(int width, int height, string name)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        private static Sprite ToSprite(Texture2D texture) =>
            Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), PixelsPerUnit);

        private static Sprite Cached(string key, Func<Sprite> build)
        {
            if (SpriteCache.TryGetValue(key, out Sprite sprite) && sprite != null) return sprite;
            sprite = build();
            SpriteCache[key] = sprite;
            return sprite;
        }

        private static Sprite[] CachedFrames(string key, Func<Sprite[]> build)
        {
            if (FrameCache.TryGetValue(key, out Sprite[] frames) && frames != null &&
                Array.TrueForAll(frames, frame => frame != null))
            {
                return frames;
            }
            frames = build();
            FrameCache[key] = frames;
            return frames;
        }

        // ─────────────────────────────────────────────────────
        //  Noise
        // ─────────────────────────────────────────────────────

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint hash = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                hash = (hash ^ (hash >> 13)) * 1274126177;
                return (hash ^ (hash >> 16)) / (float)uint.MaxValue;
            }
        }

        /// <summary>Smooth value noise in 0-1.</summary>
        private static float ValueNoise(float x, float y, int seed)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float tx = x - x0;
            float ty = y - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            float a = Mathf.Lerp(Hash(x0, y0, seed), Hash(x0 + 1, y0, seed), tx);
            float b = Mathf.Lerp(Hash(x0, y0 + 1, seed), Hash(x0 + 1, y0 + 1, seed), tx);
            return Mathf.Lerp(a, b, ty);
        }
    }
}
