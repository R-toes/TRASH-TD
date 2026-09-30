using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;
using TrashTD.Systems;
using TrashTD.UI;

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
        public PlayerDeck playerDeck;

        private DraftCard pendingDeployCard = null;

        private void Awake()
        {
            if (gridManager == null) gridManager = FindFirstObjectByType<GridManager>() ?? gameObject.AddComponent<GridManager>();
            if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>() ?? gameObject.AddComponent<GameManager>();
            if (waveManager == null) waveManager = FindFirstObjectByType<WaveManager>() ?? gameObject.AddComponent<WaveManager>();
            if (enemyManager == null) enemyManager = FindFirstObjectByType<EnemyManager>() ?? gameObject.AddComponent<EnemyManager>();
            if (operatorManager == null) operatorManager = FindFirstObjectByType<OperatorManager>() ?? gameObject.AddComponent<OperatorManager>();
            if (cardDraftSystem == null) cardDraftSystem = FindFirstObjectByType<CardDraftSystem>() ?? gameObject.AddComponent<CardDraftSystem>();
            if (playerDeck == null) playerDeck = FindFirstObjectByType<PlayerDeck>() ?? gameObject.AddComponent<PlayerDeck>();
            if (FindFirstObjectByType<RarityUpgradeSystem>() == null) gameObject.AddComponent<RarityUpgradeSystem>();
            if (FindFirstObjectByType<GameplayHUDUI>() == null) gameObject.AddComponent<GameplayHUDUI>();
            if (FindFirstObjectByType<CardDraftOverlayUI>() == null) gameObject.AddComponent<CardDraftOverlayUI>();
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

            // 4. Initialize Draft System
            if (operatorPool != null && operatorPool.Count > 0)
            {
                cardDraftSystem.SetOperatorPool(operatorPool);
                cardDraftSystem.OnCardSelected += HandleCardSelectedForDeployment;
            }
            if (playerDeck != null)
            {
                playerDeck.OnCardSelectedForDeployment += HandleCardSelectedForDeployment;
            }
            cardDraftSystem.ResetForNewStage();

            // 5. Initialize Wave Manager
            waveManager.Initialize(stageData, difficulty);

            // 6. Start Stage (enters CardPick phase, which shows the CardDraftOverlayUI)
            gameManager.StartStage(stageData, difficulty);
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

        private void OnDestroy()
        {
            if (cardDraftSystem != null)
            {
                cardDraftSystem.OnCardSelected -= HandleCardSelectedForDeployment;
            }

            if (playerDeck != null)
            {
                playerDeck.OnCardSelectedForDeployment -= HandleCardSelectedForDeployment;
            }
        }

        private void HandleCardSelectedForDeployment(DraftCard card)
        {
            pendingDeployCard = card;
            Debug.Log($"[Draft] Selected: {card.operatorData.operatorName} ({card.rarity}). Click a valid tile to deploy!");
        }

        private void Update()
        {
            bool pointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (pendingDeployCard != null && gameManager != null && gameManager.CurrentPhase == StagePhase.Preparation &&
                !pointerOverUI && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                Vector3 mouseScreenPosition = Mouse.current.position.ReadValue();
                Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(mouseScreenPosition);
                Vector2Int gridPos = gridManager.WorldToGridPosition(mouseWorld);

                if (gridManager.IsInBounds(gridPos))
                {
                    if (operatorManager.TryDeployOperator(pendingDeployCard.operatorData, pendingDeployCard.rarity, gridPos, out _))
                    {
                        Debug.Log($"<color=green>Deployed {pendingDeployCard.operatorData.operatorName} at ({gridPos.x}, {gridPos.y})!</color>");
                        if (playerDeck != null)
                        {
                            playerDeck.RemoveCard(pendingDeployCard);
                        }
                        pendingDeployCard = null;
                    }
                    else
                    {
                        Debug.LogWarning($"Cannot deploy {pendingDeployCard.operatorData.position} operator on this tile!");
                    }
                }
            }
        }
    }
}
