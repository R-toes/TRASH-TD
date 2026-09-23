using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using TrashTD.Data;

namespace TrashTD.UI
{
    /// <summary>
    /// Minimal main menu and stage selector flow for the first playable shell.
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        [Header("Main Menu")]
        [SerializeField] private GameObject mainMenuPanel;
        [SerializeField] private GameObject stageSelectionPanel;

        [SerializeField] private Text titleText;
        [SerializeField] private Text subtitleText;
        [SerializeField] private Button startButton;
        [SerializeField] private Button exitButton;

        [Header("Stage Selector")]
        [SerializeField] private Button backButton;
        [SerializeField] private Transform stageListRoot;
        [SerializeField] private Button stageButtonPrefab;
        [SerializeField] private GameObject stageDetailsPanel;
        [SerializeField] private Text stageDetailsTitle;
        [SerializeField] private Text stageDetailsDescription;
        [SerializeField] private Button playStageButton;
        [SerializeField] private Button stageDetailsBackButton;

        private readonly List<StageData> availableStages = new List<StageData>();
        private StageData selectedStage;
        private static bool returnToStageSelector;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetNavigationState()
        {
            returnToStageSelector = false;
        }

        private void Awake()
        {
            if (mainMenuPanel == null || stageSelectionPanel == null || startButton == null || exitButton == null)
            {
                BuildDefaultUi();
            }

            if (titleText == null)
            {
                titleText = GameObject.Find("TitleText")?.GetComponent<Text>();
            }

            if (subtitleText == null)
            {
                subtitleText = GameObject.Find("SubtitleText")?.GetComponent<Text>();
            }

            if (backButton == null)
            {
                backButton = GameObject.Find("BackButton")?.GetComponent<Button>();
            }

            if (stageListRoot == null)
            {
                stageListRoot = GameObject.Find("StageList")?.transform;
            }

            if (stageButtonPrefab == null)
            {
                stageButtonPrefab = GameObject.Find("StageButtonPrefab")?.GetComponent<Button>();
            }

            if (stageDetailsPanel == null)
            {
                BuildStageDetailsUi();
            }

            BuildStageButtons();
            ShowMainMenu();
        }

        private void Start()
        {
            if (startButton != null)
            {
                startButton.onClick.RemoveAllListeners();
                startButton.onClick.AddListener(ShowStageSelection);
            }

            if (exitButton != null)
            {
                exitButton.onClick.RemoveAllListeners();
                exitButton.onClick.AddListener(ExitGame);
            }

            if (backButton != null)
            {
                backButton.onClick.RemoveAllListeners();
                backButton.onClick.AddListener(ShowMainMenu);
            }

            if (returnToStageSelector)
            {
                returnToStageSelector = false;
                ShowStageSelection();
            }
        }

        public static void ReturnToStageSelectorOnLoad()
        {
            returnToStageSelector = true;
        }

        public void BuildDefaultUi()
        {
            var canvasGo = GetOrCreateCanvas();
            canvasGo.transform.SetParent(null, false);

            if (mainMenuPanel == null)
            {
                mainMenuPanel = new GameObject("MainMenuPanel", typeof(RectTransform), typeof(Image));
                mainMenuPanel.transform.SetParent(canvasGo.transform, false);
                var panelRect = mainMenuPanel.GetComponent<RectTransform>();
                panelRect.anchorMin = Vector2.zero;
                panelRect.anchorMax = Vector2.one;
                panelRect.offsetMin = Vector2.zero;
                panelRect.offsetMax = Vector2.zero;
                mainMenuPanel.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 1f);
            }

            if (titleText == null)
            {
                var titleGo = CreateTextChild(mainMenuPanel.transform, "TitleText", "TRASH TD", 52, TextAnchor.MiddleCenter);
                titleText = titleGo.GetComponent<Text>();
                titleText.color = Color.white;
                titleText.fontStyle = FontStyle.Bold;
            }

            var titleRect = titleText.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 0.75f);
            titleRect.anchorMax = new Vector2(0.5f, 0.75f);
            titleRect.anchoredPosition = Vector2.zero;
            titleRect.sizeDelta = new Vector2(600f, 80f);

            if (subtitleText == null)
            {
                var subtitleGo = CreateTextChild(mainMenuPanel.transform, "SubtitleText", "Tiny Reclaimers Against Spreading Hazard", 22, TextAnchor.MiddleCenter);
                subtitleText = subtitleGo.GetComponent<Text>();
                subtitleText.color = new Color(0.75f, 0.85f, 1f, 1f);
                subtitleText.fontStyle = FontStyle.Italic;
            }

            var subtitleRect = subtitleText.GetComponent<RectTransform>();
            subtitleRect.anchorMin = new Vector2(0.5f, 0.65f);
            subtitleRect.anchorMax = new Vector2(0.5f, 0.65f);
            subtitleRect.anchoredPosition = Vector2.zero;
            subtitleRect.sizeDelta = new Vector2(800f, 60f);

            if (startButton == null)
            {
                startButton = CreateButtonChild(mainMenuPanel.transform, "StartButton", "Start");
            }

            if (exitButton == null)
            {
                exitButton = CreateButtonChild(mainMenuPanel.transform, "ExitButton", "Exit");
                var exitRect = exitButton.GetComponent<RectTransform>();
                exitRect.anchoredPosition = new Vector2(0f, -80f);
            }

            var startRect = startButton.GetComponent<RectTransform>();
            startRect.anchorMin = new Vector2(0.5f, 0.38f);
            startRect.anchorMax = new Vector2(0.5f, 0.38f);
            startRect.sizeDelta = new Vector2(220f, 60f);
            startRect.anchoredPosition = new Vector2(0f, 0f);

            var exitRect2 = exitButton.GetComponent<RectTransform>();
            exitRect2.anchorMin = new Vector2(0.5f, 0.24f);
            exitRect2.anchorMax = new Vector2(0.5f, 0.24f);
            exitRect2.sizeDelta = new Vector2(220f, 60f);
            exitRect2.anchoredPosition = new Vector2(0f, 0f);

            if (stageSelectionPanel == null)
            {
                stageSelectionPanel = new GameObject("StageSelectionPanel", typeof(RectTransform), typeof(Image));
                stageSelectionPanel.transform.SetParent(canvasGo.transform, false);
                stageSelectionPanel.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 1f);
                var stagePanelRect = stageSelectionPanel.GetComponent<RectTransform>();
                stagePanelRect.anchorMin = Vector2.zero;
                stagePanelRect.anchorMax = Vector2.one;
                stagePanelRect.offsetMin = Vector2.zero;
                stagePanelRect.offsetMax = Vector2.zero;
                stageSelectionPanel.SetActive(false);
            }

            if (backButton == null)
            {
                backButton = CreateButtonChild(stageSelectionPanel.transform, "BackButton", "Back");
            }

            var backRect = backButton.GetComponent<RectTransform>();
            backRect.anchorMin = new Vector2(0.5f, 0.1f);
            backRect.anchorMax = new Vector2(0.5f, 0.1f);
            backRect.sizeDelta = new Vector2(220f, 60f);
            backRect.anchoredPosition = Vector2.zero;

            if (stageListRoot == null)
            {
                var stageListGo = new GameObject("StageList", typeof(RectTransform), typeof(GridLayoutGroup));
                stageListGo.transform.SetParent(stageSelectionPanel.transform, false);
                stageListRoot = stageListGo.transform;

                var rect = stageListGo.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(850f, 420f);
                rect.anchoredPosition = new Vector2(0f, 40f);
            }

            var stageGrid = stageListRoot.GetComponent<GridLayoutGroup>();
            if (stageGrid == null)
            {
                stageGrid = stageListRoot.gameObject.AddComponent<GridLayoutGroup>();
            }

            stageGrid.cellSize = new Vector2(250f, 150f);
            stageGrid.spacing = new Vector2(18f, 18f);
            stageGrid.padding = new RectOffset(20, 20, 20, 20);
            stageGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            stageGrid.constraintCount = 3;
            stageGrid.childAlignment = TextAnchor.UpperCenter;

            if (stageButtonPrefab == null)
            {
                stageButtonPrefab = CreateButtonChild(stageListRoot.transform, "StageButtonPrefab", "Stage");
                stageButtonPrefab.gameObject.name = "StageButtonPrefab";
                stageButtonPrefab.gameObject.SetActive(false);
            }

            if (stageDetailsPanel == null)
            {
                BuildStageDetailsUi();
            }
        }

        public void ShowMainMenu()
        {
            if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
            if (stageSelectionPanel != null) stageSelectionPanel.SetActive(false);
            if (stageDetailsPanel != null) stageDetailsPanel.SetActive(false);
        }

        public void ShowStageSelection()
        {
            if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
            if (stageSelectionPanel != null) stageSelectionPanel.SetActive(true);
            if (stageDetailsPanel != null) stageDetailsPanel.SetActive(false);
            if (stageDetailsPanel != null) stageDetailsPanel.SetActive(false);
        }

        private void BuildStageButtons()
        {
            availableStages.Clear();

            string[] stageNames = new[]
            {
                "Stage 1", "Stage 2", "Stage 3",
                "Stage 4", "Stage 5", "Stage 6"
            };

            string[] stageDescriptions = new[]
            {
                "Clear the first wave of spreading hazard.",
                "Hold the line through the trash docks.",
                "Secure the scrapline junction.",
                "Reclaim the ruined market district.",
                "Push through the cinder dump.",
                "Take back the clockwork quarry."
            };

            for (int i = 0; i < stageNames.Length; i++)
            {
                var stage = ScriptableObject.CreateInstance<StageData>();
                stage.stageId = "STAGE_0" + (i + 1);
                stage.mapName = stageNames[i];
                stage.shortDescription = stageDescriptions[i];
                availableStages.Add(stage);
            }

            if (stageListRoot == null)
            {
                return;
            }

            for (int i = stageListRoot.childCount - 1; i >= 0; i--)
            {
                var child = stageListRoot.GetChild(i);
                if (child.name != "StageButtonPrefab")
                {
                    Destroy(child.gameObject);
                }
            }

            for (int i = 0; i < availableStages.Count; i++)
            {
                var stage = availableStages[i];
                var button = Instantiate(stageButtonPrefab, stageListRoot);
                button.gameObject.SetActive(true);
                button.name = "StageButton_" + (i + 1);

                var textComponent = button.GetComponentInChildren<Text>();
                if (textComponent != null)
                {
                    textComponent.text = stage.mapName;
                }

                var rect = button.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(250f, 150f);

                int stageIndex = i;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => SelectStage(stageIndex));
            }
        }

        private void SelectStage(int stageIndex)
        {
            if (stageIndex < 0 || stageIndex >= availableStages.Count)
            {
                return;
            }

            selectedStage = availableStages[stageIndex];
            stageDetailsTitle.text = selectedStage.mapName;
            stageDetailsDescription.text = selectedStage.shortDescription;
            stageDetailsPanel.SetActive(true);
        }

        private void PlaySelectedStage()
        {
            if (selectedStage == null)
            {
                return;
            }

            SceneManager.LoadScene("GameplayTest");
        }

        private void BuildStageDetailsUi()
        {
            stageDetailsPanel = new GameObject("StageDetailsPanel", typeof(RectTransform), typeof(Image));
            stageDetailsPanel.transform.SetParent(stageSelectionPanel.transform, false);

            var panelRect = stageDetailsPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            stageDetailsPanel.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f, 1f);

            var title = CreateTextChild(stageDetailsPanel.transform, "StageDetailsTitle", "Select a stage", 30, TextAnchor.MiddleCenter);
            stageDetailsTitle = title.GetComponent<Text>();
            var titleRect = stageDetailsTitle.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.2f, 0.62f);
            titleRect.anchorMax = new Vector2(0.8f, 0.82f);
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;

            var description = CreateTextChild(stageDetailsPanel.transform, "StageDetailsDescription", "Choose a stage to view its details.", 20, TextAnchor.MiddleCenter);
            stageDetailsDescription = description.GetComponent<Text>();
            stageDetailsDescription.color = new Color(0.8f, 0.85f, 0.9f, 1f);
            var descriptionRect = stageDetailsDescription.GetComponent<RectTransform>();
            descriptionRect.anchorMin = new Vector2(0.2f, 0.42f);
            descriptionRect.anchorMax = new Vector2(0.8f, 0.62f);
            descriptionRect.offsetMin = Vector2.zero;
            descriptionRect.offsetMax = Vector2.zero;

            playStageButton = CreateButtonChild(stageDetailsPanel.transform, "PlayStageButton", "Play");
            var playRect = playStageButton.GetComponent<RectTransform>();
            playRect.anchorMin = new Vector2(0.5f, 0.25f);
            playRect.anchorMax = new Vector2(0.5f, 0.25f);
            playRect.sizeDelta = new Vector2(220f, 60f);
            playStageButton.onClick.AddListener(PlaySelectedStage);

            stageDetailsBackButton = CreateButtonChild(stageDetailsPanel.transform, "StageDetailsBackButton", "Back");
            var backButtonRect = stageDetailsBackButton.GetComponent<RectTransform>();
            backButtonRect.anchorMin = new Vector2(0.5f, 0.12f);
            backButtonRect.anchorMax = new Vector2(0.5f, 0.12f);
            backButtonRect.sizeDelta = new Vector2(220f, 60f);
            stageDetailsBackButton.onClick.AddListener(ShowStageSelection);
            stageDetailsPanel.SetActive(false);
        }

        private void ExitGame()
        {
            Debug.Log("Exit button pressed.");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private GameObject GetOrCreateCanvas()
        {
            var existingCanvas = FindFirstObjectByType<Canvas>();
            if (existingCanvas != null)
            {
                EnsureEventSystem();
                return existingCanvas.gameObject;
            }

            var canvas = new GameObject("MainMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvasComponent = canvas.GetComponent<Canvas>();
            canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            EnsureEventSystem();
            return canvas;
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

        private static Button CreateButtonChild(Transform parent, string name, string label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(220f, 60f);

            var image = go.GetComponent<Image>();
            image.color = new Color(0.22f, 0.52f, 0.9f, 1f);

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);

            var text = textGo.GetComponent<Text>();
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 24;
            text.color = Color.white;

            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            return go.GetComponent<Button>();
        }

        private static GameObject CreateTextChild(Transform parent, string name, string text, int fontSize, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var textComp = go.GetComponent<Text>();
            textComp.text = text;
            textComp.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            textComp.fontSize = fontSize;
            textComp.alignment = alignment;
            textComp.color = Color.white;

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return go;
        }
    }
}
