using System.Collections.Generic;
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
    public class PompothAttackOriginTests
    {
        [Test]
        public void AttackSpeedPassive_IncreasesToTwentyPercentPerEnemyAndTwoHundredPercentCapAtThreeStars()
        {
            Assert.AreEqual(1.1f, PompothOperator.GetAttackSpeedMultiplier(1, OperatorRarity.Star1), 0.0001f);
            Assert.AreEqual(2f, PompothOperator.GetAttackSpeedMultiplier(20, OperatorRarity.Star1), 0.0001f);
            Assert.AreEqual(1.3f, PompothOperator.GetAttackSpeedMultiplier(1, OperatorRarity.Star3), 0.0001f);
            Assert.AreEqual(3.1f, PompothOperator.GetAttackSpeedMultiplier(7, OperatorRarity.Star3), 0.0001f);
            Assert.AreEqual(4f, PompothOperator.GetAttackSpeedMultiplier(10, OperatorRarity.Star3), 0.0001f);
            Assert.AreEqual(4f, PompothOperator.GetAttackSpeedMultiplier(20, OperatorRarity.Star3), 0.0001f);
        }

        [Test]
        public void FirstUpgrade_IgnoresOnePercentMoreDefensePerLandedHitUpToOneHundredPercent()
        {
            Assert.AreEqual(99, PompothOperator.CalculateDefenseAfterIgnore(100, 0, true));
            Assert.AreEqual(98, PompothOperator.CalculateDefenseAfterIgnore(100, 1, true));
            Assert.AreEqual(1, PompothOperator.CalculateDefenseAfterIgnore(100, 98, true));
            Assert.AreEqual(0, PompothOperator.CalculateDefenseAfterIgnore(100, 99, true));
            Assert.AreEqual(0, PompothOperator.CalculateDefenseAfterIgnore(100, 120, true));
            Assert.AreEqual(100, PompothOperator.CalculateDefenseAfterIgnore(100, 99, false));
        }

        [Test]
        public void FirstUpgrade_LandedHitRampResetsWhenTargetChanges()
        {
            var operatorObject = new GameObject("PompothRampTest");
            var firstTargetObject = new GameObject("FirstTarget");
            var secondTargetObject = new GameObject("SecondTarget");
            try
            {
                var pompoth = operatorObject.AddComponent<PompothOperator>();
                var firstTarget = firstTargetObject.AddComponent<GruntEnemy>();
                var secondTarget = secondTargetObject.AddComponent<GruntEnemy>();
                var targetField = typeof(PompothOperator).GetField("defenseRampTarget", BindingFlags.Instance | BindingFlags.NonPublic);
                var hitCountField = typeof(PompothOperator).GetField("landedHitsOnDefenseRampTarget", BindingFlags.Instance | BindingFlags.NonPublic);
                var selectTarget = typeof(PompothOperator).GetMethod("SelectDefenseRampTarget", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(targetField);
                Assert.IsNotNull(hitCountField);
                Assert.IsNotNull(selectTarget);

                targetField.SetValue(pompoth, firstTarget);
                hitCountField.SetValue(pompoth, 10);
                selectTarget.Invoke(pompoth, new object[] { firstTarget });
                Assert.AreEqual(10, pompoth.LandedHitsOnDefenseRampTarget);

                selectTarget.Invoke(pompoth, new object[] { secondTarget });
                Assert.AreEqual(0, pompoth.LandedHitsOnDefenseRampTarget);
            }
            finally
            {
                Object.DestroyImmediate(secondTargetObject);
                Object.DestroyImmediate(firstTargetObject);
                Object.DestroyImmediate(operatorObject);
            }
        }

        [Test]
        public void FirstUpgrade_LandedHitsApplyIncreasingDefenseIgnore()
        {
            var operatorObject = new GameObject("PompothDefenseIgnoreTest");
            var targetObject = new GameObject("PompothDefenseTarget");
            var operatorData = ScriptableObject.CreateInstance<OperatorData>();
            var enemyData = ScriptableObject.CreateInstance<EnemyData>();
            bool originalHazardsEnabled = StageCombatModifiers.HazardsEnabled;
            StageCombatModifiers.HazardsEnabled = false;
            try
            {
                var pompoth = operatorObject.AddComponent<PompothOperator>();
                operatorData.baseHP = 100;
                operatorData.baseATK = 100;
                operatorData.damageType = DamageType.Physical;
                pompoth.Initialize(operatorData, OperatorRarity.Star2);

                enemyData.baseHP = 1000;
                enemyData.baseDEF = 76;
                enemyData.baseRES = 0;
                var target = targetObject.AddComponent<GruntEnemy>();
                target.Initialize(enemyData, 0);

                var selectTarget = typeof(PompothOperator).GetMethod("SelectDefenseRampTarget", BindingFlags.Instance | BindingFlags.NonPublic);
                var applyDamage = typeof(PompothOperator).GetMethod("ApplyPompothDamage", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(selectTarget);
                Assert.IsNotNull(applyDamage);
                selectTarget.Invoke(pompoth, new object[] { target });

                applyDamage.Invoke(pompoth, new object[] { target, 0 });
                Assert.AreEqual(995, target.CurrentHP, "The first hit should ignore 1% DEF.");
                Assert.AreEqual(1, pompoth.LandedHitsOnDefenseRampTarget);

                applyDamage.Invoke(pompoth, new object[] { target, 1 });
                Assert.AreEqual(989, target.CurrentHP, "The second hit should ignore 2% DEF.");
                Assert.AreEqual(2, pompoth.LandedHitsOnDefenseRampTarget);
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(operatorObject);
                Object.DestroyImmediate(operatorData);
                Object.DestroyImmediate(enemyData);
                StageCombatModifiers.HazardsEnabled = originalHazardsEnabled;
            }
        }

        [Test]
        public void SkillDescription_ListsBothUpgrades()
        {
            const string dataPath = "Assets/_Project/Data/Operators/OP_Sniper_Pompoth.asset";
            OperatorData data = AssetDatabase.LoadAssetAtPath<OperatorData>(dataPath);
            Assert.IsNotNull(data);
            StringAssert.Contains("Base (1\u2605)", data.skillDescription);
            StringAssert.Contains("1st upgrade (2\u2605)", data.skillDescription);
            StringAssert.Contains("ignores 1% of its DEF", data.skillDescription);
            StringAssert.Contains("2nd upgrade (3\u2605)", data.skillDescription);
            StringAssert.Contains("+30% per enemy", data.skillDescription);
            StringAssert.Contains("+300%", data.skillDescription);
        }

        [Test]
        public void AttackOrigin_RandomlySelectsPointsOnCircleAroundSprite()
        {
            const string prefabPath = "Assets/_Project/Prefabs/Operators/Prefab_OP_Pompoth.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab);

            GameObject instance = Object.Instantiate(prefab);
            try
            {
                PompothOperator pompoth = instance.GetComponent<PompothOperator>();
                SpriteRenderer spriteRenderer = instance.GetComponent<SpriteRenderer>();
                Assert.IsNotNull(pompoth);
                Assert.IsNotNull(spriteRenderer);

                Vector3 center = pompoth.ProjectileOrigin;
                float radius = spriteRenderer.bounds.extents.y * 1.35f;
                var selectedPoints = new HashSet<int>();

                for (int i = 0; i < 32; i++)
                {
                    Vector3 origin = pompoth.GetRandomAttackOrigin();
                    Vector2 offset = origin - center;

                    Assert.AreEqual(radius, offset.magnitude, 0.0001f);
                    Assert.AreEqual(0f, origin.z - center.z, 0.0001f);

                    float angle = Mathf.Atan2(offset.y, offset.x);
                    int pointIndex = Mathf.RoundToInt(
                        angle * PompothOperator.AttackOriginPointCount / (2f * Mathf.PI));
                    pointIndex = (pointIndex % PompothOperator.AttackOriginPointCount +
                                  PompothOperator.AttackOriginPointCount) %
                                 PompothOperator.AttackOriginPointCount;
                    float expectedAngle = pointIndex * (2f * Mathf.PI / PompothOperator.AttackOriginPointCount);
                    Assert.Less(Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, expectedAngle * Mathf.Rad2Deg)), 0.01f);
                    selectedPoints.Add(pointIndex);
                }

                Assert.Greater(selectedPoints.Count, 1, "Attack origins should vary between shots.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void AttackOriginIndicator_FormsAtNextAttackPointAsAttackCharges()
        {
            const string prefabPath = "Assets/_Project/Prefabs/Operators/Prefab_OP_Pompoth.prefab";
            const string dataPath = "Assets/_Project/Data/Operators/OP_Sniper_Pompoth.asset";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            OperatorData data = AssetDatabase.LoadAssetAtPath<OperatorData>(dataPath);
            Assert.IsNotNull(prefab);
            Assert.IsNotNull(data);

            GameObject instance = Object.Instantiate(prefab);
            try
            {
                PompothOperator pompoth = instance.GetComponent<PompothOperator>();
                pompoth.Initialize(data, OperatorRarity.Star1);
                typeof(PompothOperator).GetMethod("OnDeployed", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(pompoth, null);
                typeof(OperatorBase).GetField("isDeployed", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(pompoth, true);

                SpriteRenderer indicator = instance.transform.Find("PompothAttackOriginIndicator")
                    .GetComponent<SpriteRenderer>();
                Assert.IsNotNull(indicator);
                Assert.IsNotNull(indicator.sprite);
                StringAssert.Contains("diamond", indicator.sprite.name);
                SpriteRenderer glow = instance.transform.Find("PompothAttackOriginIndicator/PompothAttackOriginGlow")
                    .GetComponent<SpriteRenderer>();
                Assert.IsNotNull(glow);
                Assert.IsNotNull(glow.sprite);

                InvokeIndicatorUpdate(pompoth);
                float startingScale = indicator.transform.localScale.x;
                Assert.AreEqual(pompoth.GetNextAttackOrigin(), indicator.transform.position);
                Assert.AreEqual(indicator.transform.position, glow.transform.position);

                float attackInterval = pompoth.GetAttackInterval();
                typeof(OperatorBase).GetField("attackTimer", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(pompoth, attackInterval);
                InvokeIndicatorUpdate(pompoth);

                Assert.Greater(indicator.transform.localScale.x, startingScale);
                Assert.Greater(indicator.transform.localScale.x, 0.9f);
                Assert.AreEqual(pompoth.GetNextAttackOrigin(), indicator.transform.position);
                Assert.AreEqual(indicator.transform.position, glow.transform.position);
                Assert.Greater(glow.color.a, 0.2f);
                Assert.Greater(indicator.color.a, 0.9f);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void InvokeIndicatorUpdate(PompothOperator pompoth)
        {
            typeof(PompothOperator).GetMethod("UpdateAttackOriginIndicator", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(pompoth, null);
        }
    }
}
