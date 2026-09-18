using NUnit.Framework;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Core.Pathfinding;
using TrashTD.Data;

namespace TrashTD.Tests
{
    public class AStarPathfindingTests
    {
        private GameObject gridObject;
        private GridManager gridManager;

        [SetUp]
        public void SetUp()
        {
            gridObject = new GameObject("TestGridManager");
            gridManager = gridObject.AddComponent<GridManager>();
        }

        [TearDown]
        public void TearDown()
        {
            if (gridObject != null)
            {
                Object.DestroyImmediate(gridObject);
            }
        }

        [Test]
        public void GroundEnemy_FindsDirectPathAroundBlockedTiles()
        {
            // 3x3 Grid
            // [S] [P] [P]
            // [B] [B] [P]
            // [E] [P] [P]
            // (0,2) is S (Spawn), (0,0) is E (Exit)
            // (0,1) and (1,1) are Blocked (B)
            // Ground enemy must navigate around the wall to reach Exit.

            var stageData = ScriptableObject.CreateInstance<StageData>();
            stageData.gridWidth = 3;
            stageData.gridHeight = 3;
            stageData.tileLayout = new TileType[]
            {
                TileType.ExitPoint, TileType.EnemyPath, TileType.EnemyPath, // y=0
                TileType.Blocked,   TileType.Blocked,   TileType.EnemyPath, // y=1
                TileType.SpawnPoint,TileType.EnemyPath, TileType.EnemyPath  // y=2
            };

            gridManager.InitializeFromStageData(stageData);

            var pathfinder = new AStarPathfinder();
            pathfinder.Initialize(gridManager);

            var path = pathfinder.FindPath(new Vector2Int(0, 2), new Vector2Int(0, 0), EnemyMovementType.Ground);

            Assert.IsNotNull(path, "Ground enemy should find a valid path navigating around blocked tiles");
            Assert.IsTrue(path.Count > 0);
            // Must visit (2,1) column to get around the blocked (0,1) and (1,1) tiles
            Assert.AreEqual(gridManager.GridToWorldPosition(0, 2), path[0]);
            Assert.AreEqual(gridManager.GridToWorldPosition(0, 0), path[path.Count - 1]);
        }

        [Test]
        public void AirEnemy_CanTraverseOverBlockedTerrain()
        {
            var stageData = ScriptableObject.CreateInstance<StageData>();
            stageData.gridWidth = 3;
            stageData.gridHeight = 3;
            stageData.tileLayout = new TileType[]
            {
                TileType.ExitPoint, TileType.EnemyPath, TileType.EnemyPath,
                TileType.Blocked,   TileType.Blocked,   TileType.Blocked,
                TileType.SpawnPoint,TileType.EnemyPath, TileType.EnemyPath
            };

            gridManager.InitializeFromStageData(stageData);

            var pathfinder = new AStarPathfinder();
            pathfinder.Initialize(gridManager);

            var groundPath = pathfinder.FindPath(new Vector2Int(0, 2), new Vector2Int(0, 0), EnemyMovementType.Ground);
            var airPath = pathfinder.FindPath(new Vector2Int(0, 2), new Vector2Int(0, 0), EnemyMovementType.Air);

            Assert.IsNull(groundPath, "Ground enemy should be blocked by wall spanning entire width");
            Assert.IsNotNull(airPath, "Air enemy should fly over obstacles directly");
        }
    }
}
