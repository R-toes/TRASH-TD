using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Tests
{
    public class RobozombieEnemyTests
    {
        private GameObject gridObject;
        private GridManager gridManager;
        private GameObject opManagerObject;
        private OperatorManager operatorManager;
        private StageData stageData;

        [SetUp]
        public void SetUp()
        {
            gridObject = new GameObject("TestGridManager");
            gridManager = gridObject.AddComponent<GridManager>();

            stageData = ScriptableObject.CreateInstance<StageData>();
            stageData.gridWidth = 5;
            stageData.gridHeight = 5;
            stageData.tileLayout = new TileType[25];
            for (int i = 0; i < 25; i++)
            {
                stageData.tileLayout[i] = TileType.LowGround;
            }

            gridManager.InitializeFromStageData(stageData);

            opManagerObject = new GameObject("TestOperatorManager");
            operatorManager = TestUtility.AddAwakened<OperatorManager>(opManagerObject);
        }

        [TearDown]
        public void TearDown()
        {
            TestUtility.ClearSingletons();
            if (opManagerObject != null) Object.DestroyImmediate(opManagerObject);
            if (gridObject != null) Object.DestroyImmediate(gridObject);
            if (stageData != null) Object.DestroyImmediate(stageData);
        }

        private (GameObject obj, GuardOperator op) CreateOperatorAt(Vector2Int gridPos, int hp = 100, int def = 20)
        {
            var opObj = new GameObject($"Guard_{gridPos.x}_{gridPos.y}");
            var op = opObj.AddComponent<GuardOperator>();

            var opData = ScriptableObject.CreateInstance<OperatorData>();
            opData.operatorName = "TestGuard";
            opData.position = OperatorPosition.Melee;
            opData.baseHP = hp;
            opData.baseATK = 50;
            opData.baseDEF = def;
            opData.blockCount = 1;
            opData.attackRange = 1;
            opData.attackInterval = 1.0f;
            opData.rangePattern = new[] { new Vector2Int(1, 0) };

            GridCell cell = gridManager.GetCell(gridPos);
            opObj.transform.position = gridManager.GridToWorldPosition(gridPos);
            op.Initialize(opData, OperatorRarity.Star1, OperatorFacing.Right);
            op.Deploy(cell);

            var deployedField = typeof(OperatorManager).GetField("deployedOperators",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (deployedField != null)
            {
                var list = (List<OperatorBase>)deployedField.GetValue(operatorManager);
                if (!list.Contains(op))
                {
                    list.Add(op);
                }
            }

            return (opObj, op);
        }

        private (GameObject obj, RobozombieEnemy enemy, EnemyData data) CreateRobozombieAt(Vector2Int gridPos)
        {
            var enemyObj = new GameObject("TestRobozombie");
            enemyObj.AddComponent<SpriteRenderer>();
            var enemy = enemyObj.AddComponent<RobozombieEnemy>();

            var data = ScriptableObject.CreateInstance<EnemyData>();
            data.enemyName = "Robozombie";
            data.archetype = EnemyArchetype.Grunt;
            data.movementType = EnemyMovementType.Ground;
            data.baseHP = 200;
            data.baseATK = 25;
            data.baseDEF = 10;
            data.baseRES = 0;
            data.moveSpeed = 0.75f;
            data.attackInterval = 2.0f;
            data.attackRange = 0;
            data.blockWeight = 1;
            data.lifePointCost = 1;

            enemy.Initialize(data, 1);
            enemy.SetGridManager(gridManager);
            enemyObj.transform.position = gridManager.GridToWorldPosition(gridPos);

            return (enemyObj, enemy, data);
        }

#if UNITY_EDITOR
        [Test]
        public void Robozombie_DataAsset_HasSameStatsAsSludgeGrunt()
        {
            var sludgeData = AssetDatabase.LoadAssetAtPath<EnemyData>("Assets/_Project/Data/Enemies/Enemy_Grunt_Sludge.asset");
            var roboData = AssetDatabase.LoadAssetAtPath<EnemyData>("Assets/_Project/Data/Enemies/Enemy_Grunt_Robozombie.asset");

            Assert.IsNotNull(sludgeData, "Sludge Grunt asset should exist.");
            Assert.IsNotNull(roboData, "Robozombie asset should exist.");

            Assert.AreEqual(EnemyArchetype.Grunt, roboData.archetype);
            Assert.AreEqual(sludgeData.movementType, roboData.movementType);
            Assert.AreEqual(sludgeData.baseHP, roboData.baseHP);
            Assert.AreEqual(sludgeData.baseATK, roboData.baseATK);
            Assert.AreEqual(sludgeData.baseDEF, roboData.baseDEF);
            Assert.AreEqual(sludgeData.baseRES, roboData.baseRES);
            Assert.AreEqual(sludgeData.moveSpeed, roboData.moveSpeed, 0.001f);
            Assert.AreEqual(sludgeData.damageType, roboData.damageType);
            Assert.AreEqual(sludgeData.attackInterval, roboData.attackInterval, 0.001f);
            Assert.AreEqual(sludgeData.attackRange, roboData.attackRange);
            Assert.AreEqual(sludgeData.isUnblockable, roboData.isUnblockable);
            Assert.AreEqual(sludgeData.blockWeight, roboData.blockWeight);
            Assert.AreEqual(sludgeData.lifePointCost, roboData.lifePointCost);
        }
#endif

        [Test]
        public void Robozombie_ExplosionChance_DefaultsTo25Percent_AndClampsProperly()
        {
            var (obj, enemy, data) = CreateRobozombieAt(new Vector2Int(2, 2));
            Assert.AreEqual(0.25f, enemy.ExplosionChance, 0.0001f, "Explosion chance should default to 25%.");

            enemy.ExplosionChance = -0.5f;
            Assert.AreEqual(0f, enemy.ExplosionChance, "Explosion chance should clamp to 0 at minimum.");

            enemy.ExplosionChance = 1.5f;
            Assert.AreEqual(1f, enemy.ExplosionChance, "Explosion chance should clamp to 1 at maximum.");

            Object.DestroyImmediate(obj);
            Object.DestroyImmediate(data);
        }

        [Test]
        public void Robozombie_Explosion_DealsTripleATKTrueDamage_ToAdjacentOperatorsOnly()
        {
            // Robozombie at (2, 2) with ATK = 25
            var (enemyObj, enemy, enemyData) = CreateRobozombieAt(new Vector2Int(2, 2));

            // Adjacent: (2, 3) Up, (3, 2) Right
            var (opUpObj, opUp) = CreateOperatorAt(new Vector2Int(2, 3), hp: 100, def: 20);
            var (opRightObj, opRight) = CreateOperatorAt(new Vector2Int(3, 2), hp: 100, def: 50);

            // Diagonal (not adjacent): (3, 3)
            var (opDiagObj, opDiag) = CreateOperatorAt(new Vector2Int(3, 3), hp: 100, def: 10);

            // Far (not adjacent): (0, 0)
            var (opFarObj, opFar) = CreateOperatorAt(new Vector2Int(0, 0), hp: 100, def: 10);

            enemy.Explode();

            Assert.IsTrue(enemy.DidExplode);

            // 3x ATK = 3 * 25 = 75 True Damage (ignores DEF)
            Assert.AreEqual(25, opUp.CurrentHP, "Adjacent (2,3) operator should take 75 true damage.");
            Assert.AreEqual(25, opRight.CurrentHP, "Adjacent (3,2) operator with 50 DEF should take 75 true damage (DEF bypassed).");
            Assert.AreEqual(100, opDiag.CurrentHP, "Diagonal (3,3) operator should take 0 explosion damage.");
            Assert.AreEqual(100, opFar.CurrentHP, "Far (0,0) operator should take 0 explosion damage.");

            Object.DestroyImmediate(opUpObj);
            Object.DestroyImmediate(opRightObj);
            Object.DestroyImmediate(opDiagObj);
            Object.DestroyImmediate(opFarObj);
            Object.DestroyImmediate(enemyObj);
            Object.DestroyImmediate(enemyData);
        }

        [Test]
        public void Robozombie_Die_WithZeroChance_DoesNotExplode()
        {
            var (enemyObj, enemy, enemyData) = CreateRobozombieAt(new Vector2Int(2, 2));
            enemy.ExplosionChance = 0f;

            var (opObj, op) = CreateOperatorAt(new Vector2Int(2, 3), hp: 100, def: 20);

            TestUtility.ExpectEditModeDestroy();
            // Deal lethal damage to trigger Die()
            enemy.TakeTrueDamage(999);

            Assert.IsFalse(enemy.DidExplode, "Enemy with 0% explosion chance should not explode on death.");
            Assert.AreEqual(100, op.CurrentHP, "Adjacent operator should take no damage.");

            Object.DestroyImmediate(opObj);
            Object.DestroyImmediate(enemyData);
        }

        [Test]
        public void Robozombie_Die_WithHundredPercentChance_AlwaysExplodes()
        {
            var (enemyObj, enemy, enemyData) = CreateRobozombieAt(new Vector2Int(2, 2));
            enemy.ExplosionChance = 1f;

            var (opObj, op) = CreateOperatorAt(new Vector2Int(2, 3), hp: 100, def: 20);

            TestUtility.ExpectEditModeDestroy();
            // Deal lethal damage to trigger Die()
            enemy.TakeTrueDamage(999);

            Assert.IsTrue(enemy.DidExplode, "Enemy with 100% explosion chance should explode on death.");
            Assert.AreEqual(25, op.CurrentHP, "Adjacent operator should take 75 true damage.");

            Object.DestroyImmediate(opObj);
            Object.DestroyImmediate(enemyData);
        }

        [Test]
        public void RobozombieSpriteAnimation_CyclesFrames()
        {
            var animObj = new GameObject("TestRobozombieAnim");
            var sr = animObj.AddComponent<SpriteRenderer>();
            var anim = animObj.AddComponent<RobozombieSpriteAnimation>();

            var frame0 = Sprite.Create(new Texture2D(32, 32), new Rect(0, 0, 32, 32), Vector2.one * 0.5f);
            var frame1 = Sprite.Create(new Texture2D(32, 32), new Rect(0, 0, 32, 32), Vector2.one * 0.5f);
            var testFrames = new[] { frame0, frame1 };

            var framesField = typeof(RobozombieSpriteAnimation).GetField("frames",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            framesField.SetValue(anim, testFrames);

            TestUtility.CallAwake(anim);

            Assert.AreEqual(frame0, sr.sprite);
            Assert.AreEqual(2, anim.Frames.Length);
            Assert.AreEqual(0.125f, anim.FrameDuration, 0.001f);

            Object.DestroyImmediate(animObj);
            Object.DestroyImmediate(frame0.texture);
            Object.DestroyImmediate(frame1.texture);
            Object.DestroyImmediate(frame0);
            Object.DestroyImmediate(frame1);
        }
    }
}
