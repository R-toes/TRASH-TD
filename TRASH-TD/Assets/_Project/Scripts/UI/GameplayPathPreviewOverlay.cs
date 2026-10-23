using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;

namespace TrashTD.UI
{
    /// <summary>
    /// In-game tactical enemy path preview overlay. Draws the animated enemy route lines,
    /// lane offsets, and pulsing spawn/exit markers matching the stage select preview.
    /// Includes the full sweep draw-in animation from spawn to exit, flowing dashes,
    /// and concentric pulsing spawn/exit rings.
    /// Rendered directly in world-space so it aligns perfectly with the active grid tiles.
    /// Can be toggled on and off at any time during gameplay.
    /// </summary>
    public sealed class GameplayPathPreviewOverlay : MonoBehaviour
    {
        private static readonly Color CasingColor = new Color(0f, 0f, 0f, 0.65f);
        private static readonly Color UnroutedSpawnColor = new Color(1f, 0.32f, 0.24f, 1f);
        private static readonly Color ExitColor = new Color(0.32f, 1f, 0.60f, 1f);

        private const float RouteDrawDuration = 0.9f;
        private const float DashSpeed = 1.4f;
        private const float MaxLaneSpacingTiles = 0.22f;
        private const float LaneSpreadTiles = 0.8f;
        private const float PingPeriod = 1.4f;

        private sealed class RouteVisual
        {
            public List<Vector3> fullPoints;
            public List<float> cumulativeLengths;
            public float totalLength;
            public LineRenderer casingLine;
            public LineRenderer coreLine;
            public LineRenderer dashLine;
            public Material dashMat;
            public float dashPeriod;
        }

        private sealed class MarkerVisual
        {
            public Transform ping;
            public SpriteRenderer baseRenderer;
            public SpriteRenderer pingRenderer;
            public SpriteRenderer dotRenderer;
            public TextMesh labelTextMesh;
            public Color color;
            public float baseScale;
            public float phase;
        }

        private GridManager gridManager;
        private StageData stageData;

        private GameObject visualRoot;
        private readonly List<RouteVisual> routes = new List<RouteVisual>();
        private readonly List<MarkerVisual> markers = new List<MarkerVisual>();
        private readonly List<Material> createdMaterials = new List<Material>();

        private Material lineBaseMaterial;

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
            if (lineBaseMaterial != null) Destroy(lineBaseMaterial);
        }

        private Material GetLineMaterial()
        {
            if (lineBaseMaterial != null) return lineBaseMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
            lineBaseMaterial = new Material(shader) { name = "PathPreviewLineMat" };
            return lineBaseMaterial;
        }

        private Material CreateDashMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader)
            {
                name = "PathPreviewDashMat",
                mainTexture = StageMapPreview.GetDashTexture(),
                color = color
            };
            createdMaterials.Add(mat);
            return mat;
        }

        public void Rebuild()
        {
            ClearVisuals();

            if (gridManager == null) gridManager = FindFirstObjectByType<GridManager>();

            if (stageData == null)
            {
                var bootstrapper = FindFirstObjectByType<TrashTD.Core.GameLoop.StageBootstrapper>();
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

            float cellSize = gridManager.CellSize;
            List<(int spawn, List<Vector2Int> cells)> cellRoutes = StageMapPreview.ComputeRoutes(stageData);
            int routeCount = cellRoutes.Count;

            if (routeCount == 0)
            {
                Debug.LogWarning("[GameplayPathPreview] No enemy routes found for stage: " + stageData.stageId);
                return;
            }

            float laneSpacing = Mathf.Min(MaxLaneSpacingTiles, LaneSpreadTiles / Mathf.Max(1, routeCount)) * cellSize;
            float thickness = Mathf.Max(0.09f * cellSize, laneSpacing * 0.7f);
            var spawnColors = new Dictionary<int, Color>();

            for (int r = 0; r < routeCount; r++)
            {
                float laneOffset = (r - (routeCount - 1) * 0.5f) * laneSpacing;
                Color color = StageMapPreview.GetRouteColor(r);
                BuildRouteVisual(cellRoutes[r].cells, thickness, laneOffset, color, cellSize);
                spawnColors[cellRoutes[r].spawn] = color;
            }

            // Spawn & Exit Markers
            if (stageData.spawnPoints != null)
            {
                for (int s = 0; s < stageData.spawnPoints.Length; s++)
                {
                    Color color = spawnColors.TryGetValue(s, out Color routeColor) ? routeColor : UnroutedSpawnColor;
                    Vector3 worldPos = gridManager.GridToWorldPosition(stageData.spawnPoints[s]);
                    CreateMarker(worldPos, color, "SPAWN", cellSize, s * 0.23f);
                }
            }

            if (stageData.exitPoints != null)
            {
                for (int e = 0; e < stageData.exitPoints.Length; e++)
                {
                    Vector3 worldPos = gridManager.GridToWorldPosition(stageData.exitPoints[e]);
                    CreateMarker(worldPos, ExitColor, "EXIT", cellSize, e * 0.31f);
                }
            }

            // Apply initial animation state (t = 0)
            Update();

            Debug.Log($"[GameplayPathPreview] Rebuilt {routeCount} routes in world-space for stage {stageData.stageId}.");
        }

        private void BuildRouteVisual(List<Vector2Int> cells, float thickness, float laneOffset, Color color, float cellSize)
        {
            if (cells == null || cells.Count < 2) return;

            // 1. Convert to world centers and drop redundant collinear mid-points
            var centers = new List<Vector2> { (Vector2)gridManager.GridToWorldPosition(cells[0]) };
            for (int i = 1; i < cells.Count; i++)
            {
                if (cells[i] == cells[i - 1]) continue;
                Vector2 pt = (Vector2)gridManager.GridToWorldPosition(cells[i]);
                if (centers.Count >= 2)
                {
                    Vector2 inDir = (centers[centers.Count - 1] - centers[centers.Count - 2]).normalized;
                    Vector2 outDir = (pt - centers[centers.Count - 1]).normalized;
                    if (Vector2.Dot(inDir, outDir) > 0.999f) centers.RemoveAt(centers.Count - 1);
                }
                centers.Add(pt);
            }
            if (centers.Count < 2) return;

            // 2. Multi-lane corner mitering
            var routePoints = new List<Vector3>();
            for (int i = 0; i < centers.Count; i++)
            {
                Vector2 inNormal = i > 0 ? LeftNormal(centers[i] - centers[i - 1]) : Vector2.zero;
                Vector2 outNormal = i < centers.Count - 1 ? LeftNormal(centers[i + 1] - centers[i]) : Vector2.zero;
                Vector2 miter = inNormal + outNormal;
                if (miter.sqrMagnitude < 0.0001f) miter = i > 0 ? inNormal : outNormal;
                miter.Normalize();
                Vector2 reference = i > 0 ? inNormal : outNormal;
                float stretch = 1f / Mathf.Max(0.35f, Vector2.Dot(miter, reference));
                Vector2 shifted = centers[i] + miter * laneOffset * stretch;
                routePoints.Add(new Vector3(shifted.x, shifted.y, -0.05f));
            }

            var cumulative = new List<float> { 0f };
            float totalLen = 0f;
            for (int p = 1; p < routePoints.Count; p++)
            {
                totalLen += Vector3.Distance(routePoints[p - 1], routePoints[p]);
                cumulative.Add(totalLen);
            }

            // 3. LineRenderers:
            // Casing (dark under-layer)
            LineRenderer casingLine = CreateLineRenderer("RouteCasing", thickness * 1.6f, CasingColor, GetLineMaterial(), 45);

            // Core (faint solid color)
            LineRenderer coreLine = CreateLineRenderer("RouteCore", thickness, new Color(color.r, color.g, color.b, 0.35f), GetLineMaterial(), 46);

            // Dashes (flowing dashed pattern)
            Material dashMat = CreateDashMaterial(color);
            float dashPeriod = Mathf.Max(0.2f, cellSize * 0.5f);
            LineRenderer dashLine = CreateLineRenderer("RouteDashes", thickness, color, dashMat, 47);
            dashLine.textureMode = LineTextureMode.Tile;

            routes.Add(new RouteVisual
            {
                fullPoints = routePoints,
                cumulativeLengths = cumulative,
                totalLength = totalLen,
                casingLine = casingLine,
                coreLine = coreLine,
                dashLine = dashLine,
                dashMat = dashMat,
                dashPeriod = dashPeriod
            });
        }

        private static Vector2 LeftNormal(Vector2 direction)
        {
            Vector2 d = direction.normalized;
            return new Vector2(-d.y, d.x);
        }

        private LineRenderer CreateLineRenderer(string name, float width, Color color, Material material, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(visualRoot.transform, false);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.positionCount = 0;
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.material = material;
            line.sortingOrder = sortingOrder;
            return line;
        }

        private void CreateMarker(Vector3 position, Color color, string label, float cellSize, float phase)
        {
            var markerGo = new GameObject(label + "MarkerGroup");
            markerGo.transform.SetParent(visualRoot.transform, false);
            markerGo.transform.position = new Vector3(position.x, position.y, -0.06f);

            Sprite ringSprite = StageMapPreview.GetRingSprite();
            float baseScale = cellSize * 0.85f;

            // Base Ring
            var baseRing = new GameObject("BaseRing");
            baseRing.transform.SetParent(markerGo.transform, false);
            baseRing.transform.localScale = Vector3.one * baseScale;
            var baseSr = baseRing.AddComponent<SpriteRenderer>();
            baseSr.sprite = ringSprite;
            baseSr.color = color;
            baseSr.sortingOrder = 48;

            // Pulsing Ring
            var pingRing = new GameObject("PingRing");
            pingRing.transform.SetParent(markerGo.transform, false);
            pingRing.transform.localScale = Vector3.one * baseScale;
            var pingSr = pingRing.AddComponent<SpriteRenderer>();
            pingSr.sprite = ringSprite;
            pingSr.color = color;
            pingSr.sortingOrder = 48;

            // Center Dot
            var dot = new GameObject("CenterDot");
            dot.transform.SetParent(markerGo.transform, false);
            dot.transform.localScale = Vector3.one * (baseScale * 0.28f);
            var dotSr = dot.AddComponent<SpriteRenderer>();
            dotSr.sprite = ringSprite;
            dotSr.color = color;
            dotSr.sortingOrder = 48;

            // Label text above marker
            var textGo = new GameObject("MarkerLabel");
            textGo.transform.SetParent(markerGo.transform, false);
            textGo.transform.localPosition = new Vector3(0f, cellSize * 0.52f, 0f);

            var textMesh = textGo.AddComponent<TextMesh>();
            textMesh.text = label;
            textMesh.fontSize = 28;
            textMesh.characterSize = 0.038f * cellSize;
            textMesh.alignment = TextAlignment.Center;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.fontStyle = FontStyle.Bold;
            textMesh.color = color;

            var meshRenderer = textGo.GetComponent<MeshRenderer>();
            meshRenderer.sortingOrder = 49;

            markers.Add(new MarkerVisual
            {
                ping = pingRing.transform,
                baseRenderer = baseSr,
                pingRenderer = pingSr,
                dotRenderer = dotSr,
                labelTextMesh = textMesh,
                color = color,
                baseScale = baseScale,
                phase = phase
            });
        }

        private void Update()
        {
            if (!isVisible) return;

            animTime += Time.unscaledDeltaTime;
            float t = animTime;

            float routeT = Mathf.Clamp01(t / RouteDrawDuration);
            float drawn = EaseOutCubic(routeT);
            float flow = t * DashSpeed;

            // 1. Draw-in sweep & flow animation
            foreach (RouteVisual route in routes)
            {
                float currentLength = drawn * route.totalLength;
                UpdateLinePositions(route, currentLength);

                if (route.dashMat != null)
                {
                    route.dashMat.mainTextureScale = new Vector2(Mathf.Max(0.01f, currentLength / route.dashPeriod), 1f);
                    route.dashMat.mainTextureOffset = new Vector2(-flow, 0f);
                }
            }

            // 2. Markers fade-in & ping animation
            float markerAlpha = Mathf.Clamp01(t * 3.3f);
            foreach (MarkerVisual marker in markers)
            {
                if (marker.baseRenderer != null)
                {
                    Color bc = marker.color;
                    bc.a = markerAlpha;
                    marker.baseRenderer.color = bc;
                }
                if (marker.dotRenderer != null)
                {
                    Color dc = marker.color;
                    dc.a = markerAlpha;
                    marker.dotRenderer.color = dc;
                }
                if (marker.labelTextMesh != null)
                {
                    Color tc = marker.color;
                    tc.a = markerAlpha;
                    marker.labelTextMesh.color = tc;
                }

                if (marker.ping != null && marker.pingRenderer != null)
                {
                    float k = Mathf.Repeat(t / PingPeriod + marker.phase, 1f);
                    float scaleMultiplier = Mathf.Lerp(1f, 2.1f, EaseOutCubic(k));
                    marker.ping.localScale = Vector3.one * (marker.baseScale * scaleMultiplier);

                    Color pc = marker.color;
                    pc.a = (1f - k) * 0.85f * markerAlpha;
                    marker.pingRenderer.color = pc;
                }
            }
        }

        private static void UpdateLinePositions(RouteVisual route, float currentLength)
        {
            if (route.fullPoints.Count < 2) return;

            if (currentLength >= route.totalLength - 0.001f)
            {
                int count = route.fullPoints.Count;
                if (route.casingLine.positionCount != count)
                {
                    Vector3[] arr = route.fullPoints.ToArray();
                    route.casingLine.positionCount = count;
                    route.casingLine.SetPositions(arr);
                    route.coreLine.positionCount = count;
                    route.coreLine.SetPositions(arr);
                    route.dashLine.positionCount = count;
                    route.dashLine.SetPositions(arr);
                }
                return;
            }

            if (currentLength <= 0.005f)
            {
                route.casingLine.positionCount = 0;
                route.coreLine.positionCount = 0;
                route.dashLine.positionCount = 0;
                return;
            }

            tempPoints.Clear();
            tempPoints.Add(route.fullPoints[0]);

            for (int i = 1; i < route.fullPoints.Count; i++)
            {
                float segEndDist = route.cumulativeLengths[i];
                if (currentLength >= segEndDist)
                {
                    tempPoints.Add(route.fullPoints[i]);
                }
                else
                {
                    float segStartDist = route.cumulativeLengths[i - 1];
                    float segLen = segEndDist - segStartDist;
                    float frac = segLen > 0.0001f ? (currentLength - segStartDist) / segLen : 0f;
                    Vector3 partial = Vector3.Lerp(route.fullPoints[i - 1], route.fullPoints[i], frac);
                    tempPoints.Add(partial);
                    break;
                }
            }

            int ptCount = tempPoints.Count;
            Vector3[] pointsArray = tempPoints.ToArray();

            route.casingLine.positionCount = ptCount;
            route.casingLine.SetPositions(pointsArray);

            route.coreLine.positionCount = ptCount;
            route.coreLine.SetPositions(pointsArray);

            route.dashLine.positionCount = ptCount;
            route.dashLine.SetPositions(pointsArray);
        }

        private static float EaseOutCubic(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }
    }
}
