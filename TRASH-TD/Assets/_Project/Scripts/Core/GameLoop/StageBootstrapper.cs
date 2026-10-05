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
        private GameplayHUDUI gameplayHudUI;
        private readonly List<GameObject> placementPreviewVisuals = new List<GameObject>();
        private Material placementPreviewMaterial;
        private Vector2Int placementGridPosition;
        private OperatorFacing placementFacing = OperatorFacing.Right;
        private bool isPlacementPreviewActive;
        private bool isOperatorRangePreviewActive;

        private void Awake()
        {
            if (gridManager == null) gridManager = FindFirstObjectByType<GridManager>() ?? gameObject.AddComponent<GridManager>();
            if (enemyManager == null) enemyManager = FindFirstObjectByType<EnemyManager>() ?? gameObject.AddComponent<EnemyManager>();
            if (gameManager == null) gameManager = FindFirstObjectByType<GameManager>() ?? gameObject.AddComponent<GameManager>();
            if (waveManager == null) waveManager = FindFirstObjectByType<WaveManager>() ?? gameObject.AddComponent<WaveManager>();
            if (operatorManager == null) operatorManager = FindFirstObjectByType<OperatorManager>() ?? gameObject.AddComponent<OperatorManager>();
            if (cardDraftSystem == null) cardDraftSystem = FindFirstObjectByType<CardDraftSystem>() ?? gameObject.AddComponent<CardDraftSystem>();
            if (playerDeck == null) playerDeck = FindFirstObjectByType<PlayerDeck>() ?? gameObject.AddComponent<PlayerDeck>();
            if (FindFirstObjectByType<RarityUpgradeSystem>() == null) gameObject.AddComponent<RarityUpgradeSystem>();
            if (FindFirstObjectByType<GameplayHUDUI>() == null) gameObject.AddComponent<GameplayHUDUI>();
            if (FindFirstObjectByType<CardDraftOverlayUI>() == null) gameObject.AddComponent<CardDraftOverlayUI>();
            if (FindFirstObjectByType<StageResultsUI>() == null) gameObject.AddComponent<StageResultsUI>();
        }

        private void Start()
        {
            gameplayHudUI = FindFirstObjectByType<GameplayHUDUI>();
            difficulty = MainMenuController.ConsumePendingStageDifficulty(difficulty);

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
                playerDeck.OnDeckChanged += HandleDeckChanged;
            }
            if (operatorManager != null)
            {
                operatorManager.OnOperatorSelected += HandleOperatorSelected;
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

            float cellSize = gridManager.CellSize;
            Vector3 gridBottomLeft = gridManager.GridToWorldPosition(0, 0) - new Vector3(cellSize * 0.5f, cellSize * 0.5f, 0f);
            Vector3 targetBottomLeft = gridBottomLeft + new Vector3(stageData.visualTileOffset.x * cellSize, stageData.visualTileOffset.y * cellSize, 0f);

            Sprite mapSprite = stageData.mapVisualSprite;
            if (mapSprite != null)
            {
                GameObject mapObject = new GameObject("MapArtwork");
                mapObject.transform.SetParent(gridContainer.transform);

                var mapRenderer = mapObject.AddComponent<SpriteRenderer>();
                mapRenderer.sprite = mapSprite;
                mapRenderer.sortingOrder = 0;

                // Scale so that 1 tile in the sprite (e.g. 32px) equals cellSize in world units, preserving square pixels.
                // Any extra rows/columns (e.g. 384x32 extra top row in Level1 complete) naturally extend outside the grid.
                float pixelPerTile = stageData.visualTilePixelSize > 0 ? stageData.visualTilePixelSize : 32f;
                float spriteUnitsPerTile = pixelPerTile / mapSprite.pixelsPerUnit;
                float tileScale = cellSize / spriteUnitsPerTile;
                Vector3 mapScale = new Vector3(tileScale, tileScale, 1f);
                mapObject.transform.localScale = mapScale;

                // Align the bottom-left of the sprite's bounding box to targetBottomLeft (cell 0,0)
                mapObject.transform.position = targetBottomLeft - Vector3.Scale(mapSprite.bounds.min, mapScale);
            }

            Sprite fgSprite = stageData.foregroundVisualSprite;
            if (fgSprite != null)
            {
                GameObject fgObject = new GameObject("ForegroundArtwork");
                fgObject.transform.SetParent(gridContainer.transform);

                var fgRenderer = fgObject.AddComponent<SpriteRenderer>();
                fgRenderer.sprite = fgSprite;
                fgRenderer.sortingOrder = 10; // In front of operators (5) and enemies (4)

                float pixelPerTile = stageData.visualTilePixelSize > 0 ? stageData.visualTilePixelSize : 32f;
                float spriteUnitsPerTile = pixelPerTile / fgSprite.pixelsPerUnit;
                float tileScale = cellSize / spriteUnitsPerTile;
                Vector3 fgScale = new Vector3(tileScale, tileScale, 1f);
                fgObject.transform.localScale = fgScale;

                fgObject.transform.position = targetBottomLeft - Vector3.Scale(fgSprite.bounds.min, fgScale);
            }

            for (int y = 0; y < stageData.gridHeight; y++)
            {
                for (int x = 0; x < stageData.gridWidth; x++)
                {
                    TileType type = stageData.GetTile(x, y);
                    Vector3 worldPos = gridManager.GridToWorldPosition(x, y);

                    GameObject tileObj = new GameObject($"Tile_{x}_{y}_{type}");
                    tileObj.transform.position = worldPos;
                    tileObj.transform.SetParent(gridContainer.transform);

                    if (mapSprite == null)
                    {
                        var sr = tileObj.AddComponent<SpriteRenderer>();
                        sr.sprite = GetSpriteForTile(type);
                        sr.sortingOrder = 0;
                    }

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

            float cellSize = gridManager != null ? gridManager.CellSize : 1.0f;
            float centerX = (stageData.gridWidth * cellSize) * 0.5f;

            // Visual height: grid height by default, but if mapVisualSprite has extra visual rows (e.g. top row),
            // take the full visual height into account so the background visuals are fully visible.
            float visualHeight = stageData.gridHeight * cellSize;
            if (stageData.mapVisualSprite != null)
            {
                float pixelPerTile = stageData.visualTilePixelSize > 0 ? stageData.visualTilePixelSize : 32f;
                float spriteTilesY = stageData.mapVisualSprite.rect.height / pixelPerTile;
                visualHeight = Mathf.Max(visualHeight, (spriteTilesY + stageData.visualTileOffset.y) * cellSize);
            }

            // Available vertical ratio between DeckBar (170px) and TopBar (90px) on 1080p reference (~76%)
            const float availableRatio = 0.759f;
            float requiredWorldHeight = (visualHeight + 0.35f) / availableRatio;
            float orthoSize = Mathf.Max(4.10f, requiredWorldHeight * 0.5f);

            // Shift camera down by the difference between bottom DeckBar (170px) and TopBar (90px)
            // so visual content is vertically centered in the unobstructed play area
            float hudCenterOffset = ((170f - 90f) / 1080f) * orthoSize;
            float camY = (visualHeight * 0.5f) - hudCenterOffset;

            cam.transform.position = new Vector3(centerX, camY, -10f);
            cam.orthographic = true;
            cam.orthographicSize = orthoSize;
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
                playerDeck.OnDeckChanged -= HandleDeckChanged;
            }
            if (operatorManager != null)
            {
                operatorManager.OnOperatorSelected -= HandleOperatorSelected;
            }

            ClearPlacementPreviewVisuals();
            if (placementPreviewMaterial != null)
            {
                Destroy(placementPreviewMaterial);
            }
        }

        private void HandleDeckChanged(IReadOnlyList<DraftCard> deck)
        {
            if (pendingDeployCard == null) return;

            for (int i = 0; i < deck.Count; i++)
            {
                if (ReferenceEquals(deck[i], pendingDeployCard)) return;
            }

            pendingDeployCard = null;
            HidePlacementPreview();
        }

        private void HandleCardSelectedForDeployment(DraftCard card)
        {
            if (isPlacementPreviewActive && !ReferenceEquals(pendingDeployCard, card))
            {
                HidePlacementPreview();
            }

            pendingDeployCard = card;
            Debug.Log($"[Draft] Selected: {card.operatorData.operatorName} ({card.rarity}). Select a tile, choose a facing, then confirm placement.");
        }

        public void BeginOperatorPlacement(DraftCard card, Vector2Int gridPosition)
        {
            if (card == null || card.operatorData == null || gridManager == null ||
                gameManager == null || gameManager.CurrentPhase != StagePhase.Preparation ||
                !gridManager.IsInBounds(gridPosition)) return;

            bool continuingPlacement = isPlacementPreviewActive && ReferenceEquals(pendingDeployCard, card);
            if (continuingPlacement && placementGridPosition == gridPosition) return;

            if (!continuingPlacement)
            {
                operatorManager?.SelectOperator(null);
                isOperatorRangePreviewActive = false;
                ClearPlacementPreviewVisuals();
            }

            pendingDeployCard = card;
            placementGridPosition = gridPosition;
            if (!continuingPlacement) placementFacing = OperatorFacing.Right;
            isPlacementPreviewActive = true;
            RefreshPlacementPreview();
        }

        public void SetPlacementFacing(OperatorFacing facing)
        {
            if (!isPlacementPreviewActive || placementFacing == facing) return;

            placementFacing = facing;
            RefreshPlacementPreview();
        }

        public void ConfirmOperatorPlacement()
        {
            if (!isPlacementPreviewActive || pendingDeployCard == null) return;

            if (operatorManager == null || operatorManager.IsAtSquadLimit)
            {
                RefreshPlacementPreview();
                return;
            }

            GridCell targetCell = gridManager != null ? gridManager.GetCell(placementGridPosition) : null;
            if (targetCell == null || !targetCell.CanDeploy(pendingDeployCard.operatorData.position))
            {
                RefreshPlacementPreview();
                return;
            }

            DraftCard deployedCard = pendingDeployCard;
            if (operatorManager == null || !operatorManager.TryDeployOperator(
                    deployedCard.operatorData,
                    deployedCard.rarity,
                    placementGridPosition,
                    placementFacing,
                    out _))
            {
                Debug.LogWarning($"Cannot deploy {deployedCard.operatorData.operatorName} here.");
                return;
            }

            playerDeck?.RemoveCard(deployedCard);
            pendingDeployCard = null;
            HidePlacementPreview();
            Debug.Log($"<color=green>Deployed {deployedCard.operatorData.operatorName} at ({placementGridPosition.x}, {placementGridPosition.y}) facing {placementFacing}!</color>");
        }

        public void CancelOperatorPlacement()
        {
            pendingDeployCard = null;
            HidePlacementPreview();
        }

        public void CancelOperatorPlacementPreview(DraftCard card)
        {
            if (isPlacementPreviewActive && ReferenceEquals(pendingDeployCard, card))
            {
                HidePlacementPreview();
            }
        }

        private void Update()
        {
            if (gameManager == null || gameManager.CurrentState != GamePlayState.Playing)
            {
                if (isPlacementPreviewActive || isOperatorRangePreviewActive) HidePlacementPreview();
                return;
            }

            bool isPreparing = gameManager.CurrentPhase == StagePhase.Preparation;
            if (!isPreparing && isPlacementPreviewActive)
            {
                pendingDeployCard = null;
                HidePlacementPreview();
            }

            Keyboard keyboard = Keyboard.current;
            if (isPreparing && isPlacementPreviewActive)
            {
                if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                {
                    CancelOperatorPlacement();
                    return;
                }

                OperatorFacing nextFacing = GetFacingInput(keyboard);
                if (nextFacing != placementFacing)
                {
                    placementFacing = nextFacing;
                    RefreshPlacementPreview();
                }

                bool pointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                Vector2Int hoveredPosition = placementGridPosition;
                bool pointerOnGrid = !pointerOverUI && Mouse.current != null &&
                    TryGetGridPosition(Mouse.current.position.ReadValue(), out hoveredPosition) &&
                    gridManager.IsInBounds(hoveredPosition);
                if (pointerOnGrid && Mouse.current.leftButton.wasPressedThisFrame &&
                    placementGridPosition != hoveredPosition)
                {
                    placementGridPosition = hoveredPosition;
                    RefreshPlacementPreview();
                }

                bool confirmWithKeyboard = keyboard != null &&
                    (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
                if (confirmWithKeyboard)
                {
                    ConfirmOperatorPlacement();
                }

                return;
            }

            if (isPreparing && pendingDeployCard != null)
            {
                if (TryGetGridClick(out Vector2Int gridPosition))
                {
                    GridCell clickedCell = gridManager.GetCell(gridPosition);
                    OperatorBase clickedOperator = clickedCell != null && clickedCell.OccupantOperator != null
                        ? clickedCell.OccupantOperator.GetComponent<OperatorBase>()
                        : null;
                    if (clickedOperator != null)
                    {
                        HandleOperatorMapSelection(clickedOperator);
                    }
                    else
                    {
                        BeginOperatorPlacement(pendingDeployCard, gridPosition);
                    }
                }
                return;
            }

            if (TryGetGridClick(out Vector2Int selectedGridPosition))
            {
                GridCell clickedCell = gridManager.GetCell(selectedGridPosition);
                OperatorBase clickedOperator = clickedCell != null && clickedCell.OccupantOperator != null
                    ? clickedCell.OccupantOperator.GetComponent<OperatorBase>()
                    : null;

                HandleOperatorMapSelection(clickedOperator);
            }
        }

        private bool TryGetGridClick(out Vector2Int gridPosition)
        {
            gridPosition = default;
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame &&
                (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()) &&
                TryGetGridPosition(Mouse.current.position.ReadValue(), out gridPosition) &&
                gridManager != null && gridManager.IsInBounds(gridPosition);
        }

        private void HandleOperatorMapSelection(OperatorBase clickedOperator)
        {
            if (operatorManager == null) return;

            if (clickedOperator == null || operatorManager.SelectedOperator == clickedOperator)
            {
                operatorManager.SelectOperator(null);
                ClearOperatorRangePreview();
                return;
            }

            operatorManager.SelectOperator(clickedOperator);
        }

        private void HandleOperatorSelected(OperatorBase op)
        {
            if (op == null || op.DeployedCell == null)
            {
                ClearOperatorRangePreview();
                gameplayHudUI?.SetSelectedOperatorName(string.Empty, Vector3.zero);
                return;
            }

            ClearPlacementPreviewVisuals();
            isOperatorRangePreviewActive = true;
            GridCell[] rangeCells = gridManager.GetCellsInRange(
                op.DeployedCell.GridPosition,
                op.GetRangePattern(),
                op.Facing);
            Color rangeColor = new Color(0.15f, 1f, 0.28f, 1f);
            for (int i = 0; i < rangeCells.Length; i++)
            {
                DrawCellOutline(rangeCells[i].WorldPosition, rangeColor);
            }

            DrawCellOutline(op.DeployedCell.WorldPosition, new Color(0.15f, 0.85f, 1f, 1f));
            Vector3 labelPosition = op.DeployedCell.WorldPosition + Vector3.up * gridManager.CellSize * 0.7f;
            gameplayHudUI?.SetSelectedOperatorName(
                op.Data.operatorName,
                op.CurrentHP,
                op.MaxHP,
                op.CurrentRarity,
                op.Data.baseRarity,
                op.Data.skillDescription,
                labelPosition);
        }

        private void ClearOperatorRangePreview()
        {
            if (!isOperatorRangePreviewActive) return;

            isOperatorRangePreviewActive = false;
            ClearPlacementPreviewVisuals();
            gameplayHudUI?.SetSelectedOperatorName(string.Empty, Vector3.zero);
        }

        private OperatorFacing GetFacingInput(Keyboard keyboard)
        {
            if (keyboard == null) return placementFacing;
            if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame) return OperatorFacing.Right;
            if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame) return OperatorFacing.Up;
            if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame) return OperatorFacing.Left;
            if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame) return OperatorFacing.Down;
            return placementFacing;
        }

        private bool TryGetGridPosition(Vector2 screenPosition, out Vector2Int gridPosition)
        {
            gridPosition = default;
            Camera gameCamera = Camera.main;
            if (gameCamera == null || gridManager == null) return false;

            Vector3 worldPosition = gameCamera.ScreenToWorldPoint(new Vector3(
                screenPosition.x,
                screenPosition.y,
                -gameCamera.transform.position.z));
            gridPosition = gridManager.WorldToGridPosition(worldPosition);
            return true;
        }

        private void RefreshPlacementPreview()
        {
            ClearPlacementPreviewVisuals();
            if (!isPlacementPreviewActive || pendingDeployCard == null || gridManager == null) return;

            GridCell targetCell = gridManager.GetCell(placementGridPosition);
            if (targetCell == null) return;

            bool squadFull = operatorManager == null || operatorManager.IsAtSquadLimit;
            bool canDeploy = targetCell.CanDeploy(pendingDeployCard.operatorData.position) && !squadFull;
            GridCell[] rangeCells = gridManager.GetCellsInRange(
                placementGridPosition,
                pendingDeployCard.operatorData.rangePattern,
                placementFacing);
            Color rangeColor = new Color(0.15f, 1f, 0.28f, 1f);
            for (int i = 0; i < rangeCells.Length; i++)
            {
                DrawCellOutline(rangeCells[i].WorldPosition, rangeColor);
            }

            Color placementColor = canDeploy ? new Color(0.15f, 0.85f, 1f, 1f) : new Color(1f, 0.12f, 0.12f, 1f);
            DrawCellOutline(targetCell.WorldPosition, placementColor);
            DrawFacingMarker(targetCell.WorldPosition, placementFacing);
            gameplayHudUI?.SetPlacementControls(true, canDeploy);
            gameplayHudUI?.SetPlacementControlsPosition(targetCell.WorldPosition);
        }

        private void DrawCellOutline(Vector3 center, Color color)
        {
            float halfSize = gridManager.CellSize * 0.46f;
            center.z = 0.05f;
            Vector3[] corners =
            {
                center + new Vector3(-halfSize, -halfSize, 0f),
                center + new Vector3(-halfSize, halfSize, 0f),
                center + new Vector3(halfSize, halfSize, 0f),
                center + new Vector3(halfSize, -halfSize, 0f)
            };

            CreatePreviewLine("PlacementOutlineGlow", gridManager.CellSize * 0.14f, new Color(color.r, color.g, color.b, 0.28f), corners, true);
            CreatePreviewLine("PlacementOutline", gridManager.CellSize * 0.045f, color, corners, true);
        }

        private void DrawFacingMarker(Vector3 center, OperatorFacing facing)
        {
            Vector2 forward = facing switch
            {
                OperatorFacing.Up => Vector2.up,
                OperatorFacing.Left => Vector2.left,
                OperatorFacing.Down => Vector2.down,
                _ => Vector2.right
            };
            Vector2 side = new Vector2(-forward.y, forward.x);
            float cellSize = gridManager.CellSize;
            Vector3 tip = center + (Vector3)(forward * cellSize * 0.36f);
            Vector3 baseCenter = center + (Vector3)(forward * cellSize * 0.08f);
            Vector3[] arrow =
            {
                tip,
                baseCenter + (Vector3)(side * cellSize * 0.14f),
                baseCenter - (Vector3)(side * cellSize * 0.14f)
            };
            for (int i = 0; i < arrow.Length; i++) arrow[i].z = 0.06f;
            CreatePreviewLine("PlacementFacingMarker", cellSize * 0.05f, Color.white, arrow, true);
        }

        private void CreatePreviewLine(string objectName, float width, Color color, Vector3[] points, bool closed)
        {
            GameObject lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = closed;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            line.numCapVertices = 2;
            line.material = GetPlacementPreviewMaterial();
            line.sortingOrder = 50;
            placementPreviewVisuals.Add(lineObject);
        }

        private Material GetPlacementPreviewMaterial()
        {
            if (placementPreviewMaterial != null) return placementPreviewMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            placementPreviewMaterial = new Material(shader);
            return placementPreviewMaterial;
        }

        private void ClearPlacementPreviewVisuals()
        {
            for (int i = 0; i < placementPreviewVisuals.Count; i++)
            {
                if (placementPreviewVisuals[i] != null) Destroy(placementPreviewVisuals[i]);
            }
            placementPreviewVisuals.Clear();
        }

        private void HidePlacementPreview()
        {
            isPlacementPreviewActive = false;
            isOperatorRangePreviewActive = false;
            ClearPlacementPreviewVisuals();
            gameplayHudUI?.SetPlacementControls(false);
        }
    }
}
