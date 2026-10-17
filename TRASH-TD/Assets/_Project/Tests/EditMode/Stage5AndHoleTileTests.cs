using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Core.Pathfinding;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;

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
    }
}
