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
    }
}
