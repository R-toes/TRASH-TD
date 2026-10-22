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
            playerDeck = TestUtility.AddAwakened<PlayerDeck>(deckObject);

            operatorManagerObject = new GameObject("TestOperatorManager");
            operatorManager = TestUtility.AddAwakened<OperatorManager>(operatorManagerObject);

            operatorData = ScriptableObject.CreateInstance<OperatorData>();
            operatorData.operatorName = "TestSniper";
            operatorData.operatorClass = OperatorClass.Sniper;
            operatorData.position = OperatorPosition.Ranged;
        }

        [TearDown]
        public void TearDown()
        {
            TestUtility.ClearSingletons();
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

            TestUtility.ExpectEditModeDestroy();
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
        public void DeployedCopy_CountsTowardMerge_AndIsUpgradedInPlace()
        {
            var upgradeObject = new GameObject("TestUpgradeSystem");
            TestUtility.AddAwakened<RarityUpgradeSystem>(upgradeObject);
            try
            {
                var fieldCopy = new DraftCard(operatorData, OperatorRarity.Star1);
                playerDeck.AddCard(fieldCopy);
                Assert.IsTrue(operatorManager.TryDeployOperator(
                    fieldCopy, new Vector2Int(0, 0), OperatorFacing.Right, out deployedOperator));
                playerDeck.RemoveCard(fieldCopy);
                int star1MaxHP = deployedOperator.MaxHP;

                playerDeck.AddCard(new DraftCard(operatorData, OperatorRarity.Star1));
                playerDeck.AddCard(new DraftCard(operatorData, OperatorRarity.Star1));

                Assert.AreEqual(OperatorRarity.Star2, deployedOperator.CurrentRarity);
                Assert.Greater(deployedOperator.MaxHP, star1MaxHP);
                Assert.AreEqual(0, playerDeck.CardCount);
                Assert.AreEqual(1, operatorManager.DeployedCount);
            }
            finally
            {
                Object.DestroyImmediate(upgradeObject);
            }
        }

        [Test]
        public void TwoDeployedCopies_MergeUsesOnlyOne_AndNothingLeavesTheField()
        {
            var upgradeObject = new GameObject("TestUpgradeSystem");
            TestUtility.AddAwakened<RarityUpgradeSystem>(upgradeObject);
            try
            {
                var firstCard = new DraftCard(operatorData, OperatorRarity.Star1);
                playerDeck.AddCard(firstCard);
                Assert.IsTrue(operatorManager.TryDeployOperator(
                    firstCard, new Vector2Int(0, 0), OperatorFacing.Right, out deployedOperator));
                playerDeck.RemoveCard(firstCard);

                var secondCard = new DraftCard(operatorData, OperatorRarity.Star1);
                playerDeck.AddCard(secondCard);
                Assert.IsTrue(operatorManager.TryDeployOperator(
                    secondCard, new Vector2Int(1, 0), OperatorFacing.Right, out retreatedOperator));
                playerDeck.RemoveCard(secondCard);

                // One card plus one field copy is only two copies: no merge yet.
                playerDeck.AddCard(new DraftCard(operatorData, OperatorRarity.Star1));
                Assert.AreEqual(1, playerDeck.CardCount);

                playerDeck.AddCard(new DraftCard(operatorData, OperatorRarity.Star1));

                Assert.AreEqual(2, operatorManager.DeployedCount);
                Assert.AreEqual(0, playerDeck.CardCount);
                Assert.AreEqual(OperatorRarity.Star2, deployedOperator.CurrentRarity);
                Assert.AreEqual(OperatorRarity.Star1, retreatedOperator.CurrentRarity);
            }
            finally
            {
                Object.DestroyImmediate(upgradeObject);
            }
        }

        [Test]
        public void RetreatedOperator_RedeploysWithItsSavedHP()
        {
            var card = new DraftCard(operatorData, OperatorRarity.Star1);
            playerDeck.AddCard(card);
            Assert.IsTrue(operatorManager.TryDeployOperator(
                card, new Vector2Int(0, 0), OperatorFacing.Right, out retreatedOperator));
            playerDeck.RemoveCard(card);

            retreatedOperator.TakeTrueDamage(37);
            int woundedHP = retreatedOperator.CurrentHP;
            Assert.Less(woundedHP, retreatedOperator.MaxHP);

            TestUtility.ExpectEditModeDestroy();
            operatorManager.RetreatOperator(retreatedOperator);

            DraftCard returnedCard = FindFirstDeckCard();
            Assert.IsNotNull(returnedCard);
            Assert.AreEqual(woundedHP, returnedCard.savedHP);

            returnedCard.cooldownRoundsRemaining = 0;
            Assert.IsTrue(operatorManager.TryDeployOperator(
                returnedCard, new Vector2Int(1, 0), OperatorFacing.Right, out deployedOperator));
            Assert.AreEqual(woundedHP, deployedOperator.CurrentHP);
        }

        [Test]
        public void FreshCard_DeploysAtFullHP()
        {
            var card = new DraftCard(operatorData, OperatorRarity.Star1);
            Assert.IsFalse(card.HasSavedHP);

            Assert.IsTrue(operatorManager.TryDeployOperator(
                card, new Vector2Int(0, 0), OperatorFacing.Right, out deployedOperator));
            Assert.AreEqual(deployedOperator.MaxHP, deployedOperator.CurrentHP);
        }

        [Test]
        public void MergingWoundedCopies_KeepsTheLowestHPFraction()
        {
            var upgradeObject = new GameObject("TestUpgradeSystem");
            TestUtility.AddAwakened<RarityUpgradeSystem>(upgradeObject);
            try
            {
                int star1MaxHP = operatorData.GetScaledHP(OperatorRarity.Star1);
                playerDeck.AddCard(new DraftCard(operatorData, OperatorRarity.Star1, 0, 0, star1MaxHP / 4));
                playerDeck.AddCard(new DraftCard(operatorData, OperatorRarity.Star1));
                playerDeck.AddCard(new DraftCard(operatorData, OperatorRarity.Star1));

                Assert.AreEqual(1, playerDeck.CardCount);
                DraftCard merged = FindFirstDeckCard();
                Assert.AreEqual(OperatorRarity.Star2, merged.rarity);
                Assert.AreEqual(0.25f, merged.GetHealthRatio(), 0.01f);
            }
            finally
            {
                Object.DestroyImmediate(upgradeObject);
            }
        }

        private DraftCard FindFirstDeckCard()
        {
            foreach (DraftCard card in playerDeck.DeckSlots)
            {
                if (card != null) return card;
            }
            return null;
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
