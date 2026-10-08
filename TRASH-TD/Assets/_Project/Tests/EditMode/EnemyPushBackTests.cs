using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;

namespace TrashTD.Tests
{
    public class EnemyPushBackTests
    {
        private GameObject gridObject;
        private GameObject enemyObject;
        private TestPushBackEnemy enemy;

        [SetUp]
        public void SetUp()
        {
            gridObject = new GameObject("PushBackTestGrid");
            var gridManager = gridObject.AddComponent<GridManager>();
            enemyObject = new GameObject("PushBackTestEnemy");
            enemy = enemyObject.AddComponent<TestPushBackEnemy>();
            enemy.SetGridManager(gridManager);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(enemyObject);
            Object.DestroyImmediate(gridObject);
        }

        [Test]
        public void PushBack_SlidesOneCellAlongRouteAndKeepsForwardPathValid()
        {
            enemy.SetPath(new List<Vector3>
            {
                new Vector3(0f, 0f),
                new Vector3(1f, 0f),
                new Vector3(1f, 1f),
                new Vector3(2f, 1f)
            });
            enemy.transform.position = new Vector3(1f, 0.75f);
            SetPathIndex(2);

            Vector3 startPosition = enemy.transform.position;
            Assert.IsTrue(enemy.PushBack(1));
            Assert.IsTrue(enemy.IsPushingBack);
            Assert.AreEqual(startPosition, enemy.transform.position);

            SetField("pushbackElapsed", GetField<float>("pushbackDuration"));
            typeof(EnemyBase)
                .GetMethod("UpdatePushback", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(enemy, null);

            Assert.That(enemy.transform.position.x, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(enemy.transform.position.y, Is.EqualTo(0f).Within(0.001f));
            Assert.AreEqual(1, GetPathIndex());
            Assert.IsFalse(enemy.IsPushingBack);
        }

        [Test]
        public void PushBack_ReturnsFalseAtTheStartOfTheRoute()
        {
            enemy.SetPath(new List<Vector3>
            {
                Vector3.zero,
                Vector3.right
            });
            enemy.transform.position = Vector3.zero;
            SetPathIndex(1);

            Assert.IsFalse(enemy.PushBack(1));
            Assert.AreEqual(Vector3.zero, enemy.transform.position);
        }

        [Test]
        public void PushBack_ExtendsThroughOperatorOccupyingDestinationCell()
        {
            var gridManager = gridObject.GetComponent<GridManager>();
            var cells = new GridCell[4, 1];
            for (int x = 0; x < cells.GetLength(0); x++)
                cells[x, 0] = new GridCell(x, 0, TileType.EnemyPath, new Vector3(x + 0.5f, 0.5f, 0f));

            typeof(GridManager)
                .GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(gridManager, cells);
            typeof(GridManager)
                .GetField("<Width>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(gridManager, 4);
            typeof(GridManager)
                .GetField("<Height>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(gridManager, 1);

            var operatorObject = new GameObject("PushbackRouteOperator");
            try
            {
                cells[1, 0].Deploy(operatorObject);
                enemy.SetPath(new List<Vector3>
                {
                    new Vector3(0.5f, 0.5f),
                    new Vector3(1.5f, 0.5f),
                    new Vector3(2.5f, 0.5f),
                    new Vector3(3.5f, 0.5f)
                });
                enemy.transform.position = new Vector3(2.5f, 0.5f);
                SetPathIndex(3);

                Assert.IsTrue(enemy.PushBack(1));
                Assert.That(GetField<Vector3>("pushbackTargetPosition").x, Is.EqualTo(0.5f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(operatorObject);
            }
        }

        private void SetPathIndex(int index)
        {
            SetField("currentPathIndex", index);
        }

        private int GetPathIndex()
        {
            return GetField<int>("currentPathIndex");
        }

        private void SetField(string fieldName, object value)
        {
            typeof(EnemyBase)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(enemy, value);
        }

        private T GetField<T>(string fieldName)
        {
            return (T)typeof(EnemyBase)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(enemy);
        }
    }

    public sealed class TestPushBackEnemy : EnemyBase
    {
    }
}
