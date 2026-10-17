using NUnit.Framework;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Data;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Tests
{
    public class Stage4HeatHazardTests
    {
        private GameObject playerDeckObj;
        private PlayerDeck deck;

        [SetUp]
        public void SetUp()
        {
            StageCombatModifiers.ConfigureForStage(null);
            playerDeckObj = new GameObject("PlayerDeckTest");
            deck = playerDeckObj.AddComponent<PlayerDeck>();
        }

        [TearDown]
        public void TearDown()
        {
            if (playerDeckObj != null)
            {
                Object.DestroyImmediate(playerDeckObj);
            }
            StageCombatModifiers.ConfigureForStage(null);
        }

        [Test]
        public void Stage4Configuration_ActivatesHeatHazard()
        {
            StageCombatModifiers.ConfigureForEnvironment("STAGE_04");
            Assert.IsTrue(StageCombatModifiers.IsHeatHazardActive);

            StageCombatModifiers.ConfigureForEnvironment("STAGE_03");
            Assert.IsFalse(StageCombatModifiers.IsHeatHazardActive);
        }

        [Test]
        public void CardInDeck_HeatStacksFadeAwayOnePerRound()
        {
            var opData = ScriptableObject.CreateInstance<OperatorData>();
            opData.operatorName = "HeatTestOp";

            var card = new DraftCard(opData, OperatorRarity.Star1, 0, 3);
            deck.AddCard(card);

            Assert.AreEqual(3, deck.DeckSlots[0].heatStacks);

            deck.AdvanceRedeployCooldownsOneRound();
            Assert.AreEqual(2, deck.DeckSlots[0].heatStacks);

            deck.AdvanceRedeployCooldownsOneRound();
            Assert.AreEqual(1, deck.DeckSlots[0].heatStacks);

            deck.AdvanceRedeployCooldownsOneRound();
            Assert.AreEqual(0, deck.DeckSlots[0].heatStacks);

            // Should not go below 0
            deck.AdvanceRedeployCooldownsOneRound();
            Assert.AreEqual(0, deck.DeckSlots[0].heatStacks);

            Object.DestroyImmediate(opData);
        }

        [Test]
        public void Operator_RetreatExtinguishesBurning()
        {
            var opObj = new GameObject("TestOperator");
            var op = opObj.AddComponent<GuardOperator>();
            var opData = ScriptableObject.CreateInstance<OperatorData>();
            opData.operatorName = "Guard";
            opData.baseRarity = OperatorRarity.Star1;
            opData.operatorClass = OperatorClass.Guard;
            opData.baseHP = 100;
            opData.baseATK = 10;
            opData.baseDEF = 0;
            opData.baseRES = 0;

            op.Initialize(opData, OperatorRarity.Star1, OperatorFacing.Right);
            op.HeatStacks = 5;
            op.IsBurning = true;

            Assert.IsTrue(op.IsBurning);

            // Retreat extinguishes burning
            op.Retreat();
            Assert.IsFalse(op.IsBurning);

            Object.DestroyImmediate(opObj);
            Object.DestroyImmediate(opData);
        }
    }
}
