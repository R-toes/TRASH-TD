using System.Collections.Generic;
using NUnit.Framework;
using TrashTD.Core.GameLoop;
using TrashTD.Data;
using TrashTD.Operators;
using UnityEditor;
using UnityEngine;

namespace TrashTD.Tests
{
    public class EchosquireOperatorTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Operators/Prefab_OP_Echosquire.prefab";
        private const string IdleSheetPath = "Assets/_Project/Art/Operators/Echosquire_Idle_Aligned_64x32.png";
        private const string AttackSheetPath = "Assets/_Project/Art/Operators/Echosquire_Attack_Aligned_64x32.png";

        [Test]
        public void Prefab_UsesAlignedIdleAndAttackSheets()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab);

            EchosquireSpriteAnimation animation = prefab.GetComponent<EchosquireSpriteAnimation>();
            Assert.IsNotNull(animation);
            Assert.AreEqual(EchosquireSpriteAnimation.IdleFrameCount, animation.Frames.Length);
            Assert.AreEqual(EchosquireSpriteAnimation.AttackFrameCount, animation.AttackFrames.Length);
            Assert.IsNull(prefab.GetComponent<OperatorSpriteAnimation>());

            AssertFrames(animation.Frames, IdleSheetPath);
            AssertFrames(animation.AttackFrames, AttackSheetPath);
            AssertImporterSettings(IdleSheetPath);
            AssertImporterSettings(AttackSheetPath);
        }

        [Test]
        public void AttackTiming_MatchesFrameTableAndBeginsImpactOnFrameSix()
        {
            float[] expectedDurations =
            {
                0.14f, 0.11f, 0.17f, 0.06f, 0.05f,
                0.055f, 0.09f, 0.10f, 0.12f, 0.22f
            };
            float totalDuration = 0f;
            float windupDuration = 0f;
            for (int i = 0; i < expectedDurations.Length; i++)
            {
                Assert.AreEqual(expectedDurations[i], EchosquireSpriteAnimation.GetAttackFrameDuration(i), 0.0001f);
                totalDuration += expectedDurations[i];
                if (i < EchosquireSpriteAnimation.ImpactFrameIndex)
                    windupDuration += expectedDurations[i];
            }

            Assert.AreEqual(1.115f, totalDuration, 0.0001f);
            Assert.AreEqual(totalDuration, EchosquireSpriteAnimation.AttackDuration, 0.0001f);
            Assert.AreEqual(0.53f, EchosquireSpriteAnimation.WindupSeconds, 0.0001f);
            Assert.AreEqual(EchosquireSpriteAnimation.WindupSeconds, windupDuration, 0.0001f);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                EchosquireSpriteAnimation animation = instance.GetComponent<EchosquireSpriteAnimation>();
                SpriteRenderer renderer = instance.GetComponent<SpriteRenderer>();

                animation.BeginAttack(EchosquireSpriteAnimation.WindupSeconds);
                Assert.AreSame(animation.AttackFrames[0], renderer.sprite);

                animation.BeginImpact();
                Assert.AreEqual(EchosquireSpriteAnimation.ImpactFrameIndex, animation.CurrentAttackFrame);
                Assert.AreSame(animation.AttackFrames[EchosquireSpriteAnimation.ImpactFrameIndex], renderer.sprite);
                Assert.IsTrue(animation.IsRecovering);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

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

        private static void AssertFrames(Sprite[] frames, string expectedPath)
        {
            foreach (Sprite frame in frames)
            {
                Assert.AreEqual(expectedPath, AssetDatabase.GetAssetPath(frame));
                Assert.AreEqual(64f, frame.rect.width);
                Assert.AreEqual(32f, frame.rect.height);
                Assert.AreEqual(32f, frame.pixelsPerUnit);
                Assert.AreEqual(new Vector2(32f, 16f), frame.pivot);
            }
        }

        private static void AssertImporterSettings(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsNotNull(importer);
            Assert.AreEqual(32, importer.spritePixelsPerUnit);
            Assert.AreEqual(FilterMode.Point, importer.filterMode);
            Assert.AreEqual(SpriteImportMode.Multiple, importer.spriteImportMode);
        }
    }
}
