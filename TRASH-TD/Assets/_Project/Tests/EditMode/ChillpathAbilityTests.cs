using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;
using TrashTD.Systems;
using UnityEditor;

namespace TrashTD.Tests
{
    public class ChillpathAbilityTests
    {
        private const string ChillpathPrefabPath = "Assets/_Project/Prefabs/Operators/Prefab_OP_Chillpath.prefab";
        private const string ChillpathDataPath = "Assets/_Project/Data/Operators/OP_Caster_Chillpath.asset";

        private GameObject chillpathObject;
        private GameObject gridObject;
        private GameObject operatorManagerObject;
        private GameObject enemyManagerObject;
        private GameObject targetAObject;
        private GameObject targetBObject;

        private OperatorData chillpathData;
        private EnemyData enemyDataA;
        private EnemyData enemyDataB;
        private StageData stageData;

        private ChillpathOperator chillpath;
        private GridManager gridManager;
        private OperatorManager operatorManager;
        private EnemyManager enemyManager;
        private GruntEnemy targetA;
        private GruntEnemy targetB;

        [Test]
        public void SkillDescription_ListsBaseAndBothUpgradeTiers()
        {
            var data = AssetDatabase.LoadAssetAtPath<OperatorData>(ChillpathDataPath);
            Assert.IsNotNull(data);
            StringAssert.Contains("Base (1\u2605)", data.skillDescription);
            StringAssert.Contains("1st upgrade (2\u2605)", data.skillDescription);
            StringAssert.Contains("50% more damage to frozen enemies", data.skillDescription);
            StringAssert.Contains("2nd upgrade (3\u2605)", data.skillDescription);
            StringAssert.Contains("Fires 3 shards per attack", data.skillDescription);
            StringAssert.Contains("freeze haste remains", data.skillDescription);
        }

        [Test]
        public void AttackAnimation_UsesAllFramesWithSpecifiedTimingAndAlignedPivots()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChillpathPrefabPath);
            Assert.IsNotNull(prefab);

            var animation = prefab.GetComponent<ChillpathSpriteAnimation>();
            Assert.IsNotNull(animation);
            Assert.AreEqual(8, animation.Frames.Length);
            Assert.AreEqual(ChillpathSpriteAnimation.AttackFrameCount, animation.AttackFrames.Length);
            Assert.AreEqual(ChillpathSpriteAnimation.CastFrameIndex, 9);
            Assert.AreEqual(0.74f, ChillpathSpriteAnimation.CastWindupSeconds, 0.0001f);
            Assert.AreEqual(0.99f, ChillpathSpriteAnimation.AttackDuration, 0.0001f);

            foreach (Sprite frame in animation.Frames)
            {
                Assert.AreEqual(new Vector2(32f, 32f), frame.rect.size);
                Assert.AreEqual(32f, frame.pixelsPerUnit);
                Assert.AreEqual(new Vector2(16f, 16f), frame.pivot);
            }

            foreach (Sprite frame in animation.AttackFrames)
            {
                Assert.AreEqual(new Vector2(32f, 64f), frame.rect.size);
                Assert.AreEqual(32f, frame.pixelsPerUnit);
                Assert.AreEqual(new Vector2(16f, 16f), frame.pivot);
            }

            float[] expectedDurations =
            {
                0.10f, 0.09f, 0.08f, 0.08f, 0.08f, 0.08f,
                0.08f, 0.08f, 0.07f, 0.08f, 0.08f, 0.09f
            };
            for (int i = 0; i < expectedDurations.Length; i++)
                Assert.AreEqual(expectedDurations[i], ChillpathSpriteAnimation.GetAttackFrameDuration(i), 0.0001f);
        }

        [Test]
        public void Muzzle_IsAtCrystalPositionAndMirrorsWithFacing()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChillpathPrefabPath);
            Assert.IsNotNull(prefab);

            var instance = Object.Instantiate(prefab);
            instance.transform.position = new Vector3(4f, 5f, 0f);
            var data = ScriptableObject.CreateInstance<OperatorData>();
            var chillpath = instance.GetComponent<ChillpathOperator>();

            chillpath.Initialize(data, OperatorRarity.Star1, OperatorFacing.Left);
            Assert.AreEqual(new Vector3(4f - 1.5f / 32f, 5f + 14.5f / 32f, 0f),
                instance.transform.Find("Muzzle").position);

            instance.GetComponent<SpriteRenderer>().flipX = true;
            Assert.AreEqual(new Vector3(4f + 1.5f / 32f, 5f + 14.5f / 32f, 0f),
                instance.GetComponent<ChillpathSpriteAnimation>().Muzzle.position);

            instance.GetComponent<SpriteRenderer>().flipX = false;
            instance.transform.localScale = new Vector3(-1f, 1f, 1f);
            Assert.AreEqual(new Vector3(4f + 1.5f / 32f, 5f + 14.5f / 32f, 0f),
                instance.GetComponent<ChillpathSpriteAnimation>().Muzzle.position);

            instance.transform.localScale = Vector3.one;
            chillpath.SetFacing(OperatorFacing.Right);
            Assert.AreEqual(new Vector3(4f + 1.5f / 32f, 5f + 14.5f / 32f, 0f),
                instance.transform.Find("Muzzle").position);

            Object.DestroyImmediate(data);
            Object.DestroyImmediate(instance);
        }

        [Test]
        public void HealthBar_UsesIdleSpriteHeightWhenCreatedDuringAttackAnimation()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChillpathPrefabPath);
            Assert.IsNotNull(prefab);

            var instance = Object.Instantiate(prefab);
            var animation = instance.GetComponent<ChillpathSpriteAnimation>();
            var spriteRenderer = instance.GetComponent<SpriteRenderer>();
            spriteRenderer.sprite = animation.AttackFrames[0];

            WorldHealthBar.UpdateFor(instance, 50, 100);

            var healthBar = instance.GetComponent<WorldHealthBar>();
            var offsetField = typeof(WorldHealthBar).GetField("verticalOffset", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(offsetField);
            float actualOffset = (float)offsetField.GetValue(healthBar);
            float expectedOffset = animation.Frames[0].bounds.extents.y + 0.15f;
            Assert.AreEqual(expectedOffset, actualOffset, 0.0001f);

            Object.DestroyImmediate(instance);
        }

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

            operatorManagerObject = new GameObject("TestOperatorManager");
            operatorManager = operatorManagerObject.AddComponent<OperatorManager>();

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
            operatorManager.RegisterOperator(chillpath);

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
            if (operatorManagerObject != null) Object.DestroyImmediate(operatorManagerObject);
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

        [Test]
        public void FirstUpgrade_OperatorsDealFiftyPercentMoreDamageToFrozenEnemiesWhileDeployed()
        {
            Assert.IsTrue(chillpath.HasFrozenDamageUpgrade);
            Assert.IsTrue(ChillpathOperator.IsFrozenDamageBonusActive);
            Assert.IsTrue(targetA.ApplyChill(
                chillpathData.chillFreezeThreshold,
                chillpathData.chillSlowMultiplier,
                chillpathData.chillSlowDuration,
                chillpathData.chillFreezeThreshold,
                chillpathData.chillFreezeDuration));
            Assert.IsTrue(targetA.IsFrozen);

            int hpBeforeHit = targetA.CurrentHP;
            Assert.IsTrue(targetA.TryTakeAttackDamage(20, DamageType.Physical, chillpath));
            Assert.AreEqual(hpBeforeHit - 30, targetA.CurrentHP);

            chillpath.Retreat();
            Assert.IsFalse(ChillpathOperator.IsFrozenDamageBonusActive);

            int hpAfterRetreat = targetA.CurrentHP;
            Assert.IsTrue(targetA.TryTakeAttackDamage(20, DamageType.Physical, chillpath));
            Assert.AreEqual(hpAfterRetreat - 20, targetA.CurrentHP);
        }

        [Test]
        public void SecondUpgrade_FiresThreeShardsAtDifferentTargetsWhenAvailable()
        {
            UpgradeAndRedeployChillpath(OperatorRarity.Star3);
            enemyManager.RegisterActiveEnemyForTesting(targetA);
            enemyManager.RegisterActiveEnemyForTesting(targetB);

            int projectileCountBefore = Object.FindObjectsByType<CombatProjectileVisual>().Length;
            chillpath.Attack(targetA);

            Assert.AreEqual(targetB, chillpath.LastUpgradeShardTarget);
            Assert.AreEqual(projectileCountBefore + ChillpathOperator.UpgradedShardCount,
                Object.FindObjectsByType<CombatProjectileVisual>().Length);

            float normalInterval = chillpath.GetAttackInterval();
            chillpath.ApplyAttackImpact(targetA);
            chillpath.ApplyAttackImpact(targetA);
            chillpath.ApplyAttackImpact(targetA);
            Assert.IsTrue(chillpath.IsHasteActive);
            Assert.AreEqual(normalInterval * 0.75f, chillpath.GetAttackInterval(), 0.001f);
        }

        [Test]
        public void SecondUpgrade_FiresAllThreeShardsAtSameTargetWhenAlone()
        {
            UpgradeAndRedeployChillpath(OperatorRarity.Star3);
            enemyManager.RegisterActiveEnemyForTesting(targetA);

            int projectileCountBefore = Object.FindObjectsByType<CombatProjectileVisual>().Length;
            chillpath.Attack(targetA);

            Assert.AreEqual(targetA, chillpath.LastUpgradeShardTarget);
            Assert.AreEqual(projectileCountBefore + ChillpathOperator.UpgradedShardCount,
                Object.FindObjectsByType<CombatProjectileVisual>().Length);
        }

        private void UpgradeAndRedeployChillpath(OperatorRarity rarity)
        {
            chillpath.Retreat();
            chillpath.Initialize(chillpathData, rarity);
            Assert.IsTrue(chillpath.Deploy(gridManager.GetCell(new Vector2Int(0, 0))));
            operatorManager.RegisterOperator(chillpath);
        }
    }
}
