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
    /// StageData.enemyPaths, otherwise A* from each spawn) in the shared PathPreviewArt style: conveyor lanes
    /// with chevrons marching spawn → exit, junction nodes on turns, hazard-striped spawn frames, a shield exit
    /// marker, square pings, a tracer blip and a faint phosphor tile grid. Each route has its own colour and
    /// lane, so routes that share tiles run side by side instead of on top of each other. Put it on a
    /// RectTransform with no Graphic and call Show(stage): it adds a CrtScreen (Monitor settings) there and
    /// builds the preview on that screen's offscreen canvas. Runs on unscaled time.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class StageMapPreview : MonoBehaviour
    {
        private static readonly Color ScreenColor = new Color(0.012f, 0.035f, 0.022f, 1f);

        /// <summary>Route colours in spawn order; exits keep the green PathPreviewArt.ExitColor.</summary>
        public static readonly Color[] RouteColors =
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
        private const float GridAlpha = 0.09f;
        // Matches StageBootstrapper.Stage3BackgroundAnimationFrameDuration
        private const float SheetFrameDuration = 0.12f;

        private static readonly Dictionary<Sprite, Sprite[]> SheetFrameCache = new Dictionary<Sprite, Sprite[]>();

        public static Color GetRouteColor(int routeIndex) => RouteColors[Mathf.Abs(routeIndex) % RouteColors.Length];

        private RectTransform display; // where the CRT picture is shown
        private CrtScreen monitor;
        private RectTransform root;    // the CRT monitor's offscreen content root
        private RectTransform content;
        private RectTransform bootCover;
        private RectTransform bootScanLine;
        private Image bootScanImage;
        private CanvasGroup gridGroup;
        private StageData stage;
        private Vector2 builtForSize;
        private float shownAt;
        private float tileScale = 1f; // UI units per tile
        private float artPixel = 1f;  // UI units per stage-art pixel

        private readonly List<Route> routes = new List<Route>();
        private readonly List<Marker> markers = new List<Marker>();
        private readonly List<SheetAnimation> sheets = new List<SheetAnimation>();

        /// <summary>Number of distinct enemy routes in the stage shown last.</summary>
        public int RouteCount => routes.Count;

        private sealed class Route
        {
            public List<Vector2> points;   // UI space, inside content
            public List<float> cumulative;
            public float length;
            public float phase;
            public Color color;
            public readonly List<Segment> segments = new List<Segment>();
            public readonly List<(Image image, float distance)> junctions = new List<(Image, float)>();
            public Image tracerHead;
            public Image[] tracerTrail;
        }

        private sealed class Segment
        {
            public RawImage lane;
            public float start;  // distance along the route where this segment's rect begins
            public float length;
        }

        private sealed class Marker
        {
            public RectTransform root;
            public CanvasGroup group;
            public bool isSpawn;
            public float appearAt;
            public float phase;
            public Color color;
            public Image frame;    // spawn: hazard frame; exit: plate
            public Image icon;
            public Image brackets; // exit only
            public Image ping;
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
            gridGroup = null;
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
            artPixel = tileScale / pixelsPerTile;

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

            BuildTileGrid(ToUi);

            // Routes: one colour and one lane each, so shared tiles show every route side by side
            List<(int spawn, List<Vector2Int> cells)> cellRoutes = ComputeRoutes(stage);
            int routeCount = cellRoutes.Count;
            float laneWidth = PathPreviewArt.LaneWidthPx(routeCount, pixelsPerTile) * artPixel;
            var spawnRoutes = new Dictionary<int, int>(); // spawn index -> route index
            for (int i = 0; i < routeCount; i++)
            {
                float laneOffset = PathPreviewArt.LaneOffsetTiles(i, routeCount) * tileScale;
                Route route = BuildRoute(cellRoutes[i].cells, ToUi, laneWidth, laneOffset, GetRouteColor(i), i / (float)routeCount);
                if (route == null) continue;
                spawnRoutes[cellRoutes[i].spawn] = routes.Count;
                routes.Add(route);
            }

            // Draw order across all routes: lanes, then junctions, then tracers, so no route's junction
            // covers a neighbouring lane's tracer
            foreach (Route route in routes)
            {
                foreach (var junction in route.junctions) junction.image.transform.SetAsLastSibling();
            }
            foreach (Route route in routes)
            {
                for (int i = route.tracerTrail.Length - 1; i >= 0; i--) route.tracerTrail[i].transform.SetAsLastSibling();
                route.tracerHead.transform.SetAsLastSibling();
            }

            // Spawn markers take their route's colour (and number, when there are several); exits are green
            float routesStart = BootDelay + BootDuration;
            if (stage.spawnPoints != null)
            {
                for (int s = 0; s < stage.spawnPoints.Length; s++)
                {
                    bool routed = spawnRoutes.TryGetValue(s, out int routeIndex);
                    Color color = routed ? routes[routeIndex].color : PathPreviewArt.UnroutedSpawnColor;
                    string label = routed && routes.Count > 1 ? $"SPAWN {routeIndex + 1}" : "SPAWN";
                    AddMarker(ToUi(CellCenter(stage.spawnPoints[s])), color, label, true, routesStart + s * 0.08f);
                }
            }
            if (stage.exitPoints != null)
            {
                // Exits lock on as the routes finish drawing in
                float exitsAppear = routesStart + PathPreviewArt.RouteDrawDuration * 0.85f;
                for (int e = 0; e < stage.exitPoints.Length; e++)
                {
                    AddMarker(ToUi(CellCenter(stage.exitPoints[e])), PathPreviewArt.ExitColor, "EXIT", false, exitsAppear + e * 0.08f);
                }
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
                    if (type == TileType.Blocked) continue; // Void, as on the tutorial's floor (TutorialStageVisuals)
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

        /// <summary>Faint phosphor lines on the tile borders, fading in with the routes.</summary>
        private void BuildTileGrid(System.Func<Vector2, Vector2> toUi)
        {
            var gridGo = new GameObject("TileGrid", typeof(RectTransform), typeof(CanvasGroup));
            gridGo.transform.SetParent(content, false);
            Stretch((RectTransform)gridGo.transform);
            gridGroup = gridGo.GetComponent<CanvasGroup>();
            gridGroup.alpha = 0f;
            gridGroup.blocksRaycasts = false;

            float line = Mathf.Max(1f, artPixel);
            Vector2 origin = toUi(Vector2.zero);
            Vector2 size = new Vector2(stage.gridWidth, stage.gridHeight) * tileScale;
            Color color = PathPreviewArt.WithAlpha(PathPreviewArt.PhosphorColor, GridAlpha);

            for (int x = 0; x <= stage.gridWidth; x++)
            {
                Image image = CreateImage(gridGo.transform, "GridColumn", null, color);
                PlaceBottomLeft(image.rectTransform, origin + new Vector2(x * tileScale - line * 0.5f, 0f), new Vector2(line, size.y));
            }
            for (int y = 0; y <= stage.gridHeight; y++)
            {
                Image image = CreateImage(gridGo.transform, "GridRow", null, color);
                PlaceBottomLeft(image.rectTransform, origin + new Vector2(0f, y * tileScale - line * 0.5f), new Vector2(size.x, line));
            }
        }

        private Route BuildRoute(List<Vector2Int> cells, System.Func<Vector2, Vector2> toUi, float laneWidth, float laneOffset, Color color, float phase)
        {
            if (cells == null || cells.Count < 2) return null;

            var centers = new List<Vector2>(cells.Count);
            foreach (Vector2Int cell in cells) centers.Add(toUi(CellCenter(cell)));
            centers = PathPreviewArt.KeepCorners(centers);
            if (centers.Count < 2) return null;

            var route = new Route
            {
                points = PathPreviewArt.OffsetIntoLane(centers, laneOffset),
                color = color,
                phase = phase
            };
            route.cumulative = PathPreviewArt.CumulativeLengths(route.points, out route.length);
            if (route.length < 1f) return null;

            for (int i = 1; i < route.points.Count; i++)
            {
                Vector2 a = route.points[i - 1];
                Vector2 b = route.points[i];
                Vector2 dir = (b - a).normalized;
                // Overlap half a lane width at both ends so corners join without gaps (junctions cover the overlap)
                Vector2 start = a - dir * laneWidth * 0.5f;
                float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

                var lane = new GameObject("RouteLane", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
                lane.transform.SetParent(content, false);
                lane.texture = PathPreviewArt.GetLaneTexture();
                lane.color = color;
                lane.raycastTarget = false;
                PlaceSegment(lane.rectTransform, start, angle, laneWidth);

                route.segments.Add(new Segment
                {
                    lane = lane,
                    start = route.cumulative[i - 1] - laneWidth * 0.5f,
                    length = Vector2.Distance(a, b) + laneWidth
                });
            }

            // Junction nodes on every turn, sized to cover the lane overlap
            float junctionSize = laneWidth + 2f * artPixel;
            for (int i = 1; i < route.points.Count - 1; i++)
            {
                Image junction = CreateImage(content, "RouteJunction", PathPreviewArt.GetJunction(), color);
                PlaceCentered(junction.rectTransform, route.points[i], Vector2.one * junctionSize);
                route.junctions.Add((junction, route.cumulative[i]));
            }

            route.tracerHead = CreateImage(content, "TracerHead", PathPreviewArt.GetPixel(), Color.Lerp(color, Color.white, 0.55f));
            PlaceCentered(route.tracerHead.rectTransform, route.points[0], Vector2.one * PathPreviewArt.TracerHeadPx * artPixel);
            route.tracerTrail = new Image[PathPreviewArt.TracerTrailLength];
            for (int i = 0; i < route.tracerTrail.Length; i++)
            {
                float size = Mathf.Max(2, PathPreviewArt.TracerHeadPx - 1 - i / 2) * artPixel;
                route.tracerTrail[i] = CreateImage(content, "TracerTrail", PathPreviewArt.GetPixel(), color);
                PlaceCentered(route.tracerTrail[i].rectTransform, route.points[0], Vector2.one * size);
            }

            return route;
        }

        private static void PlaceSegment(RectTransform rt, Vector2 start, float angle, float thickness)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = start;
            rt.sizeDelta = new Vector2(0f, thickness);
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private void AddMarker(Vector2 position, Color color, string label, bool isSpawn, float appearAt)
        {
            var markerGo = new GameObject(label + "Marker", typeof(RectTransform), typeof(CanvasGroup));
            markerGo.transform.SetParent(content, false);
            var markerRoot = (RectTransform)markerGo.transform;
            PlaceCentered(markerRoot, position, Vector2.zero);
            var group = markerGo.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;

            var marker = new Marker
            {
                root = markerRoot,
                group = group,
                isSpawn = isSpawn,
                appearAt = appearAt,
                phase = Random.value,
                color = color
            };

            marker.ping = CreateMarkerPart(markerRoot, "Ping", PathPreviewArt.GetPingSquare(), color, PathPreviewArt.PingSizePx);
            float bodySize;
            if (isSpawn)
            {
                marker.frame = CreateMarkerPart(markerRoot, "HazardFrame", PathPreviewArt.GetSpawnFrames()[0], color, PathPreviewArt.SpawnFrameSizePx);
                marker.icon = CreateMarkerPart(markerRoot, "Warning", PathPreviewArt.GetWarningIcon(), color, 0);
                bodySize = PathPreviewArt.SpawnFrameSizePx;
            }
            else
            {
                marker.brackets = CreateMarkerPart(markerRoot, "Brackets", PathPreviewArt.GetExitBrackets(), color, PathPreviewArt.ExitBracketSizePx);
                marker.frame = CreateMarkerPart(markerRoot, "Plate", PathPreviewArt.GetExitPlate(), color, PathPreviewArt.ExitPlateSizePx);
                marker.icon = CreateMarkerPart(markerRoot, "Shield", PathPreviewArt.GetShieldIcon(), color, 0);
                bodySize = PathPreviewArt.ExitBracketSizePx;
            }

            AddMarkerLabel(markerRoot, position, label, color, bodySize * artPixel);
            markers.Add(marker);
        }

        /// <summary>A pixel sprite drawn at stage-art scale. sizePx 0 = the sprite's own size.</summary>
        private Image CreateMarkerPart(Transform parent, string objectName, Sprite sprite, Color color, int sizePx)
        {
            Image image = CreateImage(parent, objectName, sprite, color);
            Vector2 size = sizePx > 0 ? Vector2.one * sizePx : sprite.rect.size;
            PlaceCentered(image.rectTransform, Vector2.zero, size * artPixel);
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            return image;
        }

        /// <summary>A terminal tag above the marker (below it when there's no room above).</summary>
        private void AddMarkerLabel(RectTransform markerRoot, Vector2 markerPosition, string label, Color color, float bodySize)
        {
            var plate = CreateImage(markerRoot, "LabelPlate", PathPreviewArt.GetPixel(), PathPreviewArt.LabelPlateColor);
            var text = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(plate.transform, false);
            text.font = UIFontHelper.GetPixelFont();
            text.fontSize = Mathf.Clamp(Mathf.RoundToInt(tileScale * 0.22f), 8, 13);
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = color;
            text.text = label;
            text.raycastTarget = false;
            Stretch(text.rectTransform);

            float padding = Mathf.Max(3f, artPixel * 2f);
            Vector2 plateSize = new Vector2(text.preferredWidth + padding * 2f, text.fontSize + padding * 1.5f);
            float offset = bodySize * 0.5f + plateSize.y * 0.5f + padding;
            bool roomAbove = markerPosition.y + offset + plateSize.y * 0.5f <= content.sizeDelta.y;
            PlaceCentered(plate.rectTransform, new Vector2(0f, roomAbove ? offset : -offset), plateSize);
            plate.rectTransform.anchorMin = plate.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);

            // A 1 px route-coloured underline ties the tag to its marker
            Image underline = CreateImage(plate.transform, "Underline", PathPreviewArt.GetPixel(), color);
            underline.rectTransform.anchorMin = Vector2.zero;
            underline.rectTransform.anchorMax = new Vector2(1f, 0f);
            underline.rectTransform.pivot = new Vector2(0.5f, 0f);
            underline.rectTransform.sizeDelta = new Vector2(0f, Mathf.Max(1f, artPixel));
            underline.rectTransform.anchoredPosition = Vector2.zero;
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

            bootScanImage = CreateImage(root, "BootScanLine", null, PathPreviewArt.PhosphorColor);
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
                Color c = PathPreviewArt.PhosphorColor;
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

            float routesStart = BootDelay + BootDuration;
            if (gridGroup != null) gridGroup.alpha = Mathf.Clamp01((t - routesStart) / PathPreviewArt.GridFadeDuration);

            // 2. Routes draw in from the spawn behind a scan head, then the chevrons keep marching toward
            // the exit and a tracer runs the route now and then. The lane pattern is laid along the route's
            // length, so it stays continuous around corners.
            float routeT = Mathf.Clamp01((t - routesStart) / PathPreviewArt.RouteDrawDuration);
            float drawn = PathPreviewArt.EaseOutCubic(routeT);
            float period = PathPreviewArt.LanePeriodPx * artPixel;
            float flow = t * PathPreviewArt.FlowTilesPerSecond * tileScale;
            float sinceDrawn = t - routesStart - PathPreviewArt.RouteDrawDuration;
            foreach (Route route in routes)
            {
                float visible = drawn * route.length;
                foreach (Segment segment in route.segments)
                {
                    float segmentVisible = Mathf.Clamp(visible - segment.start, 0f, segment.length);
                    RectTransform rt = segment.lane.rectTransform;
                    rt.sizeDelta = new Vector2(segmentVisible, rt.sizeDelta.y);
                    segment.lane.gameObject.SetActive(segmentVisible > 0.5f);
                    segment.lane.uvRect = new Rect((segment.start - flow) / period, 0f, segmentVisible / period, 1f);
                }

                foreach (var junction in route.junctions)
                {
                    float pop = Mathf.Clamp01((visible - junction.distance) / (0.25f * tileScale));
                    junction.image.gameObject.SetActive(pop > 0f);
                    junction.image.rectTransform.localScale = Vector3.one * PathPreviewArt.PopScale(PathPreviewArt.EaseOutCubic(pop));
                }

                float head = routeT < 1f
                    ? (routeT > 0f ? visible : -1f)
                    : PathPreviewArt.TracerDistance(sinceDrawn, route.length, tileScale, route.phase);
                PlaceTracer(route, head);
            }

            // 3. Markers pop in, then ping; spawn stripes march and exit brackets breathe
            int stripeFrame = Mathf.FloorToInt(t * PathPreviewArt.StripeFramesPerSecond) % PathPreviewArt.SpawnFrameCount;
            foreach (Marker marker in markers)
            {
                float pop = PathPreviewArt.Pop(t, marker.appearAt);
                marker.group.alpha = pop;
                marker.root.gameObject.SetActive(pop > 0f);
                marker.root.localScale = Vector3.one * PathPreviewArt.PopScale(pop);

                float k = Mathf.Repeat(t / PathPreviewArt.PingPeriod + marker.phase, 1f);
                float pingScale = Mathf.Lerp(1f, 1.9f, PathPreviewArt.EaseOutCubic(k));
                marker.ping.rectTransform.localScale = new Vector3(pingScale, pingScale, 1f);
                marker.ping.color = PathPreviewArt.WithAlpha(marker.color, (1f - k) * 0.8f);

                if (marker.isSpawn)
                {
                    marker.frame.sprite = PathPreviewArt.GetSpawnFrames()[stripeFrame];
                    marker.icon.color = PathPreviewArt.WithAlpha(marker.color, Mathf.Repeat(t + marker.phase, 1f) < 0.65f ? 1f : 0.45f);
                }
                else
                {
                    float breathe = 1f + 0.1f * (0.5f + 0.5f * Mathf.Sin((t + marker.phase) * 3f));
                    marker.brackets.rectTransform.localScale = new Vector3(breathe, breathe, 1f);
                }
            }
        }

        /// <summary>Tracer head at the given distance with its trail behind it; a negative distance hides it.</summary>
        private void PlaceTracer(Route route, float headDistance)
        {
            bool show = headDistance >= 0f;
            route.tracerHead.gameObject.SetActive(show);
            if (show)
            {
                route.tracerHead.rectTransform.anchoredPosition = PathPreviewArt.PointAlong(route.points, route.cumulative, headDistance);
            }

            for (int i = 0; i < route.tracerTrail.Length; i++)
            {
                float distance = headDistance - (i + 1) * PathPreviewArt.TracerTrailSpacingPx * artPixel;
                Image trail = route.tracerTrail[i];
                trail.gameObject.SetActive(show && distance >= 0f);
                if (!trail.gameObject.activeSelf) continue;

                trail.rectTransform.anchoredPosition = PathPreviewArt.PointAlong(route.points, route.cumulative, distance);
                trail.color = PathPreviewArt.WithAlpha(route.color, 0.75f - i * 0.22f);
            }
        }

        // ─────────────────────────────────────────────────────
        //  Routes (mirrors WaveManager.PrecomputePaths)
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// One ground route per spawn point, chosen the way WaveManager does: an explicit enemyPaths entry
        /// (waypoints, or A* to its exit), otherwise A* from the spawn to the best exit. Later entries for
        /// the same spawn replace earlier ones, as in WaveManager's path cache.
        /// </summary>
        public static List<(int spawn, List<Vector2Int> cells)> ComputeRoutes(StageData stage)
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
    }
}
