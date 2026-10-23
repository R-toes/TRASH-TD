using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TrashTD.Core.Grid;
using TrashTD.Core.Pathfinding;
using TrashTD.Data;

namespace TrashTD.UI
{
    /// <summary>
    /// Animated stage preview for the stage dossier. Lays out the stage art exactly as gameplay does
    /// (StageBootstrapper.SpawnVisualGridTiles: every layer placed in tile space from the grid's bottom-left,
    /// shifted by visualTileOffset), then draws the enemy routes WaveManager would use (explicit
    /// StageData.enemyPaths, otherwise A* from each spawn) as coloured dashed lines flowing from spawn to
    /// exit, and pulses the spawn and exit tiles. Each route has its own colour and lane, so routes that share
    /// tiles run side by side instead of on top of each other. Put it on a RectTransform with no Graphic and call
    /// Show(stage): it adds a CrtScreen (Monitor settings) there and builds the preview on that screen's
    /// offscreen canvas. Runs on unscaled time.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class StageMapPreview : MonoBehaviour
    {
        private static readonly Color ScreenColor = new Color(0.012f, 0.035f, 0.022f, 1f);
        private static readonly Color PhosphorColor = new Color(0.22f, 0.91f, 0.50f, 1f);
        private static readonly Color CasingColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color UnroutedSpawnColor = new Color(1f, 0.32f, 0.24f, 1f);
        private static readonly Color ExitColor = new Color(0.32f, 1f, 0.60f, 1f);

        /// <summary>Route colours in spawn order; exits keep the green ExitColor.</summary>
        private static readonly Color[] RouteColors =
        {
            new Color(1.00f, 0.38f, 0.26f, 1f), // red-orange
            new Color(0.30f, 0.82f, 1.00f, 1f), // cyan
            new Color(1.00f, 0.86f, 0.24f, 1f), // yellow
            new Color(0.98f, 0.42f, 0.92f, 1f), // magenta
            new Color(0.70f, 0.58f, 1.00f, 1f), // violet
            new Color(1.00f, 0.62f, 0.20f, 1f), // amber
        };

        private const float BootDelay = 0.10f;
        private const float BootDuration = 0.45f;
        private const float RouteDrawDuration = 0.9f;
        private const float DashPeriodTiles = 0.5f;          // one dash + gap
        private const float DashSpeedTilesPerSecond = 1.4f;  // dashes flow spawn -> exit
        private const float MaxLaneSpacingTiles = 0.22f;
        private const float LaneSpreadTiles = 0.8f;          // all lanes together stay within this width
        private const float MarkerSizeTiles = 0.9f;
        private const float PingPeriod = 1.4f;
        // Matches StageBootstrapper.Stage3BackgroundAnimationFrameDuration
        private const float SheetFrameDuration = 0.12f;

        private static Sprite ringSprite;
        private static Sprite dotSprite;
        private static Texture2D dashTexture;
        private static readonly Dictionary<Sprite, Sprite[]> SheetFrameCache = new Dictionary<Sprite, Sprite[]>();

        public static Color GetRouteColor(int routeIndex) => RouteColors[Mathf.Abs(routeIndex) % RouteColors.Length];

        private RectTransform display; // where the CRT picture is shown
        private CrtScreen monitor;
        private RectTransform root;    // the CRT monitor's offscreen content root
        private RectTransform content;
        private RectTransform bootCover;
        private RectTransform bootScanLine;
        private Image bootScanImage;
        private StageData stage;
        private Vector2 builtForSize;
        private float shownAt;
        private float tileScale = 1f; // UI units per tile

        private readonly List<Route> routes = new List<Route>();
        private readonly List<Marker> markers = new List<Marker>();
        private readonly List<SheetAnimation> sheets = new List<SheetAnimation>();

        /// <summary>Number of distinct enemy routes in the stage shown last.</summary>
        public int RouteCount => routes.Count;

        private sealed class Route
        {
            public readonly List<Vector2> points = new List<Vector2>(); // UI space, inside content
            public readonly List<float> cumulative = new List<float>();
            public readonly List<Segment> segments = new List<Segment>();
            public float length;
        }

        private sealed class Segment
        {
            public RectTransform casing;
            public RectTransform core;
            public RawImage dashes;
            public float start;  // distance along the route where this segment's rect begins
            public float length;
        }

        private sealed class Marker
        {
            public RectTransform ping;
            public Image pingImage;
            public Color color;
            public float phase;
        }

        private sealed class SheetAnimation
        {
            public Image image;
            public Sprite[] frames;
        }

        private void Awake()
        {
            EnsureRoot();
        }

        private void EnsureRoot()
        {
            if (root != null) return;

            // The preview is drawn on its own curved CRT monitor; this object shows the picture and the
            // preview UI is built on the monitor's offscreen canvas (which also clips it to the screen).
            display = (RectTransform)transform;
            monitor = GetComponent<CrtScreen>();
            if (monitor == null) monitor = CrtScreen.Create(display, GetMonitorSettings(), false);
            root = monitor.Content;
            GameGraphicsSettings.Changed += ApplyMonitorSettings;
        }

        private static CrtScreen.Settings GetMonitorSettings()
        {
            switch (GameGraphicsSettings.CrtStyle)
            {
                case CrtStyle.Classic:
                    return CrtScreen.ClassicMonitor;
                case CrtStyle.Off:
                    CrtScreen.Settings plain = CrtScreen.Clean;
                    plain.opaque = 0f;
                    return plain;
                default:
                    return CrtScreen.Monitor;
            }
        }

        private void ApplyMonitorSettings()
        {
            if (monitor != null) monitor.SetSettings(GetMonitorSettings());
        }

        private void OnDestroy()
        {
            GameGraphicsSettings.Changed -= ApplyMonitorSettings;
        }

        /// <summary>Builds the preview for a stage and restarts its boot / route animation.</summary>
        public void Show(StageData stageData)
        {
            EnsureRoot();
            stage = stageData;
            Build();
            shownAt = Time.unscaledTime;
            Animate(0f);
        }

        private void Update()
        {
            if (stage == null || root == null) return;

            // Rebuild if the frame was resized (first layout pass, resolution change)
            Vector2 size = display.rect.size;
            if ((size - builtForSize).sqrMagnitude > 1f)
            {
                Build();
            }

            Animate(Time.unscaledTime - shownAt);
        }

        // ─────────────────────────────────────────────────────
        //  Build
        // ─────────────────────────────────────────────────────

        private void Build()
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Destroy(root.GetChild(i).gameObject);
            }

            routes.Clear();
            markers.Clear();
            sheets.Clear();
            content = null;
            // Size from the display: the offscreen canvas follows it a frame later
            builtForSize = display.rect.size;
            if (stage == null || builtForSize.x < 1f || builtForSize.y < 1f) return;

            Image background = CreateImage(root, "ScreenBackground", null, ScreenColor);
            Stretch(background.rectTransform);

            float pixelsPerTile = stage.visualTilePixelSize > 0 ? stage.visualTilePixelSize : 32f;
            Vector2 mapOrigin = stage.visualTileOffset;
            Sprite mapSprite = stage.mapVisualSprite;

            // Art layers in gameplay draw order, in tile space (origin = grid bottom-left)
            var layers = new List<(Sprite sprite, Vector2 min, Vector2 size, Sprite[] frames)>();
            AddLayer(layers, stage.backgroundVisualSprite, true, mapSprite, mapOrigin, pixelsPerTile);
            AddLayer(layers, mapSprite, false, mapSprite, mapOrigin, pixelsPerTile);
            if (stage.upperBackgroundVisualSprites != null)
            {
                foreach (Sprite upper in stage.upperBackgroundVisualSprites)
                {
                    AddLayer(layers, upper, true, mapSprite, mapOrigin, pixelsPerTile);
                }
            }
            AddLayer(layers, stage.foregroundVisualSprite, false, mapSprite, mapOrigin, pixelsPerTile);

            // Frame the art, falling back to the bare grid
            Vector2 boundsMin = Vector2.zero;
            Vector2 boundsMax = new Vector2(Mathf.Max(1, stage.gridWidth), Mathf.Max(1, stage.gridHeight));
            if (layers.Count > 0)
            {
                boundsMin = layers[0].min;
                boundsMax = layers[0].min + layers[0].size;
                foreach (var layer in layers)
                {
                    boundsMin = Vector2.Min(boundsMin, layer.min);
                    boundsMax = Vector2.Max(boundsMax, layer.min + layer.size);
                }
            }

            Vector2 boundsSize = boundsMax - boundsMin;
            tileScale = Mathf.Min(builtForSize.x / boundsSize.x, builtForSize.y / boundsSize.y);

            var contentGo = new GameObject("PreviewContent", typeof(RectTransform));
            contentGo.transform.SetParent(root, false);
            content = contentGo.GetComponent<RectTransform>();
            content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f);
            content.pivot = new Vector2(0.5f, 0.5f);
            content.sizeDelta = boundsSize * tileScale;
            content.anchoredPosition = Vector2.zero;

            Vector2 ToUi(Vector2 tile) => (tile - boundsMin) * tileScale;

            if (layers.Count > 0)
            {
                foreach (var layer in layers)
                {
                    Image image = CreateImage(content, "Art_" + layer.sprite.name, layer.frames != null ? layer.frames[0] : layer.sprite, Color.white);
                    PlaceBottomLeft(image.rectTransform, ToUi(layer.min), layer.size * tileScale);
                    if (layer.frames != null) sheets.Add(new SheetAnimation { image = image, frames = layer.frames });
                }
            }
            else
            {
                BuildFallbackTiles(ToUi);
            }

            // Routes: one colour and one lane each, so shared tiles show every route side by side
            List<(int spawn, List<Vector2Int> cells)> cellRoutes = ComputeRoutes(stage);
            int routeCount = cellRoutes.Count;
            float laneSpacing = Mathf.Min(MaxLaneSpacingTiles, LaneSpreadTiles / Mathf.Max(1, routeCount)) * tileScale;
            float thickness = Mathf.Max(3f, laneSpacing * 0.6f);
            var spawnColors = new Dictionary<int, Color>();
            for (int i = 0; i < routeCount; i++)
            {
                float laneOffset = (i - (routeCount - 1) * 0.5f) * laneSpacing;
                Color color = GetRouteColor(i);
                Route route = BuildRoute(cellRoutes[i].cells, ToUi, thickness, laneOffset, color);
                if (route == null) continue;
                routes.Add(route);
                spawnColors[cellRoutes[i].spawn] = color;
            }

            // Draw order across all routes: casings, then faint cores, then dashes, so no route's
            // dark casing covers a neighbouring lane's dashes
            foreach (Route route in routes)
            {
                foreach (Segment segment in route.segments) segment.core.SetAsLastSibling();
            }
            foreach (Route route in routes)
            {
                foreach (Segment segment in route.segments) segment.dashes.transform.SetAsLastSibling();
            }

            // Spawn markers take their route's colour; exits are green
            if (stage.spawnPoints != null)
            {
                for (int s = 0; s < stage.spawnPoints.Length; s++)
                {
                    Color color = spawnColors.TryGetValue(s, out Color routeColor) ? routeColor : UnroutedSpawnColor;
                    AddMarker(ToUi(CellCenter(stage.spawnPoints[s])), color, "SPAWN");
                }
            }
            if (stage.exitPoints != null)
            {
                foreach (Vector2Int exit in stage.exitPoints) AddMarker(ToUi(CellCenter(exit)), ExitColor, "EXIT");
            }

            BuildBootOverlay();
        }

        private static void AddLayer(
            List<(Sprite, Vector2, Vector2, Sprite[])> layers,
            Sprite sprite,
            bool alignToSourceRect,
            Sprite mapSprite,
            Vector2 mapOrigin,
            float pixelsPerTile)
        {
            if (sprite == null) return;
            if (sprite.texture != null) sprite.texture.filterMode = FilterMode.Point;

            Rect rect = sprite.rect;
            Sprite[] frames = GetSheetFrames(sprite, mapSprite);
            Vector2 pixelSize = frames != null ? frames[0].rect.size : rect.size;
            Vector2 min = mapOrigin + (alignToSourceRect ? rect.position / pixelsPerTile : Vector2.zero);
            layers.Add((sprite, min, pixelSize / pixelsPerTile, frames));
        }

        /// <summary>
        /// A layer several map-widths wide (Stage 3's background) is a horizontal animation sheet:
        /// split it into frames the size of the map art, as StageBootstrapper.CreateSpriteSheetAnimation does.
        /// </summary>
        internal static Sprite[] GetSheetFrames(Sprite sprite, Sprite mapSprite)
        {
            if (mapSprite == null || sprite == mapSprite) return null;

            float frameWidth = mapSprite.rect.width;
            Rect rect = sprite.rect;
            if (frameWidth < 1f || rect.width < frameWidth * 2f || !Mathf.Approximately(rect.height, mapSprite.rect.height)) return null;
            if (!Mathf.Approximately(rect.width % frameWidth, 0f)) return null;

            if (SheetFrameCache.TryGetValue(sprite, out Sprite[] cached) && cached != null && cached.Length > 0 && cached[0] != null)
                return cached;

            int count = Mathf.RoundToInt(rect.width / frameWidth);
            var frames = new Sprite[count];
            for (int i = 0; i < count; i++)
            {
                frames[i] = Sprite.Create(
                    sprite.texture,
                    new Rect(rect.x + i * frameWidth, rect.y, frameWidth, rect.height),
                    new Vector2(0.5f, 0.5f),
                    sprite.pixelsPerUnit);
            }

            SheetFrameCache[sprite] = frames;
            return frames;
        }

        private void BuildFallbackTiles(System.Func<Vector2, Vector2> toUi)
        {
            for (int y = 0; y < stage.gridHeight; y++)
            {
                for (int x = 0; x < stage.gridWidth; x++)
                {
                    TileType type = stage.GetTile(x, y);
                    Color color = type switch
                    {
                        TileType.LowGround => new Color(0.10f, 0.24f, 0.15f, 1f),
                        TileType.HighGround => new Color(0.06f, 0.15f, 0.10f, 1f),
                        _ => new Color(0.03f, 0.07f, 0.05f, 1f)
                    };

                    Image tile = CreateImage(content, $"Tile_{x}_{y}", null, color);
                    PlaceBottomLeft(tile.rectTransform, toUi(new Vector2(x, y)) + Vector2.one, Vector2.one * (tileScale - 2f));
                }
            }
        }

        private Route BuildRoute(List<Vector2Int> cells, System.Func<Vector2, Vector2> toUi, float thickness, float laneOffset, Color color)
        {
            if (cells == null || cells.Count < 2) return null;

            var route = new Route();

            // Corners only: drop points that continue in a straight line (and repeated cells)
            var centers = new List<Vector2> { toUi(CellCenter(cells[0])) };
            for (int i = 1; i < cells.Count; i++)
            {
                if (cells[i] == cells[i - 1]) continue;
                Vector2 point = toUi(CellCenter(cells[i]));
                if (centers.Count >= 2)
                {
                    Vector2 inDir = (centers[centers.Count - 1] - centers[centers.Count - 2]).normalized;
                    Vector2 outDir = (point - centers[centers.Count - 1]).normalized;
                    if (Vector2.Dot(inDir, outDir) > 0.999f) centers.RemoveAt(centers.Count - 1);
                }
                centers.Add(point);
            }
            if (centers.Count < 2) return null;

            // Shift the whole polyline sideways into this route's lane, mitring the corners
            for (int i = 0; i < centers.Count; i++)
            {
                Vector2 inNormal = i > 0 ? LeftNormal(centers[i] - centers[i - 1]) : Vector2.zero;
                Vector2 outNormal = i < centers.Count - 1 ? LeftNormal(centers[i + 1] - centers[i]) : Vector2.zero;
                Vector2 miter = inNormal + outNormal;
                if (miter.sqrMagnitude < 0.0001f) miter = i > 0 ? inNormal : outNormal;
                miter.Normalize();
                Vector2 reference = i > 0 ? inNormal : outNormal;
                float stretch = 1f / Mathf.Max(0.35f, Vector2.Dot(miter, reference));
                route.points.Add(centers[i] + miter * laneOffset * stretch);
            }

            route.cumulative.Add(0f);
            for (int i = 1; i < route.points.Count; i++)
            {
                route.length += Vector2.Distance(route.points[i - 1], route.points[i]);
                route.cumulative.Add(route.length);
            }
            if (route.length < 1f) return null;

            for (int i = 1; i < route.points.Count; i++)
            {
                Vector2 a = route.points[i - 1];
                Vector2 b = route.points[i];
                Vector2 dir = (b - a).normalized;
                // Overlap half a thickness at both ends so corners join without gaps
                Vector2 start = a - dir * thickness * 0.5f;
                float length = Vector2.Distance(a, b) + thickness;
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

                var dashes = new GameObject("RouteDashes", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                dashes.transform.SetParent(content, false);
                dashes.texture = GetDashTexture();
                dashes.color = color;
                dashes.raycastTarget = false;
                PlaceSegment(dashes.rectTransform, start, angle, thickness);

                var segment = new Segment
                {
                    casing = CreateSegment("RouteCasing", start, angle, thickness * 1.6f, CasingColor),
                    core = CreateSegment("RouteCore", start, angle, thickness, new Color(color.r, color.g, color.b, 0.35f)),
                    dashes = dashes,
                    start = route.cumulative[i - 1] - thickness * 0.5f,
                    length = length
                };
                route.segments.Add(segment);
            }

            return route;
        }

        private static Vector2 LeftNormal(Vector2 direction)
        {
            Vector2 d = direction.normalized;
            return new Vector2(-d.y, d.x);
        }

        private RectTransform CreateSegment(string objectName, Vector2 start, float angle, float thickness, Color color)
        {
            Image image = CreateImage(content, objectName, null, color);
            PlaceSegment(image.rectTransform, start, angle, thickness);
            return image.rectTransform;
        }

        private static void PlaceSegment(RectTransform rt, Vector2 start, float angle, float thickness)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = start;
            rt.sizeDelta = new Vector2(0f, thickness);
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private void AddMarker(Vector2 position, Color color, string label)
        {
            float size = MarkerSizeTiles * tileScale;

            Image ring = CreateImage(content, label + "Marker", GetRingSprite(), color);
            PlaceCentered(ring.rectTransform, position, Vector2.one * size);

            Image ping = CreateImage(content, label + "Ping", GetRingSprite(), color);
            PlaceCentered(ping.rectTransform, position, Vector2.one * size);

            Image core = CreateImage(content, label + "Core", GetDotSprite(), color);
            PlaceCentered(core.rectTransform, position, Vector2.one * size * 0.28f);

            var text = new GameObject(label + "Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(content, false);
            text.font = UIFontHelper.GetPixelFont();
            text.fontSize = Mathf.Clamp(Mathf.RoundToInt(tileScale * 0.26f), 10, 15);
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.LowerCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.color = color;
            text.text = label;
            text.raycastTarget = false;
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
            shadow.effectDistance = new Vector2(1f, -1f);
            PlaceCentered(text.rectTransform, position + new Vector2(0f, size * 0.5f + 10f), new Vector2(120f, 20f));

            markers.Add(new Marker { ping = ping.rectTransform, pingImage = ping, color = color, phase = Random.value });
        }

        private void BuildBootOverlay()
        {
            Image cover = CreateImage(root, "BootCover", null, ScreenColor);
            bootCover = cover.rectTransform;
            bootCover.pivot = new Vector2(0.5f, 0f);
            bootCover.anchorMin = Vector2.zero;
            bootCover.anchorMax = Vector2.one;
            bootCover.offsetMin = Vector2.zero;
            bootCover.offsetMax = Vector2.zero;

            bootScanImage = CreateImage(root, "BootScanLine", null, PhosphorColor);
            bootScanLine = bootScanImage.rectTransform;
            bootScanLine.anchorMin = new Vector2(0f, 1f);
            bootScanLine.anchorMax = new Vector2(1f, 1f);
            bootScanLine.pivot = new Vector2(0.5f, 0.5f);
            bootScanLine.sizeDelta = new Vector2(0f, 3f);
            bootScanLine.anchoredPosition = Vector2.zero;
        }

        // ─────────────────────────────────────────────────────
        //  Animation
        // ─────────────────────────────────────────────────────

        private void Animate(float t)
        {
            if (content == null) return;

            // 1. Boot: a phosphor line sweeps down, uncovering the art
            float boot = Mathf.Clamp01((t - BootDelay) / BootDuration);
            if (bootCover != null)
            {
                bootCover.anchorMax = new Vector2(1f, 1f - boot);
                bootCover.gameObject.SetActive(boot < 1f);
            }
            if (bootScanLine != null)
            {
                bootScanLine.anchorMin = new Vector2(0f, 1f - boot);
                bootScanLine.anchorMax = new Vector2(1f, 1f - boot);
                Color c = PhosphorColor;
                c.a = boot < 1f ? 1f : Mathf.Clamp01(1f - (t - BootDelay - BootDuration) * 4f);
                bootScanImage.color = c;
            }

            // Animated background sheets (Stage 3)
            if (sheets.Count > 0)
            {
                foreach (SheetAnimation sheet in sheets)
                {
                    int frame = Mathf.FloorToInt(t / SheetFrameDuration) % sheet.frames.Length;
                    sheet.image.sprite = sheet.frames[frame];
                }
            }

            // 2. Routes draw in from the spawn, then their dashes keep flowing toward the exit.
            // The dash pattern is laid along the route's length, so it stays continuous around corners.
            float routeT = Mathf.Clamp01((t - BootDelay - BootDuration) / RouteDrawDuration);
            float drawn = EaseOutCubic(routeT);
            float dashPeriod = DashPeriodTiles * tileScale;
            float flow = t * DashSpeedTilesPerSecond * tileScale;
            foreach (Route route in routes)
            {
                float visible = drawn * route.length;
                foreach (Segment segment in route.segments)
                {
                    float segmentVisible = Mathf.Clamp(visible - segment.start, 0f, segment.length);
                    SetLength(segment.casing, segmentVisible);
                    SetLength(segment.core, segmentVisible);
                    SetLength(segment.dashes.rectTransform, segmentVisible);
                    segment.dashes.uvRect = new Rect((segment.start - flow) / dashPeriod, 0f, segmentVisible / dashPeriod, 1f);
                }
            }

            // 3. Spawn / exit pings
            float markerAlpha = Mathf.Clamp01((t - BootDelay - BootDuration) * 3f);
            foreach (Marker marker in markers)
            {
                float k = Mathf.Repeat(t / PingPeriod + marker.phase, 1f);
                float scale = Mathf.Lerp(1f, 2.1f, EaseOutCubic(k));
                marker.ping.localScale = new Vector3(scale, scale, 1f);
                Color c = marker.color;
                c.a = (1f - k) * 0.8f * markerAlpha;
                marker.pingImage.color = c;
            }
        }

        private static void SetLength(RectTransform rt, float length)
        {
            Vector2 size = rt.sizeDelta;
            size.x = length;
            rt.sizeDelta = size;
            rt.gameObject.SetActive(length > 0.5f);
        }

        // ─────────────────────────────────────────────────────
        //  Routes (mirrors WaveManager.PrecomputePaths)
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// One ground route per spawn point, chosen the way WaveManager does: an explicit enemyPaths entry
        /// (waypoints, or A* to its exit), otherwise A* from the spawn to the best exit. Later entries for
        /// the same spawn replace earlier ones, as in WaveManager's path cache.
        /// </summary>
        private static List<(int spawn, List<Vector2Int> cells)> ComputeRoutes(StageData stage)
        {
            var bySpawn = new SortedDictionary<int, List<Vector2Int>>();
            var result = new List<(int spawn, List<Vector2Int> cells)>();
            if (stage.spawnPoints == null || stage.spawnPoints.Length == 0) return result;

            GameObject gridObject = null;
            GridManager grid = null;
            AStarPathfinder pathfinder = null;

            AStarPathfinder GetPathfinder()
            {
                if (pathfinder != null) return pathfinder;
                gridObject = new GameObject("StagePreviewGrid") { hideFlags = HideFlags.HideAndDontSave };
                grid = gridObject.AddComponent<GridManager>();
                grid.InitializeFromStageData(stage);
                pathfinder = new AStarPathfinder();
                pathfinder.Initialize(grid);
                return pathfinder;
            }

            List<Vector2Int> ToCells(List<Vector3> worldPath)
            {
                if (worldPath == null || worldPath.Count == 0) return null;
                var cells = new List<Vector2Int>(worldPath.Count);
                foreach (Vector3 point in worldPath) cells.Add(grid.WorldToGridPosition(point));
                return cells;
            }

            try
            {
                if (stage.enemyPaths != null)
                {
                    foreach (PathData path in stage.enemyPaths)
                    {
                        if (path == null) continue;
                        int s = path.spawnPointIndex;
                        if (s < 0 || s >= stage.spawnPoints.Length) continue;

                        if (path.waypoints != null && path.waypoints.Length > 0)
                        {
                            bySpawn[s] = new List<Vector2Int>(path.waypoints);
                            continue;
                        }

                        int e = path.exitPointIndex;
                        if (stage.exitPoints != null && e >= 0 && e < stage.exitPoints.Length)
                        {
                            List<Vector2Int> cells = ToCells(GetPathfinder().FindPath(stage.spawnPoints[s], stage.exitPoints[e], EnemyMovementType.Ground));
                            if (cells != null) bySpawn[s] = cells;
                        }
                    }
                }

                if (stage.exitPoints != null && stage.exitPoints.Length > 0)
                {
                    for (int s = 0; s < stage.spawnPoints.Length; s++)
                    {
                        if (bySpawn.ContainsKey(s)) continue;
                        List<Vector2Int> cells = ToCells(GetPathfinder().FindBestPath(stage.spawnPoints[s], stage.exitPoints, EnemyMovementType.Ground));
                        if (cells != null) bySpawn[s] = cells;
                    }
                }
            }
            finally
            {
                if (gridObject != null) Destroy(gridObject);
            }

            foreach (KeyValuePair<int, List<Vector2Int>> entry in bySpawn) result.Add((entry.Key, entry.Value));
            return result;
        }

        // ─────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────

        private static Vector2 CellCenter(Vector2Int cell) => new Vector2(cell.x + 0.5f, cell.y + 0.5f);

        private static Image CreateImage(Transform parent, string objectName, Sprite sprite, Color color)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void PlaceBottomLeft(RectTransform rt, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static void PlaceCentered(RectTransform rt, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static float EaseOutCubic(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }

        private static Sprite GetRingSprite()
        {
            if (ringSprite == null) ringSprite = CreateCircleSprite(64, 0.80f, 1f);
            return ringSprite;
        }

        private static Sprite GetDotSprite()
        {
            if (dotSprite == null) dotSprite = CreateCircleSprite(32, 0f, 1f);
            return dotSprite;
        }

        /// <summary>One dash period along U (60% dash, 40% gap), repeated by the RawImage's uvRect.</summary>
        private static Texture2D GetDashTexture()
        {
            if (dashTexture != null) return dashTexture;

            const int width = 10;
            dashTexture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };
            for (int x = 0; x < width; x++)
            {
                dashTexture.SetPixel(x, 0, x < 6 ? Color.white : Color.clear);
            }
            dashTexture.Apply();
            return dashTexture;
        }

        /// <summary>White circle (innerRatio 0) or ring (inner radius as a fraction of the outer one).</summary>
        private static Sprite CreateCircleSprite(int size, float innerRatio, float outerRatio)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            float outer = center * outerRatio;
            float inner = center * innerRatio;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float a = Mathf.Clamp01(outer - d + 0.5f) * (innerRatio > 0f ? Mathf.Clamp01(d - inner + 0.5f) : 1f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
