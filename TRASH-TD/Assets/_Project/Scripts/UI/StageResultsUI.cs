using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TrashTD.Core.GameLoop;
using TrashTD.Data;

namespace TrashTD.UI
{
    /// <summary>
    /// Displays stage completion results (Victory with 1-3 stars, or Defeat) (GDD 1.4.6 & 1.9).
    /// </summary>
    public class StageResultsUI : MonoBehaviour
    {
        [Header("Root Panels")]
        [SerializeField] private GameObject resultsRoot;
        [SerializeField] private GameObject victoryPanel;
        [SerializeField] private GameObject defeatPanel;

        [Header("Victory Displays")]
        [SerializeField] private Text starsText;
        [SerializeField] private Text victoryTitleText;
        [SerializeField] private Text victoryMessageText;
        [SerializeField] private Text defeatMessageText;

        [Header("Buttons")]
        [SerializeField] private Button retryButton;
        [SerializeField] private Button returnToMapSelectButton;

        private void Awake()
        {
            if (resultsRoot == null) BuildRuntimeUI();

            if (retryButton != null) retryButton.onClick.AddListener(OnRetryClicked);
            if (returnToMapSelectButton != null) returnToMapSelectButton.onClick.AddListener(OnReturnClicked);

            if (resultsRoot != null) resultsRoot.SetActive(false);
        }

        private void Start()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStageVictory += ShowVictory;
                GameManager.Instance.OnStageDefeat += ShowDefeat;
            }
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnStageVictory -= ShowVictory;
                GameManager.Instance.OnStageDefeat -= ShowDefeat;
            }
        }

        public void ShowVictory(int stars)
        {
            if (resultsRoot != null) resultsRoot.SetActive(true);
            if (victoryPanel != null) victoryPanel.SetActive(true);
            if (defeatPanel != null) defeatPanel.SetActive(false);

            if (victoryTitleText != null) victoryTitleText.text = "STAGE COMPLETE";
            if (victoryMessageText != null)
            {
                GameManager manager = GameManager.Instance;
                string stageName = manager != null && manager.CurrentStage != null ? manager.CurrentStage.mapName : "Stage";
                victoryMessageText.text = $"{stageName} cleared";
            }

            if (starsText != null)
            {
                starsText.text = $"{new string('★', stars)}{new string('☆', 3 - stars)}";
            }
        }

        public void ShowDefeat()
        {
            if (resultsRoot != null) resultsRoot.SetActive(true);
            if (victoryPanel != null) victoryPanel.SetActive(false);
            if (defeatPanel != null) defeatPanel.SetActive(true);

            if (defeatMessageText != null)
            {
                GameManager manager = GameManager.Instance;
                string stageName = manager != null && manager.CurrentStage != null ? manager.CurrentStage.mapName : "the stage";
                defeatMessageText.text = $"No lives remain in {stageName}.";
            }
        }

        private void OnRetryClicked()
        {
            GameManager manager = GameManager.Instance;
            if (manager != null)
            {
                MainMenuController.SetPendingStage(manager.CurrentStage);
                MainMenuController.SetPendingStageDifficulty(manager.CurrentDifficulty);
            }

            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void OnReturnClicked()
        {
            Time.timeScale = 1f;
            MainMenuController.ReturnToStageSelectorOnLoad();
            SceneManager.LoadScene("MainMenu");
        }

        private void BuildRuntimeUI()
        {
            GameObject canvasObject = new GameObject("StageResultsCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            resultsRoot = new GameObject("StageResultsOverlay", typeof(RectTransform), typeof(Image));
            resultsRoot.transform.SetParent(canvasObject.transform, false);
            RectTransform overlayRect = resultsRoot.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            resultsRoot.GetComponent<Image>().color = new Color(0.015f, 0.025f, 0.035f, 0.88f);

            victoryPanel = CreateResultPanel(resultsRoot.transform, "VictoryPanel");
            defeatPanel = CreateResultPanel(resultsRoot.transform, "DefeatPanel");

            victoryTitleText = CreateResultText(victoryPanel.transform, "VictoryTitle", "STAGE COMPLETE", 38, new Vector2(0f, 95f), new Vector2(500f, 60f));
            starsText = CreateResultText(victoryPanel.transform, "VictoryStars", "★★★", 48, new Vector2(0f, 25f), new Vector2(440f, 68f));
            victoryMessageText = CreateResultText(victoryPanel.transform, "VictoryMessage", "Stage cleared", 22, new Vector2(0f, -35f), new Vector2(480f, 42f));
            CreateResultText(defeatPanel.transform, "DefeatTitle", "GAME OVER", 38, new Vector2(0f, 95f), new Vector2(500f, 60f));
            defeatMessageText = CreateResultText(defeatPanel.transform, "DefeatMessage", "No lives remain.", 22, new Vector2(0f, 25f), new Vector2(480f, 50f));

            retryButton = CreateResultButton(resultsRoot.transform, "RetryButton", "RETRY", new Color(0.14f, 0.58f, 0.34f), new Vector2(-115f, -145f));
            returnToMapSelectButton = CreateResultButton(resultsRoot.transform, "ReturnToMenuButton", "MAIN MENU", new Color(0.16f, 0.2f, 0.24f), new Vector2(115f, -145f));
        }

        private static GameObject CreateResultPanel(Transform parent, string objectName)
        {
            GameObject panel = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(620f, 390f);
            panel.GetComponent<Image>().color = new Color(0.055f, 0.075f, 0.09f, 1f);
            return panel;
        }

        private static Text CreateResultText(Transform parent, string objectName, string value, int fontSize, Vector2 position, Vector2 size)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Text text = textObject.GetComponent<Text>();
            text.text = value;
            text.font = UIFontHelper.GetPixelFont();
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateResultButton(Transform parent, string objectName, string label, Color color, Vector2 position)
        {
            GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(210f, 62f);
            buttonObject.GetComponent<Image>().color = color;

            CreateResultText(buttonObject.transform, "Label", label, 20, Vector2.zero, rect.sizeDelta);
            return buttonObject.GetComponent<Button>();
        }
    }
}
