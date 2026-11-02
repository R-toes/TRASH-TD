using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using TrashTD.Data;
using TrashTD.Operators;

namespace TrashTD.Tests
{
    public class FlurmonicaTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in created)
                if (obj != null) Object.DestroyImmediate(obj);
            created.Clear();
        }

        private T MakeOperator<T>(OperatorClass operatorClass, int hp = 100, int def = 40, int res = 10, float interval = 1f)
            where T : OperatorBase
        {
            var data = ScriptableObject.CreateInstance<OperatorData>();
            data.operatorName = "Test" + operatorClass;
            data.operatorClass = operatorClass;
            data.baseRarity = OperatorRarity.Star1;
            data.baseHP = hp;
            data.baseATK = 50;
            data.baseDEF = def;
            data.baseRES = res;
            data.attackInterval = interval;
            data.damageType = operatorClass == OperatorClass.Caster ? DamageType.Arts : DamageType.Physical;
            created.Add(data);

            var obj = new GameObject("Test" + operatorClass);
            obj.AddComponent<SpriteRenderer>();
            created.Add(obj);
            T op = obj.AddComponent<T>();
            op.Initialize(data, OperatorRarity.Star1);
            return op;
        }

        // ---- Per-class bonus ----

        [Test]
        public void Defender_GainsFourPercentBaseDefPerStack()
        {
            var op = MakeOperator<DefenderOperator>(OperatorClass.Defender, def: 50);
            int baseDef = op.Data.GetScaledDEF(op.CurrentRarity);

            op.SetHarmonyStacks(3);
            Assert.AreEqual(baseDef + Mathf.RoundToInt(baseDef * 0.12f), op.CurrentDEF);

            op.SetHarmonyStacks(0);
            Assert.AreEqual(baseDef, op.CurrentDEF);
        }

        [Test]
        public void Guard_GainsFourPercentBaseResPerStack()
        {
            var op = MakeOperator<GuardOperator>(OperatorClass.Guard, res: 50);
            int baseRes = op.Data.GetScaledRES(op.CurrentRarity);

            op.SetHarmonyStacks(2);
            Assert.AreEqual(baseRes + Mathf.RoundToInt(baseRes * 0.08f), op.CurrentRES);
            Assert.AreEqual(op.Data.GetScaledDEF(op.CurrentRarity), op.CurrentDEF, "Guards don't get DEF");

            op.SetHarmonyStacks(0);
            Assert.AreEqual(baseRes, op.CurrentRES);
        }

        [Test]
        public void Specialist_MaxHpRisesWithStacks_AndCurrentHpIsClampedWhenTheyExpire()
        {
            var op = MakeOperator<SpecialistOperator>(OperatorClass.Specialist, hp: 200);
            int baseHp = op.Data.GetScaledHP(op.CurrentRarity);
            int bonus = Mathf.RoundToInt(baseHp * 0.12f);

            op.SetHarmonyStacks(3);
            Assert.AreEqual(baseHp + bonus, op.MaxHP);
            Assert.AreEqual(baseHp + bonus, op.CurrentHP, "Current HP rises with max HP");

            op.SetHarmonyStacks(0);
            Assert.AreEqual(baseHp, op.MaxHP);
            Assert.AreEqual(baseHp, op.CurrentHP, "Current HP is clamped to the new max");
        }

        [Test]
        public void SniperAndMedic_AttackFourPercentFasterPerStack()
        {
            var sniper = MakeOperator<SniperOperator>(OperatorClass.Sniper, interval: 1.12f);
            var medic = MakeOperator<MedicOperator>(OperatorClass.Medic, interval: 2.24f);
            float sniperBase = sniper.GetAttackInterval();
            float medicBase = medic.GetAttackInterval();

            sniper.SetHarmonyStacks(3);
            medic.SetHarmonyStacks(3);

            Assert.AreEqual(sniperBase / 1.12f, sniper.GetAttackInterval(), 0.0001f);
            Assert.AreEqual(medicBase / 1.12f, medic.GetAttackInterval(), 0.0001f);
        }

        [Test]
        public void Caster_DealsFourPercentMoreArtsDamagePerStack_OtherClassesDont()
        {
            var caster = MakeOperator<CasterOperator>(OperatorClass.Caster);
            var guard = MakeOperator<GuardOperator>(OperatorClass.Guard);

            caster.SetHarmonyStacks(2);
            guard.SetHarmonyStacks(2);

            Assert.AreEqual(1.08f, caster.OutgoingArtsDamageMultiplier, 0.0001f);
            Assert.AreEqual(1f, guard.OutgoingArtsDamageMultiplier, 0.0001f);
            Assert.AreEqual(1f, caster.HarmonyAttackSpeedMultiplier, 0.0001f, "Casters don't get attack speed");
        }

        // ---- Stacks ----

        [Test]
        public void Stacks_CapAtThree_AndAFourthRefreshesTheOldestTimer()
        {
            var op = MakeOperator<GuardOperator>(OperatorClass.Guard);
            HarmonyBuff buff = HarmonyBuff.GetOrAdd(op);

            buff.AddStack();         // t0: expires at 8
            buff.Tick(2f);
            buff.AddStack();         // t2: expires at 10
            buff.Tick(2f);
            buff.AddStack();         // t4: expires at 12
            buff.Tick(2f);
            Assert.AreEqual(3, buff.AddStack(), "t6: stays at 3; the t0 stack restarts and now expires at 14");

            buff.Tick(3f);           // t9: the t0 stack would be gone without the refresh
            Assert.AreEqual(3, buff.Stacks);
            buff.Tick(1.5f);         // t10.5: the t2 stack is gone
            Assert.AreEqual(2, buff.Stacks);
            Assert.AreEqual(2, op.HarmonyStacks, "The operator's bonus follows the stack count");
        }

        [Test]
        public void ChooseBuffTarget_PrefersFewestStacks()
        {
            var a = MakeOperator<GuardOperator>(OperatorClass.Guard);
            var b = MakeOperator<GuardOperator>(OperatorClass.Guard);
            var c = MakeOperator<GuardOperator>(OperatorClass.Guard);
            HarmonyBuff.GetOrAdd(a).AddStack();
            HarmonyBuff.GetOrAdd(a).AddStack();
            HarmonyBuff.GetOrAdd(b).AddStack();

            var allies = new List<OperatorBase> { a, b, c };
            Assert.AreSame(c, FlurmonicaOperator.ChooseBuffTarget(allies, n => 0));

            HarmonyBuff.GetOrAdd(c).AddStack();
            Assert.AreSame(b, FlurmonicaOperator.ChooseBuffTarget(allies, n => 0), "Tie at 1 stack: b or c, never a");
            Assert.AreSame(c, FlurmonicaOperator.ChooseBuffTarget(allies, n => n - 1));
        }

        // ---- The spec's steady states (cast every 2 s, 8 s stacks, 60 fps) ----

        [TestCase(1, 3, 3)]
        [TestCase(2, 2, 2)]
        [TestCase(3, 1, 2)]
        [TestCase(4, 1, 1)]
        public void SteadyState_MatchesTheSpec(int allyCount, int expectedMin, int expectedMax)
        {
            Simulate(allyCount, out int[] min, out int[] max, out int peakTotal);
            for (int i = 0; i < allyCount; i++)
            {
                Assert.AreEqual(expectedMin, min[i], $"ally {i} lowest stacks");
                Assert.AreEqual(expectedMax, max[i], $"ally {i} highest stacks");
            }
            Assert.LessOrEqual(peakTotal, 4);
        }

        [Test]
        public void SteadyState_FiveAllies_FourHoldOneStackAndOneWaits()
        {
            Simulate(5, out int[] min, out int[] max, out int peakTotal);
            Assert.AreEqual(4, peakTotal, "Never more than 4 stacks in total");
            for (int i = 0; i < 5; i++)
                Assert.LessOrEqual(max[i], 1);
        }

        private void Simulate(int allyCount, out int[] min, out int[] max, out int peakTotal)
        {
            var allies = new List<OperatorBase>();
            var buffs = new List<HarmonyBuff>();
            for (int i = 0; i < allyCount; i++)
            {
                var ally = MakeOperator<GuardOperator>(OperatorClass.Guard);
                allies.Add(ally);
                buffs.Add(HarmonyBuff.GetOrAdd(ally));
            }

            var rng = new System.Random(7);
            const float dt = 1f / 60f;
            const float interval = 2f;
            float timer = interval;
            min = new int[allyCount];
            max = new int[allyCount];
            for (int i = 0; i < allyCount; i++) min[i] = int.MaxValue;
            peakTotal = 0;

            for (int frame = 0; frame < 60 * 60; frame++)
            {
                timer += dt;
                foreach (HarmonyBuff buff in buffs) buff.Tick(dt);

                // Same cast rule as FlurmonicaOperator.Update
                if (timer >= interval - HarmonyBuff.TimerSlack)
                {
                    OperatorBase target = FlurmonicaOperator.ChooseBuffTarget(allies, n => rng.Next(n));
                    HarmonyBuff.GetOrAdd(target).AddStack();
                    timer = Mathf.Clamp(timer - interval, 0f, interval);
                }

                if (frame < 60 * 10) continue; // Let it settle
                int total = 0;
                for (int i = 0; i < allyCount; i++)
                {
                    int stacks = buffs[i].Stacks;
                    min[i] = Mathf.Min(min[i], stacks);
                    max[i] = Mathf.Max(max[i], stacks);
                    total += stacks;
                }
                peakTotal = Mathf.Max(peakTotal, total);
            }
        }
    }
}
