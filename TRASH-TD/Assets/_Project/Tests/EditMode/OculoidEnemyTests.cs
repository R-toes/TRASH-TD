using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Tests
{
    public class OculoidEnemyTests
    {
        private GameObject gridObject;
        private GridManager gridManager;
        private GameObject enemyManagerObject;
        private EnemyManager enemyManager;
        private StageData stageData;

        [SetUp]
        public void SetUp()
        {
            gridObject = new GameObject("TestGridManager");
            gridManager = gridObject.AddComponent<GridManager>();

            stageData = ScriptableObject.CreateInstance<StageData>();
            stageData.gridWidth = 6;
            stageData.gridHeight = 3;
            stageData.tileLayout = new TileType[18];

            for (int x = 0; x < 6; x++)
            {
                stageData.tileLayout[0 * 6 + x] = TileType.LowGround;
            }
            for (int y = 1; y < 3; y++)
            {
                for (int x = 0; x < 6; x++)
                {
                    stageData.tileLayout[y * 6 + x] = TileType.HighGround;
                }
            }

            gridManager.InitializeFromStageData(stageData);

            enemyManagerObject = new GameObject("TestEnemyManager");
            enemyManager = enemyManagerObject.AddComponent<EnemyManager>();
            enemyManager.Initialize(gridManager);
        }

        [TearDown]
        public void TearDown()
        {
            TestUtility.ClearSingletons();
            if (enemyManagerObject != null) Object.DestroyImmediate(enemyManagerObject);
            if (gridObject != null) Object.DestroyImmediate(gridObject);
            if (stageData != null) Object.DestroyImmediate(stageData);
        }

        private List<Vector3> CreateStraightPath()
        {
            var path = new List<Vector3>();
            for (int x = 0; x < 6; x++)
            {
                path.Add(gridManager.GridToWorldPosition(x, 0));
            }
            return path;
        }

        private (GameObject obj, GuardOperator op) CreateGuardOperator(Vector2Int gridPos, int blockCount = 2)
        {
            var opObj = new GameObject($"Guard_{gridPos.x}_{gridPos.y}");
            var op = opObj.AddComponent<GuardOperator>();

            var opData = ScriptableObject.CreateInstance<OperatorData>();
            opData.operatorName = "TestGuard";
            opData.position = OperatorPosition.Melee;
            opData.baseHP = 500;
            opData.baseATK = 50;
            opData.baseDEF = 20;
            opData.blockCount = blockCount;
            opData.attackRange = 1;
            opData.attackInterval = 1.0f;
            opData.rangePattern = new[] { new Vector2Int(1, 0) };

            GridCell cell = gridManager.GetCell(gridPos);
            opObj.transform.position = gridManager.GridToWorldPosition(gridPos);
            op.Initialize(opData, OperatorRarity.Star1, OperatorFacing.Right);
            op.Deploy(cell);

            return (opObj, op);
        }

        private (GameObject obj, OculoidCarrierEnemy carrier, EnemyData carrierData, EnemyData spawnData) CreateCarrier(List<Vector3> path)
        {
            var carrierObj = new GameObject("TestCarrier");
            carrierObj.AddComponent<SpriteRenderer>();
            var carrier = carrierObj.AddComponent<OculoidCarrierEnemy>();

            var spawnData = ScriptableObject.CreateInstance<EnemyData>();
            spawnData.enemyName = "Oculite Spawn";
            spawnData.archetype = EnemyArchetype.Grunt;
            spawnData.baseHP = 220;
            spawnData.baseATK = 25;
            spawnData.baseDEF = 30;
            spawnData.baseRES = 0;
            spawnData.moveSpeed = 0.85f;
            spawnData.isAcidRainImmune = true;
            spawnData.isAcidTileImmune = true;

            var carrierData = ScriptableObject.CreateInstance<EnemyData>();
            carrierData.enemyName = "Oculoid Carrier";
            carrierData.archetype = EnemyArchetype.Tank;
            carrierData.baseHP = 850;
            carrierData.baseATK = 40;
            carrierData.baseDEF = 45;
            carrierData.baseRES = 0;
            carrierData.moveSpeed = 0.5f;
            carrierData.isAcidRainImmune = true;
            carrierData.isAcidTileImmune = true;

            carrier.SetOculiteSpawnData(spawnData);
            carrier.Initialize(carrierData, 0);
            carrier.SetGridManager(gridManager);
            carrier.SetPath(path);
            enemyManager.RegisterActiveEnemyForTesting(carrier);

            return (carrierObj, carrier, carrierData, spawnData);
        }

        [Test]
        public void CarrierAndSpawn_AreImmuneToAcidRainAndTiles()
        {
            var path = CreateStraightPath();
            var (carrierObj, carrier, carrierData, spawnData) = CreateCarrier(path);

            var spawnObj = new GameObject("TestSpawn");
            spawnObj.AddComponent<SpriteRenderer>();
            var spawn = spawnObj.AddComponent<OculiteSpawnEnemy>();
            spawn.Initialize(spawnData, 0);

            Assert.IsTrue(carrier.IsImmuneToAcidRain, "Carrier must be immune to acid rain.");
            Assert.IsTrue(carrier.IsImmuneToAcidTiles, "Carrier must be immune to acid tiles.");
            Assert.IsTrue(carrierData.isAcidRainImmune, "Carrier EnemyData must flag acid rain immunity.");
            Assert.IsTrue(carrierData.isAcidTileImmune, "Carrier EnemyData must flag acid tile immunity.");

            Assert.IsTrue(spawn.IsImmuneToAcidRain, "Spawn must be immune to acid rain.");
            Assert.IsTrue(spawn.IsImmuneToAcidTiles, "Spawn must be immune to acid tiles.");
            Assert.IsTrue(spawnData.isAcidRainImmune, "Spawn EnemyData must flag acid rain immunity.");
            Assert.IsTrue(spawnData.isAcidTileImmune, "Spawn EnemyData must flag acid tile immunity.");

            Object.DestroyImmediate(spawnObj);
            Object.DestroyImmediate(carrierObj);
            Object.DestroyImmediate(carrierData);
            Object.DestroyImmediate(spawnData);
        }

        [Test]
        public void Carrier_WhenBlocked_ReducesBlockCountBy1()
        {
            var path = CreateStraightPath();
            var (carrierObj, carrier, carrierData, spawnData) = CreateCarrier(path);
            var (opObj, op) = CreateGuardOperator(new Vector2Int(2, 0), blockCount: 2);

            Assert.AreEqual(2, op.GetBlockCount(), "Initial block count should be 2.");
            Assert.AreEqual(0, op.BlockCountModifier);

            bool canBlock = BlockingSystem.CanBlock(op, carrier);
            Assert.IsTrue(canBlock, "Operator with 2 block count should be eligible to block carrier.");

            bool blocked = BlockingSystem.TryEngageBlock(op, carrier);
            Assert.IsTrue(blocked, "Operator should successfully block carrier.");

            // Debuff applied: -1 block count
            Assert.AreEqual(1, op.GetBlockCount(), "Block count should be reduced to 1 while blocking carrier.");
            Assert.AreEqual(-1, op.BlockCountModifier, "BlockCountModifier should be -1.");
            Assert.AreEqual(1, op.GetCurrentBlockCount(), "Operator is blocking 1 enemy.");

            // Create a second enemy trying to be blocked
            var gruntObj = new GameObject("Grunt2");
            gruntObj.AddComponent<SpriteRenderer>();
            var grunt = gruntObj.AddComponent<GruntEnemy>();
            var gruntData = ScriptableObject.CreateInstance<EnemyData>();
            gruntData.baseHP = 100;
            grunt.Initialize(gruntData, 0);

            bool canBlockSecond = BlockingSystem.CanBlock(op, grunt);
            Assert.IsFalse(canBlockSecond, "Operator holding carrier should not be able to block a second unit because effective capacity is 1.");

            Object.DestroyImmediate(gruntObj);
            Object.DestroyImmediate(gruntData);
            Object.DestroyImmediate(carrierObj);
            Object.DestroyImmediate(carrierData);
            Object.DestroyImmediate(spawnData);
            Object.DestroyImmediate(opObj);
        }

        [Test]
        public void Operator_With1BlockCount_CannotBlockCarrier()
        {
            var path = CreateStraightPath();
            var (carrierObj, carrier, carrierData, spawnData) = CreateCarrier(path);
            var (opObj, op) = CreateGuardOperator(new Vector2Int(2, 0), blockCount: 1);

            Assert.AreEqual(1, op.GetBlockCount(), "Initial block count should be 1.");

            bool canBlock = BlockingSystem.CanBlock(op, carrier);
            Assert.IsFalse(canBlock, "Operator with 1 block count cannot block carrier because the -1 block debuff would leave 0 capacity.");

            bool tryBlockResult = op.TryBlock(carrier);
            Assert.IsFalse(tryBlockResult, "Direct TryBlock on 1-block operator should also be rejected.");

            Object.DestroyImmediate(carrierObj);
            Object.DestroyImmediate(carrierData);
            Object.DestroyImmediate(spawnData);
            Object.DestroyImmediate(opObj);
        }

        [Test]
        public void Carrier_WhenUnblocked_RestoresBlockCount()
        {
            var path = CreateStraightPath();
            var (carrierObj, carrier, carrierData, spawnData) = CreateCarrier(path);
            var (opObj, op) = CreateGuardOperator(new Vector2Int(2, 0), blockCount: 2);

            BlockingSystem.TryEngageBlock(op, carrier);
            Assert.AreEqual(1, op.GetBlockCount());

            // Release the block
            op.ReleaseBlock(carrier);

            Assert.AreEqual(2, op.GetBlockCount(), "Block count should be restored to 2 after release.");
            Assert.AreEqual(0, op.BlockCountModifier, "BlockCountModifier should be restored to 0.");
            Assert.AreEqual(0, op.GetCurrentBlockCount(), "No enemies should be blocked.");

            Object.DestroyImmediate(carrierObj);
            Object.DestroyImmediate(carrierData);
            Object.DestroyImmediate(spawnData);
            Object.DestroyImmediate(opObj);
        }

        [Test]
        public void Carrier_WhenKilled_RestoresBlockCountAndSpawnsTwoOculites()
        {
            var path = CreateStraightPath();
            var (carrierObj, carrier, carrierData, spawnData) = CreateCarrier(path);
            var (opObj, op) = CreateGuardOperator(new Vector2Int(2, 0), blockCount: 2);

            BlockingSystem.TryEngageBlock(op, carrier);
            Assert.AreEqual(1, op.GetBlockCount());
            Assert.AreEqual(1, enemyManager.ActiveEnemyCount);

            TestUtility.ExpectEditModeDestroy();

            // Deal fatal damage to carrier
            carrier.TakeDamage(1000, DamageType.True);

            // Block debuff must be removed from operator
            Assert.AreEqual(2, op.GetBlockCount(), "Block count should be restored to 2 when carrier dies.");
            Assert.AreEqual(0, op.BlockCountModifier);

            // Carrier is dead, and two Oculite Spawns should have spawned
            Assert.IsTrue(carrier.IsDead);
            Assert.AreEqual(2, enemyManager.ActiveEnemyCount, "Two Oculite Spawns should be spawned into active enemies.");

            foreach (var enemy in enemyManager.ActiveEnemies)
            {
                Assert.AreEqual("Oculite Spawn", enemy.Data.enemyName);
                Assert.IsTrue(enemy.IsImmuneToAcidRain);
                Assert.IsTrue(enemy.IsImmuneToAcidTiles);
                Assert.IsNotNull(enemy.Path, "Spawned children must have a movement path assigned.");
                Assert.Greater(enemy.Path.Count, 0, "Spawned children path must not be empty.");
            }

            Object.DestroyImmediate(carrierObj);
            Object.DestroyImmediate(carrierData);
            Object.DestroyImmediate(spawnData);
            Object.DestroyImmediate(opObj);
        }
    }
}

