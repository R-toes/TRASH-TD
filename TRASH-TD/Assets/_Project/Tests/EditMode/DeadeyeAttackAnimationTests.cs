using NUnit.Framework;
using TrashTD.Operators;
using UnityEditor;
using UnityEngine;

namespace TrashTD.Tests
{
    public class DeadeyeAttackAnimationTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Operators/Prefab_OP_Deadeye.prefab";
        private const string IdleSheetPath = "Assets/_Project/Art/Operators/Deadeye/Deadeye-Sheet.png";
        private const string AttackSheetPath = "Assets/_Project/Art/Operators/Deadeye/Deadeye Attack Sheet.png";

        [Test]
        public void Prefab_UsesScriptAnimationAndAlignedPointFilteredFrames()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab);
            Assert.IsNull(prefab.GetComponent<Animator>());

            DeadeyeSpriteAnimation animation = prefab.GetComponent<DeadeyeSpriteAnimation>();
            Assert.IsNotNull(animation);
            Assert.AreEqual(DeadeyeSpriteAnimation.AttackFrameCount, animation.AttackFrames.Length);

            foreach (Sprite frame in animation.Frames)
            {
                Assert.AreEqual(32f, frame.rect.width);
                Assert.AreEqual(32f, frame.rect.height);
                Assert.AreEqual(32f, frame.pixelsPerUnit);
                Assert.AreEqual(new Vector2(16f, 16f), frame.pivot);
            }

            foreach (Sprite frame in animation.AttackFrames)
            {
                Assert.AreEqual(48f, frame.rect.width);
                Assert.AreEqual(32f, frame.rect.height);
                Assert.AreEqual(32f, frame.pixelsPerUnit);
                Assert.AreEqual(new Vector2(32f, 16f), frame.pivot);
            }

            AssertImporterSettings(IdleSheetPath, false);
            AssertImporterSettings(AttackSheetPath, true);
        }

        [Test]
        public void AttackFrameTiming_MatchesRequestedDurationsAndLaunchesOnFrameFive()
        {
            float[] expectedDurations =
            {
                0.12f, 0.07f, 0.06f, 0.10f, 0.11f,
                0.06f, 0.06f, 0.08f, 0.08f, 0.12f
            };
            float totalDuration = 0f;
            float windupDuration = 0f;
            for (int i = 0; i < expectedDurations.Length; i++)
            {
                Assert.AreEqual(expectedDurations[i], DeadeyeSpriteAnimation.GetAttackFrameDuration(i), 0.0001f);
                totalDuration += expectedDurations[i];
                if (i < DeadeyeSpriteAnimation.FireFrame)
                    windupDuration += expectedDurations[i];
            }

            Assert.AreEqual(0.86f, totalDuration, 0.0001f);
            Assert.AreEqual(DeadeyeSpriteAnimation.AttackDuration, totalDuration, 0.0001f);
            Assert.AreEqual(DeadeyeSpriteAnimation.WindupTime, windupDuration, 0.0001f);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                DeadeyeSpriteAnimation animation = instance.GetComponent<DeadeyeSpriteAnimation>();
                SpriteRenderer renderer = instance.GetComponent<SpriteRenderer>();

                animation.BeginAttack(DeadeyeSpriteAnimation.WindupTime);
                Assert.AreSame(animation.AttackFrames[0], renderer.sprite);

                animation.ShotFired();
                Assert.AreEqual(DeadeyeSpriteAnimation.FireFrame, animation.CurrentAttackFrame);
                Assert.AreSame(animation.AttackFrames[DeadeyeSpriteAnimation.FireFrame], renderer.sprite);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void FirePoint_IsPlacedAtFingertipAndMirrorsWithFacing()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                DeadeyeSpriteAnimation animation = instance.GetComponent<DeadeyeSpriteAnimation>();
                SpriteRenderer renderer = instance.GetComponent<SpriteRenderer>();
                Transform firePoint = animation.FirePoint;

                Assert.AreEqual("FirePoint", firePoint.name);
                Assert.AreEqual(new Vector3(-14f / 32f, 1f / 32f, 0f), firePoint.localPosition);

                renderer.flipX = true;
                animation.RefreshFirePointFacing();
                Assert.AreEqual(new Vector3(14f / 32f, 1f / 32f, 0f), firePoint.localPosition);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void AssertImporterSettings(string path, bool requireUncompressed)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsNotNull(importer);
            Assert.AreEqual(32, importer.spritePixelsPerUnit);
            Assert.AreEqual(FilterMode.Point, importer.filterMode);
            Assert.AreEqual(SpriteImportMode.Multiple, importer.spriteImportMode);
            if (requireUncompressed)
                Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression);
        }
    }
}
