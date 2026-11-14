using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using TrashTD.Data;
using TrashTD.Operators;
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
            TestUtility.ClearSingletons();
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
            draftSystem.GenerateDraftOffer(); // the opening draft is 1★ only, so it offers nothing here

            var offer = draftSystem.GenerateDraftOffer();

            Assert.AreEqual(1, offer.Count);
            Assert.AreSame(operatorData, offer[0].operatorData);
            Assert.AreEqual(operatorData.baseRarity, offer[0].rarity);
        }

        [Test]
        public void GenerateDraftOffer_OpeningDraftHasNoMedicsOrSpecialists()
        {
            var specialist = ScriptableObject.CreateInstance<OperatorData>();
            specialist.operatorName = "Test_Specialist";
            specialist.operatorClass = OperatorClass.Specialist;
            specialist.baseRarity = OperatorRarity.Star1;
            testPool.Add(specialist);
            draftSystem.SetOperatorPool(testPool);

            for (int trial = 0; trial < 30; trial++)
            {
                draftSystem.ResetForNewStage();
                var offer = draftSystem.GenerateDraftOffer();
                Assert.AreEqual(3, offer.Count, "The opening draft still offers 3 cards");
                foreach (var card in offer)
                {
                    Assert.IsFalse(CardDraftSystem.IsExcludedFromOpeningDraft(card.operatorData.operatorClass),
                        $"Opening draft offered a {card.operatorData.operatorClass}");
                }
            }

            // From the second draft on, Medics and Specialists can be offered again
            draftSystem.ResetForNewStage();
            draftSystem.GenerateDraftOffer();
            bool sawExcludedClass = false;
            for (int trial = 0; trial < 50 && !sawExcludedClass; trial++)
            {
                foreach (var card in draftSystem.GenerateDraftOffer())
                {
                    if (CardDraftSystem.IsExcludedFromOpeningDraft(card.operatorData.operatorClass)) sawExcludedClass = true;
                }
            }
            Assert.IsTrue(sawExcludedClass, "Later drafts should be able to offer Medics and Specialists");

            Object.DestroyImmediate(specialist);
        }

        private static void AssertOdds(DraftOdds odds, float star1, float star2, float star3, string context)
        {
            Assert.AreEqual(star1, odds.Star1, 0.01f, $"1★ odds {context}");
            Assert.AreEqual(star2, odds.Star2, 0.01f, $"2★ odds {context}");
            Assert.AreEqual(star3, odds.Star3, 0.01f, $"3★ odds {context}");
        }

        [Test]
        public void DraftOddsTable_Default_StepsUpEveryFiveWavesCleared()
        {
            DraftOddsTable table = DraftOddsTable.GetBuiltInDefault();

            AssertOdds(table.GetOdds(0), 82f, 16f, 2f, "after 0 waves");
            AssertOdds(table.GetOdds(4), 82f, 16f, 2f, "after 4 waves");
            AssertOdds(table.GetOdds(5), 76f, 20f, 4f, "after 5 waves");
            AssertOdds(table.GetOdds(9), 76f, 20f, 4f, "after 9 waves");
            AssertOdds(table.GetOdds(10), 70f, 24f, 6f, "after 10 waves");
            AssertOdds(table.GetOdds(15), 64f, 28f, 8f, "after 15 waves");
            AssertOdds(table.GetOdds(20), 58f, 32f, 10f, "after 20 waves");
            AssertOdds(table.GetOdds(25), 52f, 36f, 12f, "after 25 waves");
            AssertOdds(table.GetOdds(40), 52f, 36f, 12f, "after 40 waves (last step holds)");
        }

        [Test]
        public void DraftOddsTable_Default_HigherRaritiesNeverDrop_AndOneStarStaysMostLikely()
        {
            DraftOddsTable table = DraftOddsTable.GetBuiltInDefault();
            DraftOdds previous = table.GetOdds(0);
            for (int cleared = 1; cleared <= 30; cleared++)
            {
                DraftOdds odds = table.GetOdds(cleared);
                Assert.GreaterOrEqual(odds.Star3, previous.Star3, $"3★ odds dropped after {cleared} waves");
                Assert.GreaterOrEqual(odds.Star2 + odds.Star3, previous.Star2 + previous.Star3,
                    $"2★+ odds dropped after {cleared} waves");
                Assert.Greater(odds.Star1, odds.Star2, $"1★ must stay the most likely after {cleared} waves");
                Assert.Greater(odds.Star1, odds.Star3, $"1★ must stay the most likely after {cleared} waves");
                previous = odds;
            }
        }

        [Test]
        public void DraftOddsTable_Default_ReportsStepStartsAndNextStep()
        {
            DraftOddsTable table = DraftOddsTable.GetBuiltInDefault();

            Assert.IsFalse(table.IsStepStart(0), "The first row isn't a step-up");
            Assert.IsTrue(table.IsStepStart(5));
            Assert.IsFalse(table.IsStepStart(6));
            Assert.IsTrue(table.IsStepStart(25));

            Assert.IsTrue(table.TryGetNextStep(0, out int next));
            Assert.AreEqual(5, next);
            Assert.IsTrue(table.TryGetNextStep(5, out next));
            Assert.AreEqual(10, next);
            Assert.IsFalse(table.TryGetNextStep(25, out _), "25 is the last step");
        }

        [Test]
        public void GetCurrentOdds_OpeningDraftIsAllOneStar_LaterDraftsFollowWavesCleared()
        {
            draftSystem.SetOddsTable(DraftOddsTable.GetBuiltInDefault());

            draftSystem.GenerateDraftOffer();
            AssertOdds(draftSystem.GetCurrentOdds(), 100f, 0f, 0f, "in the opening draft");

            draftSystem.SetWavesCleared(10);
            draftSystem.GenerateDraftOffer();
            AssertOdds(draftSystem.GetCurrentOdds(), 70f, 24f, 6f, "after 10 waves");

            draftSystem.ResetForNewStage();
            Assert.AreEqual(0, draftSystem.WavesCleared, "A new stage starts from 0 waves cleared");
        }

        [Test]
        public void GenerateDraftOffer_RollsRaritiesWithTheCurrentOdds()
        {
            // Three classes with a 1★, 2★ and 3★ each, so every card rolls from all three tiers
            var pool = new List<OperatorData>();
            foreach (var opClass in new[] { OperatorClass.Guard, OperatorClass.Sniper, OperatorClass.Caster })
            {
                foreach (var rarity in new[] { OperatorRarity.Star1, OperatorRarity.Star2, OperatorRarity.Star3 })
                {
                    var op = ScriptableObject.CreateInstance<OperatorData>();
                    op.operatorName = $"Test_{opClass}_{rarity}";
                    op.operatorClass = opClass;
                    op.baseRarity = rarity;
                    pool.Add(op);
                }
            }

            draftSystem.SetOperatorPool(pool);
            draftSystem.SetOddsTable(DraftOddsTable.GetBuiltInDefault());
            draftSystem.GenerateDraftOffer(); // opening draft
            draftSystem.SetWavesCleared(25);  // 52 / 36 / 12

            UnityEngine.Random.InitState(1234);
            var counts = new Dictionary<OperatorRarity, int>
            {
                { OperatorRarity.Star1, 0 }, { OperatorRarity.Star2, 0 }, { OperatorRarity.Star3, 0 }
            };
            const int offers = 2000;
            for (int i = 0; i < offers; i++)
            {
                foreach (DraftCard card in draftSystem.GenerateDraftOffer()) counts[card.rarity]++;
            }

            float total = offers * 3f;
            Assert.AreEqual(0.52f, counts[OperatorRarity.Star1] / total, 0.03f, "1★ share");
            Assert.AreEqual(0.36f, counts[OperatorRarity.Star2] / total, 0.03f, "2★ share");
            Assert.AreEqual(0.12f, counts[OperatorRarity.Star3] / total, 0.03f, "3★ share");

            foreach (OperatorData op in pool) Object.DestroyImmediate(op);
        }

#if UNITY_EDITOR || DEBUG
        [Test]
        public void SetDeveloperOffer_ReplacesOfferWithChosenOperator()
        {
            draftSystem.GenerateDraftOffer();
            OperatorData chosenOperator = testPool[0];
            IReadOnlyList<DraftCard> notifiedOffer = null;
            draftSystem.OnCardsOffered += offer => notifiedOffer = offer;

            bool wasSet = draftSystem.SetDeveloperOffer(chosenOperator);

            Assert.IsTrue(wasSet);
            Assert.AreEqual(1, draftSystem.CurrentOfferedCards.Count);
            Assert.AreSame(chosenOperator, draftSystem.CurrentOfferedCards[0].operatorData);
            Assert.AreSame(draftSystem.CurrentOfferedCards, notifiedOffer);
        }

        [Test]
        public void SetDeveloperOffer_RejectsOperatorOutsidePool()
        {
            OperatorData outsideOperator = ScriptableObject.CreateInstance<OperatorData>();
            outsideOperator.operatorName = "OutsidePool";

            bool wasSet = draftSystem.SetDeveloperOffer(outsideOperator);

            Assert.IsFalse(wasSet);
            Assert.AreEqual(0, draftSystem.CurrentOfferedCards.Count);
            Object.DestroyImmediate(outsideOperator);
        }
#endif

        [Test]
        public void DrawgooDeathSkill_DrawsTwoRandomCardsFromOperatorPool()
        {
            var deckObject = new GameObject("DrawgooTestDeck");
            var deck = TestUtility.AddAwakened<PlayerDeck>(deckObject);
            var operatorObject = new GameObject("DrawgooTestOperator");
            var drawgoo = operatorObject.AddComponent<DrawgooTestOperator>();
            var drawgooData = ScriptableObject.CreateInstance<OperatorData>();
            drawgooData.operatorName = "Drawgoo";
            drawgooData.drawTwoCardsOnDeath = true;
            drawgoo.Initialize(drawgooData, OperatorRarity.Star1);

            drawgoo.ActivateDeathSkill();

            Assert.AreEqual(2, deck.CardCount);
            foreach (DraftCard card in deck.DeckSlots)
            {
                Assert.IsTrue(testPool.Contains(card.operatorData));
                Assert.AreEqual(card.operatorData.baseRarity, card.rarity);
            }

            Object.DestroyImmediate(operatorObject);
            Object.DestroyImmediate(deckObject);
            Object.DestroyImmediate(drawgooData);
        }

        [Test]
        public void DrawgooUpgradedDeathSkill_DrawsOneOrTwoRandom3StarOperatorsFromPool()
        {
            Assert.AreEqual(1, OperatorBase.RollUpgradedDrawgooCardCount(0.0f));
            Assert.AreEqual(1, OperatorBase.RollUpgradedDrawgooCardCount(0.59f));
            Assert.AreEqual(2, OperatorBase.RollUpgradedDrawgooCardCount(0.60f));
            Assert.AreEqual(2, OperatorBase.RollUpgradedDrawgooCardCount(0.99f));

            var star3Op = ScriptableObject.CreateInstance<OperatorData>();
            star3Op.operatorName = "TestStar3";
            star3Op.operatorClass = OperatorClass.Defender;
            star3Op.baseRarity = OperatorRarity.Star3;
            testPool.Add(star3Op);
            draftSystem.SetOperatorPool(testPool);

            var deckObject = new GameObject("DrawgooTestDeck");
            var deck = TestUtility.AddAwakened<PlayerDeck>(deckObject);
            var operatorObject = new GameObject("DrawgooTestOperator");
            var drawgoo = operatorObject.AddComponent<DrawgooTestOperator>();
            var drawgooData = ScriptableObject.CreateInstance<OperatorData>();
            drawgooData.operatorName = "Drawgoo";
            drawgooData.drawTwoCardsOnDeath = true;
            drawgoo.Initialize(drawgooData, OperatorRarity.Star2);

            drawgoo.ActivateDeathSkill();

            Assert.IsTrue(deck.CardCount >= 1 && deck.CardCount <= 2, "Upgraded Drawgoo should draw 1 or 2 3-star cards.");
            foreach (DraftCard card in deck.DeckSlots)
            {
                Assert.AreEqual("TestStar3", card.operatorData.operatorName);
                Assert.AreEqual(OperatorRarity.Star3, card.rarity);
                Assert.AreEqual(OperatorRarity.Star3, card.operatorData.baseRarity);
            }

            Object.DestroyImmediate(operatorObject);
            Object.DestroyImmediate(deckObject);
            Object.DestroyImmediate(drawgooData);
            Object.DestroyImmediate(star3Op);
        }

        [Test]
        public void Drawgoo_SkillDescription_ChangesWithRarity()
        {
            var operatorObject = new GameObject("DrawgooTestOperator");
            var drawgoo = operatorObject.AddComponent<DrawgooTestOperator>();
            var drawgooData = ScriptableObject.CreateInstance<OperatorData>();
            drawgooData.operatorName = "Drawgoo";
            drawgooData.drawTwoCardsOnDeath = true;
            drawgooData.skillDescription = "When Drawgoo dies, draw two random operator cards from the pool. (If upgraded, draws 1 (60%) or 2 (40%) random 3-star operators instead.)";

            drawgoo.Initialize(drawgooData, OperatorRarity.Star1);
            Assert.AreEqual("When Drawgoo dies, draw two random operator cards from the pool.", drawgoo.GetSkillDescription());

            drawgoo.Initialize(drawgooData, OperatorRarity.Star2);
            Assert.AreEqual("When Drawgoo dies, draw 1 (60%) or 2 (40%) random 3-star operator cards from the pool.", drawgoo.GetSkillDescription());

            Object.DestroyImmediate(operatorObject);
            Object.DestroyImmediate(drawgooData);
        }

        [Test]
        public void OperatorDeath_RemovesOperatorFromSquadWithoutReturningItsCard()
        {
            var managerObject = new GameObject("TestOperatorManager");
            var manager = TestUtility.AddAwakened<OperatorManager>(managerObject);
            manager.SquadLimit = 1;
            var deckObject = new GameObject("TestOperatorDeathDeck");
            var deck = TestUtility.AddAwakened<PlayerDeck>(deckObject);
            var operatorObject = new GameObject("DeadOperator");
            var op = operatorObject.AddComponent<DrawgooTestOperator>();
            var operatorData = ScriptableObject.CreateInstance<OperatorData>();
            op.Initialize(operatorData, OperatorRarity.Star1);

            var deployedOperators = (List<OperatorBase>)typeof(OperatorManager)
                .GetField("deployedOperators", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(manager);
            deployedOperators.Add(op);
            Assert.IsTrue(manager.IsAtSquadLimit);

            TestUtility.ExpectEditModeDestroy();
            op.TriggerDeath();

            Assert.AreEqual(0, manager.DeployedCount);
            Assert.AreEqual(0, deck.CardCount);
            Assert.IsFalse(manager.IsAtSquadLimit);

            Object.DestroyImmediate(operatorObject);
            Object.DestroyImmediate(deckObject);
            Object.DestroyImmediate(managerObject);
            Object.DestroyImmediate(operatorData);
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

    public class DrawgooTestOperator : GuardOperator
    {
        public void ActivateDeathSkill()
        {
            OnDeath();
        }

        public void TriggerDeath()
        {
            Die();
        }
    }
}
