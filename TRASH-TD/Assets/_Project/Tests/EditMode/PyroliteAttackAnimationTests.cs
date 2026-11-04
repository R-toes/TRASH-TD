using NUnit.Framework;
using TrashTD.Combat;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;
using TrashTD.Systems;
using UnityEngine;

namespace TrashTD.Tests
{
    public class PyroliteAttackAnimationTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Operators/Prefab_OP_Pyrolite.prefab";

        [Test]
        public void Prefab_UsesCenteredIdleAndAttackFramesAtSamePixelsPerUnit()
        {
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab);

            var animation = prefab.GetComponent<PyroliteSpriteAnimation>();
            Assert.IsNotNull(animation);
            Assert.IsNull(prefab.GetComponent<OperatorSpriteAnimation>());
            Assert.AreEqual(4, animation.Frames.Length);
            Assert.AreEqual(PyroliteSpriteAnimation.AttackFrameCount, animation.AttackFrames.Length);

            foreach (Sprite frame in animation.Frames)
            {
                Assert.AreEqual(32f, frame.rect.width);
                Assert.AreEqual(32f, frame.rect.height);
                Assert.AreEqual(32f, frame.pixelsPerUnit);
                Assert.AreEqual(new Vector2(16f, 16f), frame.pivot);
            }

            foreach (Sprite frame in animation.AttackFrames)
            {
                Assert.AreEqual(64f, frame.rect.width);
                Assert.AreEqual(32f, frame.rect.height);
                Assert.AreEqual(32f, frame.pixelsPerUnit);
                Assert.AreEqual(new Vector2(32f, 16f), frame.pivot);
            }
        }

        [Test]
        public void Animation_FiresOnNinthFrameAndUsesSpecifiedTiming()
        {
            Assert.AreEqual(8, PyroliteSpriteAnimation.FiringFrameIndex);
            Assert.AreEqual(0.64f, PyroliteSpriteAnimation.AttackWindupSeconds, 0.0001f);
            Assert.AreEqual(0.06f, PyroliteSpriteAnimation.GetAttackFrameDuration(8), 0.0001f);
            Assert.AreEqual(1.02f, PyroliteSpriteAnimation.AttackDuration, 0.0001f);

            float windupDuration = 0f;
            for (int i = 0; i < PyroliteSpriteAnimation.FiringFrameIndex; i++)
                windupDuration += PyroliteSpriteAnimation.GetAttackFrameDuration(i);
            Assert.AreEqual(PyroliteSpriteAnimation.AttackWindupSeconds, windupDuration, 0.0001f);

            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var instance = Object.Instantiate(prefab);
            var animation = instance.GetComponent<PyroliteSpriteAnimation>();
            var spriteRenderer = instance.GetComponent<SpriteRenderer>();

            animation.BeginAttack(PyroliteSpriteAnimation.AttackWindupSeconds);
            Assert.IsTrue(animation.IsWindingUp);
            Assert.AreSame(animation.AttackFrames[0], spriteRenderer.sprite);

            animation.Fire();
            Assert.IsFalse(animation.IsWindingUp);
            Assert.IsTrue(animation.IsRecovering);
            Assert.AreEqual(PyroliteSpriteAnimation.FiringFrameIndex, animation.CurrentAttackFrame);
            Assert.AreSame(animation.AttackFrames[PyroliteSpriteAnimation.FiringFrameIndex], spriteRenderer.sprite);

            Object.DestroyImmediate(instance);
        }

        [Test]
        public void MuzzleOffset_IsMirroredByFacingAndUsesFrameCenter()
        {
            var gameObject = new GameObject("PyroliteMuzzleTest");
            gameObject.transform.position = new Vector3(3f, 4f, 0f);
            var pyrolite = gameObject.AddComponent<PyroliteOperator>();
            var data = ScriptableObject.CreateInstance<OperatorData>();
            pyrolite.Initialize(data, OperatorRarity.Star1, OperatorFacing.Left);

            Assert.AreEqual(new Vector3(3f - 25f / 32f, 4f - 2f / 32f, 0f),
                pyrolite.GetAttackMuzzlePosition());

            pyrolite.SetFacing(OperatorFacing.Right);
            Assert.AreEqual(new Vector3(3f + 25f / 32f, 4f - 2f / 32f, 0f),
                pyrolite.GetAttackMuzzlePosition());

            Object.DestroyImmediate(data);
            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void FirstUpgrade_AddsTwoCellsBesideFurthestRangeTile()
        {
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<OperatorData>(
                "Assets/_Project/Data/Operators/OP_Caster_Pyrolite.asset");
            Assert.IsNotNull(data);

            Vector2Int[] baseRange = PyroliteOperator.GetRangePatternForRarity(data, OperatorRarity.Star1);
            Vector2Int[] upgradedRange = PyroliteOperator.GetRangePatternForRarity(data, OperatorRarity.Star2);

            Assert.AreEqual(data.rangePattern.Length, baseRange.Length);
            Assert.AreEqual(data.rangePattern.Length + 2, upgradedRange.Length);
            CollectionAssert.Contains(upgradedRange, new Vector2Int(2, 1));
            CollectionAssert.Contains(upgradedRange, new Vector2Int(2, -1));
        }

        [TestCase(OperatorRarity.Star2, 1)]
        [TestCase(OperatorRarity.Star3, 2)]
        [TestCase(OperatorRarity.Star4, 2)]
        [TestCase(OperatorRarity.Star5, 2)]
        public void AoeDamageHitCount_ScalesAtSecondUpgrade(OperatorRarity rarity, int expectedHits)
        {
            bool originalHazardsEnabled = StageCombatModifiers.HazardsEnabled;
            StageCombatModifiers.HazardsEnabled = false;
            var managerObject = new GameObject("PyroliteAoeTestEnemyManager");
            var enemyManager = managerObject.AddComponent<EnemyManager>();
            var operatorData = ScriptableObject.CreateInstance<OperatorData>();
            operatorData.baseATK = 80;
            var enemyData = ScriptableObject.CreateInstance<EnemyData>();
            enemyData.baseHP = 10000;
            enemyData.baseDEF = 0;
            enemyData.baseRES = 0;

            var operatorObject = new GameObject("PyroliteAoeTestOperator");
            var pyrolite = operatorObject.AddComponent<PyroliteOperator>();
            pyrolite.Initialize(operatorData, rarity);

            var primaryObject = new GameObject("PyroliteAoeTestPrimary");
            var primary = primaryObject.AddComponent<PyroliteAoeTestEnemy>();
            primary.Initialize(enemyData, 1);
            primary.transform.position = Vector3.zero;
            enemyManager.RegisterEnemy(primary);

            var secondaryObject = new GameObject("PyroliteAoeTestSecondary");
            var secondary = secondaryObject.AddComponent<PyroliteAoeTestEnemy>();
            secondary.Initialize(enemyData, 1);
            secondary.transform.position = Vector3.right;
            enemyManager.RegisterEnemy(secondary);

            int primaryHpBefore = primary.CurrentHP;
            int secondaryHpBefore = secondary.CurrentHP;
            int expectedPrimaryDamage = DamageCalculator.CalculateDamage(pyrolite.CurrentATK, primary.CurrentRES);
            int expectedSecondaryATK = Mathf.RoundToInt(pyrolite.CurrentATK * 0.75f);
            int expectedSecondaryDamage = DamageCalculator.CalculateDamage(expectedSecondaryATK, secondary.CurrentRES);
            int blastVisualCountBefore = Object.FindObjectsByType<AoeBlastVisual>().Length;

            try
            {
                pyrolite.ApplyAttackImpact(primary);

                Assert.AreEqual(expectedPrimaryDamage * expectedHits, primaryHpBefore - primary.CurrentHP);
                Assert.AreEqual(expectedSecondaryDamage * expectedHits, secondaryHpBefore - secondary.CurrentHP);
                Assert.AreEqual(expectedHits, Object.FindObjectsByType<AoeBlastVisual>().Length - blastVisualCountBefore);
            }
            finally
            {
                StageCombatModifiers.HazardsEnabled = originalHazardsEnabled;
                foreach (AoeBlastVisual visual in Object.FindObjectsByType<AoeBlastVisual>())
                    Object.DestroyImmediate(visual.gameObject);
                Object.DestroyImmediate(primaryObject);
                Object.DestroyImmediate(secondaryObject);
                Object.DestroyImmediate(operatorObject);
                Object.DestroyImmediate(managerObject);
                Object.DestroyImmediate(enemyData);
                Object.DestroyImmediate(operatorData);
            }
        }
    }

    public sealed class PyroliteAoeTestEnemy : EnemyBase
    {
    }
}
