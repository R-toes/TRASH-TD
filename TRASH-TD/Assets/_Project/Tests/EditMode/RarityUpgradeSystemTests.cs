using NUnit.Framework;
using UnityEngine;
using TrashTD.Data;
using TrashTD.Systems;

namespace TrashTD.Tests
{
    public class RarityUpgradeSystemTests
    {
        private GameObject upgradeObject;
        private GameObject deckObject;
        private RarityUpgradeSystem upgradeSystem;
        private PlayerDeck playerDeck;
        private OperatorData testOperator;

        [SetUp]
        public void SetUp()
        {
            upgradeObject = new GameObject("TestUpgradeSystem");
            upgradeSystem = TestUtility.AddAwakened<RarityUpgradeSystem>(upgradeObject);
            deckObject = new GameObject("TestPlayerDeck");
            playerDeck = TestUtility.AddAwakened<PlayerDeck>(deckObject);

            testOperator = ScriptableObject.CreateInstance<OperatorData>();
            testOperator.operatorName = "TrashBot";
            testOperator.baseRarity = OperatorRarity.Star1;
        }

        [TearDown]
        public void TearDown()
        {
            TestUtility.ClearSingletons();
            if (upgradeObject != null)
            {
                Object.DestroyImmediate(upgradeObject);
            }
            if (deckObject != null)
            {
                Object.DestroyImmediate(deckObject);
            }
            if (testOperator != null)
            {
                Object.DestroyImmediate(testOperator);
            }
        }

        [Test]
        public void ThreeDuplicates_UpgradesRarityByOneTier()
        {
            bool upgradeFired = false;
            OperatorRarity upgradedTo = OperatorRarity.Star1;

            upgradeSystem.OnOperatorUpgraded += (op, oldR, newR) =>
            {
                upgradeFired = true;
                upgradedTo = newR;
            };

            playerDeck.AddCard(new DraftCard(testOperator, OperatorRarity.Star1));
            Assert.IsFalse(upgradeFired);
            Assert.AreEqual(1, upgradeSystem.GetCopyCount(testOperator.operatorName, OperatorRarity.Star1));

            playerDeck.AddCard(new DraftCard(testOperator, OperatorRarity.Star1));
            Assert.IsFalse(upgradeFired);
            Assert.AreEqual(2, upgradeSystem.GetCopyCount(testOperator.operatorName, OperatorRarity.Star1));

            playerDeck.AddCard(new DraftCard(testOperator, OperatorRarity.Star1));
            Assert.IsTrue(upgradeFired, "Collecting 3 duplicate copies must upgrade rarity by one tier (GDD 1.6)");
            Assert.AreEqual(OperatorRarity.Star2, upgradedTo);
            Assert.AreEqual(0, upgradeSystem.GetCopyCount(testOperator.operatorName, OperatorRarity.Star1));
            Assert.AreEqual(1, upgradeSystem.GetCopyCount(testOperator.operatorName, OperatorRarity.Star2));
            Assert.AreEqual(OperatorRarity.Star2, upgradeSystem.GetHighestRarity(testOperator));
            Assert.AreEqual(1, playerDeck.CardCount);
            Assert.Greater(testOperator.GetScaledHP(OperatorRarity.Star2), testOperator.GetScaledHP(OperatorRarity.Star1));
            Assert.Greater(testOperator.GetScaledATK(OperatorRarity.Star2), testOperator.GetScaledATK(OperatorRarity.Star1));
            Assert.Greater(testOperator.GetScaledDEF(OperatorRarity.Star2), testOperator.GetScaledDEF(OperatorRarity.Star1));
            Assert.Greater(testOperator.GetScaledRES(OperatorRarity.Star2), testOperator.GetScaledRES(OperatorRarity.Star1));
        }

        [Test]
        public void NineOneStarCopies_CascadeIntoOneThreeStarCard()
        {
            for (int i = 0; i < 9; i++)
            {
                playerDeck.AddCard(new DraftCard(testOperator, OperatorRarity.Star1));
            }

            Assert.AreEqual(OperatorRarity.Star3, upgradeSystem.GetHighestRarity(testOperator));
            Assert.AreEqual(1, upgradeSystem.GetCopyCount(testOperator.operatorName, OperatorRarity.Star3));
            Assert.AreEqual(0, upgradeSystem.GetCopyCount(testOperator.operatorName, OperatorRarity.Star2));
            Assert.AreEqual(0, upgradeSystem.GetCopyCount(testOperator.operatorName, OperatorRarity.Star1));
            Assert.AreEqual(1, playerDeck.CardCount);
        }

        [Test]
        public void UpgradesStopTwoTiersAboveBaseRarity()
        {
            testOperator.baseRarity = OperatorRarity.Star2;
            for (int i = 0; i < 27; i++)
            {
                playerDeck.AddCard(new DraftCard(testOperator, OperatorRarity.Star2));
            }

            Assert.AreEqual(3, upgradeSystem.GetCopyCount(testOperator.operatorName, OperatorRarity.Star4));
            Assert.AreEqual(0, upgradeSystem.GetCopyCount(testOperator.operatorName, OperatorRarity.Star5));
            Assert.AreEqual(OperatorRarity.Star4, upgradeSystem.GetHighestRarity(testOperator));
        }

        [Test]
        public void RecalledCopy_MergesImmediately_AndUpgradedCardKeepsTheRecallCooldown()
        {
            playerDeck.AddCard(new DraftCard(testOperator, OperatorRarity.Star1));
            playerDeck.AddCard(new DraftCard(testOperator, OperatorRarity.Star1));
            playerDeck.AddReturnedCard(new DraftCard(testOperator, OperatorRarity.Star1, 2)); // returned by a recall

            Assert.AreEqual(1, playerDeck.CardCount, "A recalled copy merges with the two in the deck.");
            DraftCard upgraded = FindCard(OperatorRarity.Star2);
            Assert.IsNotNull(upgraded);
            Assert.AreEqual(2, upgraded.cooldownRoundsRemaining, "The upgrade still serves the recall's normal cooldown.");
        }

        [Test]
        public void RecalledCopy_MergesIntoAFullDeck_AndKeepsTheRecallCooldown()
        {
            var fillers = new System.Collections.Generic.List<OperatorData>();
            for (int i = 0; i < PlayerDeck.MAX_DECK_SIZE - 2; i++)
            {
                OperatorData filler = ScriptableObject.CreateInstance<OperatorData>();
                filler.operatorName = $"Filler{i}";
                fillers.Add(filler);
                playerDeck.AddCard(new DraftCard(filler, OperatorRarity.Star1));
            }
            playerDeck.AddCard(new DraftCard(testOperator, OperatorRarity.Star1));
            playerDeck.AddCard(new DraftCard(testOperator, OperatorRarity.Star1));
            Assert.IsTrue(playerDeck.IsFull);

            playerDeck.AddReturnedCard(new DraftCard(testOperator, OperatorRarity.Star1, 1));

            Assert.AreEqual(PlayerDeck.MAX_DECK_SIZE - 1, playerDeck.CardCount);
            Assert.AreEqual(1, FindCard(OperatorRarity.Star2).cooldownRoundsRemaining);

            foreach (OperatorData filler in fillers) Object.DestroyImmediate(filler);
        }

        private DraftCard FindCard(OperatorRarity rarity)
        {
            foreach (DraftCard card in playerDeck.DeckSlots)
                if (card != null && card.operatorData == testOperator && card.rarity == rarity) return card;
            return null;
        }

        [Test]
        public void FullDeck_AcceptsOnlyACardThatMergesOnArrival()
        {
            var fillers = new System.Collections.Generic.List<OperatorData>();
            for (int i = 0; i < PlayerDeck.MAX_DECK_SIZE - 2; i++)
            {
                OperatorData filler = ScriptableObject.CreateInstance<OperatorData>();
                filler.operatorName = $"Filler{i}";
                fillers.Add(filler);
                playerDeck.AddCard(new DraftCard(filler, OperatorRarity.Star1));
            }
            playerDeck.AddCard(new DraftCard(testOperator, OperatorRarity.Star1));
            playerDeck.AddCard(new DraftCard(testOperator, OperatorRarity.Star1));
            Assert.IsTrue(playerDeck.IsFull);

            var nonMerging = new DraftCard(fillers[0], OperatorRarity.Star1);
            Assert.IsFalse(playerDeck.CanAcceptCard(nonMerging));
            Assert.IsFalse(playerDeck.AddCard(nonMerging));

            var merging = new DraftCard(testOperator, OperatorRarity.Star1);
            Assert.IsTrue(playerDeck.CanAcceptCard(merging));
            Assert.IsTrue(playerDeck.AddCard(merging));
            Assert.AreEqual(PlayerDeck.MAX_DECK_SIZE - 1, playerDeck.CardCount);
            Assert.AreEqual(1, upgradeSystem.GetCopyCount(testOperator.operatorName, OperatorRarity.Star2));

            foreach (OperatorData filler in fillers) Object.DestroyImmediate(filler);
        }

        [Test]
        public void DifferentOperatorsAndRarities_DoNotMerge()
        {
            OperatorData otherOperator = ScriptableObject.CreateInstance<OperatorData>();
            otherOperator.operatorName = "OtherBot";
            playerDeck.AddCard(new DraftCard(testOperator, OperatorRarity.Star1));
            playerDeck.AddCard(new DraftCard(testOperator, OperatorRarity.Star2));
            playerDeck.AddCard(new DraftCard(otherOperator, OperatorRarity.Star1));

            Assert.AreEqual(3, playerDeck.CardCount);
            Assert.AreEqual(1, upgradeSystem.GetCopyCount(testOperator.operatorName, OperatorRarity.Star1));
            Object.DestroyImmediate(otherOperator);
        }
    }
}
