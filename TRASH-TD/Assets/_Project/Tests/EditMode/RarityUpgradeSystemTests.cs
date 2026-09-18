using NUnit.Framework;
using UnityEngine;
using TrashTD.Data;
using TrashTD.Systems;

namespace TrashTD.Tests
{
    public class RarityUpgradeSystemTests
    {
        private GameObject upgradeObject;
        private RarityUpgradeSystem upgradeSystem;
        private OperatorData testOperator;

        [SetUp]
        public void SetUp()
        {
            upgradeObject = new GameObject("TestUpgradeSystem");
            upgradeSystem = upgradeObject.AddComponent<RarityUpgradeSystem>();

            testOperator = ScriptableObject.CreateInstance<OperatorData>();
            testOperator.operatorName = "TrashBot";
            testOperator.baseRarity = OperatorRarity.Star1;
        }

        [TearDown]
        public void TearDown()
        {
            if (upgradeObject != null)
            {
                Object.DestroyImmediate(upgradeObject);
            }
        }

        [Test]
        public void ThreeDuplicates_UpgradesRarityByOneTier()
        {
            bool upgradeFired = false;
            OperatorRarity upgradedTo = OperatorRarity.Star1;

            upgradeSystem.OnOperatorUpgraded += (op, oldR, newR) =>
            {
                upgradeFired = true;
                upgradedTo = newR;
            };

            // Add 1st copy
            upgradeSystem.AddCardCopy(testOperator, OperatorRarity.Star1);
            Assert.IsFalse(upgradeFired);
            Assert.AreEqual(1, upgradeSystem.GetCopyCount(testOperator.operatorName, OperatorRarity.Star1));

            // Add 2nd copy
            upgradeSystem.AddCardCopy(testOperator, OperatorRarity.Star1);
            Assert.IsFalse(upgradeFired);
            Assert.AreEqual(2, upgradeSystem.GetCopyCount(testOperator.operatorName, OperatorRarity.Star1));

            // Add 3rd copy -> triggers upgrade to Star2
            upgradeSystem.AddCardCopy(testOperator, OperatorRarity.Star1);
            Assert.IsTrue(upgradeFired, "Collecting 3 duplicate copies must upgrade rarity by one tier (GDD 1.6)");
            Assert.AreEqual(OperatorRarity.Star2, upgradedTo);
            Assert.AreEqual(0, upgradeSystem.GetCopyCount(testOperator.operatorName, OperatorRarity.Star1));
            Assert.AreEqual(1, upgradeSystem.GetCopyCount(testOperator.operatorName, OperatorRarity.Star2));
            Assert.AreEqual(OperatorRarity.Star2, upgradeSystem.GetHighestRarity(testOperator));
        }

        [Test]
        public void CascadingUpgrade_ReachesStarThreeWithNineCopies()
        {
            // 9 Star1 copies -> 3 Star2 copies -> 1 Star3 copy
            for (int i = 0; i < 9; i++)
            {
                upgradeSystem.AddCardCopy(testOperator, OperatorRarity.Star1);
            }

            Assert.AreEqual(OperatorRarity.Star3, upgradeSystem.GetHighestRarity(testOperator));
            Assert.AreEqual(1, upgradeSystem.GetCopyCount(testOperator.operatorName, OperatorRarity.Star3));
            Assert.AreEqual(0, upgradeSystem.GetCopyCount(testOperator.operatorName, OperatorRarity.Star2));
            Assert.AreEqual(0, upgradeSystem.GetCopyCount(testOperator.operatorName, OperatorRarity.Star1));
        }
    }
}
