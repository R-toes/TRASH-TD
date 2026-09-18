using System;
using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;

namespace TrashTD.Systems
{
    /// <summary>
    /// Manages all active enemies in the stage.
    /// Handles spawning, spatial tracking, target queries for operators,
    /// and blocking interactions with operators.
    /// </summary>
    public class EnemyManager : MonoBehaviour
    {
        public static EnemyManager Instance { get; private set; }

        [SerializeField] private GridManager gridManager;

        private readonly List<EnemyBase> activeEnemies = new List<EnemyBase>();

        public IReadOnlyList<EnemyBase> ActiveEnemies => activeEnemies;
        public int ActiveEnemyCount => activeEnemies.Count;

        public event Action<EnemyBase> OnEnemySpawned;
        public event Action<EnemyBase> OnEnemyDied;
        public event Action<EnemyBase> OnEnemyReachedExit;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (gridManager == null)
            {
                gridManager = FindFirstObjectByType<GridManager>();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Spawn an enemy from EnemyData along an A* path.
        /// </summary>
        public EnemyBase SpawnEnemy(EnemyData enemyData, List<Vector3> path, int difficultyLevel)
        {
            if (enemyData == null || path == null || path.Count == 0) return null;

            GameObject enemyObj;
            if (enemyData.enemyPrefab != null)
            {
                enemyObj = Instantiate(enemyData.enemyPrefab, path[0], Quaternion.identity);
            }
            else
            {
                // Fallback placeholder GameObject with sprite renderer
                enemyObj = new GameObject($"Enemy_{enemyData.enemyName}");
                enemyObj.transform.position = path[0];
                var sr = enemyObj.AddComponent<SpriteRenderer>();
                if (enemyData.sprite != null)
                {
                    sr.sprite = enemyData.sprite;
                }
            }

            // Ensure archetype component
            EnemyBase enemyComp = enemyObj.GetComponent<EnemyBase>();
            if (enemyComp == null)
            {
                enemyComp = AddArchetypeComponent(enemyObj, enemyData.archetype);
            }

            enemyComp.Initialize(enemyData, difficultyLevel);
            enemyComp.SetPath(path);

            enemyComp.OnDied += HandleEnemyDied;
            enemyComp.OnReachedExit += HandleEnemyReachedExit;

            activeEnemies.Add(enemyComp);
            OnEnemySpawned?.Invoke(enemyComp);

            return enemyComp;
        }

        private EnemyBase AddArchetypeComponent(GameObject obj, EnemyArchetype archetype)
        {
            return archetype switch
            {
                EnemyArchetype.Grunt => obj.AddComponent<GruntEnemy>(),
                EnemyArchetype.Rusher => obj.AddComponent<RusherEnemy>(),
                EnemyArchetype.Tank => obj.AddComponent<TankEnemy>(),
                EnemyArchetype.Caster => obj.AddComponent<EnemyCaster>(),
                EnemyArchetype.Flyer => obj.AddComponent<FlyerEnemy>(),
                _ => obj.AddComponent<GruntEnemy>()
            };
        }

        private void Update()
        {
            CheckBlockingInteractions();
        }

        /// <summary>
        /// Checks if any unblocked ground enemy is on a cell with an available blocker.
        /// </summary>
        private void CheckBlockingInteractions()
        {
            if (gridManager == null) return;

            for (int i = 0; i < activeEnemies.Count; i++)
            {
                var enemy = activeEnemies[i];
                if (enemy == null || enemy.IsDead || enemy.IsBlocked) continue;
                if (enemy.MovementType == EnemyMovementType.Air) continue;
                if (enemy.Data != null && enemy.Data.isUnblockable) continue;

                Vector2Int gridPos = gridManager.WorldToGridPosition(enemy.transform.position);
                GridCell cell = gridManager.GetCell(gridPos);
                if (cell != null && cell.IsOccupied && cell.OccupantOperator != null)
                {
                    var op = cell.OccupantOperator.GetComponent<OperatorBase>();
                    if (op != null && BlockingSystem.CanBlock(op, enemy))
                    {
                        BlockingSystem.TryEngageBlock(op, enemy);
                    }
                }
            }
        }

        private void HandleEnemyDied(EnemyBase enemy)
        {
            activeEnemies.Remove(enemy);
            OnEnemyDied?.Invoke(enemy);
        }

        private void HandleEnemyReachedExit(EnemyBase enemy)
        {
            activeEnemies.Remove(enemy);
            OnEnemyReachedExit?.Invoke(enemy);
        }

        /// <summary>
        /// Get all enemies currently inside any of the specified grid cells.
        /// </summary>
        public List<EnemyBase> GetEnemiesInCells(IEnumerable<GridCell> cells)
        {
            var result = new List<EnemyBase>();
            if (cells == null || gridManager == null) return result;

            var cellSet = new HashSet<Vector2Int>();
            foreach (var c in cells)
            {
                if (c != null) cellSet.Add(c.GridPosition);
            }

            foreach (var enemy in activeEnemies)
            {
                if (enemy == null || enemy.IsDead) continue;
                Vector2Int pos = gridManager.WorldToGridPosition(enemy.transform.position);
                if (cellSet.Contains(pos))
                {
                    result.Add(enemy);
                }
            }

            return result;
        }

        /// <summary>
        /// Get all enemies within a world-space radius of a point.
        /// </summary>
        public List<EnemyBase> GetEnemiesInRadius(Vector3 center, float radius)
        {
            var result = new List<EnemyBase>();
            float sqrRadius = radius * radius;

            foreach (var enemy in activeEnemies)
            {
                if (enemy == null || enemy.IsDead) continue;
                if ((enemy.transform.position - center).sqrMagnitude <= sqrRadius)
                {
                    result.Add(enemy);
                }
            }

            return result;
        }

        /// <summary>
        /// Clear all active enemies (e.g. stage reset).
        /// </summary>
        public void ClearAll()
        {
            for (int i = activeEnemies.Count - 1; i >= 0; i--)
            {
                if (activeEnemies[i] != null)
                {
                    Destroy(activeEnemies[i].gameObject);
                }
            }
            activeEnemies.Clear();
        }
    }
}
