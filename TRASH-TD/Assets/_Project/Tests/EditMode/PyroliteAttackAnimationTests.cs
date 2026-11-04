using NUnit.Framework;
using TrashTD.Data;
using TrashTD.Operators;
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
    }
}
