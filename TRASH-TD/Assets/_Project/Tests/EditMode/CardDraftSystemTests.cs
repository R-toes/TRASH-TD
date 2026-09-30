using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using TrashTD.Data;
using TrashTD.Systems;

namespace TrashTD.Tests
{
    public class CardDraftSystemTests
    {
        private GameObject draftObject;
        private CardDraftSystem draftSystem;
        private List<OperatorData> testPool;

        [SetUp]
        public void SetUp()
        {
            draftObject = new GameObject("TestDraftSystem");
            draftSystem = draftObject.AddComponent<CardDraftSystem>();

            testPool = new List<OperatorData>();
            // Create 1 operator of each of the 5 classes
            var classes = new[]
            {
                OperatorClass.Guard,
                OperatorClass.Defender,
                OperatorClass.Sniper,
                OperatorClass.Caster,
                OperatorClass.Medic
            };

            foreach (var opClass in classes)
            {
                var op = ScriptableObject.CreateInstance<OperatorData>();
                op.operatorName = $"Test_{opClass}";
                op.operatorClass = opClass;
                op.baseRarity = OperatorRarity.Star1;
                testPool.Add(op);
            }

            draftSystem.SetOperatorPool(testPool);
        }

        [TearDown]
        public void TearDown()
        {
            if (draftObject != null)
            {
                Object.DestroyImmediate(draftObject);
            }
        }

        [Test]
        public void GenerateDraftOffer_ProducesExactlyThreeCards()
        {
            var offer = draftSystem.GenerateDraftOffer();
            Assert.AreEqual(3, offer.Count, "Draft offer must consist of exactly 3 cards (GDD 1.6)");
        }

        [Test]
        public void GenerateDraftOffer_HasNoDuplicateClasses()
        {
            // Repeat several times to test random rolls
            for (int trial = 0; trial < 10; trial++)
            {
                var offer = draftSystem.GenerateDraftOffer();
                var seenClasses = new HashSet<OperatorClass>();

                foreach (var card in offer)
                {
                    Assert.IsFalse(seenClasses.Contains(card.operatorData.operatorClass),
                        $"Duplicate class {card.operatorData.operatorClass} found in draft offer (GDD 1.6: no two cards share same class)");
                    seenClasses.Add(card.operatorData.operatorClass);
                }
            }
        }

        [Test]
        public void GenerateDraftOffer_OnlyOffersOneToThreeStarRarities()
        {
            for (int trial = 0; trial < 20; trial++)
            {
                var offer = draftSystem.GenerateDraftOffer();
                foreach (var card in offer)
                {
                    int starInt = (int)card.rarity;
                    Assert.IsTrue(starInt >= 1 && starInt <= 3,
                        $"Draft offered rarity {card.rarity}, but cards only ever offer 1★, 2★, or 3★ base creatures (GDD 1.6)");
                }
            }
        }

        [Test]
        public void GenerateDraftOffer_UsesOperatorConfiguredRarity()
        {
            var operatorData = testPool[0];
            operatorData.baseRarity = OperatorRarity.Star2;
            draftSystem.SetOperatorPool(new[] { operatorData });

            var offer = draftSystem.GenerateDraftOffer();

            Assert.AreEqual(1, offer.Count);
            Assert.AreSame(operatorData, offer[0].operatorData);
            Assert.AreEqual(operatorData.baseRarity, offer[0].rarity);
        }

        [Test]
        public void RerollOffer_StartsAtThree_DecrementsOnEachReroll()
        {
            draftSystem.ResetForNewStage();
            Assert.AreEqual(3, draftSystem.RerollsRemaining, "Player should start with 3 rerolls");

            var firstOffer = draftSystem.GenerateDraftOffer();
            Assert.AreEqual(3, firstOffer.Count);

            var reroll1 = draftSystem.RerollOffer();
            Assert.IsNotNull(reroll1);
            Assert.AreEqual(3, reroll1.Count);
            Assert.AreEqual(2, draftSystem.RerollsRemaining);

            var reroll2 = draftSystem.RerollOffer();
            Assert.IsNotNull(reroll2);
            Assert.AreEqual(1, draftSystem.RerollsRemaining);

            var reroll3 = draftSystem.RerollOffer();
            Assert.IsNotNull(reroll3);
            Assert.AreEqual(0, draftSystem.RerollsRemaining);

            // 4th reroll should fail
            var reroll4 = draftSystem.RerollOffer();
            Assert.IsNull(reroll4, "Reroll when exhausted should return null");
            Assert.AreEqual(0, draftSystem.RerollsRemaining);
        }

        [Test]
        public void PlayerDeck_AddUpToEightCards_AndRejectNinth()
        {
            var deckObj = new GameObject("TestDeck");
            var deck = deckObj.AddComponent<PlayerDeck>();

            var dummyOp = ScriptableObject.CreateInstance<OperatorData>();
            dummyOp.operatorName = "Dummy";

            for (int i = 0; i < 8; i++)
            {
                var card = new DraftCard(dummyOp, OperatorRarity.Star1);
                bool added = deck.AddCard(card);
                Assert.IsTrue(added, $"Card {i + 1} should be successfully added");
            }

            Assert.AreEqual(8, deck.CardCount);
            Assert.IsTrue(deck.IsFull);

            // 9th card should fail
            var ninthCard = new DraftCard(dummyOp, OperatorRarity.Star2);
            bool ninthAdded = deck.AddCard(ninthCard);
            Assert.IsFalse(ninthAdded, "9th card should be rejected because max deck size is 8");
            Assert.AreEqual(8, deck.CardCount);

            // Removing a card allows adding again
            deck.RemoveCard(ninthCard); // Not in deck
            Assert.AreEqual(8, deck.CardCount);

            var firstCard = deck.GetCard(0);
            bool removed = deck.RemoveCard(firstCard);
            Assert.IsTrue(removed);
            Assert.AreEqual(7, deck.CardCount);
            Assert.IsFalse(deck.IsFull);

            bool canAddAgain = deck.AddCard(ninthCard);
            Assert.IsTrue(canAddAgain);
            Assert.AreEqual(8, deck.CardCount);

            Object.DestroyImmediate(deckObj);
        }

        [Test]
        public void PlayerDeck_MoveCardIntoEmptySlot_PreservesSlotAndReusesVacatedSlot()
        {
            var deckObject = new GameObject("TestDeckSlots");
            var deck = deckObject.AddComponent<PlayerDeck>();
            var firstCard = new DraftCard(null, OperatorRarity.Star1);
            var secondCard = new DraftCard(null, OperatorRarity.Star1);
            var thirdCard = new DraftCard(null, OperatorRarity.Star1);

            Assert.IsTrue(deck.AddCard(firstCard));
            Assert.IsTrue(deck.AddCard(secondCard));
            Assert.IsTrue(deck.MoveCard(0, 5));

            Assert.IsNull(deck.GetCard(0));
            Assert.AreSame(secondCard, deck.GetCard(1));
            Assert.AreSame(firstCard, deck.GetCard(5));
            Assert.AreEqual(2, deck.CardCount);

            Assert.IsTrue(deck.AddCard(thirdCard));
            Assert.AreSame(thirdCard, deck.GetCard(0));
            Assert.AreEqual(3, deck.CardCount);

            Object.DestroyImmediate(deckObject);
        }
    }
}
