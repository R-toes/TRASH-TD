using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TrashTD.Data;
using TrashTD.Operators;
using UnityEditor;
using UnityEngine;

namespace TrashTD.Tests
{
    public class PompothAttackOriginTests
    {
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
