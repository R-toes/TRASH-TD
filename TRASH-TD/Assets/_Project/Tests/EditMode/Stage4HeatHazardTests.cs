using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.GameLoop;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Tests
{
    public class Stage4HeatHazardTests
    {
        private GameObject playerDeckObj;
        private PlayerDeck deck;
        private GameObject operatorManagerObj;
        private OperatorManager operatorManager;
        private GameObject hazardObj;
        private GameObject operatorObj;
        private GameObject emberObj;
        private OperatorData operatorData;

        [SetUp]
        public void SetUp()
        {
            StageCombatModifiers.ConfigureForStage(null);
            playerDeckObj = new GameObject("PlayerDeckTest");
            deck = playerDeckObj.AddComponent<PlayerDeck>();

            operatorManagerObj = new GameObject("OperatorManagerTest");
            operatorManager = TestUtility.AddAwakened<OperatorManager>(operatorManagerObj);
            hazardObj = new GameObject("Stage4HeatHazardTest");
        }

        [TearDown]
        public void TearDown()
        {
            if (playerDeckObj != null)
            {
                Object.DestroyImmediate(playerDeckObj);
            }
            if (operatorObj != null) Object.DestroyImmediate(operatorObj);
            if (emberObj != null) Object.DestroyImmediate(emberObj);
            if (operatorData != null) Object.DestroyImmediate(operatorData);
            if (hazardObj != null) Object.DestroyImmediate(hazardObj);
            if (operatorManagerObj != null) Object.DestroyImmediate(operatorManagerObj);
            TestUtility.ClearSingletons();
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

        [Test]
        public void BurningOperator_TakesTwoTrueDamageEveryTick()
        {
            StageCombatModifiers.ConfigureForEnvironment("STAGE_04");

            operatorData = ScriptableObject.CreateInstance<OperatorData>();
            operatorData.operatorName = "HighDefenseGuard";
            operatorData.baseHP = 100;
            operatorData.baseDEF = 100;

            operatorObj = new GameObject("BurningOperator");
            GuardOperator op = operatorObj.AddComponent<GuardOperator>();
            op.Initialize(operatorData, OperatorRarity.Star1, OperatorFacing.Right);
            op.Deploy(new GridCell(0, 0, TileType.LowGround, Vector3.zero));
            op.IsBurning = true;
            operatorManager.RegisterOperator(op);

            Stage4HeatHazard hazard = hazardObj.AddComponent<Stage4HeatHazard>();
            typeof(Stage4HeatHazard)
                .GetField("operatorManager", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(hazard, operatorManager);

            int startingHP = op.CurrentHP;
            typeof(Stage4HeatHazard)
                .GetMethod("ApplyBurningDamage", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(hazard, null);

            Assert.AreEqual(startingHP - 2, op.CurrentHP);
        }

        [Test]
        public void WaveStartHeatStackGain_StartsMapFlare()
        {
            StageCombatModifiers.ConfigureForEnvironment("STAGE_04");

            operatorData = ScriptableObject.CreateInstance<OperatorData>();
            operatorData.operatorName = "HeatFlareGuard";
            operatorData.baseHP = 100;

            operatorObj = new GameObject("HeatFlareOperator");
            GuardOperator op = operatorObj.AddComponent<GuardOperator>();
            op.Initialize(operatorData, OperatorRarity.Star1, OperatorFacing.Right);
            op.Deploy(new GridCell(0, 0, TileType.LowGround, Vector3.zero));
            operatorManager.RegisterOperator(op);

            Stage4HeatHazard hazard = hazardObj.AddComponent<Stage4HeatHazard>();
            emberObj = new GameObject("TestEmbers");
            ParticleSystem embers = emberObj.AddComponent<ParticleSystem>();
            var emberMain = embers.main;
            emberMain.playOnAwake = false;
            var emission = embers.emission;
            emission.rateOverTime = 10f;
            typeof(Stage4HeatHazard)
                .GetField("emberParticleSystem", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(hazard, embers);
            typeof(Stage4HeatHazard)
                .GetField("operatorManager", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(hazard, operatorManager);
            typeof(Stage4HeatHazard)
                .GetMethod("HandleWaveStarted", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(hazard, new object[] { 1, 5 });

            float flareTimer = (float)typeof(Stage4HeatHazard)
                .GetField("heatFlareTimer", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(hazard);
            float flareIntensity = (float)typeof(Stage4HeatHazard)
                .GetMethod("GetHeatFlareIntensity", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(hazard, null);
            var particles = new ParticleSystem.Particle[FlareParticleCapacity];
            int particleCount = embers.GetParticles(particles);

            Assert.AreEqual(1, op.HeatStacks);
            Assert.AreEqual(Stage4HeatHazard.HeatFlareDurationSeconds, flareTimer);
            Assert.AreEqual(1f, flareIntensity, "The map flare should peak with the round-start ember burst.");
            Assert.Greater(particleCount, 0, "Round-start heat should emit an immediate ember burst.");
            Assert.LessOrEqual(particles[0].remainingLifetime, Stage4HeatHazard.FlareEmberMaxLifetime, "Flare embers should fade with the flare rather than linger.");
        }

        private const int FlareParticleCapacity = 128; // the burst is 96 embers
    }
}
