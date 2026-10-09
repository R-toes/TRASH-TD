using NUnit.Framework;
using TrashTD.Combat;
using TrashTD.Data;
using UnityEngine;

namespace TrashTD.Tests
{
    public class StageCombatModifiersTests
    {
        private StageData stageData;

        [SetUp]
        public void SetUp()
        {
            stageData = ScriptableObject.CreateInstance<StageData>();
            StageCombatModifiers.ConfigureForStage(null);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(stageData);
            StageCombatModifiers.ConfigureForStage(null);
        }

        [Test]
        public void Stage3Sandstorm_ReducesAttackIntervalByFivePercent()
        {
            stageData.stageId = "STAGE_03";

            StageCombatModifiers.ConfigureForStage(stageData);

            Assert.IsTrue(StageCombatModifiers.IsSandstormActive);
            Assert.AreEqual(0.95f, StageCombatModifiers.GetAttackInterval(1f), 0.0001f);
        }

        [Test]
        public void OtherStages_DoNotReceiveSandstormModifiers()
        {
            stageData.stageId = "STAGE_06";

            StageCombatModifiers.ConfigureForStage(stageData);

            Assert.IsFalse(StageCombatModifiers.IsSandstormActive);
            Assert.AreEqual(1f, StageCombatModifiers.GetAttackInterval(1f));
            Assert.IsTrue(StageCombatModifiers.TryAttackHit());
        }

        [Test]
        public void Stage3Sandstorm_MissesApproximatelyTenPercentOfAttacks()
        {
            stageData.stageId = "STAGE_03";
            StageCombatModifiers.ConfigureForStage(stageData);

            UnityEngine.Random.State previousRandomState = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(42);
                int misses = 0;
                const int attempts = 10000;

                for (int i = 0; i < attempts; i++)
                {
                    if (!StageCombatModifiers.TryAttackHit())
                        misses++;
                }

                Assert.That(misses, Is.InRange(900, 1100));
            }
            finally
            {
                UnityEngine.Random.state = previousRandomState;
            }
        }

        [Test]
        public void TargetMissChance_MissesApproximatelyFortyPercentOfAttacks()
        {
            Assert.IsTrue(StageCombatModifiers.TryAttackHit(0f));
            Assert.IsFalse(StageCombatModifiers.TryAttackHit(1f));

            UnityEngine.Random.State previousRandomState = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(42);
                int misses = 0;
                const int attempts = 10000;

                for (int i = 0; i < attempts; i++)
                {
                    if (!StageCombatModifiers.TryAttackHit(0.4f))
                        misses++;
                }

                Assert.That(misses, Is.InRange(3900, 4100));
            }
            finally
            {
                UnityEngine.Random.state = previousRandomState;
            }
        }
    }
}
