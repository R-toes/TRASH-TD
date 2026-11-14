using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Tests
{
    public class MlemOperatorTests
    {
        private GameObject gridObject;
        private GridManager gridManager;
        private GameObject enemyManagerObject;
        private EnemyManager enemyManager;
        private StageData stageData;
        private OperatorData mlemData;
        private GameObject mlemObject;
        private MlemOperator mlem;
        private readonly List<GameObject> enemyObjects = new List<GameObject>();
        private readonly List<EnemyData> enemyDataAssets = new List<EnemyData>();
        private List<Vector3> path;

        [SetUp]
        public void SetUp()
        {
            gridObject = new GameObject("TestGridManager");
            gridManager = gridObject.AddComponent<GridManager>();
            stageData = ScriptableObject.CreateInstance<StageData>();
            stageData.gridWidth = 6;
            stageData.gridHeight = 3;
            stageData.tileLayout = new TileType[18];
            for (int i = 0; i < 18; i++) stageData.tileLayout[i] = TileType.LowGround;
            gridManager.InitializeFromStageData(stageData);

            enemyManagerObject = new GameObject("TestEnemyManager");
            enemyManager = enemyManagerObject.AddComponent<EnemyManager>();
            enemyManager.Initialize(gridManager);
            SetEnemyManagerInstance(enemyManager); // Awake doesn't run in edit mode

            path = new List<Vector3>();
            for (int x = 0; x < 6; x++) path.Add(gridManager.GridToWorldPosition(x, 0));

            mlemData = ScriptableObject.CreateInstance<OperatorData>();
            mlemData.operatorName = "Mlem";
            mlemData.operatorClass = OperatorClass.Defender;
            mlemData.baseRarity = OperatorRarity.Star2;
            mlemData.position = OperatorPosition.Melee;
            mlemData.baseHP = 200;
            mlemData.baseATK = 30;
            mlemData.baseDEF = 40;
            mlemData.blockCount = 4;
            mlemData.attackInterval = 1f;
            mlemData.rangePattern = new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0) };

            mlemObject = new GameObject("TestMlem");
            mlem = mlemObject.AddComponent<MlemOperator>();
            Deploy(OperatorRarity.Star2);
        }

        [TearDown]
        public void TearDown()
        {
            SetEnemyManagerInstance(null);
            TestUtility.ClearSingletons();
            foreach (GameObject enemyObject in enemyObjects)
                if (enemyObject != null) Object.DestroyImmediate(enemyObject);
            foreach (EnemyData enemyData in enemyDataAssets)
                if (enemyData != null) Object.DestroyImmediate(enemyData);
            enemyObjects.Clear();
            enemyDataAssets.Clear();

            if (mlemObject != null) Object.DestroyImmediate(mlemObject);
            if (mlemData != null) Object.DestroyImmediate(mlemData);
            if (enemyManagerObject != null) Object.DestroyImmediate(enemyManagerObject);
            if (gridObject != null) Object.DestroyImmediate(gridObject);
            if (stageData != null) Object.DestroyImmediate(stageData);
        }

        private static void SetEnemyManagerInstance(EnemyManager manager)
        {
            typeof(EnemyManager).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)
                .SetValue(null, manager);
        }

        private void Deploy(OperatorRarity rarity)
        {
            if (mlem.IsDeployed) mlem.Retreat();
            mlem.Initialize(mlemData, rarity, OperatorFacing.Right);
            Assert.IsTrue(mlem.Deploy(gridManager.GetCell(0, 0)));
        }

        private EnemyBase CreateEnemy(EnemyArchetype archetype, int cellX, int hp = 100,
            EnemyMovementType movement = EnemyMovementType.Ground)
        {
            var data = ScriptableObject.CreateInstance<EnemyData>();
            data.enemyName = archetype.ToString();
            data.archetype = archetype;
            data.movementType = movement;
            data.baseHP = hp;
            data.baseATK = 20;
            enemyDataAssets.Add(data);

            var enemyObject = new GameObject($"Test{archetype}{cellX}");
            enemyObjects.Add(enemyObject);
            enemyObject.AddComponent<SpriteRenderer>();
            var enemy = enemyObject.AddComponent<GruntEnemy>();
            enemy.Initialize(data, 1);
            enemy.SetGridManager(gridManager);
            enemy.SetPath(path);
            enemy.transform.position = gridManager.GridToWorldPosition(cellX, 0);
            enemyManager.RegisterActiveEnemyForTesting(enemy);
            return enemy;
        }

        private EnemyBase FindTarget()
        {
            return (EnemyBase)typeof(MlemOperator)
                .GetMethod("FindTarget", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(mlem, null);
        }

        // ---------------------------------------------------------------------------------------------
        // Numbers
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void DigestionTime_FollowsTheEnemyType()
        {
            Assert.AreEqual(3f, MlemOperator.GetDigestDuration(mlemData, false, new[] { EnemyArchetype.Rusher }), 0.001f);
            Assert.AreEqual(4.5f, MlemOperator.GetDigestDuration(mlemData, false, new[] { EnemyArchetype.Grunt }), 0.001f);
            Assert.AreEqual(6f, MlemOperator.GetDigestDuration(mlemData, false, new[] { EnemyArchetype.Caster }), 0.001f);
            Assert.AreEqual(12f, MlemOperator.GetDigestDuration(mlemData, false, new[] { EnemyArchetype.Tank }), 0.001f);
        }

        [Test]
        public void FirstUpgrade_ShortensDigestionByTwentyPercent()
        {
            Assert.AreEqual(9.6f, MlemOperator.GetDigestDuration(mlemData, true, new[] { EnemyArchetype.Tank }), 0.001f);
            Assert.AreEqual(2.4f, MlemOperator.GetDigestDuration(mlemData, true, new[] { EnemyArchetype.Rusher }), 0.001f);
        }

        [Test]
        public void SeveralSwallowed_DigestOverTheAverage_WithTheReductionOnTop()
        {
            var eaten = new[] { EnemyArchetype.Tank, EnemyArchetype.Rusher, EnemyArchetype.Grunt }; // 12, 3, 4.5
            Assert.AreEqual(19.5f / 3f, MlemOperator.GetDigestDuration(mlemData, false, eaten), 0.001f);
            Assert.AreEqual(19.5f / 3f * 0.8f, MlemOperator.GetDigestDuration(mlemData, true, eaten), 0.001f);
        }

        [Test]
        public void UpgradesFollowTheCardsBaseRarity_AndTheSwallowCountComesFromData()
        {
            Deploy(OperatorRarity.Star2);
            Assert.IsFalse(mlem.HasFirstUpgrade);
            Assert.AreEqual(1, mlem.MaxSwallowCount);

            Deploy(OperatorRarity.Star3);
            Assert.IsTrue(mlem.HasFirstUpgrade);
            Assert.IsFalse(mlem.HasSecondUpgrade);
            Assert.AreEqual(1, mlem.MaxSwallowCount);

            Deploy(OperatorRarity.Star4);
            Assert.IsTrue(mlem.HasSecondUpgrade);
            Assert.AreEqual(3, mlem.MaxSwallowCount);

            mlemData.devourMaxSwallowUpgrade2 = 5;
            Assert.AreEqual(5, mlem.MaxSwallowCount, "The count is data, not a constant.");
        }

        // ---------------------------------------------------------------------------------------------
        // Targeting
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void Targets_TankThenCasterThenRusherThenGrunt()
        {
            EnemyBase grunt = CreateEnemy(EnemyArchetype.Grunt, 1);
            EnemyBase rusher = CreateEnemy(EnemyArchetype.Rusher, 1);
            EnemyBase caster = CreateEnemy(EnemyArchetype.Caster, 1);
            EnemyBase tank = CreateEnemy(EnemyArchetype.Tank, 1);

            Assert.AreSame(tank, FindTarget());
            TestUtility.ExpectEditModeDestroy();
            tank.ForceKill();
            Assert.AreSame(caster, FindTarget());
            TestUtility.ExpectEditModeDestroy();
            caster.ForceKill();
            Assert.AreSame(rusher, FindTarget());
            TestUtility.ExpectEditModeDestroy();
            rusher.ForceKill();
            Assert.AreSame(grunt, FindTarget());
        }

        [Test]
        public void SameType_TheOneFurthestAlongThePathGoesFirst()
        {
            CreateEnemy(EnemyArchetype.Grunt, 0);
            EnemyBase furthest = CreateEnemy(EnemyArchetype.Grunt, 2);
            CreateEnemy(EnemyArchetype.Grunt, 1);

            Assert.AreSame(furthest, FindTarget());
        }

        [Test]
        public void Reach_IsHisOwnTileAndTwoInFront_NotThree()
        {
            EnemyBase far = CreateEnemy(EnemyArchetype.Tank, 3);
            Assert.IsNull(FindTarget(), "3 tiles away is out of range.");

            far.transform.position = gridManager.GridToWorldPosition(2, 0);
            Assert.AreSame(far, FindTarget(), "2 tiles in front is in range.");

            far.transform.position = gridManager.GridToWorldPosition(0, 0);
            Assert.AreSame(far, FindTarget(), "His own tile is in range.");
        }

        [Test]
        public void FlyingEnemies_AreNeverTargeted()
        {
            CreateEnemy(EnemyArchetype.Flyer, 1, 100, EnemyMovementType.Air);
            CreateEnemy(EnemyArchetype.Grunt, 1, 100, EnemyMovementType.Air);

            Assert.IsNull(FindTarget());
            Assert.IsFalse(MlemOperator.CanDevourArchetype(EnemyArchetype.Flyer));
        }

        // ---------------------------------------------------------------------------------------------
        // Swallowing
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void Swallow_IsAnInstantKill_WhateverTheHP()
        {
            EnemyBase tank = CreateEnemy(EnemyArchetype.Tank, 1, hp: 1000000);
            bool died = false;
            tank.OnDied += _ => died = true;

            TestUtility.ExpectEditModeDestroy();
            mlem.Attack(tank);

            Assert.IsTrue(tank.IsDead);
            Assert.IsTrue(died, "A swallowed enemy goes through the normal death path, so it counts as a kill.");
            Assert.IsTrue(mlem.IsDigesting);
            Assert.AreEqual(12f, mlem.DigestTotal, 0.001f);
        }

        [Test]
        public void WhileDigesting_HeCannotAttack_ButStillBlocks()
        {
            EnemyBase first = CreateEnemy(EnemyArchetype.Grunt, 1);
            TestUtility.ExpectEditModeDestroy();
            mlem.Attack(first);
            Assert.IsTrue(mlem.IsDigesting);

            EnemyBase second = CreateEnemy(EnemyArchetype.Grunt, 1);
            Assert.IsNull(FindTarget());
            Assert.IsFalse(mlem.CanAttackTarget(second));
            Assert.AreEqual(4, mlem.GetMaxBlockCount(), "Blocking carries on while he digests.");
            Assert.IsTrue(mlem.TryBlock(second));
        }

        [Test]
        public void Digestion_GivesBonusDEFAndMaxHP_ThenRemovesThemAndClampsHP()
        {
            int baseMaxHP = mlem.MaxHP;
            int baseDEF = mlem.CurrentDEF;

            EnemyBase grunt = CreateEnemy(EnemyArchetype.Grunt, 1);
            TestUtility.ExpectEditModeDestroy();
            mlem.Attack(grunt);

            Assert.AreEqual(baseDEF + Mathf.RoundToInt(baseDEF * mlemData.digestDefBonusFraction), mlem.CurrentDEF);
            Assert.AreEqual(baseMaxHP + Mathf.RoundToInt(baseMaxHP * mlemData.digestMaxHpBonusFraction), mlem.MaxHP);
            Assert.AreEqual(baseMaxHP, mlem.CurrentHP, "Eating raises his max HP but doesn't heal him.");

            mlem.AdvanceDigestion(4.4f);
            Assert.IsTrue(mlem.IsDigesting);
            mlem.AdvanceDigestion(0.2f);

            Assert.IsFalse(mlem.IsDigesting);
            Assert.AreEqual(baseDEF, mlem.CurrentDEF);
            Assert.AreEqual(baseMaxHP, mlem.MaxHP);
            Assert.AreEqual(baseMaxHP, mlem.CurrentHP, "Current HP is clamped to the normal max.");
        }

        [Test]
        public void ExtraMaxHP_IsRemovedWhenDigestionEnds_EvenAfterTheFirstUpgradeHealedIntoIt()
        {
            Deploy(OperatorRarity.Star3);
            int baseMaxHP = mlem.MaxHP;
            TestUtility.ExpectEditModeDestroy();
            mlem.Attack(CreateEnemy(EnemyArchetype.Grunt, 1));

            mlem.TakeTrueDamage(1);
            for (int i = 0; i < 4; i++) mlem.AdvanceDigestion(0.5f);
            Assert.IsTrue(mlem.IsDigesting);
            Assert.Greater(mlem.CurrentHP, baseMaxHP - 1, "The regen heals him back up.");

            mlem.AdvanceDigestion(5f);
            Assert.IsFalse(mlem.IsDigesting);
            Assert.AreEqual(baseMaxHP, mlem.MaxHP);
            Assert.LessOrEqual(mlem.CurrentHP, baseMaxHP, "Current HP is clamped to the normal max.");
        }

        [Test]
        public void WhenDigestionEnds_HeCanAttackAgainAtOnce()
        {
            TestUtility.ExpectEditModeDestroy();
            mlem.Attack(CreateEnemy(EnemyArchetype.Rusher, 1));
            mlem.AdvanceDigestion(10f);

            EnemyBase next = CreateEnemy(EnemyArchetype.Grunt, 1);
            Assert.AreSame(next, FindTarget());
            Assert.IsTrue(mlem.CanAttackTarget(next));
        }

        [Test]
        public void Whiff_WhenTheTargetIsOutOfReach_NothingIsSwallowedAndNoDigestionStarts()
        {
            EnemyBase away = CreateEnemy(EnemyArchetype.Tank, 5);
            mlem.Attack(away);

            Assert.IsFalse(away.IsDead);
            Assert.IsFalse(away.IsBeingDevoured);
            Assert.IsFalse(mlem.IsDigesting);
            Assert.IsFalse(mlem.IsLashing);

            EnemyBase near = CreateEnemy(EnemyArchetype.Grunt, 1);
            Assert.AreSame(near, FindTarget(), "He is ready to attack again.");
        }

        [Test]
        public void SecondUpgrade_SwallowsThreeAtOnce_ByPriority_AndDigestsThemTogether()
        {
            Deploy(OperatorRarity.Star4);
            EnemyBase tank = CreateEnemy(EnemyArchetype.Tank, 2);   // 12 s
            EnemyBase caster = CreateEnemy(EnemyArchetype.Caster, 1); // 6 s
            EnemyBase rusher = CreateEnemy(EnemyArchetype.Rusher, 1); // 3 s
            EnemyBase grunt = CreateEnemy(EnemyArchetype.Grunt, 1);   // left alone: only 3 fit

            TestUtility.ExpectEditModeDestroy();
            TestUtility.ExpectEditModeDestroy();
            TestUtility.ExpectEditModeDestroy();
            mlem.Attack(tank);

            Assert.IsTrue(tank.IsDead);
            Assert.IsTrue(caster.IsDead);
            Assert.IsTrue(rusher.IsDead);
            Assert.IsFalse(grunt.IsDead, "Only three enemies fit in one tongue attack.");
            Assert.AreEqual(3, mlem.EnemiesSwallowed);
            Assert.AreEqual((12f + 6f + 3f) / 3f * 0.8f, mlem.DigestTotal, 0.001f, "The average, with the 1st upgrade's 20% on top.");
        }

        [Test]
        public void FirstUpgrade_RecoversHPWhileDigesting()
        {
            Deploy(OperatorRarity.Star3);
            EnemyBase tank = CreateEnemy(EnemyArchetype.Tank, 1);
            TestUtility.ExpectEditModeDestroy();
            mlem.Attack(tank);

            mlem.TakeTrueDamage(100);
            int hurt = mlem.CurrentHP;
            for (int i = 0; i < 4; i++) mlem.AdvanceDigestion(0.5f);

            Assert.Greater(mlem.CurrentHP, hurt, "The 1st upgrade heals him while he digests.");
        }

        // ---------------------------------------------------------------------------------------------
        // Death effects
        // ---------------------------------------------------------------------------------------------

        [Test]
        public void SwallowedEnemy_DoesNotTriggerItsOnDeathEffect_ByDefault()
        {
            RobozombieEnemy zombie = CreateRobozombie();
            TestUtility.ExpectEditModeDestroy();
            mlem.Attack(zombie);

            Assert.IsTrue(zombie.IsDead);
            Assert.IsFalse(zombie.DidExplode);
        }

        [Test]
        public void SwallowedEnemy_TriggersItsOnDeathEffect_WhenTheFlagIsOn()
        {
            mlemData.devourTriggersDeathEffects = true;
            RobozombieEnemy zombie = CreateRobozombie();
            TestUtility.ExpectEditModeDestroy();
            mlem.Attack(zombie);

            Assert.IsTrue(zombie.IsDead);
            Assert.IsTrue(zombie.DidExplode);
        }

        private RobozombieEnemy CreateRobozombie()
        {
            var data = ScriptableObject.CreateInstance<EnemyData>();
            data.enemyName = "Zombie";
            data.archetype = EnemyArchetype.Grunt;
            data.baseHP = 100;
            data.baseATK = 20;
            enemyDataAssets.Add(data);

            var enemyObject = new GameObject("TestRobozombie");
            enemyObjects.Add(enemyObject);
            enemyObject.AddComponent<SpriteRenderer>();
            var zombie = enemyObject.AddComponent<RobozombieEnemy>();
            zombie.Initialize(data, 1);
            zombie.SetGridManager(gridManager);
            zombie.SetPath(path);
            zombie.ExplosionChance = 1f;
            zombie.transform.position = gridManager.GridToWorldPosition(1, 0);
            enemyManager.RegisterActiveEnemyForTesting(zombie);
            return zombie;
        }
    }
}
