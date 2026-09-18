using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TrashTD.Systems;

namespace TrashTD.UI
{
    /// <summary>
    /// UI panel displaying the 3 round-by-round draft choices (GDD 1.4.3 & 1.6).
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

        private void Awake()
        {
            if (draftSystem == null) draftSystem = FindFirstObjectByType<CardDraftSystem>();

            for (int i = 0; i < cardButtons.Length; i++)
            {
                int index = i;
                if (cardButtons[i] != null)
                {
                    cardButtons[i].onClick.AddListener(() => OnCardButtonClicked(index));
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
                    if (rarityTexts[i] != null) rarityTexts[i].text = $"{new string('★', (int)card.rarity)}";
                    if (portraitImages[i] != null)
                    {
                        portraitImages[i].sprite = card.operatorData.portrait;
                        portraitImages[i].enabled = card.operatorData.portrait != null;
                    }
                }
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
            if (draftPanel != null)
            {
                draftPanel.SetActive(false);
            }
        }
    }
}
