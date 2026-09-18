using UnityEngine;

namespace TrashTD.Core.Pathfinding
{
    /// <summary>
    /// Represents a node in the A* pathfinding graph.
    /// Wraps a GridCell with pathfinding-specific data (G, H, F costs, parent).
    /// </summary>
    public class PathNode
    {
        /// <summary>Grid X coordinate.</summary>
        public int X { get; private set; }

        /// <summary>Grid Y coordinate.</summary>
        public int Y { get; private set; }

        /// <summary>Grid position as Vector2Int.</summary>
        public Vector2Int GridPosition => new Vector2Int(X, Y);

        /// <summary>Whether this node can be traversed.</summary>
        public bool IsWalkable { get; set; }

        /// <summary>World position of this node.</summary>
        public Vector3 WorldPosition { get; set; }

        /// <summary>
        /// G cost — the actual cost from the start node to this node.
        /// </summary>
        public float GCost { get; set; }

        /// <summary>
        /// H cost — the heuristic estimated cost from this node to the end node.
        /// </summary>
        public float HCost { get; set; }

        /// <summary>
        /// F cost — total estimated cost (G + H). Used by A* to pick the best node.
        /// </summary>
        public float FCost => GCost + HCost;

        /// <summary>
        /// Parent node in the path — used to reconstruct the final path.
        /// </summary>
        public PathNode Parent { get; set; }

        public PathNode(int x, int y, bool isWalkable, Vector3 worldPosition)
        {
            X = x;
            Y = y;
            IsWalkable = isWalkable;
            WorldPosition = worldPosition;
            GCost = float.MaxValue;
            HCost = 0f;
            Parent = null;
        }

        /// <summary>
        /// Reset pathfinding state for reuse across multiple pathfinding queries.
        /// </summary>
        public void Reset()
        {
            GCost = float.MaxValue;
            HCost = 0f;
            Parent = null;
        }
    }
}
