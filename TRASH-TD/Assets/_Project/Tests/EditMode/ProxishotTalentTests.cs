using NUnit.Framework;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;

namespace TrashTD.Tests
{
    public class ProxishotTalentTests
    {
        private GameObject proxishotObject;
        private GameObject normalOperatorObject;
        private GameObject attackerObject;
        private GameObject evasiveEnemyObject;

        private OperatorData proxishotData;
        private OperatorData normalOperatorData;
        private EnemyData evasiveEnemyData;
        private EnemyData attackerEnemyData;

        private ProxishotOperator proxishot;
        private ThornchinOperator normalOperator;
        private GruntEnemy evasiveEnemy;
        private GruntEnemy attackerEnemy;
        private SpriteRenderer spriteRenderer;

        [SetUp]
        public void SetUp()
        {
            proxishotData = ScriptableObject.CreateInstance<OperatorData>();
            proxishotData.operatorName = "Proxishot";
            proxishotData.operatorClass = OperatorClass.Sniper;
            proxishotData.position = OperatorPosition.Ranged;
            proxishotData.baseHP = 100;
            proxishotData.baseATK = 150;
            proxishotData.baseDEF = 10;
            proxishotData.damageType = DamageType.Physical;

            normalOperatorData = ScriptableObject.CreateInstance<OperatorData>();
            normalOperatorData.operatorName = "NormalOp";
            normalOperatorData.operatorClass = OperatorClass.Defender;
            normalOperatorData.position = OperatorPosition.Melee;
            normalOperatorData.baseHP = 200;
            normalOperatorData.baseATK = 50;
            normalOperatorData.baseDEF = 20;
            normalOperatorData.damageType = DamageType.Physical;

            proxishotObject = new GameObject("TestProxishot");
            spriteRenderer = proxishotObject.AddComponent<SpriteRenderer>();
            spriteRenderer.color = Color.white;
            proxishot = proxishotObject.AddComponent<ProxishotOperator>();
            proxishot.Initialize(proxishotData, OperatorRarity.Star2);

            normalOperatorObject = new GameObject("TestNormalOp");
            normalOperator = normalOperatorObject.AddComponent<ThornchinOperator>();
            normalOperator.Initialize(normalOperatorData, OperatorRarity.Star2);

            evasiveEnemyData = ScriptableObject.CreateInstance<EnemyData>();
            evasiveEnemyData.baseHP = 500;
            evasiveEnemyData.baseDEF = 0;
            evasiveEnemyData.operatorAttackMissChance = 1.0f; // 100% miss chance

            evasiveEnemyObject = new GameObject("TestEvasiveEnemy");
            evasiveEnemy = evasiveEnemyObject.AddComponent<GruntEnemy>();
            evasiveEnemy.Initialize(evasiveEnemyData, 1);

            attackerEnemyData = ScriptableObject.CreateInstance<EnemyData>();
            attackerEnemyData.baseHP = 200;
            attackerEnemyData.baseATK = 25;
            attackerEnemyData.baseDEF = 0;

            attackerObject = new GameObject("TestAttackerEnemy");
            attackerEnemy = attackerObject.AddComponent<GruntEnemy>();
            attackerEnemy.Initialize(attackerEnemyData, 1);
        }

        [TearDown]
        public void TearDown()
        {
            if (proxishotObject != null) Object.DestroyImmediate(proxishotObject);
            if (normalOperatorObject != null) Object.DestroyImmediate(normalOperatorObject);
            if (attackerObject != null) Object.DestroyImmediate(attackerObject);
            if (evasiveEnemyObject != null) Object.DestroyImmediate(evasiveEnemyObject);
            if (proxishotData != null) Object.DestroyImmediate(proxishotData);
            if (normalOperatorData != null) Object.DestroyImmediate(normalOperatorData);
            if (evasiveEnemyData != null) Object.DestroyImmediate(evasiveEnemyData);
            if (attackerEnemyData != null) Object.DestroyImmediate(attackerEnemyData);
        }

        [Test]
        public void IgnoresMissMechanic_ReturnsTrueForProxishot_AndFalseForDefaultOperator()
        {
            Assert.IsTrue(proxishot.IgnoresMissMechanic, "Proxishot should ignore miss mechanic.");
            Assert.IsFalse(normalOperator.IgnoresMissMechanic, "Default operator should not ignore miss mechanic.");
        }

        [Test]
        public void TryTakeAttackDamage_FromProxishot_IgnoresMissChance_WhileNormalOperatorMisses()
        {
            int startingHP = evasiveEnemy.CurrentHP;

            // Normal operator attacks 100% miss enemy -> should miss
            bool normalHit = evasiveEnemy.TryTakeAttackDamage(20, DamageType.Physical, normalOperator);
            Assert.IsFalse(normalHit, "Normal operator attack should miss against 100% miss chance enemy.");
            Assert.AreEqual(startingHP, evasiveEnemy.CurrentHP, "Enemy HP should not change when attack misses.");

            // Proxishot attacks 100% miss enemy -> should ignore miss chance and hit
            bool proxishotHit = evasiveEnemy.TryTakeAttackDamage(20, DamageType.Physical, proxishot);
            Assert.IsTrue(proxishotHit, "Proxishot attack should ignore miss chance and hit.");
            Assert.Less(evasiveEnemy.CurrentHP, startingHP, "Enemy should have taken damage from Proxishot.");
        }

        [Test]
        public void TakeDamage_FromEnemyAttacker_ActivatesCamouflageAndLowersOpacity()
        {
            Assert.IsFalse(proxishot.IsCamouflaged, "Proxishot should not be camouflaged initially.");
            Assert.IsTrue(proxishot.IsTargetable, "Proxishot should be targetable initially.");
            Assert.AreEqual(1.0f, spriteRenderer.color.a, 0.001f, "Initial alpha should be 1.0.");

            proxishot.TakeDamage(15, DamageType.Physical, attackerEnemy);

            Assert.IsTrue(proxishot.IsCamouflaged, "Proxishot should be camouflaged after taking damage from an enemy.");
            Assert.IsFalse(proxishot.IsTargetable, "Proxishot should not be targetable while camouflaged.");
            Assert.Greater(proxishot.CamouflageTimeRemaining, 0f, "Camouflage time remaining should be positive.");
            Assert.AreEqual(ProxishotOperator.CamouflageAlpha, spriteRenderer.color.a, 0.001f, "Sprite opacity should be lowered to CamouflageAlpha.");
        }

        [Test]
        public void TakeDamage_FromEnvironmentalDamage_DoesNotActivateCamouflage()
        {
            // Environmental damage has attacker == null
            proxishot.TakeDamage(15, DamageType.Physical, null);

            Assert.IsFalse(proxishot.IsCamouflaged, "Proxishot should not gain camouflage from environmental damage.");
            Assert.IsTrue(proxishot.IsTargetable, "Proxishot should remain targetable.");
            Assert.AreEqual(1.0f, spriteRenderer.color.a, 0.001f, "Sprite opacity should remain 1.0.");
        }

        [Test]
        public void DeployAndRetreat_ResetsCamouflageAndRestoresAlpha()
        {
            var cell = new GridCell(0, 0, TileType.HighGround, Vector3.zero);
            bool deployed = proxishot.Deploy(cell);
            Assert.IsTrue(deployed, "Proxishot should deploy successfully onto ranged platform.");

            proxishot.ActivateCamouflage();
            Assert.IsTrue(proxishot.IsCamouflaged, "Proxishot should be camouflaged.");
            Assert.AreEqual(ProxishotOperator.CamouflageAlpha, spriteRenderer.color.a, 0.001f);

            proxishot.Retreat();
            Assert.IsFalse(proxishot.IsCamouflaged, "Camouflage should reset on retreat.");
            Assert.IsTrue(proxishot.IsTargetable, "Proxishot should be targetable again once retreated.");
            Assert.AreEqual(1.0f, spriteRenderer.color.a, 0.001f, "Sprite alpha should be restored to 1.0 on retreat.");
        }

#if UNITY_EDITOR
        [Test]
        public void OperatorStats_BasurocketAndProxyshot_MatchExpectedValues()
        {
            var basurocket = UnityEditor.AssetDatabase.LoadAssetAtPath<OperatorData>("Assets/_Project/Data/Operators/OP_Sniper_Basurocket.asset");
            Assert.IsNotNull(basurocket, "OP_Sniper_Basurocket asset should exist.");
            Assert.AreEqual(50, basurocket.baseHP, "Basurocket HP should be 50.");
            Assert.AreEqual(175, basurocket.baseATK, "Basurocket ATK should be 175.");
            Assert.AreEqual(2.8f, basurocket.attackInterval, 0.001f, "Basurocket attack interval should be 2.8.");

            var proxishot = UnityEditor.AssetDatabase.LoadAssetAtPath<OperatorData>("Assets/_Project/Data/Operators/OP_Sniper_Proxishot.asset");
            Assert.IsNotNull(proxishot, "OP_Sniper_Proxishot asset should exist.");
            Assert.AreEqual(2.3f, proxishot.attackInterval, 0.001f, "Proxyshot attack interval should be 2.3.");
        }
#endif
    }
}
