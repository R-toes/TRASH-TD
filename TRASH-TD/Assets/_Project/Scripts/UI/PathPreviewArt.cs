using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.UI
{
    /// <summary>
    /// Shared look and geometry for the two enemy route previews: StageMapPreview (stage dossier) and
    /// GameplayPathPreviewOverlay (in game). Routes are drawn as reclamation-plant conveyor lanes: a dark
    /// channel with bright rims and chevrons that march from spawn to exit at Grunt walking speed. Turns get
    /// junction nodes, spawns a hazard-striped breach frame with a warning sign, exits a shield in lock-on
    /// brackets, and both get square radar pings. A tracer blip runs each route now and then.
    /// All art is generated white (so it can be tinted per route) at map-art scale: 1 texel = 1 pixel of the
    /// stage art, point-filtered, so the previews sit on the pixel art without looking pasted on.
    /// </summary>
    internal static class PathPreviewArt
    {
        public const int LanePeriodPx = 12;     // One chevron + gap along the lane
        public const int LaneHeightPx = 10;     // Outline, rim, 6 px channel, rim, outline
        public const int MinLaneWidthPx = 4;
        public const int SpawnFrameSizePx = 24;
        public const int SpawnFrameCount = 6;
        public const int ExitPlateSizePx = 16;
        public const int ExitBracketSizePx = 26;
        public const int PingSizePx = 24;
        public const int JunctionSizePx = 12;
        public const int TracerHeadPx = 4;
        public const int TracerTrailLength = 3;
        public const int TracerTrailSpacingPx = 3;

        public const float MaxLaneSpacingTiles = 0.32f;
        public const float LaneSpreadTiles = 0.9f;    // All lanes together stay within this width
        public const float FlowTilesPerSecond = 1f;   // Grunt walking speed: the chevrons move like the enemies will
        public const float TracerTilesPerSecond = 3.5f;
        public const float TracerGapTiles = 5f;       // Pause between tracer runs, as distance travelled
        public const float RouteDrawDuration = 0.9f;
        public const float GridFadeDuration = 0.3f;
        public const float MarkerPopDuration = 0.25f;
        public const float PingPeriod = 1.4f;
        public const float StripeFramesPerSecond = 10f;

        public static readonly Color PhosphorColor = new Color(0.22f, 0.91f, 0.50f, 1f);
        public static readonly Color ExitColor = new Color(0.32f, 1f, 0.60f, 1f);
        public static readonly Color UnroutedSpawnColor = new Color(1f, 0.32f, 0.24f, 1f);
        public static readonly Color LabelPlateColor = new Color(0.01f, 0.03f, 0.02f, 0.82f);

        private static readonly Color Outline = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color Rim = new Color(1f, 1f, 1f, 0.85f);
        private static readonly Color Channel = new Color(0.28f, 0.28f, 0.28f, 0.75f);
        private static readonly Color Plate = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color StripeDark = new Color(0f, 0f, 0f, 0.85f);

        private static Texture2D laneTexture;
        private static Sprite[] spawnFrames;
        private static Sprite warningIcon;
        private static Sprite shieldIcon;
        private static Sprite exitPlate;
        private static Sprite exitBrackets;
        private static Sprite pingSquare;
        private static Sprite junction;
        private static Sprite pixel;

        // ─────────────────────────────────────────────────────
        //  Lane layout
        // ─────────────────────────────────────────────────────

        public static float LaneSpacingTiles(int routeCount) =>
            Mathf.Min(MaxLaneSpacingTiles, LaneSpreadTiles / Mathf.Max(1, routeCount));

        /// <summary>Lane width in art pixels: full width alone, narrower when several routes share a path.</summary>
        public static int LaneWidthPx(int routeCount, float pixelsPerTile)
        {
            if (routeCount <= 1) return LaneHeightPx;
            return Mathf.Clamp(Mathf.FloorToInt(LaneSpacingTiles(routeCount) * pixelsPerTile), MinLaneWidthPx, LaneHeightPx);
        }

        public static float LaneOffsetTiles(int routeIndex, int routeCount) =>
            (routeIndex - (routeCount - 1) * 0.5f) * LaneSpacingTiles(routeCount);

        // ─────────────────────────────────────────────────────
        //  Geometry
        // ─────────────────────────────────────────────────────

        /// <summary>Corners only: drops repeated points and points that continue in a straight line.</summary>
        public static List<Vector2> KeepCorners(List<Vector2> points)
        {
            var corners = new List<Vector2>();
            foreach (Vector2 point in points)
            {
                if (corners.Count > 0 && (point - corners[corners.Count - 1]).sqrMagnitude < 0.0001f) continue;
                if (corners.Count >= 2)
                {
                    Vector2 inDir = (corners[corners.Count - 1] - corners[corners.Count - 2]).normalized;
                    Vector2 outDir = (point - corners[corners.Count - 1]).normalized;
                    if (Vector2.Dot(inDir, outDir) > 0.999f) corners.RemoveAt(corners.Count - 1);
                }
                corners.Add(point);
            }
            return corners;
        }

        /// <summary>Shifts the whole polyline sideways into its lane, mitring the corners.</summary>
        public static List<Vector2> OffsetIntoLane(List<Vector2> centers, float laneOffset)
        {
            var points = new List<Vector2>(centers.Count);
            for (int i = 0; i < centers.Count; i++)
            {
                Vector2 inNormal = i > 0 ? LeftNormal(centers[i] - centers[i - 1]) : Vector2.zero;
                Vector2 outNormal = i < centers.Count - 1 ? LeftNormal(centers[i + 1] - centers[i]) : Vector2.zero;
                Vector2 miter = inNormal + outNormal;
                if (miter.sqrMagnitude < 0.0001f) miter = i > 0 ? inNormal : outNormal;
                miter.Normalize();
                Vector2 reference = i > 0 ? inNormal : outNormal;
                float stretch = 1f / Mathf.Max(0.35f, Vector2.Dot(miter, reference));
                points.Add(centers[i] + miter * laneOffset * stretch);
            }
            return points;
        }

        public static List<float> CumulativeLengths(List<Vector2> points, out float total)
        {
            var cumulative = new List<float>(points.Count) { 0f };
            total = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                total += Vector2.Distance(points[i - 1], points[i]);
                cumulative.Add(total);
            }
            return cumulative;
        }

        public static Vector2 PointAlong(List<Vector2> points, List<float> cumulative, float distance)
        {
            if (distance <= 0f) return points[0];
            for (int i = 1; i < points.Count; i++)
            {
                if (distance > cumulative[i]) continue;
                float segment = cumulative[i] - cumulative[i - 1];
                float t = segment > 0.0001f ? (distance - cumulative[i - 1]) / segment : 0f;
                return Vector2.Lerp(points[i - 1], points[i], t);
            }
            return points[points.Count - 1];
        }

        private static Vector2 LeftNormal(Vector2 direction)
        {
            Vector2 d = direction.normalized;
            return new Vector2(-d.y, d.x);
        }

        // ─────────────────────────────────────────────────────
        //  Animation helpers
        // ─────────────────────────────────────────────────────

        public static float EaseOutCubic(float t)
        {
            float u = 1f - Mathf.Clamp01(t);
            return 1f - u * u * u;
        }

        /// <summary>Marker entrance: 0 → 1 over MarkerPopDuration, starting at appearAt.</summary>
        public static float Pop(float time, float appearAt) =>
            EaseOutCubic((time - appearAt) / MarkerPopDuration);

        /// <summary>Scale for a popping marker: starts 1.6× and settles to 1×.</summary>
        public static float PopScale(float pop) => Mathf.Lerp(1.6f, 1f, pop);

        /// <summary>
        /// Distance of the tracer head along a route, or -1 while it is between runs. The scan head that
        /// drew the route has just reached the exit, so the cycle starts in the gap; phase (0–1) staggers
        /// routes within that gap so their tracers don't all leave at once.
        /// </summary>
        public static float TracerDistance(float timeSinceDrawn, float routeLength, float tileSize, float phase)
        {
            float gap = TracerGapTiles * tileSize;
            float cycle = routeLength + gap;
            float distance = Mathf.Repeat(routeLength + phase * gap + timeSinceDrawn * TracerTilesPerSecond * tileSize, cycle);
            return distance <= routeLength ? distance : -1f;
        }

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        // ─────────────────────────────────────────────────────
        //  Textures and sprites (white, tinted per route)
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// One chevron period of the conveyor lane. U runs along the lane (repeat), V across it:
        /// outline, rim, a 6 px channel with a 2 px ">" chevron pointing toward the exit, rim, outline.
        /// </summary>
        public static Texture2D GetLaneTexture()
        {
            if (laneTexture != null) return laneTexture;

            laneTexture = NewTexture(LanePeriodPx, LaneHeightPx, "PathPreview Lane");
            laneTexture.wrapModeU = TextureWrapMode.Repeat;
            laneTexture.wrapModeV = TextureWrapMode.Clamp;

            for (int y = 0; y < LaneHeightPx; y++)
            {
                for (int x = 0; x < LanePeriodPx; x++)
                {
                    Color color;
                    if (y == 0 || y == LaneHeightPx - 1) color = Outline;
                    else if (y == 1 || y == LaneHeightPx - 2) color = Rim;
                    else
                    {
                        int row = y - 2;                       // 0-5 across the channel
                        int bend = row < 3 ? row : 5 - row;    // 0,1,2,2,1,0: a ">" with its tip at x 5
                        bool chevron = x == 2 + bend || x == 3 + bend;
                        color = chevron ? Color.white : Channel;
                    }
                    laneTexture.SetPixel(x, y, color);
                }
            }

            laneTexture.Apply();
            return laneTexture;
        }

        /// <summary>Breach frame: a 3 px hazard-striped border around a dark plate. The frames march the stripes.</summary>
        public static Sprite[] GetSpawnFrames()
        {
            // Domain reload is off (Enter Play Mode options), so this array outlives the sprites in it:
            // runtime sprites are destroyed when play mode ends. Check the elements, not just the array.
            if (spawnFrames != null && System.Array.TrueForAll(spawnFrames, frame => frame != null)) return spawnFrames;

            const int size = SpawnFrameSizePx;
            const int border = 3;
            spawnFrames = new Sprite[SpawnFrameCount];
            for (int f = 0; f < SpawnFrameCount; f++)
            {
                Texture2D texture = NewTexture(size, size, "PathPreview Spawn Frame " + f);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        bool onBorder = x < border || x >= size - border || y < border || y >= size - border;
                        Color color = !onBorder
                            ? Plate
                            : ((x + y + SpawnFrameCount - f) / 3) % 2 == 0 ? Color.white : StripeDark;
                        texture.SetPixel(x, y, color);
                    }
                }
                texture.Apply();
                spawnFrames[f] = ToSprite(texture);
            }
            return spawnFrames;
        }

        public static Sprite GetWarningIcon()
        {
            if (warningIcon == null)
            {
                warningIcon = FromAscii("PathPreview Warning",
                    ".....#.....",
                    "....###....",
                    "....###....",
                    "...##.##...",
                    "...##.##...",
                    "..###.###..",
                    "..###.###..",
                    ".#########.",
                    ".####.####.",
                    "###########");
            }
            return warningIcon;
        }

        public static Sprite GetShieldIcon()
        {
            if (shieldIcon == null)
            {
                shieldIcon = FromAscii("PathPreview Shield",
                    ".#######.",
                    "#########",
                    "####.####",
                    "##.....##",
                    "####.####",
                    ".###.###.",
                    ".#######.",
                    "..#####..",
                    "...###...",
                    "....#....");
            }
            return shieldIcon;
        }

        /// <summary>Exit plate: a dark square with a 1 px rim.</summary>
        public static Sprite GetExitPlate()
        {
            if (exitPlate != null) return exitPlate;

            const int size = ExitPlateSizePx;
            Texture2D texture = NewTexture(size, size, "PathPreview Exit Plate");
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool rim = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                    texture.SetPixel(x, y, rim ? Rim : Plate);
                }
            }
            texture.Apply();
            exitPlate = ToSprite(texture);
            return exitPlate;
        }

        /// <summary>Four 2 px lock-on corner brackets, 7 px long.</summary>
        public static Sprite GetExitBrackets()
        {
            if (exitBrackets != null) return exitBrackets;

            const int size = ExitBracketSizePx;
            const int arm = 7;
            const int thickness = 2;
            Texture2D texture = NewTexture(size, size, "PathPreview Exit Brackets");
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int dx = Mathf.Min(x, size - 1 - x);
                    int dy = Mathf.Min(y, size - 1 - y);
                    bool bracket = (dx < thickness && dy < arm) || (dy < thickness && dx < arm);
                    texture.SetPixel(x, y, bracket ? Color.white : Color.clear);
                }
            }
            texture.Apply();
            exitBrackets = ToSprite(texture);
            return exitBrackets;
        }

        /// <summary>1 px square outline for radar pings.</summary>
        public static Sprite GetPingSquare()
        {
            if (pingSquare != null) return pingSquare;

            const int size = PingSizePx;
            Texture2D texture = NewTexture(size, size, "PathPreview Ping");
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                    texture.SetPixel(x, y, edge ? Color.white : Color.clear);
                }
            }
            texture.Apply();
            pingSquare = ToSprite(texture);
            return pingSquare;
        }

        /// <summary>Junction node for route turns: rimmed dark square with a 2 px centre light.</summary>
        public static Sprite GetJunction()
        {
            if (junction != null) return junction;

            const int size = JunctionSizePx;
            Texture2D texture = NewTexture(size, size, "PathPreview Junction");
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool outline = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                    bool rim = !outline && (x == 1 || y == 1 || x == size - 2 || y == size - 2);
                    bool light = (x == size / 2 - 1 || x == size / 2) && (y == size / 2 - 1 || y == size / 2);
                    Color color = outline ? Outline : rim ? Rim : light ? Color.white : Plate;
                    texture.SetPixel(x, y, color);
                }
            }
            texture.Apply();
            junction = ToSprite(texture);
            return junction;
        }

        /// <summary>A single white pixel for grid lines, tracers and plates.</summary>
        public static Sprite GetPixel()
        {
            if (pixel != null) return pixel;

            Texture2D texture = NewTexture(1, 1, "PathPreview Pixel");
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            pixel = ToSprite(texture);
            return pixel;
        }

        private static Sprite FromAscii(string name, params string[] rows)
        {
            int height = rows.Length;
            int width = rows[0].Length;
            Texture2D texture = NewTexture(width, height, name);
            for (int row = 0; row < height; row++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Rows are written top to bottom; textures start at the bottom
                    texture.SetPixel(x, height - 1 - row, rows[row][x] == '#' ? Color.white : Color.clear);
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

        /// <summary>1 pixel per unit, so a sprite is as many units across as it has pixels.</summary>
        private static Sprite ToSprite(Texture2D texture) =>
            Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 1f);
    }
}
