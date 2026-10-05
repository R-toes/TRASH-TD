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
    }
}