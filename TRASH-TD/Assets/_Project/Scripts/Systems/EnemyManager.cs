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
            int safetyLimit = 1000;
            while (activeEnemies.Count > 0 && safetyLimit-- > 0)
            {
                EnemyBase enemy = activeEnemies[activeEnemies.Count - 1];
                activeEnemies.RemoveAt(activeEnemies.Count - 1);
                if (enemy != null)
                {
                    enemy.ForceKill();
                    if (enemy != null && enemy.gameObject != null)
                    {
                        if (Application.isPlaying)
                            Destroy(enemy.gameObject);
                        else
                            DestroyImmediate(enemy.gameObject);
                    }
                }
            }
            activeEnemies.Clear();

            EnemyBase[] lingering = FindObjectsByType<EnemyBase>();
            for (int i = 0; i < lingering.Length; i++)
            {
                EnemyBase enemy = lingering[i];
                if (enemy != null && enemy.gameObject != null)
                {
                    enemy.ForceKill();
                    if (enemy != null && enemy.gameObject != null)
                    {
                        if (Application.isPlaying)
                            Destroy(enemy.gameObject);
                        else
                            DestroyImmediate(enemy.gameObject);
                    }
                }
            }
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
                gridManager = FindAnyObjectByType<GridManager>();
            }
        }

        public void Initialize(GridManager grid)
        {
            gridManager = grid;
        }

        public void SetGridManager(GridManager grid)
        {
            gridManager = grid;
        }

        public void RegisterEnemy(EnemyBase enemy)
        {
            if (enemy != null && !activeEnemies.Contains(enemy))
            {
                activeEnemies.Add(enemy);
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
                gridManager = FindAnyObjectByType<GridManager>();
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
                if (enemyData != null && (enemyData.enemyName == "Askalaser" || enemyData.enemyName == "Askalazer"))
                    enemyComp = enemyObj.AddComponent<Enemies.AskalaserEnemy>();
                else if (enemyData != null && enemyData.enemyName == "Hextension")
                    enemyComp = enemyObj.AddComponent<Enemies.HextensionEnemy>();
                else if (enemyData != null && (enemyData.enemyName == "Occulorm" || enemyData.enemyName == "Oculorm"))
                    enemyComp = enemyObj.AddComponent<Enemies.OcculormEnemy>();
                else if (enemyData != null && (enemyData.enemyName == "Junkernaut" || enemyData.enemyName == "The Junkernaut"))
                    enemyComp = enemyObj.AddComponent<Enemies.JunkernautEnemy>();
                else
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
                if (enemy == null || enemy.IsDead || enemy.IsBlocked || enemy.IsPushingBack || enemy.IsInsideHole) continue;
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

        public void RegisterActiveEnemyForTesting(EnemyBase enemy)
        {
            if (enemy != null && !activeEnemies.Contains(enemy))
            {
                activeEnemies.Add(enemy);
            }
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
            var list = GetEnemiesInCells(cells, operatorPosition != OperatorPosition.Melee);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] != null && !list[i].CanBeTargetedBy(operatorPosition))
                {
                    list.RemoveAt(i);
                }
            }
            return list;
        }

        /// <summary>
        /// How many enemies GetEnemiesInCells(cells, operatorPosition) would return, without building the list
        /// (for checks that run every frame, e.g. Pompoth's attack speed).
        /// </summary>
        public int CountEnemiesInCells(IEnumerable<GridCell> cells, OperatorPosition operatorPosition)
        {
            if (!FillQueryCellSet(cells)) return 0;

            bool canTargetAir = operatorPosition != OperatorPosition.Melee;
            int count = 0;
            for (int i = 0; i < activeEnemies.Count; i++)
            {
                var enemy = activeEnemies[i];
                if (MatchesQueryCells(enemy, canTargetAir) && enemy.CanBeTargetedBy(operatorPosition)) count++;
            }
            return count;
        }

        private List<EnemyBase> GetEnemiesInCells(IEnumerable<GridCell> cells, bool canTargetAir)
        {
            var result = new List<EnemyBase>();
            if (!FillQueryCellSet(cells)) return result;

            for (int i = 0; i < activeEnemies.Count; i++)
            {
                var enemy = activeEnemies[i];
                if (MatchesQueryCells(enemy, canTargetAir))
                {
                    result.Add(enemy);
                }
            }

            return result;
        }

        // Reused by every cell query instead of a new HashSet per call (operators query this every attack)
        private readonly HashSet<Vector2Int> queryCellSet = new HashSet<Vector2Int>();

        /// <summary>Loads the cells' grid positions into queryCellSet; false when there is nothing to match.</summary>
        private bool FillQueryCellSet(IEnumerable<GridCell> cells)
        {
            queryCellSet.Clear();
            if (cells == null || gridManager == null) return false;

            // Arrays and lists without a boxed enumerator
            if (cells is GridCell[] array)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    if (array[i] != null) queryCellSet.Add(array[i].GridPosition);
                }
            }
            else if (cells is List<GridCell> list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] != null) queryCellSet.Add(list[i].GridPosition);
                }
            }
            else
            {
                foreach (var c in cells)
                {
                    if (c != null) queryCellSet.Add(c.GridPosition);
                }
            }

            return queryCellSet.Count > 0;
        }

        /// <summary>Whether an enemy counts as inside the cells loaded by FillQueryCellSet.</summary>
        private bool MatchesQueryCells(EnemyBase enemy, bool canTargetAir)
        {
            if (enemy == null || enemy.IsDead || enemy.IsInsideHole) return false;
            if (!canTargetAir && enemy.MovementType == EnemyMovementType.Air) return false;

            // 1. Direct discrete grid position check
            Vector2Int pos = gridManager.WorldToGridPosition(enemy.transform.position);
            if (queryCellSet.Contains(pos)) return true;

            // 2. If blocked, check both the blocker's tile and the adjacent path tile (in between tiles)
            if (enemy.IsBlocked && enemy.BlockingOperator != null)
            {
                var blocker = enemy.BlockingOperator;
                Vector2Int blockerPos = blocker.DeployedCell != null
                    ? blocker.DeployedCell.GridPosition
                    : gridManager.WorldToGridPosition(blocker.transform.position);

                if (queryCellSet.Contains(blockerPos) || queryCellSet.Contains(enemy.BlockedAdjacentGridPos)) return true;
            }

            // 3. Spatial bounding overlap check (handles transitioning enemies in-between tiles before leaving range)
            float cellSize = gridManager.CellSize > 0f ? gridManager.CellSize : 1.0f;
            float tolerance = 0.35f * cellSize;
            Vector3 origin = gridManager.GridOrigin;
            Vector3 enemyPos = enemy.transform.position;
            int minX = Mathf.FloorToInt((enemyPos.x - tolerance - origin.x) / cellSize);
            int maxX = Mathf.FloorToInt((enemyPos.x + tolerance - origin.x) / cellSize);
            int minY = Mathf.FloorToInt((enemyPos.y - tolerance - origin.y) / cellSize);
            int maxY = Mathf.FloorToInt((enemyPos.y + tolerance - origin.y) / cellSize);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    if (queryCellSet.Contains(new Vector2Int(x, y))) return true;
                }
            }

            return false;
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
            var list = GetEnemiesInRadius(center, radius, operatorPosition != OperatorPosition.Melee);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] != null && !list[i].CanBeTargetedBy(operatorPosition))
                {
                    list.RemoveAt(i);
                }
            }
            return list;
        }

        /// <summary>How many enemies GetEnemiesInRadius(center, radius, operatorPosition) would return, without building the list.</summary>
        public int CountEnemiesInRadius(Vector3 center, float radius, OperatorPosition operatorPosition)
        {
            bool canTargetAir = operatorPosition != OperatorPosition.Melee;
            float sqrRadius = radius * radius;
            int count = 0;
            for (int i = 0; i < activeEnemies.Count; i++)
            {
                var enemy = activeEnemies[i];
                if (IsInRadius(enemy, center, sqrRadius, canTargetAir) && enemy.CanBeTargetedBy(operatorPosition)) count++;
            }
            return count;
        }

        private List<EnemyBase> GetEnemiesInRadius(Vector3 center, float radius, bool canTargetAir)
        {
            var result = new List<EnemyBase>();
            float sqrRadius = radius * radius;

            for (int i = 0; i < activeEnemies.Count; i++)
            {
                var enemy = activeEnemies[i];
                if (IsInRadius(enemy, center, sqrRadius, canTargetAir))
                {
                    result.Add(enemy);
                }
            }

            return result;
        }

        private static bool IsInRadius(EnemyBase enemy, Vector3 center, float sqrRadius, bool canTargetAir)
        {
            if (enemy == null || enemy.IsDead || enemy.IsInsideHole) return false;
            if (!canTargetAir && enemy.MovementType == EnemyMovementType.Air) return false;
            return (enemy.transform.position - center).sqrMagnitude <= sqrRadius;
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
