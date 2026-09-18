using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Core.Grid
{
    /// <summary>
    /// Manages the game grid — creates cells from StageData, provides
    /// queries for deployment, pathfinding, and spatial lookups.
    /// Uses Unity Tilemap for visuals; this class manages the gameplay logic layer.
    /// </summary>
    public class GridManager : MonoBehaviour
    {
        [Header("Grid Settings")]
        [Tooltip("Size of each grid cell in world units")]
        [SerializeField] private float cellSize = 1.0f;

        [Tooltip("World-space origin of the grid (bottom-left corner)")]
        [SerializeField] private Vector3 gridOrigin = Vector3.zero;

        /// <summary>The logical grid of cells.</summary>
        private GridCell[,] grid;

        /// <summary>Width of the grid in cells.</summary>
        public int Width { get; private set; }

        /// <summary>Height of the grid in cells.</summary>
        public int Height { get; private set; }

        /// <summary>Cell size in world units.</summary>
        public float CellSize => cellSize;

        /// <summary>
        /// Initialize the grid from stage data.
        /// Call this when a stage is loaded.
        /// </summary>
        public void InitializeFromStageData(StageData stageData)
        {
            Width = stageData.gridWidth;
            Height = stageData.gridHeight;
            grid = new GridCell[Width, Height];

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    TileType tileType = stageData.GetTile(x, y);
                    Vector3 worldPos = GridToWorldPosition(x, y);
                    grid[x, y] = new GridCell(x, y, tileType, worldPos);
                }
            }
        }

        /// <summary>
        /// Get the cell at grid coordinates. Returns null if out of bounds.
        /// </summary>
        public GridCell GetCell(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return null;

            return grid[x, y];
        }

        /// <summary>
        /// Get the cell at grid coordinates. Returns null if out of bounds.
        /// </summary>
        public GridCell GetCell(Vector2Int pos)
        {
            return GetCell(pos.x, pos.y);
        }

        /// <summary>
        /// Convert grid coordinates to world position (center of cell).
        /// </summary>
        public Vector3 GridToWorldPosition(int x, int y)
        {
            return gridOrigin + new Vector3(
                x * cellSize + cellSize * 0.5f,
                y * cellSize + cellSize * 0.5f,
                0f
            );
        }

        /// <summary>
        /// Convert grid coordinates to world position (center of cell).
        /// </summary>
        public Vector3 GridToWorldPosition(Vector2Int pos)
        {
            return GridToWorldPosition(pos.x, pos.y);
        }

        /// <summary>
        /// Convert a world position to grid coordinates.
        /// </summary>
        public Vector2Int WorldToGridPosition(Vector3 worldPos)
        {
            int x = Mathf.FloorToInt((worldPos.x - gridOrigin.x) / cellSize);
            int y = Mathf.FloorToInt((worldPos.y - gridOrigin.y) / cellSize);
            return new Vector2Int(x, y);
        }

        /// <summary>
        /// Check if a position is within the grid bounds.
        /// </summary>
        public bool IsInBounds(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        /// <summary>
        /// Check if a position is within the grid bounds.
        /// </summary>
        public bool IsInBounds(Vector2Int pos)
        {
            return IsInBounds(pos.x, pos.y);
        }

        /// <summary>
        /// Try to deploy an operator onto a cell. Validates tile type and occupancy.
        /// </summary>
        public bool TryDeploy(int x, int y, OperatorPosition operatorPosition, GameObject operatorObject)
        {
            GridCell cell = GetCell(x, y);
            if (cell == null) return false;
            if (!cell.CanDeploy(operatorPosition)) return false;

            return cell.Deploy(operatorObject);
        }

        /// <summary>
        /// Remove an operator from a cell.
        /// </summary>
        public void VacateCell(int x, int y)
        {
            GridCell cell = GetCell(x, y);
            cell?.Vacate();
        }

        /// <summary>
        /// Get all neighboring cells (4-directional: up, down, left, right).
        /// Used by A* pathfinding.
        /// </summary>
        public GridCell[] GetNeighbors(int x, int y)
        {
            var neighbors = new System.Collections.Generic.List<GridCell>(4);

            if (IsInBounds(x + 1, y)) neighbors.Add(grid[x + 1, y]);
            if (IsInBounds(x - 1, y)) neighbors.Add(grid[x - 1, y]);
            if (IsInBounds(x, y + 1)) neighbors.Add(grid[x, y + 1]);
            if (IsInBounds(x, y - 1)) neighbors.Add(grid[x, y - 1]);

            return neighbors.ToArray();
        }

        /// <summary>
        /// Get all cells of a specific type (e.g., find all spawn points).
        /// </summary>
        public GridCell[] GetCellsOfType(TileType tileType)
        {
            var result = new System.Collections.Generic.List<GridCell>();

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (grid[x, y].TileType == tileType)
                        result.Add(grid[x, y]);
                }
            }

            return result.ToArray();
        }

        /// <summary>
        /// Get cells within a range pattern relative to a center position.
        /// Used for operator attack range calculation.
        /// </summary>
        public GridCell[] GetCellsInRange(Vector2Int center, Vector2Int[] rangePattern)
        {
            var result = new System.Collections.Generic.List<GridCell>();

            if (rangePattern == null) return result.ToArray();

            foreach (Vector2Int offset in rangePattern)
            {
                int tx = center.x + offset.x;
                int ty = center.y + offset.y;
                GridCell cell = GetCell(tx, ty);
                if (cell != null)
                    result.Add(cell);
            }

            return result.ToArray();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Draw grid gizmos in the editor for debugging.
        /// </summary>
        private void OnDrawGizmos()
        {
            if (grid == null) return;

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    GridCell cell = grid[x, y];
                    Gizmos.color = cell.TileType switch
                    {
                        TileType.LowGround => new Color(0.4f, 0.8f, 0.4f, 0.3f),
                        TileType.HighGround => new Color(0.4f, 0.4f, 0.8f, 0.3f),
                        TileType.EnemyPath => new Color(0.8f, 0.8f, 0.4f, 0.3f),
                        TileType.SpawnPoint => new Color(0.8f, 0.2f, 0.2f, 0.5f),
                        TileType.ExitPoint => new Color(0.2f, 0.2f, 0.8f, 0.5f),
                        TileType.Blocked => new Color(0.3f, 0.3f, 0.3f, 0.3f),
                        _ => new Color(0.5f, 0.5f, 0.5f, 0.2f)
                    };

                    Vector3 pos = cell.WorldPosition;
                    Gizmos.DrawCube(pos, Vector3.one * cellSize * 0.9f);
                    Gizmos.color = Color.white;
                    Gizmos.DrawWireCube(pos, Vector3.one * cellSize);
                }
            }
        }
#endif
    }
}
