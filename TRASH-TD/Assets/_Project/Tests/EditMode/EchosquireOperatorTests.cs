using System.Collections.Generic;
using NUnit.Framework;
using TrashTD.Core.GameLoop;
using TrashTD.Data;
using TrashTD.Operators;
using UnityEngine;

namespace TrashTD.Tests
{
    public class EchosquireOperatorTests
    {
        [Test]
        public void LowHealthStats_IncreaseWithMissingHealthAndCapAtSixtyPercentWithoutChangingAttackSpeed()
        {
            var data = ScriptableObject.CreateInstance<OperatorData>();
            data.baseHP = 100;
            data.baseATK = 100;
            data.baseDEF = 40;
            data.baseRES = 20;
            data.attackInterval = 2f;

            var gameObject = new GameObject("EchosquireLowHealthStatsTest");
            var echosquire = gameObject.AddComponent<EchosquireOperator>();
            echosquire.Initialize(data, OperatorRarity.Star2);

            int baseAtk = data.GetScaledATK(OperatorRarity.Star2);
            int baseDef = data.GetScaledDEF(OperatorRarity.Star2);
            int baseRes = data.GetScaledRES(OperatorRarity.Star2);
            int maxHp = echosquire.MaxHP;

            Assert.AreEqual(baseAtk, echosquire.CurrentATK);
            Assert.AreEqual(baseDef, echosquire.CurrentDEF);
            Assert.AreEqual(baseRes, echosquire.CurrentRES);
            Assert.AreEqual(2f, echosquire.GetAttackInterval(), 0.0001f);

            echosquire.TakeTrueDamage(maxHp / 2);
            Assert.AreEqual(Mathf.RoundToInt(baseAtk * 1.5f), echosquire.CurrentATK);
            Assert.AreEqual(Mathf.RoundToInt(baseDef * 1.5f), echosquire.CurrentDEF);
            Assert.AreEqual(Mathf.RoundToInt(baseRes * 1.5f), echosquire.CurrentRES);
            Assert.AreEqual(2f, echosquire.GetAttackInterval(), 0.0001f);

            echosquire.TakeTrueDamage(maxHp / 4);
            Assert.AreEqual(Mathf.RoundToInt(baseAtk * 1.6f), echosquire.CurrentATK);
            Assert.AreEqual(Mathf.RoundToInt(baseDef * 1.6f), echosquire.CurrentDEF);
            Assert.AreEqual(Mathf.RoundToInt(baseRes * 1.6f), echosquire.CurrentRES);
            Assert.AreEqual(2f, echosquire.GetAttackInterval(), 0.0001f);

            Object.DestroyImmediate(gameObject);
            Object.DestroyImmediate(data);
        }

        [Test]
        public void SecondChance_RevivesOncePerWaveAndResetsOnNextWave()
        {
            var existingFloatingNumbers = new HashSet<GameObject>();
            foreach (GameObject existing in Object.FindObjectsByType<GameObject>())
                if (existing.name == "FloatingCombatNumber")
                    existingFloatingNumbers.Add(existing);

            var waveObject = new GameObject("EchosquireSecondChanceWaveTest");
            var waveManager = waveObject.AddComponent<WaveManager>();
            waveManager.SetWaveIndex(1);

            var data = ScriptableObject.CreateInstance<OperatorData>();
            data.baseHP = 100;
            var gameObject = new GameObject("EchosquireSecondChanceTest");
            var echosquire = gameObject.AddComponent<EchosquireOperator>();
            echosquire.Initialize(data, OperatorRarity.Star4);

            echosquire.TakeTrueDamage(echosquire.CurrentHP);
            Assert.GreaterOrEqual(echosquire.CurrentHP, Mathf.CeilToInt(echosquire.MaxHP * 0.5f));

            echosquire.TakeTrueDamage(echosquire.CurrentHP);
            Assert.AreEqual(0, echosquire.CurrentHP, "A second lethal hit in the same wave should be fatal.");

            echosquire.Initialize(data, OperatorRarity.Star4);
            waveManager.SetWaveIndex(2);
            echosquire.TakeTrueDamage(echosquire.CurrentHP);
            Assert.GreaterOrEqual(echosquire.CurrentHP, Mathf.CeilToInt(echosquire.MaxHP * 0.5f),
                "The second chance should be available again in the next wave.");

            Object.DestroyImmediate(gameObject);
            Object.DestroyImmediate(data);
            Object.DestroyImmediate(waveObject);

            GameObject[] floatingNumbers = Object.FindObjectsByType<GameObject>();
            foreach (GameObject floatingNumber in floatingNumbers)
                if (floatingNumber.name == "FloatingCombatNumber" &&
                    !existingFloatingNumbers.Contains(floatingNumber))
                    Object.DestroyImmediate(floatingNumber);
        }
    }
}
