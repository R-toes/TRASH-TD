using NUnit.Framework;
using TrashTD.Data;
using TrashTD.Operators;
using UnityEditor;
using UnityEngine;

namespace TrashTD.Tests
{
    public class QiFuAttackAnimationTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Operators/Prefab_OP_QiFu.prefab";
        [Test]
        public void Prefab_UsesCenteredIdleAndAttackFramesAtSamePixelsPerUnit()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab);

            var animation = prefab.GetComponent<QiFuSpriteAnimation>();
            Assert.IsNotNull(animation);
            Assert.IsNull(prefab.GetComponent<OperatorSpriteAnimation>());
            Assert.AreEqual(8, animation.Frames.Length);
            Assert.AreEqual(QiFuSpriteAnimation.AttackFrameCount, animation.AttackFrames.Length);

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
                Assert.AreEqual(new Vector2(46f, 16f), frame.pivot);
            }
        }

        [Test]
        public void Strike_DisplaysSixthAttackFrameAtAttackStart()
        {
            Assert.AreEqual(5, QiFuSpriteAnimation.StrikeFrameIndex);
            Assert.AreEqual(0.35f, QiFuSpriteAnimation.WindupDuration, 0.0001f);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var instance = Object.Instantiate(prefab);
            var animation = instance.GetComponent<QiFuSpriteAnimation>();
            var spriteRenderer = instance.GetComponent<SpriteRenderer>();

            animation.BeginAttack(QiFuSpriteAnimation.WindupDuration);
            Assert.IsTrue(animation.IsWindingUp);
            Assert.AreSame(animation.AttackFrames[0], spriteRenderer.sprite);

            animation.Strike();
            Assert.IsFalse(animation.IsWindingUp);
            Assert.IsTrue(animation.IsRecovering);
            Assert.AreEqual(QiFuSpriteAnimation.StrikeFrameIndex, animation.CurrentAttackFrame);
            Assert.AreSame(animation.AttackFrames[5], spriteRenderer.sprite);

            Object.DestroyImmediate(instance);
        }

        [Test]
        public void AttackFrameTiming_MatchesRequestedDurationsAndKeepsStrikeAtFrameFive()
        {
            float[] expectedDurations =
            {
                0.10f, 0.10f, 0.05f, 0.05f, 0.05f, 0.05f,
                0.05f, 0.05f, 0.03f, 0.04f, 0.10f
            };

            float totalDuration = 0f;
            float windupDuration = 0f;
            for (int i = 0; i < expectedDurations.Length; i++)
            {
                Assert.AreEqual(expectedDurations[i], QiFuSpriteAnimation.GetAttackFrameDuration(i), 0.0001f);
                totalDuration += expectedDurations[i];
                if (i < QiFuSpriteAnimation.StrikeFrameIndex)
                    windupDuration += expectedDurations[i];
            }

            Assert.AreEqual(0.67f, totalDuration, 0.0001f);
            Assert.AreEqual(totalDuration, QiFuSpriteAnimation.AttackDuration, 0.0001f);
            Assert.AreEqual(QiFuSpriteAnimation.WindupDuration, windupDuration, 0.0001f);
            Assert.AreEqual(0f, QiFuSpriteAnimation.GetAttackFrameDuration(expectedDurations.Length), 0.0001f);
        }

        [Test]
        public void StrikePoint_IsAtThePawAndMirrorsWithFacing()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var instance = Object.Instantiate(prefab);
            instance.transform.position = new Vector3(4f, 5f, 0f);
            var animation = instance.GetComponent<QiFuSpriteAnimation>();
            var spriteRenderer = instance.GetComponent<SpriteRenderer>();

            animation.Strike();
            Assert.AreEqual(new Vector3(4f - 24f / 32f, 5f + 4f / 32f, 0f),
                animation.GetStrikePointWorldPosition());

            spriteRenderer.flipX = true;
            Assert.AreEqual(new Vector3(4f + 24f / 32f, 5f + 4f / 32f, 0f),
                animation.GetStrikePointWorldPosition());

            Object.DestroyImmediate(instance);
        }

        [TestCase(OperatorRarity.Star1, 4)]
        [TestCase(OperatorRarity.Star2, 3)]
        [TestCase(OperatorRarity.Star3, 2)]
        [TestCase(OperatorRarity.Star4, 2)]
        [TestCase(OperatorRarity.Star5, 2)]
        public void KnockbackStats_ScaleWithRarity(OperatorRarity rarity, int expectedHits)
        {
            var gameObject = new GameObject("QiFuKnockbackFrequencyTest");
            var qiFu = gameObject.AddComponent<QiFuOperator>();
            var data = ScriptableObject.CreateInstance<OperatorData>();

            qiFu.Initialize(data, rarity);

            Assert.AreEqual(expectedHits, qiFu.HitsPerKnockback);
            Assert.AreEqual(rarity >= OperatorRarity.Star3 ? 2 : 1, qiFu.KnockbackDistance);

            Object.DestroyImmediate(data);
            Object.DestroyImmediate(gameObject);
        }
    }
}
