using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Core.Grid
{
    /// <summary>
    /// Represents a single cell in the game grid.
    /// Stores tile type, deployment state, and occupant references.
    /// </summary>
    public class GridCell
    {
        /// <summary>Grid X coordinate.</summary>
        public int X { get; private set; }

        /// <summary>Grid Y coordinate.</summary>
        public int Y { get; private set; }

        /// <summary>The type of tile at this cell (GDD 1.4.1).</summary>
        public TileType TileType { get; private set; }

        /// <summary>Whether an operator is currently deployed on this cell.</summary>
        public bool IsOccupied { get; private set; }

        /// <summary>Reference to the deployed operator on this cell (null if unoccupied).</summary>
        public GameObject OccupantOperator { get; private set; }

        /// <summary>World position of the center of this cell.</summary>
        public Vector3 WorldPosition { get; private set; }

        /// <summary>Grid position as Vector2Int.</summary>
        public Vector2Int GridPosition => new Vector2Int(X, Y);

        // -- Pathfinding fields (used by A*) --

        /// <summary>Whether ground enemies can walk through this cell.</summary>
        public bool IsWalkable { get; private set; }

        /// <summary>Whether air enemies can fly over this cell (always true unless explicitly blocked).</summary>
        public bool IsAirPassable { get; private set; }

        public GridCell(int x, int y, TileType tileType, Vector3 worldPosition)
        {
            X = x;
            Y = y;
            TileType = tileType;
            WorldPosition = worldPosition;
            IsOccupied = false;
            OccupantOperator = null;

            // Determine walkability based on tile type
            IsWalkable = tileType == TileType.EnemyPath
                      || tileType == TileType.SpawnPoint
                      || tileType == TileType.ExitPoint
                      || tileType == TileType.LowGround;
            IsAirPassable = tileType != TileType.Blocked;
        }

        /// <summary>
        /// Check if a specific operator position type can deploy on this tile (GDD 1.4.1).
        /// </summary>
        public bool CanDeploy(OperatorPosition operatorPosition)
        {
            if (IsOccupied) return false;

            return (TileType, operatorPosition) switch
            {
                (TileType.LowGround, OperatorPosition.Melee) => true,
                (TileType.EnemyPath, OperatorPosition.Melee) => true,
                (TileType.HighGround, OperatorPosition.Ranged) => true,
                // High Ground sometimes allows melee — marked via separate flag if needed
                // For now, strictly ranged-only on High Ground per GDD
                _ => false
            };
        }

        /// <summary>
        /// Deploy an operator on this cell.
        /// </summary>
        public bool Deploy(GameObject operatorObject)
        {
            if (IsOccupied) return false;

            IsOccupied = true;
            OccupantOperator = operatorObject;
            return true;
        }

        /// <summary>
        /// Remove the deployed operator from this cell.
        /// </summary>
        public void Vacate()
        {
            IsOccupied = false;
            OccupantOperator = null;
        }

        /// <summary>
        /// Update the tile type (e.g., for dynamic map effects).
        /// </summary>
        public void SetTileType(TileType newType)
        {
            TileType = newType;
            IsWalkable = newType == TileType.EnemyPath
                      || newType == TileType.SpawnPoint
                      || newType == TileType.ExitPoint
                      || newType == TileType.LowGround;
            IsAirPassable = newType != TileType.Blocked;
        }
    }
}
