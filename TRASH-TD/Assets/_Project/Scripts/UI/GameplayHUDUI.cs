using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TrashTD.Core.GameLoop;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.UI
{
    /// <summary>
    /// Runtime-built in-game HUD for the stage view.
    /// </summary>
    public class GameplayHUDUI : MonoBehaviour
    {
        private const int RerollsRemaining = 3;
        private const int CreatureSlotCount = 8;

        private Canvas canvas;
        private GameObject pausePanel;
        private Text waveText;
        private Text enemyText;
        private Text rerollText;
        private Button startWaveButton;
        private Button[] creatureButtons;
        private CardDraftSystem draftSystem;
        private WaveManager waveManager;
        private EnemyManager enemyManager;
        private GameManager gameManager;
        private bool waveStarted;

        private void Awake()
        {
            draftSystem = FindFirstObjectByType<CardDraftSystem>();
            waveManager = FindFirstObjectByType<WaveManager>();
            enemyManager = FindFirstObjectByType<EnemyManager>();
            gameManager = FindFirstObjectByType<GameManager>();

            EnsureEventSystem();
            BuildHud();
        }

        private void Start()
        {
            if (waveManager != null)
            {
                waveManager.OnWaveStarted += HandleWaveStarted;
                waveManager.OnWaveCompleted += HandleWaveCompleted;
            }

            if (enemyManager != null)
            {
                enemyManager.OnEnemySpawned += HandleEnemyCountChanged;
                enemyManager.OnEnemyDied += HandleEnemyCountChanged;
                enemyManager.OnEnemyReachedExit += HandleEnemyCountChanged;
            }

            if (draftSystem != null)
            {
                draftSystem.OnCardsOffered += HandleCardsOffered;
                HandleCardsOffered(draftSystem.CurrentOfferedCards);
            }

            RefreshCounters();
        }

        private void Update()
        {
            RefreshCounters();
        }

        private void OnDestroy()
        {
            if (waveManager != null)
            {
                waveManager.OnWaveStarted -= HandleWaveStarted;
                waveManager.OnWaveCompleted -= HandleWaveCompleted;
            }

            if (enemyManager != null)
            {
                enemyManager.OnEnemySpawned -= HandleEnemyCountChanged;
                enemyManager.OnEnemyDied -= HandleEnemyCountChanged;
                enemyManager.OnEnemyReachedExit -= HandleEnemyCountChanged;
            }

            if (draftSystem != null)
            {
                draftSystem.OnCardsOffered -= HandleCardsOffered;
            }
        }

        private void BuildHud()
        {
            var canvasObject = new GameObject("GameplayHUDCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var root = canvasObject.transform;
            CreateTopBar(root);
            CreateCreatureBar(root);
            CreatePausePanel(root);
        }

        private void CreateTopBar(Transform root)
        {
            var pauseButton = CreateButton(root, "PauseButton", "||", new Vector2(70f, 70f));
            SetPosition(pauseButton.GetComponent<RectTransform>(), new Vector2(50f, -50f), new Vector2(0f, 1f));
            pauseButton.onClick.AddListener(PauseGame);

            waveText = CreateText(root, "WaveText", "WAVE 1/15", 34, TextAnchor.MiddleLeft);
            SetPosition(waveText.GetComponent<RectTransform>(), new Vector2(270f, -50f), new Vector2(0f, 1f), new Vector2(300f, 70f));

            enemyText = CreateText(root, "EnemyText", "0\nENEMIES LEFT", 30, TextAnchor.MiddleCenter);
            SetPosition(enemyText.GetComponent<RectTransform>(), new Vector2(0f, -50f), new Vector2(0.5f, 1f), new Vector2(280f, 85f));

            rerollText = CreateText(root, "RerollText", "3\nREROLLS LEFT", 30, TextAnchor.MiddleCenter);
            SetPosition(rerollText.GetComponent<RectTransform>(), new Vector2(-180f, -50f), new Vector2(1f, 1f), new Vector2(230f, 85f));
        }

        private void CreateCreatureBar(Transform root)
        {
            var bar = new GameObject("CreatureBar", typeof(RectTransform));
            bar.transform.SetParent(root, false);
            var barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.offsetMin = new Vector2(50f, 28f);
            barRect.offsetMax = new Vector2(-280f, 198f);

            var layout = bar.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            creatureButtons = new Button[CreatureSlotCount];
            for (int i = 0; i < CreatureSlotCount; i++)
            {
                int slotIndex = i;
                creatureButtons[i] = CreateCreatureSlot(bar.transform, "CreatureSlot_" + (i + 1));
                creatureButtons[i].onClick.AddListener(() => SelectCreature(slotIndex));
            }

            startWaveButton = CreateButton(root, "StartWaveButton", "START\nWAVE", new Vector2(150f, 90f));
            SetPosition(startWaveButton.GetComponent<RectTransform>(), new Vector2(-95f, 95f), new Vector2(1f, 0f));
            startWaveButton.onClick.AddListener(StartWave);
        }

        private void CreatePausePanel(Transform root)
        {
            pausePanel = new GameObject("PausePanel", typeof(RectTransform), typeof(Image));
            pausePanel.transform.SetParent(root, false);
            var panelRect = pausePanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            pausePanel.GetComponent<Image>().color = new Color(0.03f, 0.04f, 0.06f, 0.94f);

            var title = CreateText(pausePanel.transform, "PauseTitle", "PAUSED", 54, TextAnchor.MiddleCenter);
            SetPosition(title.GetComponent<RectTransform>(), new Vector2(0f, -300f), new Vector2(0.5f, 1f), new Vector2(500f, 100f));

            var resumeButton = CreateButton(pausePanel.transform, "ResumeButton", "RESUME", new Vector2(260f, 70f));
            SetPosition(resumeButton.GetComponent<RectTransform>(), new Vector2(0f, -480f), new Vector2(0.5f, 1f));
            resumeButton.onClick.AddListener(ResumeGame);

            var exitButton = CreateButton(pausePanel.transform, "ExitButton", "EXIT TO MENU", new Vector2(260f, 70f));
            SetPosition(exitButton.GetComponent<RectTransform>(), new Vector2(0f, -580f), new Vector2(0.5f, 1f));
            exitButton.onClick.AddListener(ExitToMenu);

            pausePanel.SetActive(false);
        }

        private Button CreateCreatureSlot(Transform parent, string objectName)
        {
            var button = CreateButton(parent, objectName, "", new Vector2(120f, 130f));
            var image = button.GetComponent<Image>();
            image.sprite = CreateTrapezoidSprite();
            image.color = new Color(0.04f, 0.05f, 0.07f, 1f);
            image.type = Image.Type.Simple;
            return button;
        }

        private void HandleCardsOffered(IReadOnlyList<DraftCard> cards)
        {
            for (int i = 0; i < creatureButtons.Length; i++)
            {
                var label = creatureButtons[i].GetComponentInChildren<Text>();
                if (label == null) continue;

                if (i < cards.Count && cards[i] != null && cards[i].operatorData != null)
                {
                    label.text = cards[i].operatorData.operatorName;
                    creatureButtons[i].interactable = true;
                }
                else
                {
                    label.text = string.Empty;
                    creatureButtons[i].interactable = false;
                }
            }
        }

        private void SelectCreature(int index)
        {
            if (draftSystem != null && index < draftSystem.CurrentOfferedCards.Count)
            {
                draftSystem.SelectCard(index, out _);
            }
        }

        private void StartWave()
        {
            if (waveStarted || waveManager == null) return;
            waveStarted = true;
            startWaveButton.interactable = false;
            waveManager.StartWaves();
        }

        private void PauseGame()
        {
            if (gameManager == null) return;
            gameManager.PauseGame();
            pausePanel.SetActive(true);
        }

        private void ResumeGame()
        {
            if (gameManager == null) return;
            gameManager.ResumeGame();
            pausePanel.SetActive(false);
        }

        private void ExitToMenu()
        {
            Time.timeScale = 1f;
            MainMenuController.ReturnToStageSelectorOnLoad();
            SceneManager.LoadScene("MainMenu");
        }

        private void HandleWaveStarted(int current, int total)
        {
            waveText.text = $"WAVE {current}/{total}";
            startWaveButton.interactable = false;
        }

        private void HandleWaveCompleted(int waveIndex)
        {
            if (waveManager != null && waveManager.CurrentWaveNumber < waveManager.TotalWaves)
            {
                startWaveButton.interactable = true;
                waveStarted = false;
            }
        }

        private void HandleEnemyCountChanged(EnemyBase enemy)
        {
            RefreshCounters();
        }

        private void RefreshCounters()
        {
            if (enemyText != null && enemyManager != null)
            {
                enemyText.text = $"{enemyManager.ActiveEnemyCount}\nENEMIES LEFT";
            }

            if (waveText != null && waveManager != null && waveManager.TotalWaves > 0)
            {
                waveText.text = $"WAVE {Mathf.Max(1, waveManager.CurrentWaveNumber)}/{waveManager.TotalWaves}";
            }
        }

        private static Text CreateText(Transform parent, string objectName, string value, int fontSize, TextAnchor alignment)
        {
            var textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = alignment;
            text.color = Color.white;
            return text;
        }

        private static Button CreateButton(Transform parent, string objectName, string label, Vector2 size)
        {
            var buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var button = buttonObject.GetComponent<Button>();
            buttonObject.GetComponent<RectTransform>().sizeDelta = size;
            buttonObject.GetComponent<Image>().color = Color.black;

            var text = CreateText(buttonObject.transform, "Label", label, 22, TextAnchor.MiddleCenter);
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            return button;
        }

        private static void SetPosition(RectTransform rect, Vector2 position, Vector2 anchor, Vector2? size = null)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            if (size.HasValue) rect.sizeDelta = size.Value;
        }

        private static Sprite CreateTrapezoidSprite()
        {
            const int width = 128;
            const int height = 112;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

            for (int y = 0; y < height; y++)
            {
                float inset = Mathf.Lerp(18f, 0f, y / (float)(height - 1));
                int left = Mathf.RoundToInt(inset);
                int right = width - left;
                for (int x = left; x < right; x++) pixels[y * width + x] = Color.white;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
        }

        private static void EnsureEventSystem()
        {
            var eventSystem = FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventSystem = eventSystemObject.GetComponent<EventSystem>();
            }
            else if (eventSystem.GetComponent<BaseInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }
        }
    }
}
