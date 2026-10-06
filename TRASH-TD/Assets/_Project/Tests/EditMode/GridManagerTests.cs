using NUnit.Framework;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;

namespace TrashTD.Tests
{
    public class GridManagerTests
    {
        private GameObject gridObject;
        private GridManager gridManager;
        private StageData stageData;

        [SetUp]
        public void SetUp()
        {
            gridObject = new GameObject("TestGridManager");
            gridManager = gridObject.AddComponent<GridManager>();
            stageData = ScriptableObject.CreateInstance<StageData>();
            stageData.gridWidth = 5;
            stageData.gridHeight = 5;
            stageData.tileLayout = new TileType[25];
            gridManager.InitializeFromStageData(stageData);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(stageData);
            Object.DestroyImmediate(gridObject);
        }

        [TestCase(OperatorFacing.Right, 4, 2)]
        [TestCase(OperatorFacing.Up, 2, 4)]
        [TestCase(OperatorFacing.Left, 0, 2)]
        [TestCase(OperatorFacing.Down, 2, 0)]
        public void GetCellsInRange_RotatesForwardOffsetByFacing(OperatorFacing facing, int expectedX, int expectedY)
        {
            var range = gridManager.GetCellsInRange(
                new Vector2Int(2, 2),
                new[] { new Vector2Int(2, 0) },
                facing);

            Assert.AreEqual(1, range.Length);
            Assert.AreEqual(new Vector2Int(expectedX, expectedY), range[0].GridPosition);
        }

        [Test]
        public void StageLayoutBounds_SeparatesVisualsFromLogic_IgnoresExtraVisualTopRow()
        {
            var stage12x6 = ScriptableObject.CreateInstance<StageData>();
            stage12x6.gridWidth = 12;
            stage12x6.gridHeight = 6;
            stage12x6.tileLayout = new TileType[12 * 6];

            gridManager.InitializeFromStageData(stage12x6);

            // Valid grid cells in 12x6
            Assert.IsTrue(gridManager.IsInBounds(0, 0));
            Assert.IsTrue(gridManager.IsInBounds(11, 5));
            Assert.IsNotNull(gridManager.GetCell(0, 0));
            Assert.IsNotNull(gridManager.GetCell(11, 5));

            // Extra visual top row (y = 6, corresponding to top 384x32 of level1 complete)
            // must NOT be counted in the logic grid
            Assert.IsFalse(gridManager.IsInBounds(0, 6));
            Assert.IsFalse(gridManager.IsInBounds(5, 6));
            Assert.IsFalse(gridManager.IsInBounds(11, 6));
            Assert.IsNull(gridManager.GetCell(0, 6));
            Assert.IsNull(gridManager.GetCell(5, 6));
            Assert.IsNull(gridManager.GetCell(11, 6));

            Object.DestroyImmediate(stage12x6);
        }

        [Test]
        public void Stage02_ScrapyardJunction_LayoutAndPointsVerified()
        {
            var stage = ScriptableObject.CreateInstance<StageData>();
            stage.stageId = "STAGE_02";
            stage.mapName = "Scrapyard Junction";
            stage.gridWidth = 11;
            stage.gridHeight = 7;

            string[] layoutRows =
            {
                "BBBBBBBBBBS",
                "BBBBLLLLLLL",
                "BHHHLHHHHHH",
                "ELLLLLLLLLE",
                "HHHHHHLHHHB",
                "LLLLLLLBBBB",
                "SBBBBBBBBBB"
            };
            stage.tileLayout = new TileType[11 * 7];
            var spawnPoints = new System.Collections.Generic.List<Vector2Int>();
            var exitPoints = new System.Collections.Generic.List<Vector2Int>();
            for (int row = 0; row < layoutRows.Length; row++)
            {
                for (int x = 0; x < 11; x++)
                {
                    int y = layoutRows.Length - 1 - row;
                    int index = y * 11 + x;
                    switch (layoutRows[row][x])
                    {
                        case 'H':
                            stage.tileLayout[index] = TileType.HighGround;
                            break;
                        case 'S':
                            stage.tileLayout[index] = TileType.SpawnPoint;
                            spawnPoints.Add(new Vector2Int(x, y));
                            break;
                        case 'E':
                            stage.tileLayout[index] = TileType.ExitPoint;
                            exitPoints.Add(new Vector2Int(x, y));
                            break;
                        case 'B':
                            stage.tileLayout[index] = TileType.Blocked;
                            break;
                        default:
                            stage.tileLayout[index] = TileType.LowGround;
                            break;
                    }
                }
            }
            stage.spawnPoints = spawnPoints.ToArray();
            stage.exitPoints = exitPoints.ToArray();

            gridManager.InitializeFromStageData(stage);

            Assert.AreEqual(11, gridManager.Width);
            Assert.AreEqual(7, gridManager.Height);

            // Spawns
            Assert.AreEqual(2, stage.spawnPoints.Length);
            CollectionAssert.Contains(stage.spawnPoints, new Vector2Int(0, 0));
            CollectionAssert.Contains(stage.spawnPoints, new Vector2Int(10, 6));

            // Exits
            Assert.AreEqual(2, stage.exitPoints.Length);
            CollectionAssert.Contains(stage.exitPoints, new Vector2Int(0, 3));
            CollectionAssert.Contains(stage.exitPoints, new Vector2Int(10, 3));

            // Blocked tiles (cannot deploy melee or ranged)
            var blockedCell = gridManager.GetCell(1, 0);
            Assert.AreEqual(TileType.Blocked, blockedCell.TileType);
            Assert.IsFalse(blockedCell.CanDeploy(OperatorPosition.Melee));
            Assert.IsFalse(blockedCell.CanDeploy(OperatorPosition.Ranged));

            // HighGround (ranged only)
            var rangedCell = gridManager.GetCell(1, 2);
            Assert.AreEqual(TileType.HighGround, rangedCell.TileType);
            Assert.IsFalse(rangedCell.CanDeploy(OperatorPosition.Melee));
            Assert.IsTrue(rangedCell.CanDeploy(OperatorPosition.Ranged));

            // LowGround (melee deployable / walkable)
            var pathCell = gridManager.GetCell(4, 3);
            Assert.AreEqual(TileType.LowGround, pathCell.TileType);
            Assert.IsTrue(pathCell.CanDeploy(OperatorPosition.Melee));
            Assert.IsFalse(pathCell.CanDeploy(OperatorPosition.Ranged));
            Assert.IsTrue(pathCell.IsWalkable);

            Object.DestroyImmediate(stage);
        }

        [Test]
        public void Stage2_PathsAndWaves_AreCorrectlyConfigured()
        {
            var stage = Resources.Load<StageData>("Stages/STAGE_02");
            Assert.IsNotNull(stage, "STAGE_02 resource asset must be loadable");
            Assert.IsNotNull(stage.foregroundVisualSprite, "Stage 2 foregroundVisualSprite must be assigned");

            gridManager.InitializeFromStageData(stage);

            // Paths verification
            Assert.AreEqual(2, stage.enemyPaths.Length, "Stage 2 must have exactly 2 designated paths");

            // Path 0: Top-Right (10, 6) -> Right Exit (10, 3)
            var path0 = stage.enemyPaths[0];
            Assert.AreEqual(0, path0.spawnPointIndex);
            Assert.AreEqual(1, path0.exitPointIndex);
            Assert.AreEqual(new Vector2Int(10, 6), path0.waypoints[0]);
            Assert.AreEqual(new Vector2Int(10, 3), path0.waypoints[path0.waypoints.Length - 1]);
            foreach (var wp in path0.waypoints)
            {
                var cell = gridManager.GetCell(wp.x, wp.y);
                Assert.IsNotNull(cell, $"Cell at waypoint {wp} must exist");
                Assert.IsTrue(cell.IsWalkable, $"Cell at waypoint {wp} must be walkable");
            }

            // Path 1: Bottom-Left (0, 0) -> Left Exit (0, 3)
            var path1 = stage.enemyPaths[1];
            Assert.AreEqual(1, path1.spawnPointIndex);
            Assert.AreEqual(0, path1.exitPointIndex);
            Assert.AreEqual(new Vector2Int(0, 0), path1.waypoints[0]);
            Assert.AreEqual(new Vector2Int(0, 3), path1.waypoints[path1.waypoints.Length - 1]);
            foreach (var wp in path1.waypoints)
            {
                var cell = gridManager.GetCell(wp.x, wp.y);
                Assert.IsNotNull(cell, $"Cell at waypoint {wp} must exist");
                Assert.IsTrue(cell.IsWalkable, $"Cell at waypoint {wp} must be walkable");
            }

            // Wave counts
            Assert.AreEqual(10, stage.wavesEasy.Length, "Easy must have 10 waves");
            Assert.AreEqual(20, stage.wavesNormal.Length, "Normal must have 20 waves");
            Assert.AreEqual(30, stage.wavesHard.Length, "Hard must have 30 waves");

            // Verify enemy distribution across both spawn points in all difficulties
            void VerifyWaveDistribution(WaveData[] waves, string difficultyName)
            {
                for (int i = 0; i < waves.Length; i++)
                {
                    var wave = waves[i];
                    Assert.IsNotNull(wave.entries, $"{difficultyName} Wave {i + 1} must have entries");
                    Assert.IsTrue(wave.entries.Length >= 2, $"{difficultyName} Wave {i + 1} must distribute across both lanes");

                    bool hasSpawn0 = false;
                    bool hasSpawn1 = false;
                    foreach (var entry in wave.entries)
                    {
                        Assert.IsNotNull(entry.enemyData, $"{difficultyName} Wave {i + 1} entry must have valid EnemyData");
                        Assert.IsTrue(entry.count > 0, $"{difficultyName} Wave {i + 1} entry must have count > 0");
                        if (entry.spawnPointIndex == 0) hasSpawn0 = true;
                        if (entry.spawnPointIndex == 1) hasSpawn1 = true;
                    }

                    Assert.IsTrue(hasSpawn0, $"{difficultyName} Wave {i + 1} must spawn enemies from spawn 0 (Top-Right)");
                    Assert.IsTrue(hasSpawn1, $"{difficultyName} Wave {i + 1} must spawn enemies from spawn 1 (Bottom-Left)");
                }
            }

            VerifyWaveDistribution(stage.wavesEasy, "Easy");
            VerifyWaveDistribution(stage.wavesNormal, "Normal");
            VerifyWaveDistribution(stage.wavesHard, "Hard");
        }

        [Test]
        public void Stage3_RailyardCrossing_LayoutTrapsAndPathsAreCorrectlyConfigured()
        {
            var stage = Resources.Load<StageData>("Stages/STAGE_03");
            Assert.IsNotNull(stage, "STAGE_03 resource asset must be loadable");
            Assert.AreEqual(14, stage.gridWidth);
            Assert.AreEqual(7, stage.gridHeight);
            Assert.AreEqual(14 * 7, stage.tileLayout.Length);
            Assert.AreEqual(32, stage.visualTilePixelSize);
            Assert.IsNotNull(stage.mapVisualSprite);
            Assert.IsNotNull(stage.backgroundVisualSprite);
            Assert.IsNotNull(stage.upperBackgroundVisualSprites);
            Assert.IsNotEmpty(stage.upperBackgroundVisualSprites);
            foreach (var upperLayer in stage.upperBackgroundVisualSprites)
            {
                Assert.IsNotNull(upperLayer);
            }

            CollectionAssert.AreEqual(
                new[] { new Vector2Int(0, 4), new Vector2Int(0, 1) },
                stage.spawnPoints);
            CollectionAssert.AreEqual(
                new[] { new Vector2Int(13, 0) },
                stage.exitPoints);
            Assert.AreEqual(2, stage.enemyPaths.Length);
            Assert.IsNotEmpty(stage.wavesEasy);
            Assert.IsNotEmpty(stage.wavesNormal);
            Assert.IsNotEmpty(stage.wavesHard);
            foreach (var difficulty in new[] { StageDifficulty.Easy, StageDifficulty.Normal, StageDifficulty.Hard })
            {
                foreach (var wave in stage.GetWaves(difficulty))
                {
                    Assert.IsNotEmpty(wave.entries);
                    foreach (var entry in wave.entries)
                    {
                        Assert.IsNotNull(entry.enemyData);
                        Assert.That(entry.spawnPointIndex, Is.InRange(0, stage.spawnPoints.Length - 1));
                    }
                }
            }

            gridManager.InitializeFromStageData(stage);

            foreach (var path in stage.enemyPaths)
            {
                Assert.AreEqual(stage.spawnPoints[path.spawnPointIndex], path.waypoints[0]);
                Assert.AreEqual(stage.exitPoints[path.exitPointIndex], path.waypoints[path.waypoints.Length - 1]);
                foreach (var waypoint in path.waypoints)
                {
                    Assert.IsTrue(gridManager.GetCell(waypoint).IsWalkable, $"Path tile {waypoint} must be walkable");
                }
            }

            foreach (var trapPosition in new[] { new Vector2Int(3, 0), new Vector2Int(3, 1) })
            {
                var trap = gridManager.GetCell(trapPosition);
                Assert.AreEqual(TileType.Trap, trap.TileType);
                Assert.IsTrue(trap.IsWalkable);
                Assert.IsTrue(trap.CanDeploy(OperatorPosition.Melee));
                Assert.IsTrue(trap.CanDeploy(OperatorPosition.Ranged));
            }

            var blockedCell = gridManager.GetCell(6, 0);
            Assert.AreEqual(TileType.Blocked, blockedCell.TileType);
            Assert.IsFalse(blockedCell.IsWalkable);
            Assert.IsFalse(blockedCell.CanDeploy(OperatorPosition.Melee));
            Assert.IsFalse(blockedCell.CanDeploy(OperatorPosition.Ranged));

            var upperRouteGap = gridManager.GetCell(11, 2);
            Assert.AreEqual(TileType.Blocked, upperRouteGap.TileType);
            Assert.IsFalse(upperRouteGap.IsWalkable);
            Assert.IsFalse(upperRouteGap.CanDeploy(OperatorPosition.Melee));
            Assert.IsFalse(upperRouteGap.CanDeploy(OperatorPosition.Ranged));
            CollectionAssert.Contains(stage.enemyPaths[0].waypoints, new Vector2Int(12, 3));
            CollectionAssert.Contains(stage.enemyPaths[0].waypoints, new Vector2Int(12, 2));
            CollectionAssert.DoesNotContain(stage.enemyPaths[0].waypoints, new Vector2Int(11, 2));
        }
    }
}