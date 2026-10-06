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
    }
}