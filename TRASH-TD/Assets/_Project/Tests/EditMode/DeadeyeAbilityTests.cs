using System.Reflection;
using NUnit.Framework;
using TrashTD.Combat;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;
using UnityEditor;
using UnityEngine;

namespace TrashTD.Tests
{
    public class DeadeyeAbilityTests
    {
        private const string DeadeyeDataPath = "Assets/_Project/Data/Operators/OP_Sniper_Deadeye.asset";

        [Test]
        public void BaseAirBonusAndFirstUpgradeIntervalTradeoff()
        {
            Assert.AreEqual(1.2f, DeadeyeOperator.GetAirAttackIntervalMultiplier(true, OperatorRarity.Star1), 0.0001f);
            Assert.AreEqual(1f, DeadeyeOperator.GetAirAttackIntervalMultiplier(true, OperatorRarity.Star2), 0.0001f);
            Assert.AreEqual(1f, DeadeyeOperator.GetAirAttackIntervalMultiplier(false, OperatorRarity.Star1), 0.0001f);
            Assert.AreEqual(120, DeadeyeOperator.CalculateAttackPower(100, 0, false, true, false));
        }

        [Test]
        public void FirstUpgrade_RampsFromFirstHitAndCapsAtOneHundredPercent()
        {
            Assert.AreEqual(102, DeadeyeOperator.CalculateAttackPower(100, 0, true, false, false));
            Assert.AreEqual(198, DeadeyeOperator.CalculateAttackPower(100, 48, true, false, false));
            Assert.AreEqual(200, DeadeyeOperator.CalculateAttackPower(100, 49, true, false, false));
            Assert.AreEqual(200, DeadeyeOperator.CalculateAttackPower(100, 80, true, false, false));
        }

        [Test]
        public void SecondUpgrade_KillShotBonusStacksWithTargetRamp()
        {
            Assert.AreEqual(202, DeadeyeOperator.CalculateAttackPower(100, 0, true, false, true));
            Assert.AreEqual(222, DeadeyeOperator.CalculateAttackPower(100, 0, true, true, true));
        }

        [Test]
        public void SwitchingRampTargetResetsLandedHitCount()
        {
            var operatorObject = new GameObject("DeadeyeRampTest");
            var firstTargetObject = new GameObject("FirstTarget");
            var secondTargetObject = new GameObject("SecondTarget");
            try
            {
                var deadeye = operatorObject.AddComponent<DeadeyeOperator>();
                var firstTarget = firstTargetObject.AddComponent<GruntEnemy>();
                var secondTarget = secondTargetObject.AddComponent<GruntEnemy>();

                var targetField = typeof(DeadeyeOperator).GetField("rampTarget", BindingFlags.Instance | BindingFlags.NonPublic);
                var hitCountField = typeof(DeadeyeOperator).GetField("landedHitsOnRampTarget", BindingFlags.Instance | BindingFlags.NonPublic);
                var selectTarget = typeof(DeadeyeOperator).GetMethod("SelectRampTarget", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(targetField);
                Assert.IsNotNull(hitCountField);
                Assert.IsNotNull(selectTarget);

                targetField.SetValue(deadeye, firstTarget);
                hitCountField.SetValue(deadeye, 10);
                selectTarget.Invoke(deadeye, new object[] { firstTarget });
                Assert.AreEqual(10, deadeye.LandedHitsOnRampTarget, "Continuing to attack the same target should retain its ramp.");

                selectTarget.Invoke(deadeye, new object[] { secondTarget });
                Assert.AreEqual(0, deadeye.LandedHitsOnRampTarget, "Switching targets should reset the ramp.");
            }
            finally
            {
                Object.DestroyImmediate(secondTargetObject);
                Object.DestroyImmediate(firstTargetObject);
                Object.DestroyImmediate(operatorObject);
            }
        }

        [Test]
        public void LandedHitsIncreaseDamageAndSwitchingTargetsStartsAtTwoPercent()
        {
            var operatorObject = new GameObject("DeadeyeDamageRampTest");
            var firstTargetObject = new GameObject("RampTargetA");
            var secondTargetObject = new GameObject("RampTargetB");
            var operatorData = ScriptableObject.CreateInstance<OperatorData>();
            var enemyData = ScriptableObject.CreateInstance<EnemyData>();
            bool originalHazardsEnabled = StageCombatModifiers.HazardsEnabled;
            StageCombatModifiers.HazardsEnabled = false;
            try
            {
                var deadeye = operatorObject.AddComponent<DeadeyeOperator>();
                operatorData.baseHP = 100;
                operatorData.baseATK = 100;
                operatorData.damageType = DamageType.Physical;
                deadeye.Initialize(operatorData, OperatorRarity.Star2);

                enemyData.baseHP = 2000;
                enemyData.baseDEF = 0;
                enemyData.baseRES = 0;
                var firstTarget = firstTargetObject.AddComponent<GruntEnemy>();
                firstTarget.Initialize(enemyData, 0);
                var secondTarget = secondTargetObject.AddComponent<GruntEnemy>();
                secondTarget.Initialize(enemyData, 0);

                var selectTarget = typeof(DeadeyeOperator).GetMethod("SelectRampTarget", BindingFlags.Instance | BindingFlags.NonPublic);
                var applyDamage = typeof(DeadeyeOperator).GetMethod("ApplyDeadeyeDamage", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(selectTarget);
                Assert.IsNotNull(applyDamage);

                selectTarget.Invoke(deadeye, new object[] { firstTarget });
                applyDamage.Invoke(deadeye, new object[] { firstTarget, false, 0, false });
                Assert.AreEqual(1898, firstTarget.CurrentHP, "The first landed hit should add 2% ATK as damage.");
                Assert.AreEqual(1, deadeye.LandedHitsOnRampTarget);

                applyDamage.Invoke(deadeye, new object[] { firstTarget, false, 1, false });
                Assert.AreEqual(1794, firstTarget.CurrentHP, "The second landed hit should add 4% ATK as damage.");
                Assert.AreEqual(2, deadeye.LandedHitsOnRampTarget);

                selectTarget.Invoke(deadeye, new object[] { secondTarget });
                applyDamage.Invoke(deadeye, new object[] { secondTarget, false, 0, false });
                Assert.AreEqual(1898, secondTarget.CurrentHP, "A new target should start its ramp at +2%.");
                Assert.AreEqual(1, deadeye.LandedHitsOnRampTarget);
            }
            finally
            {
                Object.DestroyImmediate(secondTargetObject);
                Object.DestroyImmediate(firstTargetObject);
                Object.DestroyImmediate(operatorObject);
                Object.DestroyImmediate(operatorData);
                Object.DestroyImmediate(enemyData);
                StageCombatModifiers.HazardsEnabled = originalHazardsEnabled;
            }
        }

        [Test]
        public void ThirdUpgrade_ArmsKillBonusWhenDeadeyeKillsAndConsumesItOnNextShot()
        {
            var operatorObject = new GameObject("DeadeyeKillBonusTest");
            var enemyObject = new GameObject("DeadeyeKillTarget");
            var nextEnemyObject = new GameObject("DeadeyeCriticalHitTarget");
            var operatorData = ScriptableObject.CreateInstance<OperatorData>();
            var enemyData = ScriptableObject.CreateInstance<EnemyData>();
            bool originalHazardsEnabled = StageCombatModifiers.HazardsEnabled;
            StageCombatModifiers.HazardsEnabled = false;
            try
            {
                var deadeye = operatorObject.AddComponent<DeadeyeOperator>();
                operatorData.baseHP = 100;
                operatorData.baseATK = 100;
                operatorData.baseDEF = 0;
                operatorData.damageType = DamageType.Physical;
                deadeye.Initialize(operatorData, OperatorRarity.Star3);

                var target = enemyObject.AddComponent<GruntEnemy>();
                enemyData.baseHP = 1;
                enemyData.baseDEF = 0;
                enemyData.baseRES = 0;
                target.Initialize(enemyData, 0);

                var applyDamage = typeof(DeadeyeOperator).GetMethod("ApplyDeadeyeDamage", BindingFlags.Instance | BindingFlags.NonPublic);
                var consumeBonus = typeof(DeadeyeOperator).GetMethod("ConsumeKillShotBonus", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(applyDamage);
                Assert.IsNotNull(consumeBonus);

                applyDamage.Invoke(deadeye, new object[] { target, false, 0, false });
                Assert.IsTrue(deadeye.HasPendingKillShotBonus, "Killing an enemy should arm the next-shot bonus.");

                var nextTarget = nextEnemyObject.AddComponent<GruntEnemy>();
                enemyData.baseHP = 1000;
                nextTarget.Initialize(enemyData, 0);
                bool usesKillShotBonus = (bool)consumeBonus.Invoke(deadeye, null);
                Assert.IsTrue(usesKillShotBonus, "The next shot should consume the kill bonus.");
                Assert.IsFalse(deadeye.HasPendingKillShotBonus, "The kill bonus must apply to only one shot.");
                applyDamage.Invoke(deadeye, new object[] { nextTarget, false, 0, usesKillShotBonus });

                TextMesh[] textMeshes = Object.FindObjectsByType<TextMesh>();
                bool showedCriticalPopup = false;
                foreach (TextMesh textMesh in textMeshes)
                {
                    if (textMesh.text == "CRIT!")
                    {
                        showedCriticalPopup = true;
                        Object.DestroyImmediate(textMesh.gameObject);
                    }
                }
                Assert.IsTrue(showedCriticalPopup, "A landed kill-bonus shot should display a CRIT popup.");
            }
            finally
            {
                Object.DestroyImmediate(nextEnemyObject);
                Object.DestroyImmediate(enemyObject);
                Object.DestroyImmediate(operatorObject);
                Object.DestroyImmediate(operatorData);
                Object.DestroyImmediate(enemyData);
                StageCombatModifiers.HazardsEnabled = originalHazardsEnabled;
            }
        }

        [Test]
        public void SkillDescription_ListsBaseAbilityAndBothUpgrades()
        {
            var data = AssetDatabase.LoadAssetAtPath<OperatorData>(DeadeyeDataPath);
            Assert.IsNotNull(data);
            StringAssert.Contains("Base (1\u2605)", data.skillDescription);
            StringAssert.Contains("20% more damage to flying enemies", data.skillDescription);
            StringAssert.Contains("1st upgrade (2\u2605)", data.skillDescription);
            StringAssert.Contains("2% of ATK", data.skillDescription);
            StringAssert.Contains("capped at +100%", data.skillDescription);
            StringAssert.Contains("2nd upgrade (3\u2605)", data.skillDescription);
            StringAssert.Contains("additional 100% damage", data.skillDescription);
        }
    }
}
