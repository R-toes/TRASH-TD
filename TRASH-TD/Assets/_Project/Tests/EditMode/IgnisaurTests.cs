using NUnit.Framework;
using UnityEngine;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;

namespace TrashTD.Tests
{
    public class IgnisaurTests
    {
        private GameObject ignisaurObject;
        private GameObject ownTileTargetObject;
        private GameObject yellowTargetObject;
        private GameObject greenTargetObject;
        private GameObject airTargetObject;

        private OperatorData operatorData;
        private EnemyData highDefEnemyData;
        private EnemyData highResEnemyData;
        private EnemyData airEnemyData;

        private IgnisaurOperator ignisaur;
        private GruntEnemy ownTileTarget;
        private GruntEnemy yellowTarget;
        private GruntEnemy greenTarget;
        private GruntEnemy airTarget;

        [SetUp]
        public void SetUp()
        {
            operatorData = ScriptableObject.CreateInstance<OperatorData>();
            operatorData.operatorName = "Ignisaur";
            operatorData.operatorClass = OperatorClass.Guard;
            operatorData.position = OperatorPosition.Melee;
            operatorData.baseHP = 115;
            operatorData.baseATK = 100;
            operatorData.baseDEF = 18;
            operatorData.baseRES = 10;
            operatorData.blockCount = 1;
            operatorData.attackRange = 2;
            operatorData.attackInterval = 1.0f;
            operatorData.rangePattern = new[]
            {
                new Vector2Int(1, 0),
                new Vector2Int(1, 1),
                new Vector2Int(1, -1),
                new Vector2Int(2, 0)
            };
            operatorData.damageType = DamageType.Arts;

            ignisaurObject = new GameObject("TestIgnisaur");
            ignisaur = ignisaurObject.AddComponent<IgnisaurOperator>();
            ignisaur.Initialize(operatorData, OperatorRarity.Star2, OperatorFacing.Right);
            ignisaur.transform.position = Vector3.zero;

            // Ground enemy with 50 DEF and 0 RES
            highDefEnemyData = ScriptableObject.CreateInstance<EnemyData>();
            highDefEnemyData.baseHP = 500;
            highDefEnemyData.baseDEF = 50;
            highDefEnemyData.baseRES = 0;
            highDefEnemyData.movementType = EnemyMovementType.Ground;

            // Ground enemy with 0 DEF and 50 RES
            highResEnemyData = ScriptableObject.CreateInstance<EnemyData>();
            highResEnemyData.baseHP = 500;
            highResEnemyData.baseDEF = 0;
            highResEnemyData.baseRES = 50;
            highResEnemyData.movementType = EnemyMovementType.Ground;

            // Air enemy
            airEnemyData = ScriptableObject.CreateInstance<EnemyData>();
            airEnemyData.baseHP = 200;
            airEnemyData.baseDEF = 10;
            airEnemyData.baseRES = 0;
            airEnemyData.movementType = EnemyMovementType.Air;

            ownTileTargetObject = new GameObject("OwnTileTarget");
            ownTileTarget = ownTileTargetObject.AddComponent<GruntEnemy>();
            ownTileTarget.Initialize(highDefEnemyData, 1);
            ownTileTarget.transform.position = Vector3.zero; // Own tile (0, 0)

            yellowTargetObject = new GameObject("YellowTarget");
            yellowTarget = yellowTargetObject.AddComponent<GruntEnemy>();
            yellowTarget.Initialize(highDefEnemyData, 1);
            yellowTarget.transform.position = new Vector3(1f, 0f, 0f); // Yellow tile (1, 0)

            greenTargetObject = new GameObject("GreenTarget");
            greenTarget = greenTargetObject.AddComponent<GruntEnemy>();
            greenTarget.Initialize(highDefEnemyData, 1);
            greenTarget.transform.position = new Vector3(1f, 1f, 0f); // Green tile (1, 1)

            airTargetObject = new GameObject("AirTarget");
            airTarget = airTargetObject.AddComponent<GruntEnemy>();
            airTarget.Initialize(airEnemyData, 1);
            airTarget.transform.position = new Vector3(2f, 0f, 0f); // Green tile (2, 0)
        }

        [TearDown]
        public void TearDown()
        {
            if (ignisaurObject != null) Object.DestroyImmediate(ignisaurObject);
            if (ownTileTargetObject != null) Object.DestroyImmediate(ownTileTargetObject);
            if (yellowTargetObject != null) Object.DestroyImmediate(yellowTargetObject);
            if (greenTargetObject != null) Object.DestroyImmediate(greenTargetObject);
            if (airTargetObject != null) Object.DestroyImmediate(airTargetObject);
            if (operatorData != null) Object.DestroyImmediate(operatorData);
            if (highDefEnemyData != null) Object.DestroyImmediate(highDefEnemyData);
            if (highResEnemyData != null) Object.DestroyImmediate(highResEnemyData);
            if (airEnemyData != null) Object.DestroyImmediate(airEnemyData);
        }

        [Test]
        public void BlockCount_IsOne()
        {
            Assert.AreEqual(1, ignisaur.GetBlockCount());
        }

        [Test]
        public void CanHitAir_ReturnsTrue()
        {
            Assert.IsTrue(ignisaur.CanHitAir);
        }

        [Test]
        public void BaseHP_IsBalanced()
        {
            Assert.AreEqual(115, operatorData.baseHP);
        }

        [Test]
        public void Protean_WhenNotBlocking_ReducesAttackInterval()
        {
            Assert.IsFalse(ignisaur.IsBlocking);
            float baseInterval = operatorData.attackInterval;
            Assert.AreEqual(baseInterval * 0.80f, ignisaur.GetAttackInterval(), 0.001f);
        }

        [Test]
        public void Protean_WhenBlocking_IncreasesAttackPower()
        {
            int baseAtk = ignisaur.CurrentATK;

            // Engage a block
            ignisaur.TryBlock(ownTileTarget);
            Assert.IsTrue(ignisaur.IsBlocking);

            Assert.AreEqual(Mathf.RoundToInt(baseAtk * 1.25f), ignisaur.CurrentATK);
            Assert.AreEqual(operatorData.attackInterval, ignisaur.GetAttackInterval(), 0.001f);
        }

        [Test]
        public void TargetTile_OnlyOwnTileIsPhysical_YellowAndGreenTilesAreArts()
        {
            Assert.IsTrue(ignisaur.IsOwnTileTarget(ownTileTarget));
            Assert.IsFalse(ignisaur.IsOwnTileTarget(yellowTarget));
            Assert.IsFalse(ignisaur.IsOwnTileTarget(greenTarget));

            Assert.AreEqual(DamageType.Physical, ignisaur.GetDamageTypeForTarget(ownTileTarget));
            Assert.AreEqual(DamageType.Arts, ignisaur.GetDamageTypeForTarget(yellowTarget));
            Assert.AreEqual(DamageType.Arts, ignisaur.GetDamageTypeForTarget(greenTarget));
        }

        [Test]
        public void Attack_OnOwnTile_DealsPhysicalDamage_MitigatedByDEF()
        {
            // Set up target on own tile with High DEF (50) and 0 RES
            ownTileTarget.Initialize(highDefEnemyData, 1);
            int startHP = ownTileTarget.CurrentHP;

            ignisaur.Attack(ownTileTarget);

            // ATK 100 vs DEF 50 Physical: Damage = 100 - 50 = 50 damage
            int expectedDamage = 100 - 50;
            Assert.AreEqual(startHP - expectedDamage, ownTileTarget.CurrentHP);
        }

        [Test]
        public void Attack_OnYellowTile_DealsArtsDamage_MitigatedByRESNotDEF()
        {
            // Yellow tile now deals Arts damage! High DEF (50) is ignored because RES is 0
            yellowTarget.Initialize(highDefEnemyData, 1);
            int startHP = yellowTarget.CurrentHP;

            ignisaur.Attack(yellowTarget);

            int expectedDamage = 100;
            Assert.AreEqual(startHP - expectedDamage, yellowTarget.CurrentHP);
        }

        [Test]
        public void Attack_OnGreenTile_DealsArtsDamage_MitigatedByRESNotDEF()
        {
            // Green tile deals Arts damage. High DEF (50) is ignored because RES is 0
            greenTarget.Initialize(highDefEnemyData, 1);
            int startHP = greenTarget.CurrentHP;

            ignisaur.Attack(greenTarget);

            int expectedDamage = 100;
            Assert.AreEqual(startHP - expectedDamage, greenTarget.CurrentHP);
        }

        [Test]
        public void Attack_CanHitAirEnemy()
        {
            int startHP = airTarget.CurrentHP;
            ignisaur.Attack(airTarget);
            Assert.Less(airTarget.CurrentHP, startHP, "Aerial enemy should take damage from Ignisaur.");
        }
    }
}
