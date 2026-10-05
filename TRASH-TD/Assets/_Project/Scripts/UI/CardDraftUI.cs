using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TrashTD.Audio;
using TrashTD.Systems;

namespace TrashTD.UI
{
    /// <summary>
    /// UI panel displaying the 3 round-by-round draft choices (GDD 1.4.3 & 1.6).
    /// Scene-assigned layout; this script adds the animation and feedback on top:
    /// panel fade, staggered card pop-in, hover/press feedback and rarity-colored stars.
    /// </summary>
    public class CardDraftUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CardDraftSystem draftSystem;
        [SerializeField] private GameObject draftPanel;

        [Header("Card Slot UI Elements (3 slots)")]
        [SerializeField] private Button[] cardButtons = new Button[3];
        [SerializeField] private Text[] nameTexts = new Text[3];
        [SerializeField] private Text[] classTexts = new Text[3];
        [SerializeField] private Text[] rarityTexts = new Text[3];
        [SerializeField] private Image[] portraitImages = new Image[3];

        [Header("Feedback (optional)")]
        [SerializeField] private AudioClip hoverClip;
        [SerializeField] private AudioClip clickClip;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.7f;

        private CanvasGroup panelGroup;
        private CanvasGroup[] cardGroups;
        private MenuButtonFeedback[] cardFeedbacks;
        private Coroutine fadeRoutine;
        private Coroutine dealRoutine;
        private AudioSource sfxSource;

        private void Awake()
        {
            if (draftSystem == null) draftSystem = FindFirstObjectByType<CardDraftSystem>();

            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;

            if (hoverClip == null) hoverClip = AudioManager.Instance?.GetClip(SfxId.UiHover);
            if (clickClip == null) clickClip = AudioManager.Instance?.GetClip(SfxId.UiClick);

            if (draftPanel != null)
            {
                panelGroup = draftPanel.GetComponent<CanvasGroup>();
                if (panelGroup == null) panelGroup = draftPanel.AddComponent<CanvasGroup>();
            }

            cardGroups = new CanvasGroup[cardButtons.Length];
            cardFeedbacks = new MenuButtonFeedback[cardButtons.Length];

            for (int i = 0; i < cardButtons.Length; i++)
            {
                int index = i;
                if (cardButtons[i] != null)
                {
                    cardButtons[i].onClick.AddListener(() => OnCardButtonClicked(index));

                    cardGroups[i] = cardButtons[i].GetComponent<CanvasGroup>();
                    if (cardGroups[i] == null) cardGroups[i] = cardButtons[i].gameObject.AddComponent<CanvasGroup>();

                    cardFeedbacks[i] = AttachFeedback(cardButtons[i]);
                }
            }
        }

        private void Start()
        {
            if (draftSystem != null)
            {
                draftSystem.OnCardsOffered += DisplayCards;
                draftSystem.OnCardSelected += HandleCardSelected;
            }

            // Hide draft panel initially if no cards are pending
            if (draftPanel != null && (draftSystem == null || draftSystem.CurrentOfferedCards.Count == 0))
            {
                draftPanel.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (draftSystem != null)
            {
                draftSystem.OnCardsOffered -= DisplayCards;
                draftSystem.OnCardSelected -= HandleCardSelected;
            }
        }

        public void DisplayCards(IReadOnlyList<DraftCard> cards)
        {
            if (draftPanel != null) draftPanel.SetActive(true);

            for (int i = 0; i < 3; i++)
            {
                bool hasCard = i < cards.Count && cards[i] != null;
                if (cardButtons[i] != null) cardButtons[i].gameObject.SetActive(hasCard);

                if (hasCard)
                {
                    var card = cards[i];
                    if (nameTexts[i] != null) nameTexts[i].text = card.operatorData.operatorName;
                    if (classTexts[i] != null) classTexts[i].text = card.operatorData.operatorClass.ToString();
                    if (rarityTexts[i] != null)
                    {
                        rarityTexts[i].text = $"{new string('★', (int)card.rarity)}";
                        rarityTexts[i].color = GetRarityColor((int)card.rarity);
                    }

                    if (cardFeedbacks[i] != null)
                    {
                        Color rc = GetRarityColor((int)card.rarity);
                        cardFeedbacks[i].glowColor = new Color(rc.r, rc.g, rc.b, 0.95f);
                    }

                    if (portraitImages[i] != null)
                    {
                        portraitImages[i].sprite = card.operatorData.portrait;
                        portraitImages[i].enabled = card.operatorData.portrait != null;
                    }
                }
            }

            if (isActiveAndEnabled)
            {
                if (fadeRoutine != null) StopCoroutine(fadeRoutine);
                fadeRoutine = StartCoroutine(FadePanelRoutine(true));

                if (dealRoutine != null) StopCoroutine(dealRoutine);
                dealRoutine = StartCoroutine(DealCardsRoutine());
            }
        }

        private void OnCardButtonClicked(int index)
        {
            if (draftSystem != null)
            {
                draftSystem.SelectCard(index, out _);
            }
        }

        private void HandleCardSelected(DraftCard card)
        {
            if (draftPanel == null) return;

            if (!isActiveAndEnabled || panelGroup == null || !draftPanel.activeSelf)
            {
                draftPanel.SetActive(false);
                return;
            }

            if (fadeRoutine != null) StopCoroutine(fadeRoutine);
            fadeRoutine = StartCoroutine(FadePanelRoutine(false));
        }

        // ============================
        // Animation
        // ============================

        private IEnumerator FadePanelRoutine(bool show)
        {
            float duration = show ? 0.25f : 0.18f;
            float from = show ? 0f : panelGroup.alpha;
            float to = show ? 1f : 0f;

            panelGroup.alpha = from;
            panelGroup.interactable = show;
            panelGroup.blocksRaycasts = show;

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                panelGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
                yield return null;
            }

            panelGroup.alpha = to;
            if (!show) draftPanel.SetActive(false);
            fadeRoutine = null;
        }

        // Cards pop in one after another (scale, fade, slight rotation).
        private IEnumerator DealCardsRoutine()
        {
            const float duration = 0.42f;
            const float stagger = 0.1f;
            int count = cardButtons.Length;
            float total = duration + stagger * (count - 1);

            for (int i = 0; i < count; i++) ApplyDeal(i, 0f);

            float t = 0f;
            while (t < total)
            {
                t += Time.unscaledDeltaTime;
                for (int i = 0; i < count; i++)
                {
                    ApplyDeal(i, Mathf.Clamp01((t - i * stagger) / duration));
                }

                yield return null;
            }

            for (int i = 0; i < count; i++) ApplyDeal(i, 1f);
            dealRoutine = null;
        }

        private void ApplyDeal(int index, float k)
        {
            if (cardButtons[index] == null || cardGroups[index] == null) return;

            float eased = EaseOutCubic(k);
            cardGroups[index].alpha = Mathf.Clamp01(k * 1.5f);
            if (cardFeedbacks[index] != null) cardFeedbacks[index].introScale = Mathf.LerpUnclamped(0.82f, 1f, EaseOutBack(k));
            cardButtons[index].transform.localRotation = Quaternion.Euler(0f, 0f, (1f - eased) * (index - 1) * -7f);
        }

        private MenuButtonFeedback AttachFeedback(Button button)
        {
            // The feedback component drives hover/press visuals, replacing the Button's color tint.
            button.transition = Selectable.Transition.None;

            var feedback = button.GetComponent<MenuButtonFeedback>();
            if (feedback == null) feedback = button.gameObject.AddComponent<MenuButtonFeedback>();

            feedback.hoverScale = 1.04f;
            feedback.respondToFocus = false;
            feedback.Hovered = () => PlaySfx(hoverClip, Random.Range(0.97f, 1.03f));
            feedback.Clicked = () => PlaySfx(clickClip, 1f);

            var image = button.GetComponent<Image>();
            if (image != null)
            {
                var outline = image.GetComponent<Outline>();
                if (outline == null) outline = image.gameObject.AddComponent<Outline>();
                outline.useGraphicAlpha = false;
                outline.effectDistance = new Vector2(2f, -2f);
                outline.effectColor = new Color(1f, 0.85f, 0.2f, 0f);

                feedback.glowOutline = outline;
                feedback.glowColor = new Color(1f, 0.85f, 0.2f, 0.95f);
            }

            return feedback;
        }

        private void PlaySfx(AudioClip clip, float pitch)
        {
            AudioManager.Instance?.PlaySfx(clip, sfxVolume, pitch);
        }

        private static Color GetRarityColor(int stars)
        {
            switch (stars)
            {
                case 1: return new Color(0.66f, 0.69f, 0.74f, 1f);
                case 2: return new Color(0.40f, 0.85f, 0.50f, 1f);
                case 3: return new Color(0.40f, 0.70f, 1.00f, 1f);
                case 4: return new Color(0.75f, 0.50f, 1.00f, 1f);
                default: return new Color(1.00f, 0.82f, 0.25f, 1f);
            }
        }

        private static float EaseOutCubic(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}