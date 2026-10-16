using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using TrashTD.Core.GameLoop;
using TrashTD.Data;
using TrashTD.Operators;

namespace TrashTD.Tests
{
    public class OperatorPreparationHealthTests
    {
        private GameObject gameManagerObject;
        private GameObject operatorObject;
        private GameManager gameManager;
        private OperatorData operatorData;
        private SniperOperator sniper;

        [SetUp]
        public void SetUp()
        {
            gameManagerObject = new GameObject("TestGameManager");
            gameManager = TestUtility.AddAwakened<GameManager>(gameManagerObject);

            operatorData = ScriptableObject.CreateInstance<OperatorData>();
            operatorData.operatorName = "TestOperator";
            operatorData.baseHP = 100;
            operatorData.baseATK = 20;

            operatorObject = new GameObject("TestOperator");
            sniper = operatorObject.AddComponent<SniperOperator>();
            sniper.Initialize(operatorData, OperatorRarity.Star1);
        }

        [TearDown]
        public void TearDown()
        {
            TestUtility.ClearSingletons();
            if (operatorObject != null) Object.DestroyImmediate(operatorObject);
            if (gameManagerObject != null) Object.DestroyImmediate(gameManagerObject);
            if (operatorData != null) Object.DestroyImmediate(operatorData);
        }

        [Test]
        public void PreparationPhase_BlocksDamageAndHealing()
        {
            SetPhase(StagePhase.Preparation);

            sniper.TakeDamage(20, DamageType.Physical);
            sniper.Heal(20);

            Assert.AreEqual(sniper.MaxHP, sniper.CurrentHP);
            Assert.IsFalse(sniper.TryTakeAttackDamage(20, DamageType.Physical));
            Assert.AreEqual(sniper.MaxHP, sniper.CurrentHP);
        }

        [Test]
        public void CardPickPhase_BlocksDamageAndHealing()
        {
            SetPhase(StagePhase.CardPick);

            sniper.TakeDamage(20, DamageType.Physical);
            sniper.Heal(20);

            Assert.IsTrue(sniper.IsCombatPaused);
            Assert.AreEqual(sniper.MaxHP, sniper.CurrentHP);
            Assert.IsFalse(sniper.TryTakeAttackDamage(20, DamageType.Physical));
        }

        [Test]
        public void BetweenWaves_BubbleShieldIsNotPopped()
        {
            SetPhase(StagePhase.Preparation);
            var bubbletsObject = new GameObject("TestBubblets");
            try
            {
                BubbletsOperator bubblets = bubbletsObject.AddComponent<BubbletsOperator>();
                BubbleShield.Apply(sniper, 20, bubblets);

                sniper.TakeDamage(20, DamageType.Physical);
                SetPhase(StagePhase.CardPick);
                sniper.TakeDamage(20, DamageType.Physical);

                Assert.IsTrue(BubbleShield.HasActiveShield(sniper));
                Assert.AreEqual(sniper.MaxHP, sniper.CurrentHP);
            }
            finally
            {
                Object.DestroyImmediate(bubbletsObject);
            }
        }

        [Test]
        public void WaveActivePhase_AllowsDamageAndHealing()
        {
            SetPhase(StagePhase.WaveActive);

            sniper.TakeDamage(20, DamageType.Physical);
            Assert.Less(sniper.CurrentHP, sniper.MaxHP);

            int damagedHP = sniper.CurrentHP;
            sniper.Heal(10);
            Assert.AreEqual(damagedHP + 10, sniper.CurrentHP);
        }

        private void SetPhase(StagePhase phase)
        {
            typeof(GameManager)
                .GetField("currentPhase", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(gameManager, phase);
        }
    }
}
