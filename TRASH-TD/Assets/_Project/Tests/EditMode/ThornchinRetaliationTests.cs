using NUnit.Framework;
using UnityEngine;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;

namespace TrashTD.Tests
{
    public class ThornchinRetaliationTests
    {
        private GameObject thornchinObject;
        private GameObject attackerObject;
        private GameObject bystanderObject;
        private OperatorData operatorData;
        private EnemyData enemyData;
        private ThornchinOperator thornchin;
        private GruntEnemy attacker;
        private GruntEnemy bystander;

        [SetUp]
        public void SetUp()
        {
            operatorData = ScriptableObject.CreateInstance<OperatorData>();
            operatorData.operatorName = "Thornchin";
            operatorData.baseHP = 1000;
            operatorData.baseATK = 50;
            operatorData.baseDEF = 10;
            operatorData.damageType = DamageType.Physical;

            thornchinObject = new GameObject("TestThornchin");
            thornchin = thornchinObject.AddComponent<ThornchinOperator>();
            thornchin.Initialize(operatorData, OperatorRarity.Star2);
            thornchin.transform.position = Vector3.zero;

            enemyData = ScriptableObject.CreateInstance<EnemyData>();
            enemyData.baseHP = 200;
            enemyData.baseATK = 30;
            enemyData.baseDEF = 0;
            enemyData.damageType = DamageType.Physical;

            attackerObject = new GameObject("TestAttacker");
            attacker = attackerObject.AddComponent<GruntEnemy>();
            attacker.Initialize(enemyData, 1);
            attacker.transform.position = new Vector3(0.5f, 0f, 0f);

            bystanderObject = new GameObject("TestBystander");
            bystander = bystanderObject.AddComponent<GruntEnemy>();
            bystander.Initialize(enemyData, 1);
            bystander.transform.position = new Vector3(0.2f, 0f, 0f);
        }

        [TearDown]
        public void TearDown()
        {
            if (thornchinObject != null) Object.DestroyImmediate(thornchinObject);
            if (attackerObject != null) Object.DestroyImmediate(attackerObject);
            if (bystanderObject != null) Object.DestroyImmediate(bystanderObject);
            if (operatorData != null) Object.DestroyImmediate(operatorData);
            if (enemyData != null) Object.DestroyImmediate(enemyData);
        }

        [Test]
        public void TakeDamage_FromEnemyAttacker_RetaliatesAgainstAttacker()
        {
            int attackerStartHP = attacker.CurrentHP;
            int bystanderStartHP = bystander.CurrentHP;

            thornchin.TakeDamage(30, DamageType.Physical, attacker);

            Assert.Less(attacker.CurrentHP, attackerStartHP, "Attacker should take retaliation damage.");
            Assert.AreEqual(bystanderStartHP, bystander.CurrentHP, "Bystander should not take retaliation damage.");
        }

        [Test]
        public void TakeDamage_FromEnvironmentalDamage_DoesNotRetaliate()
        {
            int attackerStartHP = attacker.CurrentHP;
            int bystanderStartHP = bystander.CurrentHP;

            // Environmental damage (acid rain or trap/acid tiles) passes attacker = null
            thornchin.TakeDamage(30, DamageType.Physical, null);

            Assert.AreEqual(attackerStartHP, attacker.CurrentHP, "Attacker should not be damaged when damage is environmental.");
            Assert.AreEqual(bystanderStartHP, bystander.CurrentHP, "Bystander should not be damaged when damage is environmental.");
        }
    }
}

