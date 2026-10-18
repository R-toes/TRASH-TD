using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.GameLoop;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Tests
{
    public class Stage2FallingItemsHazardTests
    {
        private GameObject hazardObject;
        private Stage2FallingItemsHazard hazard;
        private List<GameObject> spawnedObjects;

        [SetUp]
        public void SetUp()
        {
            spawnedObjects = new List<GameObject>();
            hazardObject = new GameObject("Stage2FallingItemsHazard");
            hazard = hazardObject.AddComponent<Stage2FallingItemsHazard>();
            spawnedObjects.Add(hazardObject);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < spawnedObjects.Count; i++)
            {
                if (spawnedObjects[i] != null)
                {
                    Object.DestroyImmediate(spawnedObjects[i]);
                }
            }
            spawnedObjects.Clear();
        }

        private T CreateTestOperator<T>(string name, int hp, int atk, int def = 0, int res = 0) where T : OperatorBase
        {
            var obj = new GameObject(name);
            spawnedObjects.Add(obj);
            var op = obj.AddComponent<T>();

            var opData = ScriptableObject.CreateInstance<OperatorData>();
            opData.operatorName = name;
            opData.baseHP = hp;
            opData.baseATK = atk;
            opData.baseDEF = def;
            opData.baseRES = res;
            opData.attackRange = 1;
            opData.attackInterval = 1f;
            opData.blockCount = 1;

            op.Initialize(opData, OperatorRarity.Star1, OperatorFacing.Right);
            op.Deploy(null);
            return op;
        }

        [Test]
        public void SpriteIndexMapping_MatchesUserSpecifications()
        {
            // 0 - 7 give weak but dot true damage
            for (int i = 0; i <= 7; i++)
            {
                Assert.AreEqual(Stage2ItemSet.WeakDotTrueDamage, Stage2FallingItemsHazard.GetSetForIndex(i),
                    $"Index {i} should be WeakDotTrueDamage");
            }

            // 8 - 15 deals true damage
            for (int i = 8; i <= 15; i++)
            {
                Assert.AreEqual(Stage2ItemSet.DirectTrueDamage, Stage2FallingItemsHazard.GetSetForIndex(i),
                    $"Index {i} should be DirectTrueDamage");
            }

            // 16 - 23 deals true damage and gives stun to the operator
            for (int i = 16; i <= 23; i++)
            {
                Assert.AreEqual(Stage2ItemSet.DirectTrueDamageAndStun, Stage2FallingItemsHazard.GetSetForIndex(i),
                    $"Index {i} should be DirectTrueDamageAndStun");
            }
        }

        [Test]
        public void HazardConstants_MatchSpecifications()
        {
            Assert.AreEqual(10.0f, Stage2FallingItemsHazard.DropIntervalSeconds, "Drop interval must be 10 seconds.");
            Assert.AreEqual(3.0f, Stage2FallingItemsHazard.FallHeightInTiles, "Fall height must be 3 tiles.");
            Assert.AreEqual(15, Stage2FallingItemsHazard.DirectTrueDamageAmount, "Direct true damage must be 15.");
            Assert.AreEqual(15, Stage2FallingItemsHazard.StunItemTrueDamageAmount, "Stun item true damage must be 15.");
            Assert.AreEqual(2.0f, Stage2FallingItemsHazard.StunDurationSeconds, "Stun duration must be 2 seconds.");
            Assert.AreEqual(2, Stage2FallingItemsHazard.DotDamagePerTick, "DoT tick damage must be 2.");
            Assert.AreEqual(5.0f, Stage2FallingItemsHazard.DotDurationSeconds, "DoT duration must be 5 seconds.");
        }

        [Test]
        public void DropProbabilities_When1To3OperatorsDeployed_RatesAre60Random_20Ally_20Enemies()
        {
            for (int count = 1; count <= 3; count++)
            {
                var (ally, enemy, random) = Stage2FallingItemsHazard.GetDropProbabilities(count);
                Assert.AreEqual(0.20f, ally, 0.001f, $"Ally chance should be 20% for {count} deployed operators.");
                Assert.AreEqual(0.20f, enemy, 0.001f, $"Enemy chance should be 20% for {count} deployed operators.");
                Assert.AreEqual(0.60f, random, 0.001f, $"Random tile chance should be 60% for {count} deployed operators.");
            }

            // When 4 or more operators are deployed
            var (ally4, enemy4, random4) = Stage2FallingItemsHazard.GetDropProbabilities(4);
            Assert.AreEqual(0.40f, ally4, 0.001f, "Ally chance should be 40% when 4+ deployed.");
            Assert.AreEqual(0.20f, enemy4, 0.001f, "Enemy chance should be 20% when 4+ deployed.");
            Assert.AreEqual(0.40f, random4, 0.001f, "Random tile chance should be 40% when 4+ deployed.");
        }

        [Test]
        public void Stagger_WhenDodgeSucceeds_AvoidsDirectDamageAndStun_AndGainsAtkBuff()
        {
            var stagger = CreateTestOperator<StaggerOperator>("Stagger", 110, 75);
            int initialHp = stagger.CurrentHP;

            // Force successful dodge
            stagger.RandomValueProvider = () => 0.05f;

            // Hit with stun item (16 - 23)
            hazard.ApplyEffectToOperator(stagger, Stage2ItemSet.DirectTrueDamageAndStun);

            // Should have avoided damage and stun
            Assert.AreEqual(initialHp, stagger.CurrentHP, "Stagger should avoid damage on successful dodge.");
            Assert.IsFalse(stagger.IsStunned, "Stagger should not be stunned on successful dodge.");
            Assert.IsTrue(stagger.HasDodgeAtkBuff, "Stagger should gain dodge ATK buff.");
            Assert.AreEqual(Mathf.RoundToInt(75 * 1.5f), stagger.CurrentATK, "Stagger ATK should increase by 50% after dodge.");
        }

        [Test]
        public void Stagger_WhenDodgeFails_TakesDamageAndReceivesStun()
        {
            var stagger = CreateTestOperator<StaggerOperator>("Stagger", 110, 75);
            int initialHp = stagger.CurrentHP;

            // Force failed dodge
            stagger.RandomValueProvider = () => 0.85f;

            // Hit with stun item (16 - 23)
            hazard.ApplyEffectToOperator(stagger, Stage2ItemSet.DirectTrueDamageAndStun);

            Assert.Less(stagger.CurrentHP, initialHp, "Stagger should take damage when dodge fails.");
            Assert.AreEqual(initialHp - Stage2FallingItemsHazard.StunItemTrueDamageAmount, stagger.CurrentHP);
            Assert.IsTrue(stagger.IsStunned, "Stagger should receive stun when dodge fails.");
        }

        [Test]
        public void BubbleShield_PopsAndAbsorbsFallingItemDamage()
        {
            var guard = CreateTestOperator<GuardOperator>("Guard", 100, 50);
            var bubblet = CreateTestOperator<BubbletsOperator>("Bubblet", 80, 40);

            var shield = BubbleShield.Apply(guard, bubblet.CurrentATK, bubblet);
            Assert.IsNotNull(shield);
            Assert.IsTrue(shield.IsActive);

            int hpBefore = guard.CurrentHP;

            // Apply direct true damage item (8 - 15)
            hazard.ApplyEffectToOperator(guard, Stage2ItemSet.DirectTrueDamage);

            // Bubble shield should pop and absorb damage
            Assert.IsFalse(shield.IsActive, "Bubble shield should pop.");
            Assert.AreEqual(hpBefore, guard.CurrentHP, "Operator HP should remain unchanged due to bubble shield absorption.");
        }

        [Test]
        public void Mossmo_TakesFallingItemDamage_TriggersCooldown()
        {
            var mossmo = CreateTestOperator<MossmoOperator>("Mossmo", 120, 40);
            Assert.AreEqual(0f, mossmo.HealCooldownTimer);

            // Hit with direct true damage item
            hazard.ApplyEffectToOperator(mossmo, Stage2ItemSet.DirectTrueDamage);

            Assert.AreEqual(120 - Stage2FallingItemsHazard.DirectTrueDamageAmount, mossmo.CurrentHP);
            Assert.Greater(mossmo.HealCooldownTimer, 0f, "Mossmo should enter reactive heal cooldown on damage.");
            Assert.AreEqual(3.0f, mossmo.HealCooldownTimer, 0.05f);
        }

        [Test]
        public void Proxyshot_TakesFallingItemDamage_ActivatesCamouflage()
        {
            var proxishot = CreateTestOperator<ProxishotOperator>("Proxishot", 100, 50);
            Assert.IsFalse(proxishot.IsCamouflaged);

            // Hit with direct true damage item
            hazard.ApplyEffectToOperator(proxishot, Stage2ItemSet.DirectTrueDamage);

            Assert.AreEqual(100 - Stage2FallingItemsHazard.DirectTrueDamageAmount, proxishot.CurrentHP);
            Assert.IsTrue(proxishot.IsCamouflaged, "Proxyshot should activate camouflage when damaged by falling item.");
        }

        [Test]
        public void Thornchin_TakesFallingItemDamage_DoesNotRetaliate()
        {
            var thornchin = CreateTestOperator<ThornchinOperator>("Thornchin", 150, 45);
            int initialHp = thornchin.CurrentHP;

            // Hit with falling item damage
            hazard.ApplyEffectToOperator(thornchin, Stage2ItemSet.DirectTrueDamage);

            Assert.AreEqual(initialHp - Stage2FallingItemsHazard.DirectTrueDamageAmount, thornchin.CurrentHP);
            // Thornchin simply took damage with attacker == null, no retaliation loop was invoked
        }

        [Test]
        public void DotEffect_DealsDamageOverTicks()
        {
            var guard = CreateTestOperator<GuardOperator>("Guard", 100, 50);
            int hpBefore = guard.CurrentHP;

            // Apply WeakDotTrueDamage (set 0 - 7)
            hazard.ApplyEffectToOperator(guard, Stage2ItemSet.WeakDotTrueDamage);

            // Immediate tick dealt 2 damage
            Assert.AreEqual(hpBefore - Stage2FallingItemsHazard.DotDamagePerTick, guard.CurrentHP);

            var dot = guard.GetComponent<Stage2ItemDotEffect>();
            Assert.IsNotNull(dot);

            // Advance 1 second
            dot.UpdateManual(1.0f);
            Assert.AreEqual(hpBefore - (Stage2FallingItemsHazard.DotDamagePerTick * 2), guard.CurrentHP);
        }
    }
}

