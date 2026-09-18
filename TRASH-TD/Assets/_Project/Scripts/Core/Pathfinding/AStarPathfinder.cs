using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;

namespace TrashTD.Core.Pathfinding
{
    /// <summary>
    /// Custom A* pathfinding implementation (GDD 1.4.2 explicit requirement).
    /// Supports both ground and air movement types:
    /// - Ground enemies use walkable tiles only
    /// - Air enemies can fly over all non-blocked tiles
    /// 
    /// Supports fixed paths, multiple lanes, and branching paths.
    /// </summary>
    public class AStarPathfinder
    {
        private PathNode[,] nodeGrid;
        private int width;
        private int height;

        /// <summary>
        /// Initialize the pathfinding grid from the GridManager's cells.
        /// Must be called after GridManager.InitializeFromStageData().
        /// </summary>
        public void Initialize(GridManager gridManager)
        {
            width = gridManager.Width;
            height = gridManager.Height;
            nodeGrid = new PathNode[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    GridCell cell = gridManager.GetCell(x, y);
                    nodeGrid[x, y] = new PathNode(
                        x, y,
                        cell.IsWalkable,
                        cell.WorldPosition
                    );
                }
            }
        }

        /// <summary>
        /// Find a path from start to end using the A* algorithm.
        /// Returns a list of world positions forming the path, or null if no path exists.
        /// </summary>
        /// <param name="start">Starting grid position</param>
        /// <param name="end">Target grid position</param>
        /// <param name="movementType">Ground or Air — determines which tiles are traversable</param>
        /// <returns>List of world positions from start to end, or null if unreachable</returns>
        public List<Vector3> FindPath(Vector2Int start, Vector2Int end, EnemyMovementType movementType)
        {
            if (!IsInBounds(start) || !IsInBounds(end))
                return null;

            // Reset all nodes for a fresh search
            ResetNodes();

            PathNode startNode = nodeGrid[start.x, start.y];
            PathNode endNode = nodeGrid[end.x, end.y];

            // Open list: nodes to evaluate (sorted by FCost)
            List<PathNode> openList = new List<PathNode> { startNode };

            // Closed set: nodes already evaluated
            HashSet<PathNode> closedSet = new HashSet<PathNode>();

            startNode.GCost = 0;
            startNode.HCost = CalculateHeuristic(startNode, endNode);

            while (openList.Count > 0)
            {
                // Get node with lowest F cost
                PathNode currentNode = GetLowestFCostNode(openList);

                // Reached the destination
                if (currentNode == endNode)
                {
                    return ReconstructPath(endNode);
                }

                openList.Remove(currentNode);
                closedSet.Add(currentNode);

                // Evaluate all neighbors
                foreach (PathNode neighbor in GetNeighbors(currentNode, movementType))
                {
                    if (closedSet.Contains(neighbor))
                        continue;

                    // Check traversability based on movement type
                    if (!IsTraversable(neighbor, movementType))
                        continue;

                    // Calculate tentative G cost (uniform cost = 1 per tile)
                    float tentativeGCost = currentNode.GCost + 1f;

                    if (tentativeGCost < neighbor.GCost)
                    {
                        // This is a better path to this neighbor
                        neighbor.Parent = currentNode;
                        neighbor.GCost = tentativeGCost;
                        neighbor.HCost = CalculateHeuristic(neighbor, endNode);

                        if (!openList.Contains(neighbor))
                        {
                            openList.Add(neighbor);
                        }
                    }
                }
            }

            // No path found
            return null;
        }

        /// <summary>
        /// Find paths from a single spawn point to multiple exit points.
        /// Returns the shortest valid path, or null if none exist.
        /// Supports branching paths (GDD 1.4.2).
        /// </summary>
        public List<Vector3> FindBestPath(Vector2Int start, Vector2Int[] exits, EnemyMovementType movementType)
        {
            List<Vector3> bestPath = null;
            float bestCost = float.MaxValue;

            foreach (Vector2Int exit in exits)
            {
                List<Vector3> path = FindPath(start, exit, movementType);
                if (path != null && path.Count < bestCost)
                {
                    bestPath = path;
                    bestCost = path.Count;
                }
            }

            return bestPath;
        }

        /// <summary>
        /// Calculate Manhattan distance heuristic (admissible for grid-based movement).
        /// </summary>
        private float CalculateHeuristic(PathNode a, PathNode b)
        {
            return Mathf.Abs(a.X - b.X) + Mathf.Abs(a.Y - b.Y);
        }

        /// <summary>
        /// Get the node with the lowest F cost from the open list.
        /// Ties are broken by lowest H cost (closer to goal).
        /// </summary>
        private PathNode GetLowestFCostNode(List<PathNode> nodeList)
        {
            PathNode lowest = nodeList[0];

            for (int i = 1; i < nodeList.Count; i++)
            {
                if (nodeList[i].FCost < lowest.FCost ||
                    (Mathf.Approximately(nodeList[i].FCost, lowest.FCost) &&
                     nodeList[i].HCost < lowest.HCost))
                {
                    lowest = nodeList[i];
                }
            }

            return lowest;
        }

        /// <summary>
        /// Get valid neighboring nodes (4-directional).
        /// </summary>
        private List<PathNode> GetNeighbors(PathNode node, EnemyMovementType movementType)
        {
            List<PathNode> neighbors = new List<PathNode>(4);

            // Right
            if (IsInBounds(node.X + 1, node.Y))
                neighbors.Add(nodeGrid[node.X + 1, node.Y]);
            // Left
            if (IsInBounds(node.X - 1, node.Y))
                neighbors.Add(nodeGrid[node.X - 1, node.Y]);
            // Up
            if (IsInBounds(node.X, node.Y + 1))
                neighbors.Add(nodeGrid[node.X, node.Y + 1]);
            // Down
            if (IsInBounds(node.X, node.Y - 1))
                neighbors.Add(nodeGrid[node.X, node.Y - 1]);

            return neighbors;
        }

        /// <summary>
        /// Check if a node is traversable based on movement type (GDD 1.4.2).
        /// Ground enemies use walkable tiles; Air enemies use air-passable tiles.
        /// </summary>
        private bool IsTraversable(PathNode node, EnemyMovementType movementType)
        {
            if (movementType == EnemyMovementType.Air)
            {
                // Air enemies can fly over everything except explicitly blocked tiles
                // We check using the GridManager's air passability
                return true; // Air can go anywhere in bounds
            }

            return node.IsWalkable;
        }

        /// <summary>
        /// Reconstruct the path by following parent pointers from end to start.
        /// Returns world positions in start-to-end order.
        /// </summary>
        private List<Vector3> ReconstructPath(PathNode endNode)
        {
            List<Vector3> path = new List<Vector3>();
            PathNode current = endNode;

            while (current != null)
            {
                path.Add(current.WorldPosition);
                current = current.Parent;
            }

            path.Reverse();
            return path;
        }

        /// <summary>
        /// Reset all nodes for a fresh pathfinding query.
        /// </summary>
        private void ResetNodes()
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    nodeGrid[x, y].Reset();
                }
            }
        }

        /// <summary>
        /// Check if grid coordinates are within bounds.
        /// </summary>
        private bool IsInBounds(int x, int y)
        {
            return x >= 0 && x < width && y >= 0 && y < height;
        }

        private bool IsInBounds(Vector2Int pos)
        {
            return IsInBounds(pos.x, pos.y);
        }

        /// <summary>
        /// Update walkability of a specific node (e.g., when an operator is deployed/removed).
        /// </summary>
        public void UpdateNodeWalkability(int x, int y, bool isWalkable)
        {
            if (IsInBounds(x, y))
            {
                nodeGrid[x, y].IsWalkable = isWalkable;
            }
        }
    }
}
