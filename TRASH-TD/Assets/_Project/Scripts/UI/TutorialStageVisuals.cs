using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;

namespace TrashTD.UI
{
    /// <summary>
    /// The tutorial stage's floor, in the dossier preview's look (StageMapPreview / PathPreviewArt): a black void,
    /// dark phosphor-green tiles (high ground darker, with a lit top edge) under faint grid lines, the enemy route
    /// as a conveyor lane drawn under the units once Coalesce reveals it, and pulsing hazard-yellow tile
    /// highlights that point at where to deploy. World space, Ground layer, unscaled time.
    /// </summary>
    public sealed class TutorialStageVisuals : MonoBehaviour
    {
        private const int VoidOrder = -50;
        private const int TileOrder = 0;
        private const int GridOrder = 1;
        private const int HighlightOrder = 13; // Above the route overlay (3..12), still under the units
        private const float GridAlpha = 0.20f;
        private const float HighlightPulseSpeed = 4f;

        private static readonly Color VoidColor = new Color(0.006f, 0.014f, 0.010f, 1f);
        private static readonly Color LowGroundColor = new Color(0.07f, 0.17f, 0.105f, 1f);
        private static readonly Color HighGroundColor = new Color(0.04f, 0.105f, 0.065f, 1f);
        private static readonly Color HighGroundEdgeColor = new Color(0.12f, 0.32f, 0.19f, 1f);
        public static readonly Color HighlightColor = new Color(0.98f, 0.80f, 0.18f, 1f);

        private GridManager gridManager;
        private StageData stageData;
        private GameplayPathPreviewOverlay routeOverlay;
        private float cellSize = 1f;
        private float artPixel = 1f / 32f;

        private Transform highlightRoot;
        private readonly List<SpriteRenderer> highlightFills = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> highlightEdges = new List<SpriteRenderer>();
        private readonly HashSet<Vector2Int> highlightedTiles = new HashSet<Vector2Int>();

        public bool IsRouteRevealed => routeOverlay != null && routeOverlay.IsVisible;

        public static TutorialStageVisuals Create(Transform parent, GridManager grid, StageData stage)
        {
            var go = new GameObject("TutorialStageVisuals");
            go.transform.SetParent(parent, false);
            var visuals = go.AddComponent<TutorialStageVisuals>();
            visuals.Build(grid, stage);
            return visuals;
        }

        private void Build(GridManager grid, StageData stage)
        {
            gridManager = grid;
            stageData = stage;
            cellSize = grid.CellSize;
            float pixelsPerTile = stage.visualTilePixelSize > 0 ? stage.visualTilePixelSize : 32f;
            artPixel = cellSize / pixelsPerTile;

            Vector2 gridMin = (Vector2)grid.GridToWorldPosition(0, 0) - Vector2.one * (cellSize * 0.5f);
            Vector2 gridSize = new Vector2(stage.gridWidth, stage.gridHeight) * cellSize;

            // A void far bigger than any camera framing, so zooming out never shows the clear colour
            CreateRect("Void", transform, gridMin + gridSize * 0.5f, gridSize + new Vector2(80f, 60f), VoidColor, VoidOrder);

            // Tiles: blocked tiles stay void; the grid lines cover the playable block only
            bool any = false;
            Vector2Int min = new Vector2Int(int.MaxValue, int.MaxValue);
            Vector2Int max = new Vector2Int(int.MinValue, int.MinValue);
            for (int y = 0; y < stage.gridHeight; y++)
            {
                for (int x = 0; x < stage.gridWidth; x++)
                {
                    TileType type = stage.GetTile(x, y);
                    if (type == TileType.Blocked) continue;

                    any = true;
                    min = Vector2Int.Min(min, new Vector2Int(x, y));
                    max = Vector2Int.Max(max, new Vector2Int(x, y));

                    Vector2 center = grid.GridToWorldPosition(x, y);
                    bool high = type == TileType.HighGround;
                    float inset = artPixel * 2f;
                    CreateRect($"Tile_{x}_{y}", transform, center, Vector2.one * (cellSize - inset * 2f), high ? HighGroundColor : LowGroundColor, TileOrder);
                    if (high)
                    {
                        // Lit top edge: raised platforms read apart from the lane
                        CreateRect($"TileEdge_{x}_{y}", transform, center + new Vector2(0f, cellSize * 0.5f - inset - artPixel),
                            new Vector2(cellSize - inset * 2f, artPixel * 2f), HighGroundEdgeColor, TileOrder + 1);
                    }
                }
            }

            if (any)
            {
                Color gridColor = PathPreviewArt.WithAlpha(PathPreviewArt.PhosphorColor, GridAlpha);
                Vector2 blockMin = gridMin + (Vector2)min * cellSize;
                Vector2 blockSize = (Vector2)(max - min + Vector2Int.one) * cellSize;
                for (int x = 0; x <= max.x - min.x + 1; x++)
                {
                    CreateRect("GridColumn", transform, blockMin + new Vector2(x * cellSize, blockSize.y * 0.5f), new Vector2(artPixel, blockSize.y), gridColor, GridOrder + 1);
                }
                for (int y = 0; y <= max.y - min.y + 1; y++)
                {
                    CreateRect("GridRow", transform, blockMin + new Vector2(blockSize.x * 0.5f, y * cellSize), new Vector2(blockSize.x, artPixel), gridColor, GridOrder + 1);
                }
            }

            highlightRoot = new GameObject("TileHighlights").transform;
            highlightRoot.SetParent(transform, false);

            routeOverlay = GameplayPathPreviewOverlay.CreateUnderUnits(grid, stage);
            routeOverlay.transform.SetParent(transform, false);
        }

        /// <summary>Lights the enemy route up, drawing in from the spawn (once).</summary>
        public void RevealRoute()
        {
            if (routeOverlay != null && !routeOverlay.IsVisible) routeOverlay.SetVisible(true);
        }

        /// <summary>Pulses these tiles (replaces the previous set); empty clears.</summary>
        public void SetHighlightedTiles(IEnumerable<Vector2Int> tiles)
        {
            var next = new HashSet<Vector2Int>(tiles ?? new Vector2Int[0]);
            if (next.SetEquals(highlightedTiles)) return;

            highlightedTiles.Clear();
            highlightedTiles.UnionWith(next);
            for (int i = highlightRoot.childCount - 1; i >= 0; i--) Destroy(highlightRoot.GetChild(i).gameObject);
            highlightFills.Clear();
            highlightEdges.Clear();

            float edge = artPixel * 2f;
            float half = cellSize * 0.5f - edge * 0.5f;
            foreach (Vector2Int tile in highlightedTiles)
            {
                Vector2 c = gridManager.GridToWorldPosition(tile);
                highlightFills.Add(CreateRect("HighlightFill", highlightRoot, c, Vector2.one * cellSize, Color.clear, HighlightOrder));
                highlightEdges.Add(CreateRect("HighlightEdge", highlightRoot, c + new Vector2(0f, half), new Vector2(cellSize, edge), Color.clear, HighlightOrder + 1));
                highlightEdges.Add(CreateRect("HighlightEdge", highlightRoot, c - new Vector2(0f, half), new Vector2(cellSize, edge), Color.clear, HighlightOrder + 1));
                highlightEdges.Add(CreateRect("HighlightEdge", highlightRoot, c + new Vector2(half, 0f), new Vector2(edge, cellSize), Color.clear, HighlightOrder + 1));
                highlightEdges.Add(CreateRect("HighlightEdge", highlightRoot, c - new Vector2(half, 0f), new Vector2(edge, cellSize), Color.clear, HighlightOrder + 1));
            }
        }

        private void Update()
        {
            if (highlightFills.Count == 0) return;

            float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * HighlightPulseSpeed);
            Color fill = PathPreviewArt.WithAlpha(HighlightColor, Mathf.Lerp(0.06f, 0.22f, wave));
            Color edge = PathPreviewArt.WithAlpha(HighlightColor, Mathf.Lerp(0.45f, 1f, wave));
            foreach (SpriteRenderer renderer in highlightFills) renderer.color = fill;
            foreach (SpriteRenderer renderer in highlightEdges) renderer.color = edge;
        }

        private static SpriteRenderer CreateRect(string objectName, Transform parent, Vector2 center, Vector2 size, Color color, int sortingOrder)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = PathPreviewArt.GetPixel();
            renderer.color = color;
            GridSorting.PlaceOnGround(renderer);
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }
    }
}
