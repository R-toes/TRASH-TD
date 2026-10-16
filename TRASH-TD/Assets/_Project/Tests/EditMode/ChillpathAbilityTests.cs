using NUnit.Framework;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Tests
{
    public class ChillpathAbilityTests
    {
        private GameObject chillpathObject;
        private GameObject gridObject;
        private GameObject enemyManagerObject;
        private GameObject targetAObject;
        private GameObject targetBObject;

        private OperatorData chillpathData;
        private EnemyData enemyDataA;
        private EnemyData enemyDataB;
        private StageData stageData;

        private ChillpathOperator chillpath;
        private GridManager gridManager;
        private EnemyManager enemyManager;
        private GruntEnemy targetA;
        private GruntEnemy targetB;

        [SetUp]
        public void SetUp()
        {
            stageData = ScriptableObject.CreateInstance<StageData>();
            stageData.gridWidth = 5;
            stageData.gridHeight = 5;
            stageData.tileLayout = new TileType[25];
            for (int i = 0; i < 25; i++)
            {
                stageData.tileLayout[i] = TileType.HighGround;
            }

            gridObject = new GameObject("TestGridManager");
            gridManager = gridObject.AddComponent<GridManager>();
            gridManager.InitializeFromStageData(stageData);

            enemyManagerObject = new GameObject("TestEnemyManager");
            enemyManager = TestUtility.AddAwakened<EnemyManager>(enemyManagerObject);

            chillpathData = ScriptableObject.CreateInstance<OperatorData>();
            chillpathData.operatorName = "Chillpath";
            chillpathData.operatorClass = OperatorClass.Caster;
            chillpathData.position = OperatorPosition.Ranged;
            chillpathData.baseHP = 90;
            chillpathData.baseATK = 25;
            chillpathData.baseDEF = 8;
            chillpathData.baseRES = 15;
            chillpathData.attackInterval = 1.4f;
            chillpathData.damageType = DamageType.Arts;
            chillpathData.rangePattern = new[]
            {
                new Vector2Int(1, 0),
                new Vector2Int(2, 0),
                new Vector2Int(1, 1),
                new Vector2Int(2, 1)
            };
            chillpathData.chillPerHit = 30f;
            chillpathData.chillSlowMultiplier = 0.5f;
            chillpathData.chillSlowDuration = 2.5f;
            chillpathData.chillFreezeThreshold = 90f;
            chillpathData.chillFreezeDuration = 1.5f;

            chillpathObject = new GameObject("TestChillpath");
            chillpathObject.AddComponent<SpriteRenderer>();
            chillpath = chillpathObject.AddComponent<ChillpathOperator>();
            chillpath.Initialize(chillpathData, OperatorRarity.Star2);

            var deployCell = gridManager.GetCell(new Vector2Int(0, 0));
            chillpath.Deploy(deployCell);

            enemyDataA = ScriptableObject.CreateInstance<EnemyData>();
            enemyDataA.baseHP = 200;
            enemyDataA.baseDEF = 10;
            enemyDataA.baseRES = 0;

            targetAObject = new GameObject("TestTargetA");
            targetA = targetAObject.AddComponent<GruntEnemy>();
            targetA.Initialize(enemyDataA, 1);
            targetA.transform.position = gridManager.GetCell(new Vector2Int(1, 0)).WorldPosition;

            enemyDataB = ScriptableObject.CreateInstance<EnemyData>();
            enemyDataB.baseHP = 200;
            enemyDataB.baseDEF = 50;
            enemyDataB.baseRES = 0;

            targetBObject = new GameObject("TestTargetB");
            targetB = targetBObject.AddComponent<GruntEnemy>();
            targetB.Initialize(enemyDataB, 1);
            targetB.transform.position = gridManager.GetCell(new Vector2Int(2, 0)).WorldPosition;
        }

        [TearDown]
        public void TearDown()
        {
            if (chillpathObject != null) Object.DestroyImmediate(chillpathObject);
            if (targetAObject != null) Object.DestroyImmediate(targetAObject);
            if (targetBObject != null) Object.DestroyImmediate(targetBObject);
            if (enemyManagerObject != null) Object.DestroyImmediate(enemyManagerObject);
            if (gridObject != null) Object.DestroyImmediate(gridObject);
            if (chillpathData != null) Object.DestroyImmediate(chillpathData);
            if (enemyDataA != null) Object.DestroyImmediate(enemyDataA);
            if (enemyDataB != null) Object.DestroyImmediate(enemyDataB);
            if (stageData != null) Object.DestroyImmediate(stageData);

            TestUtility.ClearSingletons();
        }

        [Test]
        public void Attack_IncrementsAttackCountUntil4thAttack_AndResets()
        {
            Assert.AreEqual(0, chillpath.AttackCount);
            Assert.AreEqual(0, chillpath.BonusAttackCount);

            chillpath.Attack(targetA);
            Assert.AreEqual(1, chillpath.AttackCount);
            Assert.AreEqual(0, chillpath.BonusAttackCount);

            chillpath.Attack(targetA);
            Assert.AreEqual(2, chillpath.AttackCount);
            Assert.AreEqual(0, chillpath.BonusAttackCount);

            chillpath.Attack(targetA);
            Assert.AreEqual(3, chillpath.AttackCount);
            Assert.AreEqual(0, chillpath.BonusAttackCount);

            // 4th attack triggers the bonus attack and resets counter to 0
            chillpath.Attack(targetA);
            Assert.AreEqual(0, chillpath.AttackCount);
            Assert.AreEqual(1, chillpath.BonusAttackCount);

            // 5th attack begins next cycle
            chillpath.Attack(targetA);
            Assert.AreEqual(1, chillpath.AttackCount);
            Assert.AreEqual(1, chillpath.BonusAttackCount);

            chillpath.Attack(targetA); // 6
            chillpath.Attack(targetA); // 7
            chillpath.Attack(targetA); // 8 -> 2nd bonus attack
            Assert.AreEqual(0, chillpath.AttackCount);
            Assert.AreEqual(2, chillpath.BonusAttackCount);
        }

        [Test]
        public void Attack_On4thAttack_WhenNoAdditionalTargetInRange_AppliesBonusAttackToCurrentTarget()
        {
            // Only targetA is registered with enemyManager
            enemyManager.RegisterActiveEnemyForTesting(targetA);

            chillpath.Attack(targetA);
            chillpath.Attack(targetA);
            chillpath.Attack(targetA);
            chillpath.Attack(targetA);

            Assert.AreEqual(1, chillpath.BonusAttackCount);
            Assert.AreEqual(targetA, chillpath.LastBonusTarget, "Bonus attack should target current target when no other enemy is in range.");
        }

        [Test]
        public void Attack_On4thAttack_WhenAdditionalTargetInRange_AppliesBonusAttackToAdditionalTarget()
        {
            // Both targetA and targetB are registered with enemyManager and within range
            enemyManager.RegisterActiveEnemyForTesting(targetA);
            enemyManager.RegisterActiveEnemyForTesting(targetB);

            chillpath.Attack(targetA);
            chillpath.Attack(targetA);
            chillpath.Attack(targetA);
            chillpath.Attack(targetA);

            Assert.AreEqual(1, chillpath.BonusAttackCount);
            Assert.AreEqual(targetB, chillpath.LastBonusTarget, "Bonus attack should target additional enemy in range.");
        }

        [Test]
        public void DeployAndRetreat_ResetsAttackCountAndBonusState()
        {
            chillpath.Attack(targetA);
            chillpath.Attack(targetA);
            Assert.AreEqual(2, chillpath.AttackCount);

            chillpath.Retreat();

            Assert.AreEqual(0, chillpath.AttackCount);
            Assert.AreEqual(0, chillpath.BonusAttackCount);
            Assert.IsNull(chillpath.LastBonusTarget);
        }

        [Test]
        public void FreezeHaste_DecreasesAttackInterval()
        {
            float normalInterval = chillpath.GetAttackInterval();
            Assert.IsFalse(chillpath.IsHasteActive);

            // Apply chill equal to threshold to freeze target
            chillpath.ApplyAttackImpact(targetA); // 30
            chillpath.ApplyAttackImpact(targetA); // 60
            chillpath.ApplyAttackImpact(targetA); // 90 -> Freezes!

            Assert.IsTrue(chillpath.IsHasteActive, "Chillpath should gain haste after freezing an enemy.");
            float hastedInterval = chillpath.GetAttackInterval();
            Assert.AreEqual(normalInterval * 0.75f, hastedInterval, 0.001f, "Attack interval should decrease by 25% while hasted.");
        }
    }
}

