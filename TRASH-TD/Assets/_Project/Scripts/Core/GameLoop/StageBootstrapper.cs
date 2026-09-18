using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Core.GameLoop
{
    /// <summary>
    /// Bootstraps and visualizes a stage for quick testing in the Unity Editor.
    /// Spawns visual grid tiles, centers the camera, initializes managers,
    /// populates the draft pool, and starts the waves.
    /// </summary>
    public class StageBootstrapper : MonoBehaviour
    {
        [Header("Stage Config")]
        [Tooltip("The stage data asset to load and test")]
        public StageData stageData;

        [Tooltip("Difficulty to test (Easy: 10 LP, Normal: 5 LP, Hard: 1 LP)")]
        public StageDifficulty difficulty = StageDifficulty.Normal;

        [Header("Operator Pool")]
        [Tooltip("Operators available to draft")]
        public List<OperatorData> operatorPool = new List<OperatorData>();

        [Header("Tile Visuals (Sprites)")]
        public Sprite lowGroundSprite;
        public Sprite highGroundSprite;
        public Sprite blockedSprite;
        public Sprite enemyPathSprite;
        public Sprite spawnPointSprite;
        public Sprite exitPointSprite;

        [Header("Managers (Auto-found if null)")]
        public GridManager gridManager;
        public GameManager gameManager;
        public WaveManager waveManager;
        public EnemyManager enemyManager;
        public OperatorManager operatorManager;
        public CardDraftSystem cardDraftSystem;

        private DraftCard pendingDeployCard = null;

        private void Awake()
        {
            if (gridManager == null) gridManager = FindFirstObjectByType<GridManager>() ?? gameObject.AddComponent<GridManager>();
            if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>() ?? gameObject.AddComponent<GameManager>();
            if (waveManager == null) waveManager = FindFirstObjectByType<WaveManager>() ?? gameObject.AddComponent<WaveManager>();
            if (enemyManager == null) enemyManager = FindFirstObjectByType<EnemyManager>() ?? gameObject.AddComponent<EnemyManager>();
            if (operatorManager == null) operatorManager = FindFirstObjectByType<OperatorManager>() ?? gameObject.AddComponent<OperatorManager>();
            if (cardDraftSystem == null) cardDraftSystem = FindFirstObjectByType<CardDraftSystem>() ?? gameObject.AddComponent<CardDraftSystem>();
            if (FindFirstObjectByType<RarityUpgradeSystem>() == null) gameObject.AddComponent<RarityUpgradeSystem>();
        }

        private void Start()
        {
            if (stageData == null)
            {
                Debug.LogWarning("StageBootstrapper: No StageData assigned! Please assign a StageData asset.");
                return;
            }

            // 1. Initialize Grid
            gridManager.InitializeFromStageData(stageData);

            // 2. Render visual tiles
            SpawnVisualGridTiles();

            // 3. Center Camera
            CenterCameraOnGrid();

            // 4. Initialize GameManager
            gameManager.StartStage(stageData, difficulty);

            // 5. Initialize Draft System
            if (operatorPool != null && operatorPool.Count > 0)
            {
                cardDraftSystem.SetOperatorPool(operatorPool);
                cardDraftSystem.OnCardSelected += HandleCardSelectedForDeployment;
            }

            // 6. Initialize and start waves
            waveManager.Initialize(stageData, difficulty);
            waveManager.StartWaves();

            // 7. Trigger initial draft offer
            if (cardDraftSystem != null && operatorPool != null && operatorPool.Count > 0)
            {
                cardDraftSystem.GenerateDraftOffer();
            }
        }

        private void SpawnVisualGridTiles()
        {
            GameObject gridContainer = new GameObject("VisualGrid");
            gridContainer.transform.SetParent(transform);

            for (int y = 0; y < stageData.gridHeight; y++)
            {
                for (int x = 0; x < stageData.gridWidth; x++)
                {
                    TileType type = stageData.GetTile(x, y);
                    Vector3 worldPos = gridManager.GridToWorldPosition(x, y);

                    GameObject tileObj = new GameObject($"Tile_{x}_{y}_{type}");
                    tileObj.transform.position = worldPos;
                    tileObj.transform.SetParent(gridContainer.transform);

                    var sr = tileObj.AddComponent<SpriteRenderer>();
                    sr.sprite = GetSpriteForTile(type);
                    sr.sortingOrder = 0;

                    // Add box collider for click-to-deploy detection
                    var col = tileObj.AddComponent<BoxCollider2D>();
                    col.size = Vector2.one * gridManager.CellSize;
                }
            }
        }

        private Sprite GetSpriteForTile(TileType type)
        {
            return type switch
            {
                TileType.LowGround => lowGroundSprite,
                TileType.HighGround => highGroundSprite,
                TileType.Blocked => blockedSprite,
                TileType.EnemyPath => lowGroundSprite,
                TileType.SpawnPoint => spawnPointSprite,
                TileType.ExitPoint => exitPointSprite,
                _ => lowGroundSprite
            };
        }

        private void CenterCameraOnGrid()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            float centerX = (stageData.gridWidth * gridManager.CellSize) * 0.5f;
            float centerY = (stageData.gridHeight * gridManager.CellSize) * 0.5f;
            cam.transform.position = new Vector3(centerX, centerY, -10f);

            // Adjust orthographic size so the grid fits nicely
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(stageData.gridHeight * 0.6f, 5f);
        }

        private void HandleCardSelectedForDeployment(DraftCard card)
        {
            pendingDeployCard = card;
            Debug.Log($"[Draft] Selected: {card.operatorData.operatorName} ({card.rarity}). Click a valid tile to deploy!");
        }

        private void Update()
        {
            // Simple Click-to-Deploy handling during Play mode testing
            if (pendingDeployCard != null && Input.GetMouseButtonDown(0))
            {
                Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                Vector2Int gridPos = gridManager.WorldToGridPosition(mouseWorld);

                if (gridManager.IsInBounds(gridPos))
                {
                    int dpCost = pendingDeployCard.operatorData.dpCost;
                    if (gameManager.TrySpendDP(dpCost))
                    {
                        if (operatorManager.TryDeployOperator(pendingDeployCard.operatorData, pendingDeployCard.rarity, gridPos, out _))
                        {
                            Debug.Log($"<color=green>Deployed {pendingDeployCard.operatorData.operatorName} at ({gridPos.x}, {gridPos.y})!</color>");
                            pendingDeployCard = null;
                        }
                        else
                        {
                            // Refund DP if invalid tile
                            gameManager.AddDP(dpCost);
                            Debug.LogWarning($"Cannot deploy {pendingDeployCard.operatorData.position} operator on this tile!");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Not enough DP! Need {dpCost}, have {gameManager.CurrentDP}.");
                    }
                }
            }
        }
    }
}
