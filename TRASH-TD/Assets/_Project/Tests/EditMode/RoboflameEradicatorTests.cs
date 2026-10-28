using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Tests
{
    /// <summary>
    /// Roboflame (flamethrower caster), THE ERADICATOR (rocket/melee tank), caster blocking and block weight.
    /// The grid is 6x5 LowGround with a HighGround top row (y = 4); enemies walk left to right along y = 2.
    /// </summary>
    public class RoboflameEradicatorTests
    {
        private const int Width = 6;
        private const int Height = 5;
        // One sweep plus a hair, so float accumulation never leaves a tick one frame short
        private const float Sweep = RoboflameEnemy.SweepDuration + 0.001f;

        private GameObject gridObject;
        private GridManager gridManager;
        private GameObject opManagerObject;
        private OperatorManager operatorManager;
        private StageData stageData;
        private readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            gridObject = new GameObject("TestGridManager");
            gridManager = gridObject.AddComponent<GridManager>();

            stageData = ScriptableObject.CreateInstance<StageData>();
            stageData.gridWidth = Width;
            stageData.gridHeight = Height;
            stageData.tileLayout = new TileType[Width * Height];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    stageData.tileLayout[y * Width + x] = y == Height - 1 ? TileType.HighGround : TileType.LowGround;
                }
            }

            gridManager.InitializeFromStageData(stageData);

            opManagerObject = new GameObject("TestOperatorManager");
            operatorManager = TestUtility.AddAwakened<OperatorManager>(opManagerObject);
            StageCombatModifiers.ConfigureForEnvironment(null);
        }

        [TearDown]
        public void TearDown()
        {
            StageCombatModifiers.ConfigureForEnvironment(null);
            TestUtility.ClearSingletons();
            foreach (Object obj in created)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            created.Clear();

            // Visual effects spawned during the tests
            foreach (var particles in Object.FindObjectsByType<PixelParticles>())
                Object.DestroyImmediate(particles.gameObject);
            foreach (var projectile in Object.FindObjectsByType<CombatProjectileVisual>())
                Object.DestroyImmediate(projectile.gameObject);
            foreach (var blast in Object.FindObjectsByType<AoeBlastVisual>())
                Object.DestroyImmediate(blast.gameObject);
            foreach (var stab in Object.FindObjectsByType<MeleeStabVisual>())
                Object.DestroyImmediate(stab.gameObject);
            foreach (var warning in Object.FindObjectsByType<BlastWarningVisual>())
                Object.DestroyImmediate(warning.gameObject);

            if (opManagerObject != null) Object.DestroyImmediate(opManagerObject);
            if (gridObject != null) Object.DestroyImmediate(gridObject);
            if (stageData != null) Object.DestroyImmediate(stageData);
        }

        // ---------- Helpers ----------

        private GuardOperator CreateOperatorAt(Vector2Int gridPos, int hp = 1000, int def = 0, int res = 0, int blockCount = 1)
        {
            var opObj = new GameObject($"Guard_{gridPos.x}_{gridPos.y}");
            created.Add(opObj);
            var op = opObj.AddComponent<GuardOperator>();

            var opData = ScriptableObject.CreateInstance<OperatorData>();
            created.Add(opData);
            opData.operatorName = "TestGuard";
            opData.position = OperatorPosition.Melee;
            opData.baseHP = hp;
            opData.baseATK = 10;
            opData.baseDEF = def;
            opData.baseRES = res;
            opData.blockCount = blockCount;
            opData.attackRange = 1;
            opData.attackInterval = 100f;
            opData.rangePattern = new[] { new Vector2Int(1, 0) };

            opObj.transform.position = gridManager.GridToWorldPosition(gridPos);
            op.Initialize(opData, OperatorRarity.Star1, OperatorFacing.Right);
            op.Deploy(gridManager.GetCell(gridPos));

            var deployedField = typeof(OperatorManager).GetField("deployedOperators",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var list = (List<OperatorBase>)deployedField.GetValue(operatorManager);
            if (!list.Contains(op)) list.Add(op);

            return op;
        }

        private EnemyData CreateEnemyData(string name, EnemyArchetype archetype, int atk, int blockWeight = 1, float moveSpeed = 1f)
        {
            var data = ScriptableObject.CreateInstance<EnemyData>();
            created.Add(data);
            data.enemyName = name;
            data.archetype = archetype;
            data.movementType = EnemyMovementType.Ground;
            data.baseHP = 1000;
            data.baseATK = atk;
            data.baseDEF = 0;
            data.baseRES = 0;
            data.moveSpeed = moveSpeed;
            data.attackInterval = 2f;
            data.blockWeight = blockWeight;
            data.lifePointCost = 1;
            return data;
        }

        private T CreateEnemy<T>(EnemyData data, Vector2Int start) where T : EnemyBase
        {
            var enemyObj = new GameObject($"Test{typeof(T).Name}");
            created.Add(enemyObj);
            enemyObj.AddComponent<SpriteRenderer>();
            var enemy = enemyObj.AddComponent<T>();
            enemy.Initialize(data, 1);
            enemy.SetGridManager(gridManager);

            var path = new List<Vector3>();
            for (int x = start.x; x < Width; x++)
                path.Add(gridManager.GridToWorldPosition(new Vector2Int(x, start.y)));
            enemy.SetPath(path);
            return enemy;
        }

        private RoboflameEnemy CreateRoboflame(Vector2Int start, int atk = 20) =>
            CreateEnemy<RoboflameEnemy>(CreateEnemyData("Roboflame", EnemyArchetype.Caster, atk), start);

        private EradicatorEnemy CreateEradicator(Vector2Int start, int atk = 60, float moveSpeed = 1f) =>
            CreateEnemy<EradicatorEnemy>(CreateEnemyData("THE ERADICATOR", EnemyArchetype.Tank, atk, 2, moveSpeed), start);

        // ---------- Caster blocking ----------

        [Test]
        public void Caster_IsBlockable_AndTakesOneSlot()
        {
            EnemyData caster = CreateEnemyData("Caster", EnemyArchetype.Caster, 10);

            GuardOperator op = CreateOperatorAt(new Vector2Int(2, 2), blockCount: 1);
            var enemy = CreateEnemy<EnemyCaster>(caster, new Vector2Int(2, 2));
            Assert.IsTrue(BlockingSystem.TryEngageBlock(op, enemy));
            Assert.IsTrue(enemy.IsBlocked);
            Assert.AreEqual(1, op.GetCurrentBlockCount());
        }

        [Test]
        public void BlockedRoboflame_HoldsInFrontOfItsBlocker_AndKeepsFlaming()
        {
            RoboflameEnemy roboflame = CreateRoboflame(new Vector2Int(0, 2));
            GuardOperator op = CreateOperatorAt(new Vector2Int(1, 2));
            Assert.IsTrue(BlockingSystem.TryEngageBlock(op, roboflame));

            roboflame.Step(0f); // Blocker is 1 tile ahead: in the cone
            Assert.IsTrue(roboflame.IsFlaming);
            for (int i = 0; i < 4; i++) roboflame.Step(1f);

            Assert.AreEqual(roboflame.BlockedPosition.x, roboflame.transform.position.x, 0.001f,
                "It stops at the block point instead of creeping past it.");
            Assert.Less(op.CurrentHP, op.MaxHP);
        }

#if UNITY_EDITOR
        [Test]
        public void AllCasterAssets_AreBlockable_WithWeightOne()
        {
            string[] guids = AssetDatabase.FindAssets("t:EnemyData", new[] { "Assets/_Project/Data/Enemies" });
            int casters = 0;
            foreach (string guid in guids)
            {
                var data = AssetDatabase.LoadAssetAtPath<EnemyData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null || data.archetype != EnemyArchetype.Caster) continue;
                casters++;
                Assert.IsFalse(data.isUnblockable, $"{data.enemyName} should be blockable.");
                Assert.AreEqual(1, data.blockWeight, $"{data.enemyName} should take one block slot.");
            }
            Assert.Greater(casters, 0);
        }
#endif

        // ---------- Block weight ----------

        [Test]
        public void HeavyEnemy_TakesTwoBlockSlots()
        {
            GuardOperator singleBlocker = CreateOperatorAt(new Vector2Int(1, 1), blockCount: 1);
            GuardOperator doubleBlocker = CreateOperatorAt(new Vector2Int(2, 2), blockCount: 2);
            EradicatorEnemy eradicator = CreateEradicator(new Vector2Int(2, 2));

            Assert.IsFalse(BlockingSystem.CanBlock(singleBlocker, eradicator), "Block 1 can't hold a weight-2 enemy.");
            Assert.IsTrue(BlockingSystem.TryEngageBlock(doubleBlocker, eradicator));
            Assert.AreEqual(2, doubleBlocker.GetCurrentBlockCount());

            var grunt = CreateEnemy<GruntEnemy>(CreateEnemyData("Grunt", EnemyArchetype.Grunt, 10), new Vector2Int(2, 2));
            Assert.IsFalse(BlockingSystem.CanBlock(doubleBlocker, grunt), "Both slots are taken.");
            Assert.IsFalse(doubleBlocker.TryBlock(grunt));
        }

        // ---------- Roboflame ----------

        [Test]
        public void Roboflame_ConeCoversOwnTileOneAheadAndThreeAcrossAtTwo()
        {
            RoboflameEnemy roboflame = CreateRoboflame(new Vector2Int(1, 2));
            List<Vector2Int> cells = roboflame.GetConeCells(Vector2Int.right);

            CollectionAssert.AreEquivalent(new[]
            {
                new Vector2Int(1, 2), new Vector2Int(2, 2), new Vector2Int(3, 2),
                new Vector2Int(3, 3), new Vector2Int(3, 1)
            }, cells);
        }

        [Test]
        public void Roboflame_WalksWhenConeIsEmpty_AndCreepsWhileFlaming()
        {
            RoboflameEnemy roboflame = CreateRoboflame(new Vector2Int(0, 2));
            CreateOperatorAt(new Vector2Int(4, 4)); // Off to the side, never in the cone from y = 2

            Vector3 start = roboflame.transform.position;
            roboflame.Step(0.1f); // The first frame only moves on from the spawn waypoint
            roboflame.Step(0.1f);
            Assert.IsFalse(roboflame.IsFlaming);
            Assert.Greater(roboflame.transform.position.x, start.x, "Nobody in reach: it keeps walking.");

            GuardOperator op = CreateOperatorAt(new Vector2Int(2, 1)); // 2 ahead, 1 to the side
            Vector3 before = roboflame.transform.position;
            roboflame.Step(0.1f);
            Assert.IsTrue(roboflame.IsFlaming);
            Assert.AreEqual(before, roboflame.transform.position, "The burst starts where it stands.");
            Assert.Less(op.CurrentHP, op.MaxHP, "The first sweep hits straight away.");

            before = roboflame.transform.position;
            roboflame.Step(0.5f);
            Assert.IsTrue(roboflame.IsFlaming);
            Assert.AreEqual(before.x + 0.5f * RoboflameEnemy.BurstMoveSpeedMultiplier, roboflame.transform.position.x, 0.001f,
                "It creeps forward at 15% speed while flaming.");
        }

        [Test]
        public void Roboflame_BurstDealsFiveArtsTicks_ThenCoolsDown()
        {
            RoboflameEnemy roboflame = CreateRoboflame(new Vector2Int(1, 2), atk: 20);
            GuardOperator op = CreateOperatorAt(new Vector2Int(2, 2), hp: 1000, def: 50, res: 5);

            roboflame.Step(0f);
            for (int i = 0; i < 6; i++) roboflame.Step(Sweep);

            // Arts damage ignores DEF and is reduced by RES: 5 ticks x (20 - 5).
            Assert.AreEqual(1000 - 5 * 15, op.CurrentHP);
            Assert.IsFalse(roboflame.IsFlaming);
            Assert.Greater(roboflame.CooldownRemaining, 0f);

            // During the cooldown it walks on (at 75% speed) and doesn't flame again until the cooldown ends.
            Vector3 position = roboflame.transform.position;
            roboflame.Step(0.5f);
            Assert.IsFalse(roboflame.IsFlaming);
            Assert.Greater(roboflame.transform.position.x, position.x);
            roboflame.Step(RoboflameEnemy.CooldownDuration);
            Assert.IsTrue(roboflame.IsFlaming);
        }

        [Test]
        public void Roboflame_OnStage4_AddsOneHeatStackPerTwoTicks()
        {
            StageCombatModifiers.ConfigureForEnvironment("STAGE_04");
            RoboflameEnemy roboflame = CreateRoboflame(new Vector2Int(1, 2));
            GuardOperator op = CreateOperatorAt(new Vector2Int(3, 2));

            roboflame.Step(0f);
            Assert.AreEqual(0, op.HeatStacks, "One tick isn't enough.");
            roboflame.Step(Sweep);
            Assert.AreEqual(1, op.HeatStacks);
            roboflame.Step(Sweep);
            roboflame.Step(Sweep);
            Assert.AreEqual(2, op.HeatStacks);
        }

        [Test]
        public void Roboflame_OffStage4_AddsNoHeat()
        {
            RoboflameEnemy roboflame = CreateRoboflame(new Vector2Int(1, 2));
            GuardOperator op = CreateOperatorAt(new Vector2Int(2, 2));

            roboflame.Step(0f);
            for (int i = 0; i < 4; i++) roboflame.Step(Sweep);

            Assert.AreEqual(0, op.HeatStacks);
        }

        // ---------- Eradicator ----------

        [Test]
        public void Eradicator_MeleeStunsOnlyOnFirstHitPerOperator()
        {
            EradicatorEnemy eradicator = CreateEradicator(new Vector2Int(1, 2), atk: 60);
            GuardOperator op = CreateOperatorAt(new Vector2Int(2, 2), hp: 1000, def: 20);

            eradicator.Step(0f);
            Assert.AreEqual(1000 - 40, op.CurrentHP, "Melee hits for ATK - DEF.");
            Assert.IsTrue(op.IsStunned, "First hit stuns.");

            op.UpdateStunStatus(EradicatorEnemy.MeleeStunDuration + 0.1f);
            Assert.IsFalse(op.IsStunned);

            eradicator.Step(2f);
            Assert.AreEqual(1000 - 80, op.CurrentHP);
            Assert.IsFalse(op.IsStunned, "Later hits on the same operator don't stun.");
            Assert.AreEqual(0, eradicator.RocketsFired, "Melee replaces rockets.");
        }

        [Test]
        public void Eradicator_IgnoresHighGroundOperatorsForMelee()
        {
            EradicatorEnemy eradicator = CreateEradicator(new Vector2Int(2, 3));
            CreateOperatorAt(new Vector2Int(2, 4)); // HighGround, adjacent

            Assert.IsNull(eradicator.FindMeleeTarget());
        }

        [Test]
        public void Eradicator_FiresRocketsInRange_EveryFourthIsHeavy()
        {
            EradicatorEnemy eradicator = CreateEradicator(new Vector2Int(0, 2), moveSpeed: 0f);
            CreateOperatorAt(new Vector2Int(2, 2)); // 2 tiles away: in rocket range, not adjacent

            for (int i = 0; i < 3; i++)
            {
                eradicator.Step(3f);
                eradicator.Step(1f); // Let the post-shot stop run out
            }
            Assert.AreEqual(3, eradicator.RocketsFired);
            Assert.IsTrue(eradicator.NextRocketIsHeavy);
        }

        [Test]
        public void Eradicator_RocketBlastsCircle_HeavyHitsHarderAndHeatsOnStage4()
        {
            StageCombatModifiers.ConfigureForEnvironment("STAGE_04");
            EradicatorEnemy eradicator = CreateEradicator(new Vector2Int(0, 0), atk: 60);
            GuardOperator center = CreateOperatorAt(new Vector2Int(3, 2), def: 10);
            GuardOperator corner = CreateOperatorAt(new Vector2Int(4, 3), def: 10); // Diagonal: 1.41 tiles away
            GuardOperator outside = CreateOperatorAt(new Vector2Int(5, 2), def: 10); // 2 tiles away

            eradicator.DetonateRocket(new Vector2Int(3, 2), false);
            Assert.AreEqual(1000 - 50, center.CurrentHP);
            Assert.AreEqual(1000 - 50, corner.CurrentHP);
            Assert.AreEqual(1000, outside.CurrentHP);
            Assert.AreEqual(0, center.HeatStacks, "Normal rockets add no heat.");

            eradicator.DetonateRocket(new Vector2Int(3, 2), true);
            Assert.AreEqual(1000 - 50 - 80, center.CurrentHP, "Heavy rocket: 1.5x ATK.");
            Assert.AreEqual(1, center.HeatStacks);
            Assert.AreEqual(1, corner.HeatStacks);
            Assert.AreEqual(0, outside.HeatStacks);
        }

#if UNITY_EDITOR
        [Test]
        public void NewEnemyAssets_AreConfigured()
        {
            var roboflame = AssetDatabase.LoadAssetAtPath<EnemyData>("Assets/_Project/Data/Enemies/Enemy_Caster_Roboflame.asset");
            var eradicator = AssetDatabase.LoadAssetAtPath<EnemyData>("Assets/_Project/Data/Enemies/Enemy_Tank_Eradicator.asset");
            Assert.IsNotNull(roboflame);
            Assert.IsNotNull(eradicator);

            Assert.AreEqual(EnemyArchetype.Caster, roboflame.archetype);
            Assert.AreEqual(DamageType.Arts, roboflame.damageType);
            Assert.AreEqual(4, roboflame.animationFrames.Length);
            Assert.IsNotNull(roboflame.enemyPrefab.GetComponent<RoboflameEnemy>());

            Assert.AreEqual(EnemyArchetype.Tank, eradicator.archetype);
            Assert.AreEqual(2, eradicator.blockWeight);
            Assert.AreEqual(2f, eradicator.visualScale);
            Assert.AreEqual(8, eradicator.animationFrames.Length);
            Assert.IsNotNull(eradicator.enemyPrefab.GetComponent<EradicatorEnemy>());
        }
#endif
    }
}
