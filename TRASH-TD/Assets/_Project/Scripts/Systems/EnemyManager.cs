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

        /// <summary>
        /// Kill every active enemy through the normal death path (releases blockers, fires OnEnemyDied).
        /// </summary>
        public void ClearAllForSandbox()
        {
            var active = new List<EnemyBase>(activeEnemies);
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i] != null) active[i].ForceKill();
            }
            activeEnemies.RemoveAll(enemy => enemy == null);
        }

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
            if (gridManager == null)
            {
                gridManager = FindFirstObjectByType<GridManager>();
            }

            GameObject enemyObj;
            if (enemyData.enemyPrefab != null)
            {
                enemyObj = Instantiate(enemyData.enemyPrefab, path[0], Quaternion.identity);
                if (enemyData.visualScale > 0f && !Mathf.Approximately(enemyData.visualScale, 1f))
                    enemyObj.transform.localScale *= enemyData.visualScale;

                if (enemyData.visualOffset != Vector3.zero)
                    OffsetEnemyVisual(enemyObj, enemyData);

                if (enemyData.sprite != null)
                {
                    SpriteRenderer spriteRenderer = enemyObj.GetComponent<SpriteRenderer>();
                    if (spriteRenderer == null)
                        spriteRenderer = enemyObj.GetComponentInChildren<SpriteRenderer>();
                    if (spriteRenderer != null)
                    {
                        bool overridesPrefabSprite = spriteRenderer.sprite != enemyData.sprite;
                        if (overridesPrefabSprite)
                        {
                            GruntSpriteAnimation prefabAnimation =
                                enemyObj.GetComponentInChildren<GruntSpriteAnimation>();
                            if (prefabAnimation != null)
                                prefabAnimation.enabled = false;

                            TankSpriteAnimation tankAnimation =
                                enemyObj.GetComponentInChildren<TankSpriteAnimation>();
                            if (tankAnimation != null)
                                tankAnimation.enabled = false;
                        }

                        spriteRenderer.sprite = enemyData.sprite;
                    }
                }
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
            enemyComp.SetGridManager(gridManager);
            enemyComp.SetPath(path);

            enemyComp.OnDied += HandleEnemyDied;
            enemyComp.OnReachedExit += HandleEnemyReachedExit;

            activeEnemies.Add(enemyComp);
            OnEnemySpawned?.Invoke(enemyComp);

            return enemyComp;
        }

        private static void OffsetEnemyVisual(GameObject enemyObject, EnemyData enemyData)
        {
            SpriteRenderer sourceRenderer = enemyObject.GetComponent<SpriteRenderer>();
            if (sourceRenderer == null) return;

            sourceRenderer.enabled = false;
            var visualObject = new GameObject("EnemyVisual");
            visualObject.transform.SetParent(enemyObject.transform, false);
            visualObject.transform.localPosition = enemyData.visualOffset;

            SpriteRenderer visualRenderer = visualObject.AddComponent<SpriteRenderer>();
            visualRenderer.sprite = enemyData.sprite != null ? enemyData.sprite : sourceRenderer.sprite;
            visualRenderer.color = sourceRenderer.color;
            visualRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
            visualRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
            visualRenderer.sortingOrder = sourceRenderer.sortingOrder;
            visualRenderer.flipX = sourceRenderer.flipX;
            visualRenderer.flipY = sourceRenderer.flipY;
            visualRenderer.maskInteraction = sourceRenderer.maskInteraction;
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
                if (enemy == null || enemy.IsDead || enemy.IsBlocked || enemy.IsPushingBack) continue;
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
            return GetEnemiesInCells(cells, true);
        }

        public List<EnemyBase> GetEnemiesInCells(IEnumerable<GridCell> cells, OperatorPosition operatorPosition)
        {
            return GetEnemiesInCells(cells, operatorPosition != OperatorPosition.Melee);
        }

        private List<EnemyBase> GetEnemiesInCells(IEnumerable<GridCell> cells, bool canTargetAir)
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
                if (!canTargetAir && enemy.MovementType == EnemyMovementType.Air) continue;
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
            return GetEnemiesInRadius(center, radius, true);
        }

        public List<EnemyBase> GetEnemiesInRadius(Vector3 center, float radius, OperatorPosition operatorPosition)
        {
            return GetEnemiesInRadius(center, radius, operatorPosition != OperatorPosition.Melee);
        }

        private List<EnemyBase> GetEnemiesInRadius(Vector3 center, float radius, bool canTargetAir)
        {
            var result = new List<EnemyBase>();
            float sqrRadius = radius * radius;

            foreach (var enemy in activeEnemies)
            {
                if (enemy == null || enemy.IsDead) continue;
                if (!canTargetAir && enemy.MovementType == EnemyMovementType.Air) continue;
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
