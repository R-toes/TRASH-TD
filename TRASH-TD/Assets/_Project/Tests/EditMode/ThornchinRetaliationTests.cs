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

        [Test]
        public void ThornchinStar3_AppliesDOTAndPoisonVisualToAttacker()
        {
            thornchin.Initialize(operatorData, OperatorRarity.Star3);

            thornchin.TakeDamage(30, DamageType.Physical, attacker);

            var dot = attacker.GetComponent<TrashTD.Combat.ThornchinSpikeDOT>();
            Assert.IsNotNull(dot, "Star3 Thornchin retaliation should apply ThornchinSpikeDOT to attacker.");
            Assert.AreEqual(1, dot.CurrentStacks);

            var visual = attacker.GetComponent<TrashTD.Combat.ThornchinPoisonVisual>();
            Assert.IsNotNull(visual, "ThornchinSpikeDOT must attach ThornchinPoisonVisual to afflicted enemy.");
            Assert.AreEqual(1, visual.CurrentStacks);

            var overlay = visual.transform.Find(TrashTD.Combat.ThornchinPoisonVisual.GlowOverlayName);
            Assert.IsNotNull(overlay, "ThornchinPoisonVisual must create the poison glow overlay.");
        }

        [Test]
        public void ThornchinPoisonVisual_TracksStacks_AndCleansUpOnExpire()
        {
            TrashTD.Combat.ThornchinSpikeDOT.Apply(attacker, thornchin);
            var dot = attacker.GetComponent<TrashTD.Combat.ThornchinSpikeDOT>();
            var visual = attacker.GetComponent<TrashTD.Combat.ThornchinPoisonVisual>();
            Assert.IsNotNull(visual);
            Assert.AreEqual(1, visual.CurrentStacks);

            // Add stacks up to 4
            dot.AddStack();
            dot.AddStack();
            dot.AddStack();
            Assert.AreEqual(4, visual.CurrentStacks);

            // Trigger damage tick
            visual.OnDamageTick(visual.CurrentStacks);

            // Tick down until expiration
            dot.Tick(6.0f);
            Assert.IsNull(attacker.GetComponent<TrashTD.Combat.ThornchinSpikeDOT>(), "DOT should expire and be destroyed.");
            Assert.IsNull(attacker.GetComponent<TrashTD.Combat.ThornchinPoisonVisual>(), "Poison visual should be removed upon expiration.");
        }

        [Test]
        public void PoisonBubbleSprites_AreGeneratedWithoutError()
        {
            Color testColor = TrashTD.Combat.ThornchinPoisonVisual.PoisonViolet;
            var lg = TrashTD.Combat.CombatFxArt.PoisonBubbleLarge(testColor);
            var md = TrashTD.Combat.CombatFxArt.PoisonBubbleMedium(testColor);
            var sm = TrashTD.Combat.CombatFxArt.PoisonBubbleSmall(testColor);

            Assert.IsNotNull(lg, "Large poison bubble sprite must be generated.");
            Assert.IsNotNull(md, "Medium poison bubble sprite must be generated.");
            Assert.IsNotNull(sm, "Small poison bubble sprite must be generated.");
        }
    }
}

