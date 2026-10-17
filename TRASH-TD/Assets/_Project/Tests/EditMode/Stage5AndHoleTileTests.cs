using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using TrashTD.Core.GameLoop;
using TrashTD.Core.Grid;
using TrashTD.Core.Pathfinding;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Tests
{
    public class Stage5AndHoleTileTests
    {
        private GameObject gridObject;
        private GridManager gridManager;
        private GameObject enemyObject;
        private GruntEnemy enemy;
        private EnemyData enemyData;

        [SetUp]
        public void SetUp()
        {
            gridObject = new GameObject("TestGridManager");
            gridManager = gridObject.AddComponent<GridManager>();

            enemyObject = new GameObject("TestEnemy");
            enemyObject.AddComponent<SpriteRenderer>();
            enemy = enemyObject.AddComponent<GruntEnemy>();

            enemyData = ScriptableObject.CreateInstance<EnemyData>();
            enemyData.enemyName = "Test Sludge Grunt";
            enemyData.baseHP = 100;
            enemyData.baseATK = 20;
            enemyData.baseDEF = 5;
            enemyData.baseRES = 0;
            enemyData.moveSpeed = 1.0f;
            enemyData.movementType = EnemyMovementType.Ground;
            enemyData.archetype = EnemyArchetype.Grunt;
            enemyData.attackInterval = 1.5f;

            enemy.Initialize(enemyData, 0);
            enemy.SetGridManager(gridManager);
        }

        [TearDown]
        public void TearDown()
        {
            if (enemyObject != null) Object.DestroyImmediate(enemyObject);
            if (gridObject != null) Object.DestroyImmediate(gridObject);
            if (enemyData != null) Object.DestroyImmediate(enemyData);
        }

        [Test]
        public void TileType_Hole_HasExpectedProperties()
        {
            var cell = new GridCell(2, 3, TileType.Hole, new Vector3(2f, 3f, 0f));

            Assert.IsTrue(cell.IsWalkable, "Hole tile should be walkable for ground units.");
            Assert.IsTrue(cell.IsAirPassable, "Hole tile should be air passable.");
            Assert.IsFalse(cell.CanDeploy(OperatorPosition.Melee), "Operators should not be deployable on Hole tiles.");
            Assert.IsFalse(cell.CanDeploy(OperatorPosition.Ranged), "Ranged operators should not be deployable on Hole tiles.");
        }

        [Test]
        public void GridManager_GetLinkedHoleCell_FindsPairedHole()
        {
            var stage = ScriptableObject.CreateInstance<StageData>();
            stage.gridWidth = 5;
            stage.gridHeight = 5;
            stage.tileLayout = new TileType[25];

            // Set holes at (1, 1) and (3, 3)
            stage.tileLayout[1 * 5 + 1] = TileType.Hole;
            stage.tileLayout[3 * 5 + 3] = TileType.Hole;

            gridManager.InitializeFromStageData(stage);

            GridCell linkedFrom1 = gridManager.GetLinkedHoleCell(new Vector2Int(1, 1));
            Assert.IsNotNull(linkedFrom1);
            Assert.AreEqual(new Vector2Int(3, 3), linkedFrom1.GridPosition);

            GridCell linkedFrom2 = gridManager.GetLinkedHoleCell(new Vector2Int(3, 3));
            Assert.IsNotNull(linkedFrom2);
            Assert.AreEqual(new Vector2Int(1, 1), linkedFrom2.GridPosition);

            Object.DestroyImmediate(stage);
        }

        [Test]
        public void AStarPathfinder_FindsPathThroughHoleConnection()
        {
            var stage = ScriptableObject.CreateInstance<StageData>();
            stage.gridWidth = 5;
            stage.gridHeight = 5;
            stage.tileLayout = new TileType[25];

            // Block everything except spawn (0, 0), hole (1, 0), hole (3, 4), and exit (4, 4)
            for (int i = 0; i < 25; i++)
            {
                stage.tileLayout[i] = TileType.Blocked;
            }

            stage.tileLayout[0 * 5 + 0] = TileType.SpawnPoint; // (0, 0)
            stage.tileLayout[0 * 5 + 1] = TileType.Hole;       // (1, 0)
            stage.tileLayout[4 * 5 + 3] = TileType.Hole;       // (3, 4)
            stage.tileLayout[4 * 5 + 4] = TileType.ExitPoint;  // (4, 4)

            gridManager.InitializeFromStageData(stage);

            var pathfinder = new AStarPathfinder();
            pathfinder.Initialize(gridManager);

            List<Vector3> path = pathfinder.FindPath(new Vector2Int(0, 0), new Vector2Int(4, 4), EnemyMovementType.Ground);

            Assert.IsNotNull(path, "Pathfinder should find path across linked holes.");
            Assert.AreEqual(4, path.Count, "Path should contain (0,0), (1,0), (3,4), and (4,4).");
            Assert.AreEqual(gridManager.GridToWorldPosition(0, 0), path[0]);
            Assert.AreEqual(gridManager.GridToWorldPosition(1, 0), path[1]);
            Assert.AreEqual(gridManager.GridToWorldPosition(3, 4), path[2]);
            Assert.AreEqual(gridManager.GridToWorldPosition(4, 4), path[3]);

            Object.DestroyImmediate(stage);
        }

        [Test]
        public void EnemyBase_EnteringHole_HidesVisualsAndGrantsInvulnerability()
        {
            Vector3 startPos = new Vector3(1f, 1f, 0f);
            Vector3 destHolePos = new Vector3(4f, 4f, 0f);

            enemy.transform.position = startPos;
            var sr = enemy.GetComponent<SpriteRenderer>();
            Assert.IsTrue(sr.enabled);

            enemy.EnterHole(destHolePos, 1.5f);

            Assert.IsTrue(enemy.IsInsideHole, "Enemy should be marked inside hole.");
            Assert.IsFalse(sr.enabled, "Visual renderer should be disabled while inside hole.");

            // Immune to damage
            int hpBefore = enemy.CurrentHP;
            enemy.TakeDamage(30, DamageType.Physical);
            Assert.AreEqual(hpBefore, enemy.CurrentHP, "Enemy should take no damage while inside hole.");

            bool attackHit = enemy.TryTakeAttackDamage(25, DamageType.Physical);
            Assert.IsFalse(attackHit, "TryTakeAttackDamage should return false while inside hole.");
            Assert.AreEqual(hpBefore, enemy.CurrentHP);

            // Immune to pushback
            bool pushed = enemy.PushBack(1);
            Assert.IsFalse(pushed, "Enemy inside hole cannot be pushed back.");

            // Immune to chill & stun
            bool chilled = enemy.ApplyChill(10f, 0.5f, 2f, 10f, 2f);
            Assert.IsFalse(chilled);
            Assert.IsFalse(enemy.IsFrozen);

            enemy.ApplyStun(2f);
            Assert.IsFalse(enemy.IsStunned);

            // Exiting hole
            enemy.ExitHole();
            Assert.IsFalse(enemy.IsInsideHole, "Enemy should no longer be inside hole.");
            Assert.AreEqual(destHolePos, enemy.transform.position, "Enemy position should teleport to destination hole.");
            Assert.IsTrue(sr.enabled, "Visual renderer should be re-enabled after exiting hole.");
        }

        [Test]
        public void OperatorBase_CannotAttackTarget_WhenEnemyInsideHole()
        {
            var opObj = new GameObject("TestSniper");
            var sniper = opObj.AddComponent<SniperOperator>();

            var opData = ScriptableObject.CreateInstance<OperatorData>();
            opData.operatorName = "Test Sniper";
            opData.position = OperatorPosition.Ranged;
            opData.rangePattern = new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) };

            // Method reflection to test CanAttackTarget
            var method = typeof(OperatorBase).GetMethod("CanAttackTarget", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.IsTrue((bool)method.Invoke(sniper, new object[] { enemy }));

            enemy.EnterHole(new Vector3(5f, 5f, 0f), 1.5f);
            Assert.IsFalse((bool)method.Invoke(sniper, new object[] { enemy }), "Operator should not be able to attack enemy inside hole.");

            enemy.ExitHole();
            Assert.IsTrue((bool)method.Invoke(sniper, new object[] { enemy }));

            Object.DestroyImmediate(opObj);
            Object.DestroyImmediate(opData);
        }

        [Test]
        public void Stage5_AssetValidation_MatchesSpecifications()
        {
            var stage5 = Resources.Load<StageData>("Stages/STAGE_05");
            Assert.IsNotNull(stage5, "STAGE_05 asset should be loaded from Resources/Stages.");

            Assert.AreEqual("STAGE_05", stage5.stageId);
            Assert.AreEqual("Boulevard", stage5.mapName);
            Assert.AreEqual(13, stage5.gridWidth);
            Assert.AreEqual(8, stage5.gridHeight);
            Assert.AreEqual(32, stage5.visualTilePixelSize);
            Assert.AreEqual(new Vector2(0, -1), stage5.visualTileOffset);

            Assert.IsNotNull(stage5.mapVisualSprite, "mapVisualSprite should be set (Level 5 complete).");
            Assert.IsNotNull(stage5.foregroundVisualSprite, "foregroundVisualSprite should be set (Level 5 bottom).");

            Assert.AreEqual(6, stage5.spawnPoints.Length, "Stage 5 must have 6 spawn points.");
            Assert.AreEqual(1, stage5.exitPoints.Length, "Stage 5 must have 1 exit point.");
            Assert.AreEqual(new Vector2Int(12, 6), stage5.exitPoints[0]);

            // Validate Hole tiles at (5, 0) and (8, 6)
            Assert.AreEqual(TileType.Hole, stage5.GetTile(5, 0), "Tile (5, 0) should be TileType.Hole.");
            Assert.AreEqual(TileType.Hole, stage5.GetTile(8, 6), "Tile (8, 6) should be TileType.Hole.");

            // Validate Exit at (12, 6)
            Assert.AreEqual(TileType.ExitPoint, stage5.GetTile(12, 6), "Tile (12, 6) should be TileType.ExitPoint.");

            // Validate Spawns at (3,7), (4,7), (0,5), (0,4), (0,1), (12,1)
            Assert.AreEqual(TileType.SpawnPoint, stage5.GetTile(3, 7));
            Assert.AreEqual(TileType.SpawnPoint, stage5.GetTile(4, 7));
            Assert.AreEqual(TileType.SpawnPoint, stage5.GetTile(0, 5));
            Assert.AreEqual(TileType.SpawnPoint, stage5.GetTile(0, 4));
            Assert.AreEqual(TileType.SpawnPoint, stage5.GetTile(0, 1));
            Assert.AreEqual(TileType.SpawnPoint, stage5.GetTile(12, 1));

            // Validate waves
            Assert.IsNotNull(stage5.wavesEasy);
            Assert.GreaterOrEqual(stage5.wavesEasy.Length, 10);
            Assert.IsNotNull(stage5.wavesNormal);
            Assert.GreaterOrEqual(stage5.wavesNormal.Length, 20);
            Assert.IsNotNull(stage5.wavesHard);
            Assert.GreaterOrEqual(stage5.wavesHard.Length, 30);
        }

        [Test]
        public void Stage5_EnemyPaths_MatchReroutedDesign()
        {
            var stage5 = Resources.Load<StageData>("Stages/STAGE_05");
            Assert.IsNotNull(stage5, "STAGE_05 asset should be loaded.");
            Assert.IsNotNull(stage5.enemyPaths);
            Assert.AreEqual(6, stage5.enemyPaths.Length, "Stage 5 should have 6 configured enemy paths.");

            var exitPos = new Vector2Int(12, 6);

            for (int i = 0; i < stage5.enemyPaths.Length; i++)
            {
                var path = stage5.enemyPaths[i];
                Assert.AreEqual(i, path.spawnPointIndex);
                Assert.AreEqual(0, path.exitPointIndex);
                Assert.AreEqual(stage5.spawnPoints[i], path.waypoints[0], $"Path {i} should start at spawn point {stage5.spawnPoints[i]}.");
                Assert.AreEqual(exitPos, path.waypoints[path.waypoints.Length - 1], $"Path {i} should end at exit point {exitPos}.");

                // Validate each waypoint is on a walkable/hole/exit tile
                foreach (var wp in path.waypoints)
                {
                    TileType tile = stage5.GetTile(wp.x, wp.y);
                    Assert.IsTrue(tile == TileType.LowGround || tile == TileType.SpawnPoint || tile == TileType.Hole || tile == TileType.ExitPoint,
                        $"Waypoint {wp} in path {i} must be on walkable, spawn, hole, or exit tile (found {tile}).");
                }
            }

            // Path 0: (3,7) -> down col 3 to (3,1) -> hole (5,0) -> teleport to (8,6) -> row 5 -> exit (12,6)
            var p0 = stage5.enemyPaths[0].waypoints;
            CollectionAssert.Contains(p0, new Vector2Int(3, 1));
            CollectionAssert.Contains(p0, new Vector2Int(5, 0));
            CollectionAssert.Contains(p0, new Vector2Int(8, 6));
            CollectionAssert.Contains(p0, new Vector2Int(8, 5));
            CollectionAssert.Contains(p0, new Vector2Int(12, 5));

            // Path 1: (4,7) -> down col 4 to (4,1) -> hole (5,0) -> teleport to (8,6) -> row 4 -> exit (12,6)
            var p1 = stage5.enemyPaths[1].waypoints;
            CollectionAssert.Contains(p1, new Vector2Int(4, 1));
            CollectionAssert.Contains(p1, new Vector2Int(5, 0));
            CollectionAssert.Contains(p1, new Vector2Int(8, 6));
            CollectionAssert.Contains(p1, new Vector2Int(8, 4));
            CollectionAssert.Contains(p1, new Vector2Int(12, 4));

            // Path 2: (0,5) -> straight row 5 -> exit (12,6)
            var p2 = stage5.enemyPaths[2].waypoints;
            CollectionAssert.Contains(p2, new Vector2Int(0, 5));
            CollectionAssert.Contains(p2, new Vector2Int(6, 5));
            CollectionAssert.Contains(p2, new Vector2Int(12, 5));

            // Path 3: (0,4) -> straight row 4 -> (12,4) -> (12,5) -> exit (12,6)
            var p3 = stage5.enemyPaths[3].waypoints;
            CollectionAssert.Contains(p3, new Vector2Int(0, 4));
            CollectionAssert.Contains(p3, new Vector2Int(6, 4));
            CollectionAssert.Contains(p3, new Vector2Int(12, 4));

            // Path 4: (0,1) -> row 1 to (3,1) -> up col 3 to (3,5) -> row 5 -> exit (12,6)
            var p4 = stage5.enemyPaths[4].waypoints;
            CollectionAssert.Contains(p4, new Vector2Int(0, 1));
            CollectionAssert.Contains(p4, new Vector2Int(3, 1));
            CollectionAssert.Contains(p4, new Vector2Int(3, 3));
            CollectionAssert.Contains(p4, new Vector2Int(3, 5));
            CollectionAssert.Contains(p4, new Vector2Int(6, 5));

            // Path 5: (12,1) -> row 1 to (4,1) -> up col 4 to (4,4) -> row 4 -> exit (12,6)
            var p5 = stage5.enemyPaths[5].waypoints;
            CollectionAssert.Contains(p5, new Vector2Int(12, 1));
            CollectionAssert.Contains(p5, new Vector2Int(8, 1));
            CollectionAssert.Contains(p5, new Vector2Int(4, 1));
            CollectionAssert.Contains(p5, new Vector2Int(4, 3));
            CollectionAssert.Contains(p5, new Vector2Int(4, 4));
            CollectionAssert.Contains(p5, new Vector2Int(8, 4));
            CollectionAssert.Contains(p5, new Vector2Int(12, 4));
        }

        [Test]
        public void Stage5_TrafficVehicleSprites_LoadedAndThreeVariants()
        {
            var stage5 = Resources.Load<StageData>("Stages/STAGE_05");
            Assert.IsNotNull(stage5, "STAGE_05 asset should exist.");
            Assert.IsNotNull(stage5.trafficVehicleSprites, "trafficVehicleSprites should be assigned.");
            Assert.AreEqual(3, stage5.trafficVehicleSprites.Length, "Should have 3 vehicle sprite variants.");

            for (int i = 0; i < stage5.trafficVehicleSprites.Length; i++)
            {
                var sprite = stage5.trafficVehicleSprites[i];
                Assert.IsNotNull(sprite, $"Vehicle sprite {i} should not be null.");
                Assert.AreEqual(96, sprite.rect.width, $"Sprite {i} width should be 96.");
                Assert.AreEqual(48, sprite.rect.height, $"Sprite {i} height should be 48.");
            }
        }

        [Test]
        public void Stage5CarEncounter_MultiRoll_GeneratesZeroToFiveCarsWithIntervals()
        {
            var encounterObj = new GameObject("TestCarEncounter");
            var encounter = encounterObj.AddComponent<Stage5CarEncounter>();
            encounter.Initialize(gridManager, null, null, null, null);

            // Test 100% chance roll -> exactly 5 cars
            var fullRoll = encounter.RollAndScheduleCarsForWave(1.0f, 5);
            Assert.AreEqual(5, fullRoll.Count, "100% chance should result in 5 scheduled cars.");
            Assert.AreEqual(5, encounter.QueuedCarCount, "Spawn queue should contain 5 cars.");

            float prevTimestamp = 0f;
            for (int i = 0; i < fullRoll.Count; i++)
            {
                var car = fullRoll[i];
                Assert.IsTrue(car.laneY == Stage5CarEncounter.TopLaneY || car.laneY == Stage5CarEncounter.BottomLaneY,
                    $"Lane should be {Stage5CarEncounter.TopLaneY} or {Stage5CarEncounter.BottomLaneY}.");
                float interval = car.intervalSeconds;
                Assert.GreaterOrEqual(interval, Stage5CarEncounter.MinIntervalSeconds - 0.01f);
                Assert.LessOrEqual(interval, Stage5CarEncounter.MaxIntervalSeconds + 0.01f);
                prevTimestamp = car.spawnTimestamp;
            }

            // Test 0% chance roll -> 0 cars
            var zeroRoll = encounter.RollAndScheduleCarsForWave(0.0f, 5);
            Assert.AreEqual(0, zeroRoll.Count, "0% chance should result in 0 scheduled cars.");

            Object.DestroyImmediate(encounterObj);
        }

        [Test]
        public void Stage5TrafficCar_DamageCalculation_SeparateCountersAndTrueDamage()
        {
            var stage5 = Resources.Load<StageData>("Stages/STAGE_05");
            gridManager.InitializeFromStageData(stage5);

            var enemyMgrObj = new GameObject("TestEnemyMgr");
            var enemyMgr = enemyMgrObj.AddComponent<EnemyManager>();
            enemyMgr.Initialize(gridManager);

            var opMgrObj = new GameObject("TestOpMgr");
            var opMgr = opMgrObj.AddComponent<OperatorManager>();
            opMgr.Initialize(gridManager);

            // Spawn Enemy A at tile x=8, y=5
            var enemyAObj = new GameObject("EnemyA");
            enemyAObj.transform.position = gridManager.GridToWorldPosition(8, 5);
            var enemyA = enemyAObj.AddComponent<GruntEnemy>();
            var enemyAData = ScriptableObject.CreateInstance<EnemyData>();
            enemyAData.baseHP = 1000;
            enemyAData.baseDEF = 50;
            enemyAData.movementType = EnemyMovementType.Ground;
            enemyA.Initialize(enemyAData, 0);
            enemyMgr.RegisterEnemy(enemyA);

            // Spawn Enemy B at tile x=10, y=5 (2 tiles after Enemy A)
            var enemyBObj = new GameObject("EnemyB");
            enemyBObj.transform.position = gridManager.GridToWorldPosition(10, 5);
            var enemyB = enemyBObj.AddComponent<GruntEnemy>();
            var enemyBData = ScriptableObject.CreateInstance<EnemyData>();
            enemyBData.baseHP = 1000;
            enemyBData.baseDEF = 50;
            enemyBData.movementType = EnemyMovementType.Ground;
            enemyB.Initialize(enemyBData, 0);
            enemyMgr.RegisterEnemy(enemyB);

            // Spawn Ally at tile x=8, y=5 (same tile as Enemy A)
            var opObj = new GameObject("TestAlly");
            opObj.transform.position = gridManager.GridToWorldPosition(8, 5);
            var op = opObj.AddComponent<BulkheadOperator>();
            var opData = ScriptableObject.CreateInstance<OperatorData>();
            opData.baseHP = 500;
            opData.baseDEF = 50;
            opData.operatorClass = OperatorClass.Defender;
            op.Initialize(opData, OperatorRarity.Star1);
            op.Deploy(gridManager.GetCell(8, 5));
            opMgr.RegisterOperator(op);

            // Spawn Car on Lane 5
            var carObj = new GameObject("Car");
            var car = carObj.AddComponent<Stage5TrafficCar>();
            car.Initialize(5, null, gridManager, enemyMgr, opMgr, speed: 10f, sortingOrder: 9);

            // Verify visual sorting order is 9 (below overlay 10)
            var sr = car.GetComponent<SpriteRenderer>();
            Assert.AreEqual(9, sr.sortingOrder, "Car sprite should be sortingOrder 9.");

            // Move car forward so front reaches x=8.5 (hitting Enemy A and Ally)
            // Car starts at x=0 (cell0CenterX=0.5, startX=0.5). At x=8.5, distance to tile 8 is 8.0 tiles!
            car.transform.position = new Vector3(gridManager.GridToWorldPosition(8, 5).x - car.FrontBumperOffsetX, car.transform.position.y, 0f);
            car.CheckCollisions(car.transform.position.x + car.FrontBumperOffsetX, car.transform.position.x + car.RearBumperOffsetX);

            // Enemy A at tile 8 (8 tiles from start) takes 40 + (8/2)*20 = 120 true damage -> 880 HP remaining
            Assert.AreEqual(880, enemyA.CurrentHP, "Enemy A should take 120 true damage (40 + 4*20) leaving 880 HP.");

            // Ally at tile 8 (8 tiles from start) takes 120 true damage (ignoring DEF 50) -> 380 HP remaining
            Assert.AreEqual(380, op.CurrentHP, "Ally should take 120 true damage (40 + 4*20) ignoring DEF, leaving 380 HP.");

            // First operator hit was not killed, so it should be stunned!
            Assert.IsTrue(op.IsStunned, "First operator hit should be stunned when surviving.");
            Assert.AreEqual(0, op.GetBlockCount(), "Stunned operator should have 0 block capacity.");
            Assert.IsFalse(op.TryBlock(enemyB), "Stunned operator cannot block.");

            // Now move car to tile 10 (2 tiles after Enemy A)
            // Enemy counter reset at tile 8, so Enemy B at tile 10 takes 40 + (2/2)*20 = 60 damage -> 940 HP remaining
            car.transform.position = new Vector3(gridManager.GridToWorldPosition(10, 5).x - car.FrontBumperOffsetX, car.transform.position.y, 0f);
            car.CheckCollisions(car.transform.position.x + car.FrontBumperOffsetX, car.transform.position.x + car.RearBumperOffsetX);

            Assert.AreEqual(940, enemyB.CurrentHP, "Enemy B at tile 10 should take 60 true damage (40 + 1*20) since counter reset at Enemy A.");

            // Clean up
            Object.DestroyImmediate(carObj);
            Object.DestroyImmediate(enemyAObj);
            Object.DestroyImmediate(enemyBObj);
            Object.DestroyImmediate(enemyAData);
            Object.DestroyImmediate(enemyBData);
            Object.DestroyImmediate(opObj);
            Object.DestroyImmediate(opData);
            Object.DestroyImmediate(enemyMgrObj);
            Object.DestroyImmediate(opMgrObj);
        }

        [Test]
        public void OperatorStun_CannotAttackOrBlock_ReleasesBlockedEnemies()
        {
            var stage5 = Resources.Load<StageData>("Stages/STAGE_05");
            gridManager.InitializeFromStageData(stage5);

            var enemyMgrObj = new GameObject("TestEnemyMgr");
            var enemyMgr = enemyMgrObj.AddComponent<EnemyManager>();
            enemyMgr.Initialize(gridManager);

            var opMgrObj = new GameObject("TestOpMgr");
            var opMgr = opMgrObj.AddComponent<OperatorManager>();
            opMgr.Initialize(gridManager);

            // Deploy a defender at (3, 3)
            var opObj = new GameObject("Defender");
            opObj.transform.position = gridManager.GridToWorldPosition(3, 3);
            var op = opObj.AddComponent<BulkheadOperator>();
            var opData = ScriptableObject.CreateInstance<OperatorData>();
            opData.baseHP = 1000;
            opData.baseATK = 100;
            opData.baseDEF = 50;
            opData.blockCount = 2;
            opData.operatorClass = OperatorClass.Defender;
            op.Initialize(opData, OperatorRarity.Star1);
            op.Deploy(gridManager.GetCell(3, 3));
            opMgr.RegisterOperator(op);

            // Spawn ground enemy and block it
            var enemyObj = new GameObject("TestEnemy");
            enemyObj.transform.position = gridManager.GridToWorldPosition(3, 3);
            var enemy = enemyObj.AddComponent<GruntEnemy>();
            var enemyData = ScriptableObject.CreateInstance<EnemyData>();
            enemyData.baseHP = 500;
            enemyData.movementType = EnemyMovementType.Ground;
            enemy.Initialize(enemyData, 0);
            enemyMgr.RegisterEnemy(enemy);

            // Block enemy initially
            Assert.IsTrue(op.TryBlock(enemy), "Operator should successfully block enemy before stun.");
            Assert.AreEqual(1, op.GetCurrentBlockCount());
            Assert.IsTrue(enemy.IsBlocked);

            // Apply stun to operator
            op.ApplyStun(3.0f);

            // Operator is stunned -> blocked enemy is released immediately!
            Assert.IsTrue(op.IsStunned, "Operator should be stunned.");
            Assert.AreEqual(0, op.GetCurrentBlockCount(), "Stunned operator should have released all blocks.");
            Assert.IsFalse(enemy.IsBlocked, "Enemy should no longer be blocked.");

            // Cannot block any enemy while stunned
            Assert.IsFalse(op.TryBlock(enemy), "Stunned operator cannot block.");
            Assert.IsFalse(TrashTD.Combat.BlockingSystem.CanBlock(op, enemy), "BlockingSystem should reject stunned blocker.");

            // Simulate time passing: 2.0s -> still stunned
            op.UpdateStunStatus(2.0f);
            Assert.IsTrue(op.IsStunned);

            // Another 1.1s (total 3.1s) -> stun expires
            op.UpdateStunStatus(1.1f);
            Assert.IsFalse(op.IsStunned, "Stun should expire after duration.");
            Assert.AreEqual(2, op.GetBlockCount(), "Block capacity should restore to 2.");

            // Can block again now that stun expired
            Assert.IsTrue(op.TryBlock(enemy), "Operator can block again once stun expires.");
            Assert.IsTrue(enemy.IsBlocked);

            // Clean up
            Object.DestroyImmediate(enemyObj);
            Object.DestroyImmediate(enemyData);
            Object.DestroyImmediate(opObj);
            Object.DestroyImmediate(opData);
            Object.DestroyImmediate(enemyMgrObj);
            Object.DestroyImmediate(opMgrObj);
        }

        [Test]
        public void Stage5TrafficCar_IgnoresBubbleShieldNegation_StillPopsBubble()
        {
            var stage5 = Resources.Load<StageData>("Stages/STAGE_05");
            gridManager.InitializeFromStageData(stage5);

            var enemyMgrObj = new GameObject("TestEnemyMgr");
            var enemyMgr = enemyMgrObj.AddComponent<EnemyManager>();
            enemyMgr.Initialize(gridManager);

            var opMgrObj = new GameObject("TestOpMgr");
            var opMgr = opMgrObj.AddComponent<OperatorManager>();
            opMgr.Initialize(gridManager);

            // Deploy an operator with 500 HP at (4, 4)
            var opObj = new GameObject("Defender");
            opObj.transform.position = gridManager.GridToWorldPosition(4, 4);
            var op = opObj.AddComponent<BulkheadOperator>();
            var opData = ScriptableObject.CreateInstance<OperatorData>();
            opData.baseHP = 500;
            opData.baseDEF = 50;
            opData.operatorClass = OperatorClass.Defender;
            op.Initialize(opData, OperatorRarity.Star1);
            op.Deploy(gridManager.GetCell(4, 4));
            opMgr.RegisterOperator(op);

            // Apply Bubblets' shield to operator
            var shield = BubbleShield.Apply(op, 30, null);
            Assert.IsTrue(BubbleShield.HasActiveShield(op), "Operator should have an active bubble shield.");

            // Spawn car on Lane 4
            var carObj = new GameObject("Car");
            var car = carObj.AddComponent<Stage5TrafficCar>();
            car.Initialize(4, null, gridManager, enemyMgr, opMgr, speed: 10f, sortingOrder: 9);

            // Move car to tile 4 (4 tiles from column 0)
            // Damage formula at 4 tiles: 40 + (4/2)*20 = 80 damage!
            car.transform.position = new Vector3(gridManager.GridToWorldPosition(4, 4).x - car.FrontBumperOffsetX, car.transform.position.y, 0f);
            car.CheckCollisions(car.transform.position.x + car.FrontBumperOffsetX, car.transform.position.x + car.RearBumperOffsetX);

            // 1. Bubble shield was popped!
            Assert.IsFalse(BubbleShield.HasActiveShield(op), "Bubble shield should be popped by car hit.");

            // 2. Shield negation was ignored -> operator took the 80 true damage!
            // (Bubble pop heal of 30 was applied during pop if recipient was missing HP, but op was full HP 500, so 500 - 80 = 420)
            Assert.AreEqual(420, op.CurrentHP, "Operator should take 80 true damage despite having a bubble shield.");

            // 3. Operator survived, so first hit stun applies!
            Assert.IsTrue(op.IsStunned, "Surviving operator should be stunned.");

            // Clean up
            Object.DestroyImmediate(carObj);
            Object.DestroyImmediate(opObj);
            Object.DestroyImmediate(opData);
            Object.DestroyImmediate(enemyMgrObj);
            Object.DestroyImmediate(opMgrObj);
        }

        [Test]
        public void Stage5TrafficCar_DespawnsWhenReachingLastVioletTile_DealsDamageAndDestroys()
        {
            var stage5 = Resources.Load<StageData>("Stages/STAGE_05");
            gridManager.InitializeFromStageData(stage5);

            var enemyMgrObj = new GameObject("TestEnemyMgr");
            var enemyMgr = enemyMgrObj.AddComponent<EnemyManager>();
            enemyMgr.Initialize(gridManager);

            var opMgrObj = new GameObject("TestOpMgr");
            var opMgr = opMgrObj.AddComponent<OperatorManager>();
            opMgr.Initialize(gridManager);

            // Last violet tile on the right is at x=12, y=5
            Vector3 lastVioletTileWorldPos = gridManager.GridToWorldPosition(12, 5);
            var enemyObj = new GameObject("TestEnemyAtLastTile");
            enemyObj.transform.position = lastVioletTileWorldPos;
            var enemy = enemyObj.AddComponent<GruntEnemy>();
            var enemyData = ScriptableObject.CreateInstance<EnemyData>();
            enemyData.baseHP = 1000;
            enemyData.baseDEF = 50;
            enemyData.movementType = EnemyMovementType.Ground;
            enemy.Initialize(enemyData, 0);
            enemyMgr.RegisterEnemy(enemy);

            // Spawn car on Lane 5
            var carObj = new GameObject("Car");
            var car = carObj.AddComponent<Stage5TrafficCar>();
            car.Initialize(5, null, gridManager, enemyMgr, opMgr, speed: 10f, sortingOrder: 9);

            // 1. ExitX should be one tile after the last violet tile on the right (x=12, center 12.5 + 1.0 = 13.5)
            Assert.AreEqual(lastVioletTileWorldPos.x + gridManager.CellSize, car.ExitX, 0.001f, "ExitX must be one tile after the last violet tile on the right.");

            // 2. Position car right before ExitX (0.1 units before ExitX)
            car.transform.position = new Vector3(car.ExitX - car.FrontBumperOffsetX - 0.1f, car.transform.position.y, 0f);
            car.Move(0.005f); // Move 0.05 units -> frontX is still < ExitX
            Assert.IsNotNull(car, "Car should still exist before reaching ExitX.");

            // 3. Move car past ExitX (0.01s at speed 10 = 0.1 units -> frontX reaches/exceeds ExitX)
            car.Move(0.01f);

            // Enemy at tile 12 took 160 true damage (12 tiles travelled: 40 + (12/2)*20 = 160)
            Assert.AreEqual(840, enemy.CurrentHP, "Enemy at last violet tile should have taken 160 true damage upon car arrival.");

            // Car should now be destroyed immediately when reaching ExitX (one tile after last violet tile)
            Assert.IsTrue(car == null || car.gameObject == null, "Car should be destroyed immediately when reaching one tile after the last violet tile on the right.");

            // Clean up
            Object.DestroyImmediate(enemyObj);
            Object.DestroyImmediate(enemyData);
            Object.DestroyImmediate(enemyMgrObj);
            Object.DestroyImmediate(opMgrObj);
        }

        [Test]
        public void OperatorStats_MossmoAndThornchin_HaveUpdatedHP()
        {
            var mossmo = UnityEditor.AssetDatabase.LoadAssetAtPath<OperatorData>("Assets/_Project/Data/Operators/OP_Defender_Mossmo.asset");
            var thornchin = UnityEditor.AssetDatabase.LoadAssetAtPath<OperatorData>("Assets/_Project/Data/Operators/OP_Defender_Thornchin.asset");

            if (mossmo != null)
            {
                Assert.AreEqual(160, mossmo.baseHP, "Mossmo base HP should be 160.");
            }

            if (thornchin != null)
            {
                Assert.AreEqual(150, thornchin.baseHP, "Thornchin base HP should be 150.");
            }
        }

        [Test]
        public void Stage5CarEncounter_DoesNotSpawnCarsOnFirst5Waves()
        {
            var encounterObj = new GameObject("TestCarEncounter");
            var encounter = encounterObj.AddComponent<Stage5CarEncounter>();

            Assert.AreEqual(6, Stage5CarEncounter.FirstCarWave, "First car wave should be Wave 6.");

            for (int wave = 1; wave <= 5; wave++)
            {
                Assert.IsFalse(encounter.CanSpawnCarsOnWave(wave), $"Car encounter should NOT spawn cars on wave {wave}.");
            }

            Assert.IsTrue(encounter.CanSpawnCarsOnWave(6), "Car encounter SHOULD spawn cars on wave 6.");
            Assert.IsTrue(encounter.CanSpawnCarsOnWave(10), "Car encounter SHOULD spawn cars on wave 10.");

            Object.DestroyImmediate(encounterObj);
        }

        [Test]
        public void DamageCalculator_TrueDamage_BypassesMitigation()
        {
            int damage = Combat.DamageCalculator.CalculateDamage(3, def: 999, res: 999, DamageType.True);
            Assert.AreEqual(3, damage, "True damage must ignore both DEF and RES completely.");

            int trapDamage = Combat.DamageCalculator.CalculateDamage(TrapTileRules.DamagePerTick, def: 50, res: 50, DamageType.True);
            Assert.AreEqual(TrapTileRules.DamagePerTick, trapDamage, "Acid/trap damage with True damage type must deal full unmitigated damage.");
        }

        [Test]
        public void OperatorAndEnemy_TakeTrueDamage_BypassesDEF()
        {
            // Operator with high DEF (50 DEF)
            var opObj = new GameObject("DefenderOp");
            var op = opObj.AddComponent<BulkheadOperator>();
            var opData = ScriptableObject.CreateInstance<OperatorData>();
            opData.baseHP = 100;
            opData.baseDEF = 50;
            opData.baseRES = 50;
            op.Initialize(opData, OperatorRarity.Star1);

            // True damage via TakeTrueDamage: should lose exactly 3 HP, not chip damage (1)
            op.TakeTrueDamage(3);
            Assert.AreEqual(97, op.CurrentHP, "Operator should take unmitigated true damage from TakeTrueDamage.");

            // True damage via TakeDamage: should also lose exactly 3 HP
            op.TakeDamage(3, DamageType.True);
            Assert.AreEqual(94, op.CurrentHP, "Operator should take unmitigated true damage from TakeDamage(..., DamageType.True).");

            // Enemy with high DEF (50 DEF)
            var enemyObj = new GameObject("TestEnemy");
            var enemy = enemyObj.AddComponent<GruntEnemy>();
            var enemyData = ScriptableObject.CreateInstance<EnemyData>();
            enemyData.baseHP = 100;
            enemyData.baseDEF = 50;
            enemyData.baseRES = 50;
            enemy.Initialize(enemyData, 0);

            enemy.TakeTrueDamage(3);
            Assert.AreEqual(97, enemy.CurrentHP, "Enemy should take unmitigated true damage from TakeTrueDamage.");

            enemy.TakeDamage(3, DamageType.True);
            Assert.AreEqual(94, enemy.CurrentHP, "Enemy should take unmitigated true damage from TakeDamage(..., DamageType.True).");

            Object.DestroyImmediate(opObj);
            Object.DestroyImmediate(opData);
            Object.DestroyImmediate(enemyObj);
            Object.DestroyImmediate(enemyData);
        }

        [Test]
        public void Bulkhead_Block4_And_FrenzyIncreasesAtkAndDef()
        {
            var bulkheadData = UnityEditor.AssetDatabase.LoadAssetAtPath<OperatorData>("Assets/_Project/Data/Operators/OP_Defender_Bulkhead.asset");
            if (bulkheadData != null)
            {
                Assert.AreEqual(4, bulkheadData.blockCount, "Bulkhead block count must be 4 in asset.");
                Assert.IsTrue(bulkheadData.skillDescription.Contains("blocking 3 or more enemies"), "Bulkhead skill description should mention 3 or more enemies.");
            }

            var opObj = new GameObject("BulkheadTest");
            var op = opObj.AddComponent<BulkheadOperator>();
            var data = ScriptableObject.CreateInstance<OperatorData>();
            data.operatorName = "Bulkhead";
            data.baseHP = 200;
            data.baseATK = 50;
            data.baseDEF = 40;
            data.blockCount = 4;
            data.operatorClass = OperatorClass.Defender;
            op.Initialize(data, OperatorRarity.Star1);

            Assert.AreEqual(4, op.GetBlockCount(), "Bulkhead block capacity must be 4.");

            // Before blocking 3: ATK is 50, DEF is 40
            Assert.AreEqual(50, op.CurrentATK);
            Assert.AreEqual(40, op.CurrentDEF);

            // Create 4 enemies to block
            var enemies = new List<EnemyBase>();
            for (int i = 0; i < 4; i++)
            {
                var eObj = new GameObject($"Enemy_{i}");
                var e = eObj.AddComponent<GruntEnemy>();
                var eData = ScriptableObject.CreateInstance<EnemyData>();
                eData.baseHP = 100;
                e.Initialize(eData, 0);
                enemies.Add(e);
            }

            // Block 1
            op.Deploy(new GridCell(0, 0, TileType.LowGround, Vector3.zero));
            op.TryBlock(enemies[0]);
            Assert.IsFalse(op.IsFrenzyActive);
            Assert.AreEqual(50, op.CurrentATK);
            Assert.AreEqual(40, op.CurrentDEF);

            // Block 2
            op.TryBlock(enemies[1]);
            Assert.IsFalse(op.IsFrenzyActive);
            Assert.AreEqual(50, op.CurrentATK);
            Assert.AreEqual(40, op.CurrentDEF);

            // Block 3 -> Frenzy triggers: ATK * 1.20 = 60, DEF * 1.20 = 48
            op.TryBlock(enemies[2]);
            Assert.IsTrue(op.IsFrenzyActive);
            Assert.AreEqual(60, op.CurrentATK, "ATK should increase by 20% (50 -> 60).");
            Assert.AreEqual(48, op.CurrentDEF, "DEF should increase by 20% (40 -> 48).");

            // Block 4 -> Still frenzy
            op.TryBlock(enemies[3]);
            Assert.IsTrue(op.IsFrenzyActive);
            Assert.AreEqual(60, op.CurrentATK);
            Assert.AreEqual(48, op.CurrentDEF);

            // Cleanup
            for (int i = 0; i < enemies.Count; i++)
            {
                Object.DestroyImmediate(enemies[i].Data);
                Object.DestroyImmediate(enemies[i].gameObject);
            }
            Object.DestroyImmediate(data);
            Object.DestroyImmediate(opObj);
        }

        [Test]
        public void Mossmo_SkillDescription_And_DoesNotAttack()
        {
            var mossmoData = UnityEditor.AssetDatabase.LoadAssetAtPath<OperatorData>("Assets/_Project/Data/Operators/OP_Defender_Mossmo.asset");
            if (mossmoData != null)
            {
                Assert.AreEqual("Mossmo does not attack and is likely to be attacked but when taking damage he heals up to 2 operators in its range including itself",
                    mossmoData.skillDescription);
            }

            var opObj = new GameObject("MossmoTest");
            var op = opObj.AddComponent<MossmoOperator>();
            var data = ScriptableObject.CreateInstance<OperatorData>();
            data.operatorName = "Mossmo";
            data.baseHP = 160;
            data.baseATK = 10;
            data.blockCount = 3;
            data.operatorClass = OperatorClass.Defender;
            op.Initialize(data, OperatorRarity.Star1);

            Assert.IsTrue(op.HasTaunt, "Mossmo must have HasTaunt enabled.");

            // Mossmo Attack method should do nothing
            var eObj = new GameObject("DummyEnemy");
            var e = eObj.AddComponent<GruntEnemy>();
            var eData = ScriptableObject.CreateInstance<EnemyData>();
            eData.baseHP = 100;
            e.Initialize(eData, 0);

            op.Attack(e);
            Assert.AreEqual(100, e.CurrentHP, "Mossmo must not deal damage on Attack.");

            Object.DestroyImmediate(eData);
            Object.DestroyImmediate(eObj);
            Object.DestroyImmediate(data);
            Object.DestroyImmediate(opObj);
        }

        [Test]
        public void Mossmo_TauntMechanic_RangedEnemyPrioritizesMossmoInSurroundingTile()
        {
            // Set up GridManager and OperatorManager
            var opMgrObj = new GameObject("OpManager");
            var opMgr = opMgrObj.AddComponent<OperatorManager>();
            opMgr.Initialize(gridManager);

            // Deploy Mossmo at (5, 5)
            var mossmoObj = new GameObject("Mossmo");
            mossmoObj.transform.position = gridManager.GridToWorldPosition(5, 5);
            var mossmo = mossmoObj.AddComponent<MossmoOperator>();
            var mossmoData = ScriptableObject.CreateInstance<OperatorData>();
            mossmoData.operatorName = "Mossmo";
            mossmoData.baseHP = 160;
            mossmoData.baseDEF = 30;
            mossmoData.operatorClass = OperatorClass.Defender;
            mossmoData.position = OperatorPosition.Melee;
            mossmo.Initialize(mossmoData, OperatorRarity.Star1);
            mossmo.Deploy(gridManager.GetCell(5, 5));
            opMgr.RegisterOperator(mossmo);

            // Deploy a Sniper (Ranged operator, normally preferred target) at (5, 6)
            var sniperObj = new GameObject("Sniper");
            sniperObj.transform.position = gridManager.GridToWorldPosition(5, 6);
            var sniper = sniperObj.AddComponent<DefenderOperator>();
            var sniperData = ScriptableObject.CreateInstance<OperatorData>();
            sniperData.operatorName = "Sniper";
            sniperData.baseHP = 80;
            sniperData.operatorClass = OperatorClass.Sniper;
            sniperData.position = OperatorPosition.Ranged;
            sniper.Initialize(sniperData, OperatorRarity.Star1);
            sniper.Deploy(gridManager.GetCell(5, 6));
            opMgr.RegisterOperator(sniper);

            // Create EnemyCaster with attack range 3
            var casterObj = new GameObject("EnemyCaster");
            var caster = casterObj.AddComponent<EnemyCaster>();
            var casterData = ScriptableObject.CreateInstance<EnemyData>();
            casterData.attackRange = 3;
            casterData.attackInterval = 2.0f;
            caster.Initialize(casterData, 0);
            caster.SetGridManager(gridManager);

            // 1. When EnemyCaster is in a surrounding tile of Mossmo (e.g. at (4, 5), dx=1, dy=0)
            casterObj.transform.position = gridManager.GridToWorldPosition(4, 5);
            Assert.IsTrue(mossmo.ShouldTauntAttacker(casterObj.transform.position, gridManager), "Mossmo should taunt when attacker is in surrounding tile.");

            // Use reflection to call FindOperatorTarget
            var findTargetMethod = typeof(EnemyBase).GetMethod("FindOperatorTarget",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var targetWhenSurrounding = (OperatorBase)findTargetMethod.Invoke(caster, new object[] { true });
            Assert.AreEqual(mossmo, targetWhenSurrounding, "Enemy caster should prioritize Mossmo because of taunt in surrounding tile, even if Sniper is preferred.");

            // 2. When EnemyCaster is NOT in a surrounding tile of Mossmo (e.g. at (3, 5), dx=2, dy=0, but still in attack range 3)
            casterObj.transform.position = gridManager.GridToWorldPosition(3, 5);
            Assert.IsFalse(mossmo.ShouldTauntAttacker(casterObj.transform.position, gridManager), "Mossmo should NOT taunt when attacker is beyond surrounding tiles.");

            var targetWhenFar = (OperatorBase)findTargetMethod.Invoke(caster, new object[] { true });
            Assert.AreEqual(sniper, targetWhenFar, "Enemy caster should target the preferred Ranged operator (Sniper) when outside Mossmo's surrounding tile.");

            // Clean up
            Object.DestroyImmediate(casterObj);
            Object.DestroyImmediate(casterData);
            Object.DestroyImmediate(sniperObj);
            Object.DestroyImmediate(sniperData);
            Object.DestroyImmediate(mossmoObj);
            Object.DestroyImmediate(mossmoData);
            Object.DestroyImmediate(opMgrObj);
        }

        [Test]
        public void Mossmo_Cooldowns_1sForEnemyDamage_3sForEnvironmentDamage()
        {
            var opObj = new GameObject("MossmoTest");
            var op = opObj.AddComponent<MossmoOperator>();
            var data = ScriptableObject.CreateInstance<OperatorData>();
            data.operatorName = "Mossmo";
            data.baseHP = 160;
            data.baseATK = 15;
            data.baseDEF = 0;
            data.operatorClass = OperatorClass.Defender;
            op.Initialize(data, OperatorRarity.Star1);

            // 1. Attacked by enemy -> Cooldown should be 1.0s
            var enemyObj = new GameObject("AttackingEnemy");
            var enemy = enemyObj.AddComponent<GruntEnemy>();
            var enemyData = ScriptableObject.CreateInstance<EnemyData>();
            enemyData.baseHP = 100;
            enemy.Initialize(enemyData, 0);

            // Deal 20 damage via enemy attack
            op.TakeDamage(20, DamageType.Physical, attacker: enemy);
            Assert.AreEqual(MossmoOperator.EnemyHealCooldownDuration, op.HealCooldownTimer, 0.05f, "Cooldown after enemy attack should be 1.0s.");

            // Force cooldown to expire
            typeof(MossmoOperator).GetField("healCooldownTimer",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(op, 0f);

            // 2. Attacked by environment (TakeTrueDamage, e.g. acid rain or acid pool) -> Cooldown should be 3.0s
            op.TakeTrueDamage(3);
            Assert.AreEqual(MossmoOperator.EnvironmentHealCooldownDuration, op.HealCooldownTimer, 0.05f, "Cooldown after environmental damage should be 3.0s.");

            // Force cooldown to expire
            typeof(MossmoOperator).GetField("healCooldownTimer",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(op, 0f);

            // 3. Attacked by environment via TakeDamage with null attacker -> Cooldown should be 3.0s
            op.TakeDamage(10, DamageType.Physical, attacker: null);
            Assert.AreEqual(MossmoOperator.EnvironmentHealCooldownDuration, op.HealCooldownTimer, 0.05f, "Cooldown after null attacker damage should be 3.0s.");

            Object.DestroyImmediate(enemyObj);
            Object.DestroyImmediate(enemyData);
            Object.DestroyImmediate(opObj);
            Object.DestroyImmediate(data);
        }

        [Test]
        public void Drawgoo_Asset_SkillDescription_And_UpgradeMerge_GrantsRandom3StarOnDeath()
        {
            var drawgooData = UnityEditor.AssetDatabase.LoadAssetAtPath<OperatorData>("Assets/_Project/Data/Operators/OP_Guard_Drawgoo.asset");
            Assert.IsNotNull(drawgooData, "Drawgoo asset should exist.");
            Assert.IsTrue(drawgooData.skillDescription.Contains("3-star"), "Drawgoo asset description should mention 3-star upgrade.");

            var upgradeObj = new GameObject("UpgradeSystem");
            var upgradeSys = TestUtility.AddAwakened<RarityUpgradeSystem>(upgradeObj);

            var deckObj = new GameObject("Deck");
            var deck = TestUtility.AddAwakened<PlayerDeck>(deckObj);

            var opMgrObj = new GameObject("OpManager");
            var opMgr = TestUtility.AddAwakened<OperatorManager>(opMgrObj);
            opMgr.Initialize(gridManager);

            var draftObj = new GameObject("DraftSystem");
            var draftSys = TestUtility.AddAwakened<CardDraftSystem>(draftObj);

            var star3Op = ScriptableObject.CreateInstance<OperatorData>();
            star3Op.operatorName = "Mossmo";
            star3Op.operatorClass = OperatorClass.Defender;
            star3Op.baseRarity = OperatorRarity.Star3;

            var pool = new List<OperatorData> { drawgooData, star3Op };
            draftSys.SetOperatorPool(pool);

            try
            {
                // 1. Add 3 1★ Drawgoos to the deck
                deck.AddCard(new DraftCard(drawgooData, OperatorRarity.Star1));
                deck.AddCard(new DraftCard(drawgooData, OperatorRarity.Star1));
                deck.AddCard(new DraftCard(drawgooData, OperatorRarity.Star1));

                // 3 cards merge into 1 2★ card
                Assert.AreEqual(1, deck.CardCount);
                DraftCard mergedCard = deck.DeckSlots[0];
                Assert.IsNotNull(mergedCard);
                Assert.AreEqual(OperatorRarity.Star2, mergedCard.rarity);

                // 2. Deploy the 2★ Drawgoo
                Assert.IsTrue(opMgr.TryDeployOperator(mergedCard, new Vector2Int(0, 0), OperatorFacing.Right, out var deployedOp));
                deck.RemoveCard(0);
                Assert.AreEqual(OperatorRarity.Star2, deployedOp.CurrentRarity);
                Assert.AreEqual("When Drawgoo dies, draw 1 (60%) or 2 (40%) random 3-star operator cards from the pool.", deployedOp.GetSkillDescription());

                // 3. Kill the 2★ Drawgoo
                TestUtility.ExpectEditModeDestroy();
                deployedOp.TakeTrueDamage(deployedOp.MaxHP + 100);

                // Deck should now have 1 or 2 cards, and all must be the 3★ operator
                Assert.IsTrue(deck.CardCount >= 1 && deck.CardCount <= 2);
                foreach (DraftCard rewardCard in deck.DeckSlots)
                {
                    Assert.AreEqual("Mossmo", rewardCard.operatorData.operatorName);
                    Assert.AreEqual(OperatorRarity.Star3, rewardCard.rarity);
                }
            }
            finally
            {
                TestUtility.ClearSingletons();
                Object.DestroyImmediate(draftObj);
                Object.DestroyImmediate(opMgrObj);
                Object.DestroyImmediate(deckObj);
                Object.DestroyImmediate(upgradeObj);
                Object.DestroyImmediate(star3Op);
            }
        }

        [Test]
        public void CalculateFacingFromDisplacement_ResolvesCardinalDirectionsAccurately()
        {
            // Zero displacement returns fallback
            Assert.AreEqual(OperatorFacing.Right, StageBootstrapper.CalculateFacingFromDisplacement(Vector2.zero, OperatorFacing.Right));
            Assert.AreEqual(OperatorFacing.Down, StageBootstrapper.CalculateFacingFromDisplacement(Vector2.zero, OperatorFacing.Down));

            // Positive X dominant => Right
            Assert.AreEqual(OperatorFacing.Right, StageBootstrapper.CalculateFacingFromDisplacement(new Vector2(10f, 0f)));
            Assert.AreEqual(OperatorFacing.Right, StageBootstrapper.CalculateFacingFromDisplacement(new Vector2(10f, 5f)));
            Assert.AreEqual(OperatorFacing.Right, StageBootstrapper.CalculateFacingFromDisplacement(new Vector2(10f, -5f)));

            // Negative X dominant => Left
            Assert.AreEqual(OperatorFacing.Left, StageBootstrapper.CalculateFacingFromDisplacement(new Vector2(-10f, 0f)));
            Assert.AreEqual(OperatorFacing.Left, StageBootstrapper.CalculateFacingFromDisplacement(new Vector2(-10f, 5f)));
            Assert.AreEqual(OperatorFacing.Left, StageBootstrapper.CalculateFacingFromDisplacement(new Vector2(-10f, -5f)));

            // Positive Y dominant => Up
            Assert.AreEqual(OperatorFacing.Up, StageBootstrapper.CalculateFacingFromDisplacement(new Vector2(0f, 10f)));
            Assert.AreEqual(OperatorFacing.Up, StageBootstrapper.CalculateFacingFromDisplacement(new Vector2(5f, 10f)));
            Assert.AreEqual(OperatorFacing.Up, StageBootstrapper.CalculateFacingFromDisplacement(new Vector2(-5f, 10f)));

            // Negative Y dominant => Down
            Assert.AreEqual(OperatorFacing.Down, StageBootstrapper.CalculateFacingFromDisplacement(new Vector2(0f, -10f)));
            Assert.AreEqual(OperatorFacing.Down, StageBootstrapper.CalculateFacingFromDisplacement(new Vector2(5f, -10f)));
            Assert.AreEqual(OperatorFacing.Down, StageBootstrapper.CalculateFacingFromDisplacement(new Vector2(-5f, -10f)));

            // Diagonal ties (|dx| >= |dy|)
            Assert.AreEqual(OperatorFacing.Right, StageBootstrapper.CalculateFacingFromDisplacement(new Vector2(10f, 10f)));
            Assert.AreEqual(OperatorFacing.Left, StageBootstrapper.CalculateFacingFromDisplacement(new Vector2(-10f, 10f)));
        }

        [Test]
        public void ArknightsDeployment_DragAndReleaseSweep_TracksFacingAndDeploysCorrectly()
        {
            var bootObj = new GameObject("Bootstrapper");
            var bootstrapper = bootObj.AddComponent<StageBootstrapper>();

            var gmObj = new GameObject("GM");
            var gm = TestUtility.AddAwakened<GameManager>(gmObj);
            typeof(GameManager).GetField("currentPhase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(gm, StagePhase.Preparation);
            typeof(GameManager).GetField("currentState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(gm, GamePlayState.Playing);

            var opMgrObj = new GameObject("OpManager");
            var opMgr = TestUtility.AddAwakened<OperatorManager>(opMgrObj);

            var deckObj = new GameObject("Deck");
            var deck = TestUtility.AddAwakened<PlayerDeck>(deckObj);

            var gridObj2 = new GameObject("GridManager2");
            var gmGrid = gridObj2.AddComponent<GridManager>();
            var stage = ScriptableObject.CreateInstance<StageData>();
            stage.gridWidth = 5;
            stage.gridHeight = 5;
            stage.tileLayout = new TileType[25]; // all LowGround
            gmGrid.InitializeFromStageData(stage);
            opMgr.Initialize(gmGrid);

            bootstrapper.gridManager = gmGrid;
            bootstrapper.gameManager = gm;
            bootstrapper.operatorManager = opMgr;
            bootstrapper.playerDeck = deck;

            var opData = ScriptableObject.CreateInstance<OperatorData>();
            opData.operatorName = "TestGroundOp";
            opData.operatorClass = OperatorClass.Guard;
            opData.position = OperatorPosition.Melee;
            opData.rangePattern = new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) };
            var card = new DraftCard(opData, OperatorRarity.Star1);
            deck.AddCard(card);

            try
            {
                // Step 1: Click & drag card to desired tile -> preview sits at (2, 2)
                var cellPos = new Vector2Int(2, 2);
                bootstrapper.BeginOperatorPlacement(card, cellPos);

                Assert.IsTrue(bootstrapper.IsPlacementPreviewActive);
                Assert.IsFalse(bootstrapper.IsDraggingToDeploy, "Step 1 should place preview without immediately dragging direction.");
                Assert.AreEqual(cellPos, bootstrapper.PlacementGridPosition);
                Assert.AreEqual(OperatorFacing.Right, bootstrapper.PlacementFacing);

                Vector3 cellWorld = gmGrid.GetCell(cellPos).WorldPosition;

                // Step 2: Click and hold where operator is to drag facing direction
                // Drag Up: displacement (0, +1)
                bootstrapper.UpdateOperatorPlacementDrag(card, cellPos, cellWorld + new Vector3(0f, 1f, 0f));
                Assert.AreEqual(OperatorFacing.Up, bootstrapper.PlacementFacing);

                // Drag Left: displacement (-1, 0)
                bootstrapper.UpdateOperatorPlacementDrag(card, cellPos, cellWorld + new Vector3(-1f, 0f, 0f));
                Assert.AreEqual(OperatorFacing.Left, bootstrapper.PlacementFacing);

                // Drag Down: displacement (0, -1)
                bootstrapper.UpdateOperatorPlacementDrag(card, cellPos, cellWorld + new Vector3(0f, -1f, 0f));
                Assert.AreEqual(OperatorFacing.Down, bootstrapper.PlacementFacing);

                // Step 3: When you let go, it accepts automatically (simulates PointerUp)
                bootstrapper.ConfirmOperatorPlacement();

                Assert.IsFalse(bootstrapper.IsPlacementPreviewActive);
                Assert.IsNull(bootstrapper.PendingDeployCard);
                Assert.AreEqual(0, deck.CardCount, "Card should have been consumed from deck upon confirmation.");

                // Verify operator deployed in cell with Down facing
                var cell = gmGrid.GetCell(cellPos);
                Assert.IsNotNull(cell.OccupantOperator);
                var deployed = cell.OccupantOperator.GetComponent<OperatorBase>();
                Assert.IsNotNull(deployed);
                Assert.AreEqual(OperatorFacing.Down, deployed.Facing);
            }
            finally
            {
                TestUtility.ClearSingletons();
                Object.DestroyImmediate(opData);
                Object.DestroyImmediate(stage);
                Object.DestroyImmediate(bootObj);
                Object.DestroyImmediate(gmObj);
                Object.DestroyImmediate(opMgrObj);
                Object.DestroyImmediate(deckObj);
                Object.DestroyImmediate(gridObj2);
            }
        }

        [Test]
        public void OperatorUndeploy_PreparationRefundAndCooldownRules()
        {
            var gmObj = new GameObject("GM");
            var gm = TestUtility.AddAwakened<GameManager>(gmObj);
            typeof(GameManager).GetField("currentPhase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(gm, StagePhase.Preparation);
            typeof(GameManager).GetField("currentState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(gm, GamePlayState.Playing);

            var opMgrObj = new GameObject("OpManager");
            var opMgr = TestUtility.AddAwakened<OperatorManager>(opMgrObj);

            var deckObj = new GameObject("Deck");
            var deck = TestUtility.AddAwakened<PlayerDeck>(deckObj);

            var gridObj = new GameObject("GridManager");
            var grid = gridObj.AddComponent<GridManager>();
            var stage = ScriptableObject.CreateInstance<StageData>();
            stage.gridWidth = 5;
            stage.gridHeight = 5;
            stage.tileLayout = new TileType[25];
            grid.InitializeFromStageData(stage);
            opMgr.Initialize(grid);

            // Sniper has 1 round cooldown on retreat
            var sniperData = ScriptableObject.CreateInstance<OperatorData>();
            sniperData.operatorName = "TestSniper";
            sniperData.operatorClass = OperatorClass.Sniper;
            sniperData.position = OperatorPosition.Ranged;
            sniperData.rangePattern = new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) };

            try
            {
                // CASE 1: Deployed in Preparation, Undeployed in same Preparation -> 0 cooldown
                var card1 = new DraftCard(sniperData, OperatorRarity.Star1);
                deck.AddCard(card1);
                Assert.IsTrue(opMgr.TryDeployOperator(card1, new Vector2Int(1, 1), OperatorFacing.Right, out var deployed1));
                deck.RemoveCard(0);

                Assert.IsTrue(deployed1.DeployedDuringCurrentPreparation);
                Assert.IsFalse(deployed1.HasFoughtInWave);

                opMgr.RetreatOperator(deployed1);
                Assert.AreEqual(1, deck.CardCount);
                Assert.AreEqual(0, deck.GetCard(0).cooldownRoundsRemaining, "Retreated during same preparation phase without fighting must have 0 cooldown.");
                deck.RemoveCard(0);

                // CASE 2: Deployed in Preparation, Wave starts (WaveActive), undeployed during Wave -> gets cooldown
                var card2 = new DraftCard(sniperData, OperatorRarity.Star1);
                deck.AddCard(card2);
                Assert.IsTrue(opMgr.TryDeployOperator(card2, new Vector2Int(1, 1), OperatorFacing.Right, out var deployed2));
                deck.RemoveCard(0);

                // Simulate wave start
                gm.EnterWavePhase();
                Assert.IsTrue(deployed2.HasFoughtInWave);
                Assert.IsFalse(deployed2.DeployedDuringCurrentPreparation);

                opMgr.RetreatOperator(deployed2);
                Assert.AreEqual(1, deck.CardCount);
                Assert.AreEqual(1, deck.GetCard(0).cooldownRoundsRemaining, "Retreated during wave phase must incur class cooldown.");
                deck.RemoveCard(0);

                // CASE 3: Operator survived previous wave, now in new Preparation phase. Undeploying gets cooldown!
                var card3 = new DraftCard(sniperData, OperatorRarity.Star1);
                deck.AddCard(card3);
                Assert.IsTrue(opMgr.TryDeployOperator(card3, new Vector2Int(1, 1), OperatorFacing.Right, out var deployed3));
                deck.RemoveCard(0);

                // Survived wave
                opMgr.MarkAllDeployedOperatorsAsFought();

                // Now transition to new preparation phase
                gm.EnterPreparationPhase();
                Assert.IsTrue(deployed3.HasFoughtInWave, "Veteran operator must be marked as having fought.");
                Assert.IsFalse(deployed3.DeployedDuringCurrentPreparation);

                // Undeploy veteran during preparation
                opMgr.RetreatOperator(deployed3);
                Assert.AreEqual(1, deck.CardCount);
                Assert.AreEqual(1, deck.GetCard(0).cooldownRoundsRemaining, "Operator who already fought on a previous round must incur cooldown even if retreated during preparation.");
                deck.RemoveCard(0);

                // CASE 4: New operator deployed during that same preparation phase, undeployed in that preparation phase -> 0 cooldown
                var card4 = new DraftCard(sniperData, OperatorRarity.Star1);
                deck.AddCard(card4);
                Assert.IsTrue(opMgr.TryDeployOperator(card4, new Vector2Int(2, 2), OperatorFacing.Right, out var deployed4));
                deck.RemoveCard(0);

                Assert.IsTrue(deployed4.DeployedDuringCurrentPreparation);
                Assert.IsFalse(deployed4.HasFoughtInWave);

                opMgr.RetreatOperator(deployed4);
                Assert.AreEqual(1, deck.CardCount);
                Assert.AreEqual(0, deck.GetCard(0).cooldownRoundsRemaining, "Fresh operator deployed and undeployed in preparation phase must have 0 cooldown.");
            }
            finally
            {
                TestUtility.ClearSingletons();
                Object.DestroyImmediate(sniperData);
                Object.DestroyImmediate(stage);
                Object.DestroyImmediate(gmObj);
                Object.DestroyImmediate(opMgrObj);
                Object.DestroyImmediate(deckObj);
                Object.DestroyImmediate(gridObj);
            }
        }

        [Test]
        public void RightClickKeybind_WhenOperatorAlreadyClicked_UndeploysOperator()
        {
            var bootObj = new GameObject("Bootstrapper");
            var bootstrapper = bootObj.AddComponent<StageBootstrapper>();

            var gmObj = new GameObject("GM");
            var gm = TestUtility.AddAwakened<GameManager>(gmObj);
            typeof(GameManager).GetField("currentPhase", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(gm, StagePhase.Preparation);
            typeof(GameManager).GetField("currentState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(gm, GamePlayState.Playing);

            var opMgrObj = new GameObject("OpManager");
            var opMgr = TestUtility.AddAwakened<OperatorManager>(opMgrObj);

            var deckObj = new GameObject("Deck");
            var deck = TestUtility.AddAwakened<PlayerDeck>(deckObj);

            var gridObj = new GameObject("GridManager");
            var grid = gridObj.AddComponent<GridManager>();
            var stage = ScriptableObject.CreateInstance<StageData>();
            stage.gridWidth = 5;
            stage.gridHeight = 5;
            stage.tileLayout = new TileType[25];
            grid.InitializeFromStageData(stage);
            opMgr.Initialize(grid);

            bootstrapper.gridManager = grid;
            bootstrapper.gameManager = gm;
            bootstrapper.operatorManager = opMgr;
            bootstrapper.playerDeck = deck;

            var opData = ScriptableObject.CreateInstance<OperatorData>();
            opData.operatorName = "TestOp";
            opData.operatorClass = OperatorClass.Guard;
            opData.position = OperatorPosition.Melee;
            opData.rangePattern = new[] { new Vector2Int(0, 0), new Vector2Int(1, 0) };

            var card = new DraftCard(opData, OperatorRarity.Star1);
            deck.AddCard(card);

            try
            {
                // 1. Deploy operator
                Assert.IsTrue(opMgr.TryDeployOperator(card, new Vector2Int(1, 1), OperatorFacing.Right, out var deployedOp));
                deck.RemoveCard(0);
                Assert.AreEqual(0, deck.CardCount);

                // 2. Click/select operator
                Assert.IsTrue(opMgr.SelectOperator(deployedOp));
                Assert.AreEqual(deployedOp, opMgr.SelectedOperator);

                // 3. Right-click undeploy trigger (simulates right-click when operator already selected)
                bootstrapper.ClearOperatorRangePreview();
                opMgr.RetreatOperator(opMgr.SelectedOperator);

                // Operator should be undeployed and returned to deck, range preview cleared
                Assert.IsNull(opMgr.SelectedOperator);
                Assert.AreEqual(1, deck.CardCount);
                Assert.IsNull(grid.GetCell(new Vector2Int(1, 1)).OccupantOperator);
            }
            finally
            {
                TestUtility.ClearSingletons();
                Object.DestroyImmediate(opData);
                Object.DestroyImmediate(stage);
                Object.DestroyImmediate(bootObj);
                Object.DestroyImmediate(gmObj);
                Object.DestroyImmediate(opMgrObj);
                Object.DestroyImmediate(deckObj);
                Object.DestroyImmediate(gridObj);
            }
        }
    }
}
