using System;
using System.Collections.Generic;
using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Systems
{
    /// <summary>
    /// Represents an offered draft card option.
    /// </summary>
    [Serializable]
    public class DraftCard
    {
        public OperatorData operatorData;
        public OperatorRarity rarity;

        public DraftCard(OperatorData data, OperatorRarity rarity)
        {
            this.operatorData = data;
            this.rarity = rarity;
        }
    }

    /// <summary>
    /// Implements the Card-Draft Deployment System (GDD 1.4.3 & 1.6):
    /// - Every round offers exactly 3 cards.
    /// - No two cards in the same offer share the same class (Guard, Defender, Sniper, Caster, Medic).
    /// - Draft pool only offers 1★, 2★, or 3★ base creatures (4★ and 5★ are obtained via upgrade only).
    /// </summary>
    public class CardDraftSystem : MonoBehaviour
    {
        [Header("Draft Pool Configuration")]
        [Tooltip("Master pool of all available OperatorData assets eligible for drafting")]
        [SerializeField] private List<OperatorData> availableOperatorPool = new List<OperatorData>();

        [Header("Rarity Offer Weights")]
        [Tooltip("Relative weight for 1★ offers")]
        [SerializeField] private float star1Weight = 60f;
        [Tooltip("Relative weight for 2★ offers")]
        [SerializeField] private float star2Weight = 30f;
        [Tooltip("Relative weight for 3★ offers")]
        [SerializeField] private float star3Weight = 10f;

        private readonly List<DraftCard> currentOfferedCards = new List<DraftCard>(3);
        private int currentRound = 0;

        public IReadOnlyList<DraftCard> CurrentOfferedCards => currentOfferedCards;
        public int CurrentRound => currentRound;

        public event Action<IReadOnlyList<DraftCard>> OnCardsOffered;
        public event Action<DraftCard> OnCardSelected;

        /// <summary>
        /// Populate the draft pool (can be set via code or inspector).
        /// </summary>
        public void SetOperatorPool(IEnumerable<OperatorData> pool)
        {
            availableOperatorPool.Clear();
            if (pool != null)
            {
                availableOperatorPool.AddRange(pool);
            }
        }

        /// <summary>
        /// Generates a new draft offering of 3 cards respecting all GDD constraints:
        /// - Exactly 3 cards
        /// - No duplicate classes
        /// - 1★ to 3★ only
        /// </summary>
        public List<DraftCard> GenerateDraftOffer()
        {
            currentOfferedCards.Clear();
            currentRound++;

            if (availableOperatorPool == null || availableOperatorPool.Count == 0)
            {
                Debug.LogWarning("CardDraftSystem: Operator pool is empty! Cannot generate draft offer.");
                OnCardsOffered?.Invoke(currentOfferedCards);
                return currentOfferedCards;
            }

            // Shuffle or group candidates by class
            var classBuckets = new Dictionary<OperatorClass, List<OperatorData>>();
            foreach (var op in availableOperatorPool)
            {
                if (op == null) continue;
                if (!classBuckets.ContainsKey(op.operatorClass))
                {
                    classBuckets[op.operatorClass] = new List<OperatorData>();
                }
                classBuckets[op.operatorClass].Add(op);
            }

            // Pick up to 3 distinct classes randomly
            var availableClasses = new List<OperatorClass>(classBuckets.Keys);
            ShuffleList(availableClasses);

            int cardsToPick = Mathf.Min(3, availableClasses.Count);
            for (int i = 0; i < cardsToPick; i++)
            {
                OperatorClass chosenClass = availableClasses[i];
                var classList = classBuckets[chosenClass];
                OperatorData chosenOp = classList[UnityEngine.Random.Range(0, classList.Count)];

                OperatorRarity offeredRarity = RollDraftRarity(chosenOp.baseRarity);
                currentOfferedCards.Add(new DraftCard(chosenOp, offeredRarity));
            }

            OnCardsOffered?.Invoke(currentOfferedCards);
            return currentOfferedCards;
        }

        /// <summary>
        /// Rolls a draft rarity between 1★ and 3★ (GDD 1.6).
        /// Respects weights while ensuring result is never > 3★.
        /// </summary>
        private OperatorRarity RollDraftRarity(OperatorRarity baseRarity)
        {
            float total = star1Weight + star2Weight + star3Weight;
            float roll = UnityEngine.Random.Range(0f, total);

            if (roll < star1Weight) return OperatorRarity.Star1;
            if (roll < star1Weight + star2Weight) return OperatorRarity.Star2;
            return OperatorRarity.Star3;
        }

        /// <summary>
        /// Player selects one of the 3 offered cards.
        /// </summary>
        public bool SelectCard(int cardIndex, out DraftCard selectedCard)
        {
            selectedCard = null;
            if (cardIndex < 0 || cardIndex >= currentOfferedCards.Count) return false;

            selectedCard = currentOfferedCards[cardIndex];
            OnCardSelected?.Invoke(selectedCard);

            // Pass to RarityUpgradeSystem to track duplicate/upgrade progression
            if (RarityUpgradeSystem.Instance != null)
            {
                RarityUpgradeSystem.Instance.AddCardCopy(selectedCard.operatorData, selectedCard.rarity);
            }

            return true;
        }

        private static void ShuffleList<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int rnd = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[rnd]) = (list[rnd], list[i]);
            }
        }
    }
}
