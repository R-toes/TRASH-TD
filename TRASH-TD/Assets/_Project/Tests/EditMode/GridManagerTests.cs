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
    }
}