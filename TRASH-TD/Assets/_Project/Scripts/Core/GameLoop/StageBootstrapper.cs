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
        private const float Level3WeatherDamageIntervalSeconds = 2f;
        public const int AcidRainDamagePerTick = 3;
        private const int AcidRainDropCount = 140;
        private static Sprite weatherDotSprite;
        private static Texture2D dustCloudTexture;
        private static Material dustCloudMaterial;

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
        public Sprite trapSprite;

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
        private readonly List<Transform> acidRainDrops = new List<Transform>();
        private Material placementPreviewMaterial;
        private float weatherDamageTimer;
        private Vector2 acidRainBoundsMin;
        private Vector2 acidRainBoundsMax;
        private Vector2 acidRainVelocity;
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
            stageData = MainMenuController.ConsumePendingStage(stageData);

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

            // 3.5 Apply acidstorm and weather effects
            if (stageData.stageId == "STAGE_03" || stageData.stageId == "STAGE_06")
            {
                CreateLevel3WeatherEffects();
            }

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

            DrawMapArtworkLayer(gridContainer.transform, stageData.backgroundVisualSprite, "BackgroundArtwork", 0, targetBottomLeft, true);

            Sprite mapSprite = stageData.mapVisualSprite;
            DrawMapArtworkLayer(gridContainer.transform, mapSprite, "MapArtwork", 1, targetBottomLeft, false);

            if (stageData.upperBackgroundVisualSprites != null)
            {
                for (int i = 0; i < stageData.upperBackgroundVisualSprites.Length; i++)
                {
                    DrawMapArtworkLayer(
                        gridContainer.transform,
                        stageData.upperBackgroundVisualSprites[i],
                        $"UpperBackgroundArtwork_{i}",
                        2,
                        targetBottomLeft,
                        true);
                }
            }

            Sprite fgSprite = stageData.foregroundVisualSprite;
            if (fgSprite != null)
            {
                DrawMapArtworkLayer(gridContainer.transform, fgSprite, "ForegroundArtwork", 10, targetBottomLeft, false);
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

        private void DrawMapArtworkLayer(Transform parent, Sprite sprite, string objectName, int sortingOrder, Vector3 targetBottomLeft, bool alignToSourceRect)
        {
            if (sprite == null) return;

            GameObject artworkObject = new GameObject(objectName);
            artworkObject.transform.SetParent(parent);

            var spriteRenderer = artworkObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            spriteRenderer.sortingOrder = sortingOrder;

            float pixelPerTile = stageData.visualTilePixelSize > 0 ? stageData.visualTilePixelSize : 32f;
            float spriteUnitsPerTile = pixelPerTile / sprite.pixelsPerUnit;
            float tileScale = gridManager.CellSize / spriteUnitsPerTile;
            Vector3 artworkScale = new Vector3(tileScale, tileScale, 1f);
            artworkObject.transform.localScale = artworkScale;

            Vector3 sourceOffset = alignToSourceRect
                ? new Vector3(sprite.rect.x, sprite.rect.y, 0f) * (gridManager.CellSize / pixelPerTile)
                : Vector3.zero;
            artworkObject.transform.position = targetBottomLeft + sourceOffset
                - Vector3.Scale(sprite.bounds.min, artworkScale);
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
                TileType.Trap => lowGroundSprite,
                _ => lowGroundSprite
            };
        }

        private void CenterCameraOnGrid()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            float cellSize = gridManager != null ? gridManager.CellSize : 1.0f;
            float centerX = (stageData.gridWidth * cellSize) * 0.5f;

            // Visual height: grid height by default, but if mapVisualSprite has extra visual rows (e.g. top/bottom rows),
            // take the full visual height and vertical span into account so background visuals are fully visible and centered.
            float visualMinY = 0f;
            float visualMaxY = stageData.gridHeight * cellSize;
            if (stageData.mapVisualSprite != null)
            {
                float pixelPerTile = stageData.visualTilePixelSize > 0 ? stageData.visualTilePixelSize : 32f;
                float spriteTilesY = stageData.mapVisualSprite.rect.height / pixelPerTile;
                float spriteBottom = stageData.visualTileOffset.y * cellSize;
                float spriteTop = (spriteTilesY + stageData.visualTileOffset.y) * cellSize;
                visualMinY = Mathf.Min(visualMinY, spriteBottom);
                visualMaxY = Mathf.Max(visualMaxY, spriteTop);
            }
            float visualHeight = visualMaxY - visualMinY;
            float visualCenterY = (visualMinY + visualMaxY) * 0.5f;

            // Available vertical ratio between DeckBar (170px) and TopBar (90px) on 1080p reference (~76%)
            const float availableRatio = 0.759f;
            float requiredWorldHeight = (visualHeight + 0.35f) / availableRatio;
            float orthoSize = Mathf.Max(4.10f, requiredWorldHeight * 0.5f);

            // Shift camera down by the difference between bottom DeckBar (170px) and TopBar (90px)
            // so visual content is vertically centered in the unobstructed play area
            float hudCenterOffset = ((170f - 90f) / 1080f) * orthoSize;
            float camY = visualCenterY - hudCenterOffset;

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
            UpdateAcidRainDrops();
            UpdateLevel3WeatherDamage();

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

        private void CreateLevel3WeatherEffects()
        {
            float cellSize = gridManager.CellSize;
            float mapWidth = stageData.gridWidth * cellSize;
            float mapHeight = stageData.gridHeight * cellSize;
            Vector3 gridBottomLeft = gridManager.GridToWorldPosition(0, 0)
                - new Vector3(cellSize * 0.5f, cellSize * 0.5f, 0f);

            var weatherRoot = new GameObject("Level3WeatherEffects");
            weatherRoot.transform.SetParent(transform, false);

            CreateAcidRainDrops(weatherRoot.transform, gridBottomLeft, mapWidth, mapHeight, cellSize);
        }

        private void CreateAcidRainDrops(Transform parent, Vector3 gridBottomLeft, float mapWidth, float mapHeight, float cellSize)
        {
            float minX = gridBottomLeft.x;
            float maxX = gridBottomLeft.x + mapWidth;
            float minY = gridBottomLeft.y;
            float maxY = gridBottomLeft.y + mapHeight;

            if (stageData.mapVisualSprite != null)
            {
                float pixelPerTile = stageData.visualTilePixelSize > 0 ? stageData.visualTilePixelSize : 32f;
                float spriteTilesX = stageData.mapVisualSprite.rect.width / pixelPerTile;
                float spriteTilesY = stageData.mapVisualSprite.rect.height / pixelPerTile;

                float spriteLeft = gridBottomLeft.x + stageData.visualTileOffset.x * cellSize;
                float spriteRight = spriteLeft + spriteTilesX * cellSize;
                float spriteBottom = gridBottomLeft.y + stageData.visualTileOffset.y * cellSize;
                float spriteTop = spriteBottom + spriteTilesY * cellSize;

                minX = Mathf.Min(minX, spriteLeft);
                maxX = Mathf.Max(maxX, spriteRight);
                minY = Mathf.Min(minY, spriteBottom);
                maxY = Mathf.Max(maxY, spriteTop);
            }

            acidRainBoundsMin = new Vector2(minX, minY);
            acidRainBoundsMax = new Vector2(maxX, maxY);
            acidRainVelocity = new Vector2(0.5f, -6f);
            float pixelSize = cellSize / 32f;
            Sprite dotSprite = GetWeatherDotSprite();

            for (int i = 0; i < AcidRainDropCount; i++)
            {
                var dropObject = new GameObject($"AcidRainDrop_{i}");
                dropObject.transform.SetParent(parent, false);
                dropObject.transform.position = new Vector3(
                    Random.Range(acidRainBoundsMin.x, acidRainBoundsMax.x),
                    Random.Range(acidRainBoundsMin.y, acidRainBoundsMax.y),
                    0f);
                dropObject.transform.localScale = Vector3.one * pixelSize;

                var renderer = dropObject.AddComponent<SpriteRenderer>();
                renderer.sprite = dotSprite;
                renderer.color = new Color(0.2f, 1f, 0.04f, 0.9f);
                renderer.sortingOrder = 25;
                acidRainDrops.Add(dropObject.transform);
            }
        }

        private static Sprite GetWeatherDotSprite()
        {
            if (weatherDotSprite == null)
            {
                weatherDotSprite = Sprite.Create(
                    Texture2D.whiteTexture,
                    new Rect(0f, 0f, 1f, 1f),
                    new Vector2(0.5f, 0.5f),
                    1f);
            }

            return weatherDotSprite;
        }

        private static ParticleSystem CreateWeatherParticleSystem(
            Transform parent,
            string objectName,
            Color color,
            Vector3 particleSize,
            float lifetime,
            float emissionRate,
            int sortingOrder)
        {
            var effectObject = new GameObject(objectName);
            effectObject.transform.SetParent(parent, false);

            var particles = effectObject.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1000;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            main.startSize3D = true;
            main.startSizeX = particleSize.x;
            main.startSizeY = particleSize.y;
            main.startSizeZ = particleSize.z;
            main.startColor = color;

            var emission = particles.emission;
            emission.rateOverTime = emissionRate;

            var renderer = effectObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = sortingOrder;
            renderer.sharedMaterial = GetWeatherParticleMaterial();

            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[]
                {
                    new GradientColorKey(color, 0f),
                    new GradientColorKey(color, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(color.a, 0.15f),
                    new GradientAlphaKey(color.a, 0.8f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

            return particles;
        }

        private static Material weatherParticleMaterial;

        private static Material GetDustCloudMaterial()
        {
            if (dustCloudMaterial != null) return dustCloudMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Material weatherMaterial = GetWeatherParticleMaterial();
                if (weatherMaterial == null) return null;
                dustCloudMaterial = new Material(weatherMaterial);
            }
            else
            {
                dustCloudMaterial = new Material(shader);
            }

            dustCloudMaterial.name = "Level3 Dust Cloud Particles";
            dustCloudMaterial.mainTexture = GetDustCloudTexture();
            return dustCloudMaterial;
        }

        private static Texture2D GetDustCloudTexture()
        {
            if (dustCloudTexture != null) return dustCloudTexture;

            const int textureSize = 64;
            dustCloudTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
            {
                name = "Level3 Soft Dust Cloud",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[textureSize * textureSize];
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    float dx = (x + 0.5f) / textureSize * 2f - 1f;
                    float dy = (y + 0.5f) / textureSize * 2f - 1f;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    float edgeVariation =
                        Mathf.Sin(dx * 5f + dy * 3f) * 0.06f +
                        Mathf.Sin(dx * 9f - dy * 7f) * 0.035f;
                    float edge = 0.88f + edgeVariation;
                    float alpha = 1f - Mathf.SmoothStep(edge - 0.5f, edge, radius);
                    alpha *= alpha;
                    pixels[y * textureSize + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            dustCloudTexture.SetPixels(pixels);
            dustCloudTexture.Apply();
            return dustCloudTexture;
        }

        private static Material GetWeatherParticleMaterial()
        {
            if (weatherParticleMaterial != null) return weatherParticleMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("StageBootstrapper: No supported shader found for Level 3 weather particles.");
                return null;
            }

            weatherParticleMaterial = new Material(shader)
            {
                name = "Level3 Weather Particles",
                mainTexture = Texture2D.whiteTexture
            };
            return weatherParticleMaterial;
        }

        private void UpdateLevel3WeatherDamage()
        {
            if (stageData == null || (stageData.stageId != "STAGE_03" && stageData.stageId != "STAGE_06") ||
                gameManager == null || gameManager.CurrentState != GamePlayState.Playing)
            {
                return;
            }

            weatherDamageTimer += Time.deltaTime;
            while (weatherDamageTimer >= Level3WeatherDamageIntervalSeconds)
            {
                weatherDamageTimer -= Level3WeatherDamageIntervalSeconds;
                ApplyLevel3WeatherDamage();
            }
        }

        private void UpdateAcidRainDrops()
        {
            if (acidRainDrops.Count == 0) return;

            float deltaTime = Time.deltaTime;
            for (int i = 0; i < acidRainDrops.Count; i++)
            {
                Transform drop = acidRainDrops[i];
                if (drop == null) continue;

                Vector3 position = drop.position;
                position.x += acidRainVelocity.x * deltaTime;
                position.y += acidRainVelocity.y * deltaTime;
                if (position.y < acidRainBoundsMin.y || position.x > acidRainBoundsMax.x)
                {
                    position.x = Random.Range(acidRainBoundsMin.x, acidRainBoundsMax.x);
                    position.y = acidRainBoundsMax.y + Random.Range(0f, 0.5f);
                }

                drop.position = position;
            }
        }

        private void ApplyLevel3WeatherDamage()
        {
            if (operatorManager != null)
            {
                for (int i = operatorManager.DeployedOperators.Count - 1; i >= 0; i--)
                {
                    OperatorBase op = operatorManager.DeployedOperators[i];
                    if (op == null || !op.IsDeployed || op.CurrentHP <= 0) continue;

                    TrashTD.Combat.AcidDamageFlash.Flash(op.gameObject);
                    op.TakeDamage(AcidRainDamagePerTick, DamageType.Physical);
                }
            }

            if (enemyManager != null)
            {
                for (int i = enemyManager.ActiveEnemies.Count - 1; i >= 0; i--)
                {
                    EnemyBase enemy = enemyManager.ActiveEnemies[i];
                    if (enemy == null || enemy.IsDead) continue;

                    TrashTD.Combat.AcidDamageFlash.Flash(enemy.gameObject);
                    enemy.TakeDamage(AcidRainDamagePerTick, DamageType.Physical);
                }
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
