using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using TrashTD.Data;
using TrashTD.Operators;

namespace TrashTD.Tests
{
    public class PompothPowerVisualsTests
    {
        [TestCase(0, 0)]
        [TestCase(4, 0)]
        [TestCase(5, 1)]
        [TestCase(9, 1)]
        [TestCase(10, 2)]
        [TestCase(20, 2)]
        public void BaseRarityPassiveMapsEnemyCountToPowerTier(int enemyCount, int expectedTier)
        {
            Assert.AreEqual(expectedTier, GetTierForEnemyCount(enemyCount, OperatorRarity.Star1));
        }

        [TestCase(0, 0)]
        [TestCase(4, 0)]
        [TestCase(5, 1)]
        [TestCase(9, 1)]
        [TestCase(10, 2)]
        [TestCase(20, 2)]
        public void IncreasedPassiveMapsEnemyCountToSamePowerTier(int enemyCount, int expectedTier)
        {
            Assert.AreEqual(expectedTier, GetTierForEnemyCount(enemyCount, OperatorRarity.Star3));
        }

        [Test]
        public void GetPowerTier_HandlesExactAndNearFiftyPercentBoundary()
        {
            Assert.AreEqual(1, PompothPowerVisuals.GetPowerTier(0.5f, 1f));
            Assert.AreEqual(1, PompothPowerVisuals.GetPowerTier(0.4995f, 1f));
            Assert.AreEqual(0, PompothPowerVisuals.GetPowerTier(0.498f, 1f));
        }

        [Test]
        public void FiveEnemiesRemainTierOneWhenPassiveIsUpgraded()
        {
            int baseTier = GetTierForEnemyCount(5, OperatorRarity.Star1);
            int upgradedTier = GetTierForEnemyCount(5, OperatorRarity.Star3);

            Assert.AreEqual(1, baseTier);
            Assert.AreEqual(baseTier, upgradedTier);
        }

        [Test]
        public void AttackProjectileColorTracksPowerTierAndPreservesCopiedAppearance()
        {
            Assert.AreEqual(new Color(0.9f, 0.2f, 0.9f), PompothOperator.GetAttackProjectileColor(0));
            Assert.AreEqual(new Color(0.55f, 0.08f, 0.28f), PompothOperator.GetAttackProjectileColor(1));
            Assert.AreEqual(Color.red, PompothOperator.GetAttackProjectileColor(2));
            Assert.AreEqual(new Color(0.85f, 0.85f, 0.85f), PompothOperator.GetAttackProjectileColor(2, true));
            Assert.AreEqual(TrashTD.Combat.ProjectileStyle.Bullet, PompothOperator.GetAttackProjectileStyle(1));
            Assert.AreEqual(TrashTD.Combat.ProjectileStyle.PompothFullPower, PompothOperator.GetAttackProjectileStyle(2));
            Assert.AreEqual(TrashTD.Combat.ProjectileStyle.Bullet, PompothOperator.GetAttackProjectileStyle(2, true));
        }

        [Test]
        public void PowerVisuals_ResetToBaseWhenNoEnemiesOrReenabled()
        {
            const string prefabPath = "Assets/_Project/Prefabs/Operators/Prefab_OP_Pompoth.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab);

            GameObject instance = Object.Instantiate(prefab);
            try
            {
                PompothPowerVisuals visuals = instance.GetComponent<PompothPowerVisuals>();
                PompothSpriteAnimation animation = instance.GetComponent<PompothSpriteAnimation>();
                Assert.IsNotNull(visuals);
                Assert.IsNotNull(animation);

                typeof(PompothPowerVisuals)
                    .GetField("downgradeDelay", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(visuals, 0f);

                typeof(PompothSpriteAnimation)
                    .GetField("currentFrame", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(animation, 3);
                typeof(PompothSpriteAnimation)
                    .GetField("frameTimer", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(animation, 0.04f);
                visuals.SetPassiveState(5, 0.5f, 1f);
                Assert.AreEqual(1, visuals.CurrentTier);
                Assert.AreEqual(3, animation.CurrentFrameIndex);
                Assert.AreEqual(0.04f / animation.FrameDuration, animation.NormalizedFrameTime, 0.0001f);
                Assert.AreEqual("Pompoth LockedOn Sheet_3", instance.GetComponent<SpriteRenderer>().sprite.name);

                visuals.SetPassiveState(0, 0f, 1f);
                Assert.AreEqual(0, visuals.CurrentTier);

                visuals.SetPassiveState(10, 1f, 1f);
                Assert.AreEqual(2, visuals.CurrentTier);
                instance.SetActive(false);
                instance.SetActive(true);
                Assert.AreEqual(0, visuals.CurrentTier);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static int GetTierForEnemyCount(int enemyCount, OperatorRarity rarity)
        {
            float bonus = PompothOperator.GetAttackSpeedPassiveBonus(enemyCount, rarity);
            float cap = PompothOperator.GetAttackSpeedPassiveCap(rarity);
            return PompothPowerVisuals.GetPowerTier(bonus, cap);
        }
    }
}
