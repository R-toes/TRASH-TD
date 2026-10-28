using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;

namespace TrashTD.UI
{
    /// <summary>
    /// In-game enemy route preview, toggled by the HUD's PREVIEW PATH button (Esc closes it).
    /// Same look as the stage dossier's StageMapPreview (PathPreviewArt): the map dims slightly and a phosphor
    /// tile grid fades in, then each route draws in from its spawn behind a scan head as a conveyor lane whose
    /// chevrons march toward the exit, with junction nodes on turns, a tracer blip, a hazard-striped spawn
    /// frame and a shield exit marker. Routes, colours and lanes come from StageMapPreview, so both previews
    /// always agree. Drawn in world space at the stage art's pixel scale, above the units and weather.
    /// Runs on unscaled time, so it animates while paused.
    /// </summary>
    public sealed class GameplayPathPreviewOverlay : MonoBehaviour
    {
        private const int DimSortingOrder = 43;      // Above units, rain (24) and the foreground art
        private const int GridSortingOrder = 44;
        private const int LaneSortingOrder = 45;
        private const int JunctionSortingOrder = 46;
        private const int TracerSortingOrder = 47;
        private const int MarkerSortingOrder = 48;   // Ping 48, body 49, icon 50, label 51-52

        private const float DimAlpha = 0.16f;
        private const float GridAlpha = 0.11f;
        private const int LabelFontSize = 32;
        private const float LabelHeightPx = 7f;

        // URP's 2D sprite shader ignores texture offset/scale, so the lanes need their own shader to flow
        private const string LaneShaderPath = "Shaders/AnimatedPathDash"; // "TrashTD/PathDash"
        private static Shader laneShader;

        private sealed class RouteVisual
        {
            public List<Vector2> points;
            public List<float> cumulative;
            public float length;
            public float phase;
            public Color color;
            public LineRenderer lane;
            public Material laneMaterial;
            public readonly List<(SpriteRenderer renderer, float distance)> junctions = new List<(SpriteRenderer, float)>();
            public float junctionScale;
            public SpriteRenderer tracerHead;
            public SpriteRenderer[] tracerTrail;
        }

        private sealed class MarkerVisual
        {
            public Transform root;
            public bool isSpawn;
            public float appearAt;
            public float phase;
            public Color color;
            public SpriteRenderer frame;    // spawn: hazard frame; exit: plate
            public SpriteRenderer icon;
            public SpriteRenderer brackets; // exit only
            public SpriteRenderer ping;
            public SpriteRenderer labelPlate;
            public SpriteRenderer labelUnderline;
            public TextMesh label;
        }

        private GridManager gridManager;
        private StageData stageData;

        // Under-units mode (the tutorial's lit route): Ground layer just above the floor, no dim or grid
        private bool drawUnderUnits;
        private const int UnderUnitsOrderOffset = -40; // 43..52 -> 3..12, above the floor tiles (0..2)

        private GameObject visualRoot;
        private readonly List<RouteVisual> routes = new List<RouteVisual>();
        private readonly List<MarkerVisual> markers = new List<MarkerVisual>();
        private readonly List<SpriteRenderer> gridLines = new List<SpriteRenderer>();
        private readonly List<Material> createdMaterials = new List<Material>();
        private SpriteRenderer dim;

        private float cellSize = 1f;
        private float artPixel = 1f / 32f; // World units per stage-art pixel

        private bool isVisible;
        private float animTime;

        private static readonly List<Vector3> tempPoints = new List<Vector3>();

        public bool IsVisible => isVisible;

        public static GameplayPathPreviewOverlay Create(Transform parent, Canvas canvas, GridManager grid, StageData stage)
        {
            var go = new GameObject("GameplayPathPreviewOverlay");
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var overlay = go.AddComponent<GameplayPathPreviewOverlay>();
            overlay.Initialize(grid, stage);
            go.SetActive(false);
            return overlay;
        }

        /// <summary>
        /// A route overlay drawn on the Ground layer under every unit, without the dim and tile grid: the
        /// tutorial's floor shows the route lit permanently. Call SetVisible(true) to draw it in.
        /// </summary>
        public static GameplayPathPreviewOverlay CreateUnderUnits(GridManager grid, StageData stage)
        {
            var go = new GameObject("TutorialRouteOverlay");
            var overlay = go.AddComponent<GameplayPathPreviewOverlay>();
            overlay.drawUnderUnits = true;
            overlay.Initialize(grid, stage);
            go.SetActive(false);
            return overlay;
        }

        public void Initialize(GridManager grid, StageData stage)
        {
            gridManager = grid;
            stageData = stage;

            if (visualRoot == null)
            {
                visualRoot = new GameObject("VisualRoot");
                visualRoot.transform.SetParent(transform, false);
            }
        }

        public void SetStage(StageData stage, GridManager grid)
        {
            stageData = stage;
            if (grid != null) gridManager = grid;
            if (isVisible)
            {
                Rebuild();
            }
        }

        public void Toggle()
        {
            SetVisible(!isVisible);
        }

        public void SetVisible(bool visible)
        {
            if (isVisible == visible) return;
            isVisible = visible;
            gameObject.SetActive(isVisible);

            if (isVisible)
            {
                animTime = 0f;
                Rebuild();
            }
            else
            {
                ClearVisuals();
            }
        }

        private void ClearVisuals()
        {
            routes.Clear();
            markers.Clear();
            gridLines.Clear();
            dim = null;

            foreach (Material m in createdMaterials)
            {
                if (m != null) Destroy(m);
            }
            createdMaterials.Clear();

            if (visualRoot != null)
            {
                for (int i = visualRoot.transform.childCount - 1; i >= 0; i--)
                {
                    Destroy(visualRoot.transform.GetChild(i).gameObject);
                }
            }
        }

        private void OnDestroy()
        {
            ClearVisuals();
        }

        private Material CreateLaneMaterial()
        {
            // Without the lane shader the lanes still draw, just standing still
            if (laneShader == null) laneShader = Resources.Load<Shader>(LaneShaderPath);
            Shader shader = laneShader != null && laneShader.isSupported
                ? laneShader
                : Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader)
            {
                name = "PathPreviewLaneMat",
                mainTexture = PathPreviewArt.GetLaneTexture(),
                color = Color.white
            };
            createdMaterials.Add(mat);
            return mat;
        }

        public void Rebuild()
        {
            ClearVisuals();

            if (gridManager == null) gridManager = FindAnyObjectByType<GridManager>();

            if (stageData == null)
            {
                var bootstrapper = FindAnyObjectByType<TrashTD.Core.GameLoop.StageBootstrapper>();
                if (bootstrapper != null && bootstrapper.stageData != null) stageData = bootstrapper.stageData;
                if (stageData == null && TrashTD.Core.GameLoop.GameManager.Instance != null)
                {
                    stageData = TrashTD.Core.GameLoop.GameManager.Instance.CurrentStage;
                }
            }

            if (stageData == null || gridManager == null)
            {
                Debug.LogWarning($"[GameplayPathPreview] Missing dependency: stageData={stageData != null}, gridManager={gridManager != null}");
                return;
            }

            if (visualRoot == null)
            {
                visualRoot = new GameObject("VisualRoot");
                visualRoot.transform.SetParent(transform, false);
            }

            cellSize = gridManager.CellSize;
            float pixelsPerTile = stageData.visualTilePixelSize > 0 ? stageData.visualTilePixelSize : 32f;
            artPixel = cellSize / pixelsPerTile;

            if (!drawUnderUnits) BuildDimAndGrid();

            List<(int spawn, List<Vector2Int> cells)> cellRoutes = StageMapPreview.ComputeRoutes(stageData);
            int routeCount = cellRoutes.Count;
            if (routeCount == 0)
            {
                Debug.LogWarning("[GameplayPathPreview] No enemy routes found for stage: " + stageData.stageId);
            }

            float laneWidth = PathPreviewArt.LaneWidthPx(routeCount, pixelsPerTile) * artPixel;
            var spawnRoutes = new Dictionary<int, int>(); // spawn index -> route index
            for (int r = 0; r < routeCount; r++)
            {
                float laneOffset = PathPreviewArt.LaneOffsetTiles(r, routeCount) * cellSize;
                RouteVisual route = BuildRouteVisual(cellRoutes[r].cells, laneWidth, laneOffset, StageMapPreview.GetRouteColor(r), r / (float)routeCount);
                if (route == null) continue;
                spawnRoutes[cellRoutes[r].spawn] = routes.Count;
                routes.Add(route);
            }

            // Spawn markers take their route's colour (and number, when there are several); exits are green
            if (stageData.spawnPoints != null)
            {
                for (int s = 0; s < stageData.spawnPoints.Length; s++)
                {
                    bool routed = spawnRoutes.TryGetValue(s, out int routeIndex);
                    Color color = routed ? routes[routeIndex].color : PathPreviewArt.UnroutedSpawnColor;
                    string label = routed && routes.Count > 1 ? $"SPAWN {routeIndex + 1}" : "SPAWN";
                    CreateMarker(gridManager.GridToWorldPosition(stageData.spawnPoints[s]), color, label, true, s * 0.08f);
                }
            }

            if (stageData.exitPoints != null)
            {
                // Exits lock on as the routes finish drawing in
                float exitsAppear = PathPreviewArt.RouteDrawDuration * 0.85f;
                for (int e = 0; e < stageData.exitPoints.Length; e++)
                {
                    CreateMarker(gridManager.GridToWorldPosition(stageData.exitPoints[e]), PathPreviewArt.ExitColor, "EXIT", false, exitsAppear + e * 0.08f);
                }
            }

            // Apply initial animation state (t = 0)
            Update();
        }

        /// <summary>A light dim over the stage art and faint phosphor lines on the tile borders.</summary>
        private void BuildDimAndGrid()
        {
            Vector2 gridMin = (Vector2)gridManager.GridToWorldPosition(0, 0) - Vector2.one * (cellSize * 0.5f);
            Vector2 gridSize = new Vector2(stageData.gridWidth, stageData.gridHeight) * cellSize;

            // The art can be larger than the grid (e.g. Stage 1's backdrop row)
            Vector2 artMin = gridMin;
            Vector2 artMax = gridMin + gridSize;
            if (stageData.mapVisualSprite != null)
            {
                float pixelsPerTile = stageData.visualTilePixelSize > 0 ? stageData.visualTilePixelSize : 32f;
                Vector2 spriteMin = gridMin + stageData.visualTileOffset * cellSize;
                Vector2 spriteMax = spriteMin + stageData.mapVisualSprite.rect.size / pixelsPerTile * cellSize;
                artMin = Vector2.Min(artMin, spriteMin);
                artMax = Vector2.Max(artMax, spriteMax);
            }

            dim = CreatePixelRect("Dim", (artMin + artMax) * 0.5f, artMax - artMin, Color.clear, DimSortingOrder);

            for (int x = 0; x <= stageData.gridWidth; x++)
            {
                Vector2 center = gridMin + new Vector2(x * cellSize, gridSize.y * 0.5f);
                gridLines.Add(CreatePixelRect("GridColumn", center, new Vector2(artPixel, gridSize.y), Color.clear, GridSortingOrder));
            }
            for (int y = 0; y <= stageData.gridHeight; y++)
            {
                Vector2 center = gridMin + new Vector2(gridSize.x * 0.5f, y * cellSize);
                gridLines.Add(CreatePixelRect("GridRow", center, new Vector2(gridSize.x, artPixel), Color.clear, GridSortingOrder));
            }
        }

        private RouteVisual BuildRouteVisual(List<Vector2Int> cells, float laneWidth, float laneOffset, Color color, float phase)
        {
            if (cells == null || cells.Count < 2) return null;

            var centers = new List<Vector2>(cells.Count);
            foreach (Vector2Int cell in cells) centers.Add(gridManager.GridToWorldPosition(cell));
            centers = PathPreviewArt.KeepCorners(centers);
            if (centers.Count < 2) return null;

            var route = new RouteVisual
            {
                points = PathPreviewArt.OffsetIntoLane(centers, laneOffset),
                color = color,
                phase = phase
            };
            route.cumulative = PathPreviewArt.CumulativeLengths(route.points, out route.length);
            if (route.length <= 0.001f) return null;

            route.laneMaterial = CreateLaneMaterial();
            route.lane = CreateLineRenderer("RouteLane", laneWidth, color, route.laneMaterial, LaneSortingOrder);

            // Junction nodes on every turn, slightly wider than the lane
            Sprite junctionSprite = PathPreviewArt.GetJunction();
            route.junctionScale = (laneWidth + 2f * artPixel) / junctionSprite.rect.width;
            for (int i = 1; i < route.points.Count - 1; i++)
            {
                SpriteRenderer junction = CreateSprite("RouteJunction", visualRoot.transform, junctionSprite, color, JunctionSortingOrder);
                junction.transform.position = route.points[i];
                junction.transform.localScale = Vector3.one * route.junctionScale;
                route.junctions.Add((junction, route.cumulative[i]));
            }

            Sprite pixel = PathPreviewArt.GetPixel();
            route.tracerHead = CreateSprite("TracerHead", visualRoot.transform, pixel, Color.Lerp(color, Color.white, 0.55f), TracerSortingOrder + 1);
            route.tracerHead.transform.localScale = Vector3.one * (PathPreviewArt.TracerHeadPx * artPixel);
            route.tracerTrail = new SpriteRenderer[PathPreviewArt.TracerTrailLength];
            for (int i = 0; i < route.tracerTrail.Length; i++)
            {
                route.tracerTrail[i] = CreateSprite("TracerTrail", visualRoot.transform, pixel, color, TracerSortingOrder);
                route.tracerTrail[i].transform.localScale = Vector3.one * (Mathf.Max(2, PathPreviewArt.TracerHeadPx - 1 - i / 2) * artPixel);
            }

            return route;
        }

        private LineRenderer CreateLineRenderer(string objectName, float width, Color color, Material material, int sortingOrder)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(visualRoot.transform, false);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.positionCount = 0;
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            // Square ends and mitred corners keep the pixel lane crisp; junction nodes sit on the corners
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.textureMode = LineTextureMode.Tile;
            line.material = material;
            ApplySorting(line, sortingOrder);
            return line;
        }

        private void CreateMarker(Vector3 position, Color color, string label, bool isSpawn, float appearAt)
        {
            var markerGo = new GameObject(label + "Marker");
            markerGo.transform.SetParent(visualRoot.transform, false);
            markerGo.transform.position = new Vector3(position.x, position.y, 0f);

            var marker = new MarkerVisual
            {
                root = markerGo.transform,
                isSpawn = isSpawn,
                appearAt = appearAt,
                phase = Random.value,
                color = color
            };

            marker.ping = CreateMarkerPart(marker.root, "Ping", PathPreviewArt.GetPingSquare(), color, MarkerSortingOrder);
            float bodySizePx;
            if (isSpawn)
            {
                marker.frame = CreateMarkerPart(marker.root, "HazardFrame", PathPreviewArt.GetSpawnFrames()[0], color, MarkerSortingOrder + 1);
                marker.icon = CreateMarkerPart(marker.root, "Warning", PathPreviewArt.GetWarningIcon(), color, MarkerSortingOrder + 2);
                bodySizePx = PathPreviewArt.SpawnFrameSizePx;
            }
            else
            {
                marker.brackets = CreateMarkerPart(marker.root, "Brackets", PathPreviewArt.GetExitBrackets(), color, MarkerSortingOrder + 1);
                marker.frame = CreateMarkerPart(marker.root, "Plate", PathPreviewArt.GetExitPlate(), color, MarkerSortingOrder + 1);
                marker.icon = CreateMarkerPart(marker.root, "Shield", PathPreviewArt.GetShieldIcon(), color, MarkerSortingOrder + 2);
                bodySizePx = PathPreviewArt.ExitBracketSizePx;
            }

            CreateMarkerLabel(marker, label, bodySizePx);
            markers.Add(marker);
        }

        /// <summary>A pixel sprite at stage-art scale (the sprites are 1 unit per pixel).</summary>
        private SpriteRenderer CreateMarkerPart(Transform parent, string objectName, Sprite sprite, Color color, int sortingOrder)
        {
            SpriteRenderer renderer = CreateSprite(objectName, parent, sprite, color, sortingOrder);
            renderer.transform.localScale = Vector3.one * artPixel;
            return renderer;
        }

        /// <summary>A terminal tag above the marker (below it when the grid ends there).</summary>
        private void CreateMarkerLabel(MarkerVisual marker, string text, float bodySizePx)
        {
            Font font = UIFontHelper.GetPixelFont();
            float textHeight = LabelHeightPx * artPixel;
            float padding = 2f * artPixel;

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(marker.root, false);
            marker.label = textGo.AddComponent<TextMesh>();
            marker.label.font = font;
            marker.label.fontSize = LabelFontSize;
            // Roughly fontSize * characterSize / 10 world units per line; corrected below from the real mesh
            marker.label.characterSize = textHeight * 10f / LabelFontSize;
            marker.label.anchor = TextAnchor.MiddleCenter;
            marker.label.alignment = TextAlignment.Center;
            marker.label.text = text;
            marker.label.color = marker.color;

            var meshRenderer = textGo.GetComponent<MeshRenderer>();
            if (font != null) meshRenderer.sharedMaterial = font.material;
            ApplySorting(meshRenderer, MarkerSortingOrder + 4);

            // The pixel font is monospaced with square glyphs, so the width follows the character count
            Vector2 textSize = new Vector2(text.Length * textHeight, textHeight);
            Vector3 measured = meshRenderer.bounds.size;
            if (measured.y > 0.0001f)
            {
                float correction = textHeight / measured.y;
                marker.label.characterSize *= correction;
                textSize = new Vector2(measured.x * correction, textHeight);
            }

            var plateSize = new Vector2(textSize.x + padding * 2f, textSize.y + padding * 2f);
            float offset = (bodySizePx * artPixel + plateSize.y) * 0.5f + padding;
            float gridTop = gridManager.GridToWorldPosition(0, stageData.gridHeight - 1).y + cellSize * 0.5f;
            bool roomAbove = marker.root.position.y + offset + plateSize.y * 0.5f <= gridTop + cellSize;
            Vector3 center = new Vector3(0f, roomAbove ? offset : -offset, 0f);
            textGo.transform.localPosition = center + new Vector3(0f, artPixel * 0.5f, 0f);

            marker.labelPlate = CreateSprite("LabelPlate", marker.root, PathPreviewArt.GetPixel(), PathPreviewArt.LabelPlateColor, MarkerSortingOrder + 3);
            marker.labelPlate.transform.localPosition = center;
            marker.labelPlate.transform.localScale = new Vector3(plateSize.x, plateSize.y, 1f);

            // A 1 px route-coloured underline ties the tag to its marker
            marker.labelUnderline = CreateSprite("LabelUnderline", marker.root, PathPreviewArt.GetPixel(), marker.color, MarkerSortingOrder + 3);
            marker.labelUnderline.transform.localPosition = center + new Vector3(0f, (artPixel - plateSize.y) * 0.5f, 0f);
            marker.labelUnderline.transform.localScale = new Vector3(plateSize.x, artPixel, 1f);
        }

        private SpriteRenderer CreatePixelRect(string objectName, Vector2 center, Vector2 size, Color color, int sortingOrder)
        {
            SpriteRenderer renderer = CreateSprite(objectName, visualRoot.transform, PathPreviewArt.GetPixel(), color, sortingOrder);
            renderer.transform.position = center;
            renderer.transform.localScale = new Vector3(size.x, size.y, 1f);
            return renderer;
        }

        private SpriteRenderer CreateSprite(string objectName, Transform parent, Sprite sprite, Color color, int sortingOrder)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            ApplySorting(renderer, sortingOrder);
            return renderer;
        }

        private void ApplySorting(Renderer renderer, int sortingOrder)
        {
            if (drawUnderUnits)
            {
                GridSorting.PlaceOnGround(renderer);
                renderer.sortingOrder = sortingOrder + UnderUnitsOrderOffset;
            }
            else
            {
                renderer.sortingOrder = sortingOrder;
            }
        }

        private void Update()
        {
            if (!isVisible) return;

            animTime += Time.unscaledDeltaTime;
            float t = animTime;

            // 1. Dim and tile grid fade in
            float gridFade = Mathf.Clamp01(t / PathPreviewArt.GridFadeDuration);
            if (dim != null) dim.color = new Color(0f, 0f, 0f, DimAlpha * gridFade);
            Color gridColor = PathPreviewArt.WithAlpha(PathPreviewArt.PhosphorColor, GridAlpha * gridFade);
            foreach (SpriteRenderer line in gridLines) line.color = gridColor;

            // 2. Routes draw in from the spawn behind a scan head, then the chevrons keep marching toward
            // the exit at Grunt speed and a tracer runs the route now and then
            float routeT = Mathf.Clamp01(t / PathPreviewArt.RouteDrawDuration);
            float drawn = PathPreviewArt.EaseOutCubic(routeT);
            float period = PathPreviewArt.LanePeriodPx * artPixel;
            float flow = Mathf.Repeat(t * PathPreviewArt.FlowTilesPerSecond * cellSize / period, 1f);
            float sinceDrawn = t - PathPreviewArt.RouteDrawDuration;
            foreach (RouteVisual route in routes)
            {
                float visible = drawn * route.length;
                UpdateLanePositions(route, visible);

                // Tile mode gives u = distance along the line in world units: one chevron per period,
                // scrolled so the chevrons travel spawn -> exit
                route.laneMaterial.mainTextureScale = new Vector2(1f / period, 1f);
                route.laneMaterial.mainTextureOffset = new Vector2(-flow, 0f);

                foreach (var junction in route.junctions)
                {
                    float pop = Mathf.Clamp01((visible - junction.distance) / (0.25f * cellSize));
                    junction.renderer.enabled = pop > 0f;
                    junction.renderer.transform.localScale = Vector3.one * (route.junctionScale * PathPreviewArt.PopScale(PathPreviewArt.EaseOutCubic(pop)));
                }

                float head = routeT < 1f
                    ? (routeT > 0f ? visible : -1f)
                    : PathPreviewArt.TracerDistance(sinceDrawn, route.length, cellSize, route.phase);
                PlaceTracer(route, head);
            }

            // 3. Markers pop in, then ping; spawn stripes march and exit brackets breathe
            int stripeFrame = Mathf.FloorToInt(t * PathPreviewArt.StripeFramesPerSecond) % PathPreviewArt.SpawnFrameCount;
            foreach (MarkerVisual marker in markers)
            {
                float pop = PathPreviewArt.Pop(t, marker.appearAt);
                marker.root.gameObject.SetActive(pop > 0f);
                marker.root.localScale = Vector3.one * PathPreviewArt.PopScale(pop);

                float k = Mathf.Repeat(t / PathPreviewArt.PingPeriod + marker.phase, 1f);
                marker.ping.transform.localScale = Vector3.one * (artPixel * Mathf.Lerp(1f, 1.9f, PathPreviewArt.EaseOutCubic(k)));
                marker.ping.color = PathPreviewArt.WithAlpha(marker.color, (1f - k) * 0.8f * pop);

                marker.frame.color = PathPreviewArt.WithAlpha(marker.color, pop);
                marker.labelPlate.color = PathPreviewArt.WithAlpha(PathPreviewArt.LabelPlateColor, PathPreviewArt.LabelPlateColor.a * pop);
                marker.labelUnderline.color = PathPreviewArt.WithAlpha(marker.color, pop);
                marker.label.color = PathPreviewArt.WithAlpha(marker.color, pop);

                if (marker.isSpawn)
                {
                    marker.frame.sprite = PathPreviewArt.GetSpawnFrames()[stripeFrame];
                    float blink = Mathf.Repeat(t + marker.phase, 1f) < 0.65f ? 1f : 0.45f;
                    marker.icon.color = PathPreviewArt.WithAlpha(marker.color, blink * pop);
                }
                else
                {
                    float breathe = 1f + 0.1f * (0.5f + 0.5f * Mathf.Sin((t + marker.phase) * 3f));
                    marker.brackets.transform.localScale = Vector3.one * (artPixel * breathe);
                    marker.brackets.color = PathPreviewArt.WithAlpha(marker.color, pop);
                    marker.icon.color = PathPreviewArt.WithAlpha(marker.color, pop);
                }
            }
        }

        /// <summary>Tracer head at the given distance with its trail behind it; a negative distance hides it.</summary>
        private void PlaceTracer(RouteVisual route, float headDistance)
        {
            bool show = headDistance >= 0f;
            route.tracerHead.enabled = show;
            if (show)
            {
                route.tracerHead.transform.position = PathPreviewArt.PointAlong(route.points, route.cumulative, headDistance);
            }

            for (int i = 0; i < route.tracerTrail.Length; i++)
            {
                float distance = headDistance - (i + 1) * PathPreviewArt.TracerTrailSpacingPx * artPixel;
                SpriteRenderer trail = route.tracerTrail[i];
                trail.enabled = show && distance >= 0f;
                if (!trail.enabled) continue;

                trail.transform.position = PathPreviewArt.PointAlong(route.points, route.cumulative, distance);
                trail.color = PathPreviewArt.WithAlpha(route.color, 0.75f - i * 0.22f);
            }
        }

        /// <summary>Shows the route from its spawn up to the given distance.</summary>
        private static void UpdateLanePositions(RouteVisual route, float visibleLength)
        {
            LineRenderer lane = route.lane;
            if (visibleLength <= 0.005f)
            {
                lane.positionCount = 0;
                return;
            }

            tempPoints.Clear();
            tempPoints.Add(route.points[0]);
            for (int i = 1; i < route.points.Count; i++)
            {
                if (visibleLength >= route.cumulative[i])
                {
                    tempPoints.Add(route.points[i]);
                    continue;
                }

                tempPoints.Add(PathPreviewArt.PointAlong(route.points, route.cumulative, visibleLength));
                break;
            }

            if (lane.positionCount != tempPoints.Count) lane.positionCount = tempPoints.Count;
            lane.SetPositions(tempPoints.ToArray());
        }
    }
}
