using System.Collections.Generic;
using System.Reflection;
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
    public class ShadesliceAttackRestrictionTests
    {
        private GameObject shadesliceObject;
        private GameObject gridObject;
        private GameObject operatorManagerObject;
        private ShadesliceOperator shadeslice;
        private OperatorData shadesliceData;
        private GridManager gridManager;
        private OperatorManager operatorManager;
        private StageData stageData;
        private readonly List<GameObject> enemyObjects = new List<GameObject>();
        private readonly List<EnemyData> enemyDataAssets = new List<EnemyData>();

        [SetUp]
        public void SetUp()
        {
            StageCombatModifiers.ConfigureForEnvironment(null);

            gridObject = new GameObject("TestGridManager");
            gridManager = gridObject.AddComponent<GridManager>();
            stageData = ScriptableObject.CreateInstance<StageData>();
            stageData.gridWidth = 3;
            stageData.gridHeight = 3;
            stageData.tileLayout = new TileType[stageData.gridWidth * stageData.gridHeight];
            gridManager.InitializeFromStageData(stageData);

            operatorManagerObject = new GameObject("TestOperatorManager");
            operatorManager = TestUtility.AddAwakened<OperatorManager>(operatorManagerObject);
            operatorManager.Initialize(gridManager);

            shadesliceData = ScriptableObject.CreateInstance<OperatorData>();
            shadesliceData.operatorName = "Shadeslice";
            shadesliceData.position = OperatorPosition.Melee;
            shadesliceData.baseHP = 300;
            shadesliceData.blockCount = 1;

            shadesliceObject = new GameObject("TestShadeslice");
            shadeslice = shadesliceObject.AddComponent<ShadesliceOperator>();
            shadeslice.Initialize(shadesliceData, OperatorRarity.Star3);
            shadeslice.Deploy(gridManager.GetCell(0, 0));
            operatorManager.RegisterOperator(shadeslice);
        }

        [TearDown]
        public void TearDown()
        {
            StageCombatModifiers.ConfigureForEnvironment(null);
            TestUtility.ClearSingletons();
            if (shadesliceObject != null) Object.DestroyImmediate(shadesliceObject);
            if (shadesliceData != null) Object.DestroyImmediate(shadesliceData);
            if (operatorManagerObject != null) Object.DestroyImmediate(operatorManagerObject);
            if (gridObject != null) Object.DestroyImmediate(gridObject);
            if (stageData != null) Object.DestroyImmediate(stageData);

            foreach (GameObject enemyObject in enemyObjects)
                if (enemyObject != null) Object.DestroyImmediate(enemyObject);
            foreach (EnemyData enemyData in enemyDataAssets)
                if (enemyData != null) Object.DestroyImmediate(enemyData);

            enemyObjects.Clear();
            enemyDataAssets.Clear();
        }

        [Test]
        public void NonTankEnemy_CanDamageShadesliceOnlyWhileBlockedByHim()
        {
            GruntEnemy attacker = CreateEnemy<GruntEnemy>(EnemyArchetype.Grunt);
            int startingHP = shadeslice.CurrentHP;

            Assert.IsFalse(shadeslice.TryTakeAttackDamage(30, DamageType.Physical, attacker));
            Assert.AreEqual(startingHP, shadeslice.CurrentHP);

            Assert.IsTrue(shadeslice.TryBlock(attacker));
            Assert.IsTrue(shadeslice.TryTakeAttackDamage(30, DamageType.Physical, attacker));
            Assert.Less(shadeslice.CurrentHP, startingHP);
        }

        [Test]
        public void TankEnemy_CanDamageShadesliceAndStillBeBlocked()
        {
            TankEnemy attacker = CreateEnemy<TankEnemy>(EnemyArchetype.Tank);
            int startingHP = shadeslice.CurrentHP;

            Assert.IsTrue(shadeslice.TryBlock(attacker), "Shadeslice should still block tank enemies.");
            Assert.IsTrue(shadeslice.TryTakeAttackDamage(30, DamageType.Physical, attacker));

            Assert.Less(shadeslice.CurrentHP, startingHP);
        }

        [Test]
        public void EnvironmentalDamage_CanDamageShadeslice()
        {
            int startingHP = shadeslice.CurrentHP;

            Assert.IsTrue(shadeslice.TakeTrueDamage(30));
            Assert.Less(shadeslice.CurrentHP, startingHP);
        }

        [Test]
        public void AreaAttackFromUnblockedEnemy_CanDamageShadeslice()
        {
            GruntEnemy attacker = CreateEnemy<GruntEnemy>(EnemyArchetype.Grunt);
            int startingHP = shadeslice.CurrentHP;

            Assert.IsTrue(shadeslice.TryTakeAreaAttackDamage(30, DamageType.Physical, attacker));

            Assert.Less(shadeslice.CurrentHP, startingHP);
        }

        [Test]
        public void PerfectParry_ReducesDirectDamageAndReflectsWithPerAttackerCooldown()
        {
            SetRarity(OperatorRarity.Star5);
            TankEnemy firstAttacker = CreateEnemy<TankEnemy>(EnemyArchetype.Tank);
            TankEnemy secondAttacker = CreateEnemy<TankEnemy>(EnemyArchetype.Tank);
            int startingHP = shadeslice.CurrentHP;
            int firstAttackerHP = firstAttacker.CurrentHP;
            int secondAttackerHP = secondAttacker.CurrentHP;

            Assert.IsTrue(shadeslice.TryTakeAttackDamage(100, DamageType.True, firstAttacker));
            Assert.AreEqual(startingHP - 65, shadeslice.CurrentHP);
            Assert.AreEqual(firstAttackerHP - 20, firstAttacker.CurrentHP);

            Assert.IsTrue(shadeslice.TryTakeAttackDamage(100, DamageType.True, firstAttacker));
            Assert.AreEqual(startingHP - 130, shadeslice.CurrentHP);
            Assert.AreEqual(firstAttackerHP - 20, firstAttacker.CurrentHP,
                "The same attacker must respect the 0.6 second reflection cooldown.");

            Assert.IsTrue(shadeslice.TryTakeAttackDamage(100, DamageType.True, secondAttacker));
            Assert.AreEqual(secondAttackerHP - 20, secondAttacker.CurrentHP,
                "A different attacker has an independent reflection cooldown.");
        }

        [Test]
        public void PerfectParry_DoesNotReduceOrReflectAreaOrEnvironmentalDamage()
        {
            SetRarity(OperatorRarity.Star5);
            TankEnemy attacker = CreateEnemy<TankEnemy>(EnemyArchetype.Tank);
            int attackerHP = attacker.CurrentHP;
            int startingHP = shadeslice.CurrentHP;

            Assert.IsTrue(shadeslice.TryTakeAreaAttackDamage(100, DamageType.True, attacker));
            Assert.AreEqual(startingHP - 100, shadeslice.CurrentHP);
            Assert.AreEqual(attackerHP, attacker.CurrentHP);

            Assert.IsTrue(shadeslice.TakeTrueDamage(100));
            Assert.AreEqual(startingHP - 200, shadeslice.CurrentHP);
            Assert.AreEqual(attackerHP, attacker.CurrentHP);
        }

        [Test]
        public void PerfectParry_IsLockedAtShadeslicesBaseRarity()
        {
            TankEnemy attacker = CreateEnemy<TankEnemy>(EnemyArchetype.Tank);
            int startingHP = shadeslice.CurrentHP;
            int attackerHP = attacker.CurrentHP;

            Assert.IsTrue(shadeslice.TryTakeAttackDamage(100, DamageType.True, attacker));
            Assert.AreEqual(startingHP - 100, shadeslice.CurrentHP);
            Assert.AreEqual(attackerHP, attacker.CurrentHP);
        }

        [Test]
        public void PhantomPursuit_UsesExitProximityAndAppliesIsolationMultiplierOnce()
        {
            GruntEnemy fartherEnemy = CreateEnemy<GruntEnemy>(EnemyArchetype.Grunt);
            GruntEnemy closestEnemy = CreateEnemy<GruntEnemy>(EnemyArchetype.Grunt);
            fartherEnemy.SetPath(new List<Vector3> { Vector3.zero, new Vector3(3f, 0f, 0f) });
            closestEnemy.SetPath(new List<Vector3> { Vector3.zero, new Vector3(2f, 0f, 0f) });
            typeof(EnemyBase).GetField("currentPathIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(closestEnemy, 1);
            fartherEnemy.transform.position = Vector3.zero;
            closestEnemy.transform.position = new Vector3(1.9f, 0f, 0f);

            Assert.AreSame(closestEnemy,
                ShadesliceOperator.FindClosestToExitTarget(new EnemyBase[] { fartherEnemy, closestEnemy }));
            Assert.AreEqual(330, ShadesliceOperator.CalculatePhantomAttackPower(110, 2f, false));
            Assert.AreEqual(660, ShadesliceOperator.CalculatePhantomAttackPower(110, 2f, true),
                "The existing 2x isolation multiplier must only be applied once.");
        }

        [Test]
        public void PhantomPursuit_CounterPersistsAcrossTargetsAndResetsEveryThirdSuccessfulHit()
        {
            SetRarity(OperatorRarity.Star4);
            TankEnemy attacker = CreateEnemy<TankEnemy>(EnemyArchetype.Tank);
            int startingHP = shadeslice.CurrentHP;
            int attackerHP = attacker.CurrentHP;
            Assert.IsTrue(shadeslice.TryTakeAttackDamage(100, DamageType.True, attacker));
            Assert.AreEqual(startingHP - 65, shadeslice.CurrentHP);
            Assert.AreEqual(attackerHP - 20, attacker.CurrentHP,
                "The 2x upgrade must retain the 1x Perfect Parry ability.");

            GruntEnemy firstTarget = CreateEnemy<GruntEnemy>(EnemyArchetype.Grunt);
            GruntEnemy secondTarget = CreateEnemy<GruntEnemy>(EnemyArchetype.Grunt);
            MethodInfo applyAttackDamage = typeof(ShadesliceOperator).GetMethod(
                "ApplyAttackDamage",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(applyAttackDamage);

            applyAttackDamage.Invoke(shadeslice, new object[] { firstTarget, 1 });
            Assert.AreEqual(1, shadeslice.SuccessfulNormalAttackCount);
            applyAttackDamage.Invoke(shadeslice, new object[] { secondTarget, 1 });
            Assert.AreEqual(2, shadeslice.SuccessfulNormalAttackCount);
            applyAttackDamage.Invoke(shadeslice, new object[] { secondTarget, 1 });
            Assert.AreEqual(0, shadeslice.SuccessfulNormalAttackCount);
        }

        [Test]
        public void FlyingEnemy_CannotTargetUnblockedShadeslice()
        {
            RangedFlyingEnemy attacker = CreateEnemy<RangedFlyingEnemy>(EnemyArchetype.Flyer);

            Assert.IsFalse(shadeslice.CanBeTargetedByEnemy(attacker));
        }

        [Test]
        public void CasterTargetSelection_IgnoresShadesliceUntilBlocked()
        {
            AskalaserEnemy attacker = CreateEnemy<AskalaserEnemy>(EnemyArchetype.Caster);

            Assert.IsNull(attacker.FindState1Target());
            Assert.IsTrue(shadeslice.TryBlock(attacker));
            Assert.AreSame(shadeslice, attacker.FindState1Target());
        }

        [Test]
        public void HextensionBindTargetSelection_IgnoresShadesliceUntilBlocked()
        {
            HextensionEnemy attacker = CreateEnemy<HextensionEnemy>(EnemyArchetype.Caster);
            attacker.transform.position = gridManager.GridToWorldPosition(1, 0);

            Assert.IsNull(attacker.FindBindTarget());
            Assert.IsTrue(shadeslice.TryBlock(attacker));
            Assert.AreSame(shadeslice, attacker.FindBindTarget());
        }

        [Test]
        public void RoboflameConeAttack_IgnoresShadesliceUntilBlocked()
        {
            RoboflameEnemy attacker = CreateEnemy<RoboflameEnemy>(EnemyArchetype.Caster);
            attacker.SetGridManager(gridManager);
            attacker.transform.position = gridManager.GridToWorldPosition(0, 0);

            Assert.IsEmpty(attacker.GetOperatorsInCone(Vector2Int.right));
            Assert.IsTrue(shadeslice.TryBlock(attacker));
            CollectionAssert.Contains(attacker.GetOperatorsInCone(Vector2Int.right), shadeslice);
        }

        [Test]
        public void ExplodingBugDeathBlast_DamagesShadeslice()
        {
            ExplodingBugEnemy attacker = CreateEnemy<ExplodingBugEnemy>(EnemyArchetype.Rusher);
            attacker.transform.position = gridManager.GridToWorldPosition(1, 0);
            int startingHP = shadeslice.CurrentHP;

            TestUtility.ExpectEditModeDestroy();
            attacker.TakeDamage(1000, DamageType.True);

            Assert.Less(shadeslice.CurrentHP, startingHP);
        }

        [Test]
        public void AttackAnimation_UsesRequestedFrameTimingAndTotalDuration()
        {
            float[] expectedDurations =
            {
                0.08f, 0.05f, 0.05f, 0.07f, 0.10f, 0.03f, 0.03f,
                0.04f, 0.05f, 0.09f, 0.07f, 0.04f, 0.06f, 0.09f
            };

            Assert.AreEqual(14, ShadesliceSpriteAnimation.AttackFrameCount);
            Assert.AreEqual(8, ShadesliceSpriteAnimation.ImpactFrameIndex);
            Assert.AreEqual(0.45f, ShadesliceSpriteAnimation.WindupSeconds, 0.0001f);
            Assert.AreEqual(0.85f, ShadesliceSpriteAnimation.AttackDuration, 0.0001f);
            for (int i = 0; i < expectedDurations.Length; i++)
                Assert.AreEqual(expectedDurations[i], ShadesliceSpriteAnimation.GetAttackFrameDuration(i), 0.0001f);
        }

        [Test]
        public void AttackAnimation_StartsWhenTargetEntersRangeAfterAttackIsReady()
        {
            const string prefabPath = "Assets/_Project/Prefabs/Operators/Prefab_OP_Shadeslice.prefab";
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab);

            ShadesliceSpriteAnimation prefabAnimation = prefab.GetComponent<ShadesliceSpriteAnimation>();
            Assert.IsNotNull(prefabAnimation);
            shadesliceObject.AddComponent<SpriteRenderer>();
            ShadesliceSpriteAnimation animation = shadesliceObject.AddComponent<ShadesliceSpriteAnimation>();
            animation.Configure(prefabAnimation.Frames, prefabAnimation.AttackFrames);
            TestUtility.CallAwake(animation);
            Assert.IsTrue(animation.HasAttackAnimation);

            GruntEnemy attacker = CreateEnemy<GruntEnemy>(EnemyArchetype.Grunt);
            Assert.IsTrue(shadeslice.TryBlock(attacker));

            float attackInterval = shadeslice.GetAttackInterval();
            typeof(OperatorBase).GetField("attackTimer", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(shadeslice, attackInterval);
            typeof(ShadesliceOperator).GetMethod("UpdateAttackWindup", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(shadeslice, null);

            Assert.IsTrue(animation.IsWindingUp);
            Assert.AreEqual(attackInterval - ShadesliceSpriteAnimation.WindupSeconds,
                (float)typeof(OperatorBase).GetField("attackTimer", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(shadeslice),
                0.0001f);
        }

        [TestCase(OperatorFacing.Left, -1f)]
        [TestCase(OperatorFacing.Right, 1f)]
        public void AttackProjectile_TravelsFixedDistanceInFacingDirectionWithoutHoming(
            OperatorFacing facing,
            float expectedDirection)
        {
            shadesliceData.projectileOrigin = new Vector2(-0.34f, 0.28f);
            shadeslice.SetFacing(facing);
            GruntEnemy attacker = CreateEnemy<GruntEnemy>(EnemyArchetype.Grunt);
            attacker.transform.position = shadeslice.transform.position + new Vector3(3f, 2f, 0f);

            var existingProjectiles = new HashSet<CombatProjectileVisual>(
                Object.FindObjectsByType<CombatProjectileVisual>());
            shadeslice.Attack(attacker);

            CombatProjectileVisual[] projectiles = Object.FindObjectsByType<CombatProjectileVisual>();
            CombatProjectileVisual attackProjectile = null;
            foreach (CombatProjectileVisual projectile in projectiles)
            {
                if (!existingProjectiles.Contains(projectile))
                {
                    attackProjectile = projectile;
                    break;
                }
            }

            Assert.IsNotNull(attackProjectile);
            try
            {
                var targetEnemyField = typeof(CombatProjectileVisual).GetField(
                    "targetEnemy",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var targetPositionField = typeof(CombatProjectileVisual).GetField(
                    "targetPosition",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(targetEnemyField);
                Assert.IsNotNull(targetPositionField);
                Assert.IsNull(targetEnemyField.GetValue(attackProjectile));

                Vector3 expectedStart = shadeslice.transform.position + Vector3.down * (2f / 32f);
                Vector3 expectedEnd = expectedStart + Vector3.right * expectedDirection;
                Assert.AreEqual(expectedStart, attackProjectile.transform.position);
                Assert.AreEqual(expectedEnd, (Vector3)targetPositionField.GetValue(attackProjectile));
                Assert.AreEqual(expectedStart.y, expectedEnd.y);
                Assert.AreNotEqual(attacker.VisualPosition, expectedEnd);
            }
            finally
            {
                Object.DestroyImmediate(attackProjectile.gameObject);
            }
        }

        private TEnemy CreateEnemy<TEnemy>(EnemyArchetype archetype) where TEnemy : EnemyBase
        {
            var data = ScriptableObject.CreateInstance<EnemyData>();
            data.enemyName = archetype.ToString();
            data.archetype = archetype;
            data.baseHP = 100;
            data.baseATK = 20;
            data.baseDEF = 0;
            data.baseRES = 0;
            enemyDataAssets.Add(data);

            var enemyObject = new GameObject($"Test{archetype}");
            enemyObjects.Add(enemyObject);
            TEnemy enemy = enemyObject.AddComponent<TEnemy>();
            enemy.Initialize(data, 1);
            return enemy;
        }

        private void SetRarity(OperatorRarity rarity)
        {
            typeof(OperatorBase).GetField("currentRarity", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(shadeslice, rarity);
        }
    }
}
