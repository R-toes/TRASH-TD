using NUnit.Framework;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Tests
{
    public class OperatorManagerCooldownTests
    {
        private GameObject gridObject;
        private GameObject deckObject;
        private GameObject operatorManagerObject;
        private GridManager gridManager;
        private PlayerDeck playerDeck;
        private OperatorManager operatorManager;
        private StageData stageData;
        private OperatorData operatorData;
        private OperatorBase retreatedOperator;
        private OperatorBase deployedOperator;

        [SetUp]
        public void SetUp()
        {
            gridObject = new GameObject("TestGridManager");
            gridManager = gridObject.AddComponent<GridManager>();
            stageData = ScriptableObject.CreateInstance<StageData>();
            stageData.gridWidth = 2;
            stageData.gridHeight = 1;
            stageData.tileLayout = new[] { TileType.HighGround, TileType.HighGround };
            gridManager.InitializeFromStageData(stageData);

            deckObject = new GameObject("TestPlayerDeck");
            playerDeck = deckObject.AddComponent<PlayerDeck>();

            operatorManagerObject = new GameObject("TestOperatorManager");
            operatorManager = operatorManagerObject.AddComponent<OperatorManager>();

            operatorData = ScriptableObject.CreateInstance<OperatorData>();
            operatorData.operatorName = "TestSniper";
            operatorData.operatorClass = OperatorClass.Sniper;
            operatorData.position = OperatorPosition.Ranged;
        }

        [TearDown]
        public void TearDown()
        {
            if (retreatedOperator != null) Object.DestroyImmediate(retreatedOperator.gameObject);
            if (deployedOperator != null) Object.DestroyImmediate(deployedOperator.gameObject);
            if (operatorManagerObject != null) Object.DestroyImmediate(operatorManagerObject);
            if (deckObject != null) Object.DestroyImmediate(deckObject);
            if (gridObject != null) Object.DestroyImmediate(gridObject);
            if (stageData != null) Object.DestroyImmediate(stageData);
            if (operatorData != null) Object.DestroyImmediate(operatorData);
        }

        [Test]
        public void RetreatedCopyCooldown_DoesNotBlockReadyCopyOfSameOperator()
        {
            var retreatedCopy = new DraftCard(operatorData, OperatorRarity.Star1);
            var readyCopy = new DraftCard(operatorData, OperatorRarity.Star1);
            playerDeck.AddCard(retreatedCopy);
            playerDeck.AddCard(readyCopy);

            Assert.IsTrue(operatorManager.TryDeployOperator(
                retreatedCopy,
                new Vector2Int(0, 0),
                OperatorFacing.Right,
                out retreatedOperator));
            playerDeck.RemoveCard(retreatedCopy);

            operatorManager.RetreatOperator(retreatedOperator);

            DraftCard coolingCopy = null;
            foreach (DraftCard card in playerDeck.DeckSlots)
            {
                if (card != null && card != readyCopy)
                {
                    coolingCopy = card;
                    break;
                }
            }

            Assert.IsNotNull(coolingCopy);
            Assert.AreEqual(1, coolingCopy.cooldownRoundsRemaining);
            Assert.AreEqual(0, readyCopy.cooldownRoundsRemaining);
            Assert.IsTrue(operatorManager.TryDeployOperator(
                readyCopy,
                new Vector2Int(1, 0),
                OperatorFacing.Right,
                out deployedOperator));
        }

        [Test]
        public void TryDeployOperator_RejectsTheCopyThatIsOnCooldown()
        {
            var coolingCopy = new DraftCard(operatorData, OperatorRarity.Star1, 1);

            Assert.IsFalse(operatorManager.TryDeployOperator(
                coolingCopy,
                new Vector2Int(0, 0),
                OperatorFacing.Right,
                out deployedOperator));
            Assert.IsNull(deployedOperator);
        }
    }
}
