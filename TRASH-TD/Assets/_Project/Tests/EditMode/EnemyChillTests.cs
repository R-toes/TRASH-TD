using NUnit.Framework;
using UnityEngine;
using TrashTD.Data;
using TrashTD.Enemies;

namespace TrashTD.Tests
{
    public class EnemyChillTests
    {
        private GameObject enemyObject;
        private GameObject rusherObject;
        private EnemyData enemyData;
        private ChillTestEnemy enemy;
        private SpriteRenderer spriteRenderer;

        [SetUp]
        public void SetUp()
        {
            enemyObject = new GameObject("ChillTestEnemy");
            spriteRenderer = enemyObject.AddComponent<SpriteRenderer>();
            enemy = enemyObject.AddComponent<ChillTestEnemy>();

            enemyData = ScriptableObject.CreateInstance<EnemyData>();
            enemyData.moveSpeed = 1f;
            enemy.Initialize(enemyData, 1);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(enemyObject);
            if (rusherObject != null) Object.DestroyImmediate(rusherObject);
            Object.DestroyImmediate(enemyData);
        }

        [Test]
        public void ApplyChill_SlowsEnemyAndTintsItBlue()
        {
            enemy.ApplyChill(40f, 0.5f, 2.5f, 100f, 1.5f);

            Assert.AreEqual(0.5f, enemy.CurrentMoveSpeed, 0.001f);
            Assert.Greater(spriteRenderer.color.b, spriteRenderer.color.r * 3f);
            Assert.IsFalse(enemy.IsFrozen);
        }

        [Test]
        public void ApplyChill_ThresholdFreezesEnemyThenRestoresRemainingSlow()
        {
            enemy.ApplyChill(40f, 0.5f, 2.5f, 100f, 1.5f);
            enemy.ApplyChill(60f, 0.5f, 2.5f, 100f, 1.5f);

            Assert.IsTrue(enemy.IsFrozen);
            Assert.AreEqual(0f, enemy.CurrentMoveSpeed);
            Assert.AreEqual(0f, enemy.ChillAmount);

            enemy.AdvanceChillStatus(1.6f);

            Assert.IsFalse(enemy.IsFrozen);
            Assert.AreEqual(0.5f, enemy.CurrentMoveSpeed, 0.001f);

            enemy.AdvanceChillStatus(1f);

            Assert.AreEqual(1f, enemy.CurrentMoveSpeed, 0.001f);
            Assert.AreEqual(Color.white, spriteRenderer.color);
        }

        [Test]
        public void UpdateChillStatus_LeavesRusherBaseSpeedUntouched()
        {
            rusherObject = new GameObject("ChillTestRusher");
            rusherObject.AddComponent<SpriteRenderer>();
            var rusher = rusherObject.AddComponent<ChillTestRusher>();
            enemyData.moveSpeed = 0.75f;
            rusher.Initialize(enemyData, 1);

            rusher.AdvanceChillStatus(1f);

            Assert.AreEqual(1.125f, rusher.CurrentMoveSpeed, 0.001f);
        }
    }

    public class ChillTestEnemy : GruntEnemy
    {
        public void AdvanceChillStatus(float elapsedSeconds)
        {
            UpdateChillStatus(elapsedSeconds);
        }
    }

    public class ChillTestRusher : RusherEnemy
    {
        public void AdvanceChillStatus(float elapsedSeconds)
        {
            UpdateChillStatus(elapsedSeconds);
        }
    }
}
