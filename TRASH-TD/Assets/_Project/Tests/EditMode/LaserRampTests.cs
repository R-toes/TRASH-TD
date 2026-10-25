using NUnit.Framework;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Enemies;

namespace TrashTD.Tests
{
    /// <summary>The ramping laser's power stages (Castiel, Askalaser) line up with the damage steps.</summary>
    public class LaserRampTests
    {
        [Test]
        public void AskalaserStage_FollowsDamageSteps()
        {
            for (float t = 0f; t < AskalaserEnemy.LaserStepDuration * 8f; t += 0.25f)
            {
                int stage = AskalaserEnemy.GetLaserStageForDuration(t);
                int[] damageByStage = { 5, 15, 30, 80, 100, 150 };
                Assert.AreEqual(damageByStage[stage], AskalaserEnemy.GetLaserDamageForDuration(t), $"t = {t}");
            }

            Assert.AreEqual(LightLaserVisual.MaxStage, AskalaserEnemy.GetLaserStageForDuration(999f));
        }

        [Test]
        public void LaserVisual_ClampsStage()
        {
            var source = new GameObject("Source");
            var target = new GameObject("Target");
            target.transform.position = new Vector3(3f, 0f, 0f);
            LightLaserVisual laser = LightLaserVisual.Fire(source.transform, target.transform, LaserPalette.Crimson, 0.375f, null);

            try
            {
                Assert.AreEqual(0, laser.Stage);
                laser.SetStage(3);
                Assert.AreEqual(3, laser.Stage);
                laser.SetStage(99);
                Assert.AreEqual(LightLaserVisual.MaxStage, laser.Stage);
                laser.SetStage(-4);
                Assert.AreEqual(0, laser.Stage);
            }
            finally
            {
                Object.DestroyImmediate(laser.gameObject);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(target);
                foreach (var particles in Object.FindObjectsByType<PixelParticles>())
                    Object.DestroyImmediate(particles.gameObject);
            }
        }
    }
}
