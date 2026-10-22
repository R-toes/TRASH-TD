using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TrashTD.Data;

namespace TrashTD.UI
{
    /// <summary>
    /// One clickable stage dot on the Pip-Boy style map.
    /// Placed by a normalized position (0-1 across the map art, origin top-left) so it stays on its
    /// location at any 16:9 resolution. Sprite state: Idle / Selected / Locked.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class StageMapNode : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Stage")]
        [SerializeField] private StageData stage;

        [Header("Placement (0-1 over the map art, origin = top-left)")]
        [SerializeField] private Vector2 normalizedPosition = new Vector2(0.5f, 0.5f);
        [Tooltip("Size of the circular node in UI units (64x64 is clear, readable, and comfortable to click).")]
        [SerializeField] private Vector2 nativeSize = new Vector2(64f, 64f);

        [Header("Visuals")]
        [SerializeField] private int stageNumber = 1;
        [SerializeField] private Image dotImage;
        [SerializeField] private Text numberText;
        [SerializeField] private Button button;
        [SerializeField] private Sprite idleSprite;
        [SerializeField] private Sprite selectedSprite;
        [SerializeField] private Sprite lockedSprite;

        [Header("Header Display (above node)")]
        [SerializeField] private GameObject headerRoot;
        [SerializeField] private Text levelTagText;
        [SerializeField] private Text stageNameText;
        [SerializeField] private Outline headerOutline;

        [Header("Selected Pulse")]
        [SerializeField, Range(0f, 0.2f)] private float pulseAmount = 0.06f;
        [SerializeField] private float pulseSpeed = 4f;

        private bool isSelected;
        private bool isLocked;
        private bool isHovered;
        private UnityAction clickAction;

        /// <summary>Raised when the dot is clicked (never while locked).</summary>
        public event Action<StageMapNode> Clicked;

        /// <summary>Raised when the pointer enters (true) or leaves (false) the dot, locked or not.</summary>
        public event Action<StageMapNode, bool> HoverChanged;

        public StageData Stage => stage;
        public bool IsLocked => isLocked;
        public int StageNumber => stageNumber;

        /// <summary>
        /// Runtime initializer with stage number, numberText, and header display.
        /// </summary>
        public void Initialize(StageData stageData, int number, Vector2 normPos, Sprite idle, Sprite selected, Sprite locked,
            Text numText = null, GameObject header = null, Text lvlText = null, Text nameText = null)
        {
            stage = stageData;
            stageNumber = number;
            normalizedPosition = normPos;
            idleSprite = idle;
            selectedSprite = selected;
            lockedSprite = locked;
            if (numText != null) numberText = numText;
            if (header != null)
            {
                headerRoot = header;
                headerOutline = header.GetComponent<Outline>();
            }
            if (lvlText != null) levelTagText = lvlText;
            if (nameText != null) stageNameText = nameText;

            if (dotImage == null) dotImage = GetComponent<Image>();
            if (numberText == null) numberText = GetComponentInChildren<Text>();
            if (dotImage != null) dotImage.preserveAspect = true;
            if (numberText != null) numberText.text = stageNumber.ToString();
            if (levelTagText != null) levelTagText.text = "LEVEL " + stageNumber;
            if (stageNameText != null && stage != null) stageNameText.text = stage.mapName.ToUpper();
            RefreshVisual();
        }

        public void Initialize(StageData stageData, Vector2 normPos, Sprite idle, Sprite selected, Sprite locked)
        {
            Initialize(stageData, 1, normPos, idle, selected, locked, null, null, null, null);
        }

        private void Awake()
        {
            if (dotImage == null) dotImage = GetComponent<Image>();
            if (button == null) button = GetComponent<Button>();
            clickAction = () => { if (!isLocked) Clicked?.Invoke(this); };
            if (button != null) button.onClick.AddListener(clickAction);

            if (stageNameText != null && stage != null) stageNameText.text = stage.mapName.ToUpper();
            if (levelTagText != null) levelTagText.text = "LEVEL " + stageNumber;
            RefreshVisual();
        }

        private void OnDestroy()
        {
            if (button != null && clickAction != null) button.onClick.RemoveListener(clickAction);
        }

        private void Update()
        {
            if (isSelected && pulseAmount > 0f)
            {
                // unscaled so the menu still pulses if Time.timeScale is 0
                transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount);
            }
        }

        /// <summary>Called by StageMapLayout whenever the map art changes size.</summary>
        public void ApplyLayout(float pixelScale)
        {
            var rt = (RectTransform)transform;
            rt.anchorMin = rt.anchorMax = new Vector2(normalizedPosition.x, 1f - normalizedPosition.y);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = nativeSize * pixelScale;
        }

        public void SetSelected(bool selected)
        {
            isSelected = selected;
            RefreshVisual();
        }

        public void SetLocked(bool locked)
        {
            isLocked = locked;
            RefreshVisual();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            isHovered = true;
            RefreshVisual();
            HoverChanged?.Invoke(this, true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHovered = false;
            RefreshVisual();
            HoverChanged?.Invoke(this, false);
        }

        private void RefreshVisual()
        {
            if (dotImage != null)
            {
                Sprite s = isLocked ? lockedSprite : (isSelected ? selectedSprite : idleSprite);
                if (s != null)
                {
                    if (s.texture != null) s.texture.filterMode = FilterMode.Point;
                    dotImage.sprite = s;
                    dotImage.color = Color.white;
                    dotImage.preserveAspect = true;
                }
            }

            if (numberText != null)
            {
                if (isLocked)
                {
                    numberText.color = new Color(0.38f, 0.44f, 0.50f, 0.85f);
                }
                else if (isSelected)
                {
                    numberText.color = new Color(0.02f, 0.08f, 0.04f, 1f); // Dark contrast text on glowing green selected badge
                }
                else if (isHovered)
                {
                    numberText.color = new Color(0.55f, 1.0f, 0.75f, 1f); // Extra bright on hover
                }
                else
                {
                    numberText.color = new Color(0.22f, 0.91f, 0.50f, 1f); // Neon phosphor green
                }
            }

            if (levelTagText != null)
            {
                if (isLocked)
                {
                    levelTagText.color = new Color(0.38f, 0.44f, 0.50f, 0.70f);
                }
                else if (isSelected)
                {
                    levelTagText.color = new Color(0.98f, 1.0f, 0.65f, 1f);
                }
                else if (isHovered)
                {
                    levelTagText.color = new Color(0.55f, 1.0f, 0.75f, 1f);
                }
                else
                {
                    levelTagText.color = new Color(0.30f, 0.85f, 0.50f, 0.95f);
                }
            }

            if (stageNameText != null)
            {
                if (isLocked)
                {
                    stageNameText.color = new Color(0.50f, 0.55f, 0.62f, 0.70f);
                }
                else if (isSelected)
                {
                    stageNameText.color = Color.white;
                }
                else if (isHovered)
                {
                    stageNameText.color = Color.white;
                }
                else
                {
                    stageNameText.color = new Color(0.90f, 0.94f, 0.98f, 0.95f);
                }
            }

            if (headerOutline != null)
            {
                headerOutline.effectColor = isSelected
                    ? new Color(0.98f, 1f, 0.65f, 0.85f)
                    : (isLocked ? new Color(0.25f, 0.30f, 0.36f, 0.40f) : (isHovered ? new Color(0.45f, 1.0f, 0.70f, 0.80f) : new Color(0.22f, 0.91f, 0.50f, 0.45f)));
            }

            if (button != null) button.interactable = !isLocked;
            if (headerRoot != null) headerRoot.SetActive(true);
            if (!isSelected) transform.localScale = Vector3.one;
        }
    }
}