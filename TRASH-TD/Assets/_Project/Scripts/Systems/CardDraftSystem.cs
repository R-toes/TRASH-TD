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
        public int cooldownRoundsRemaining;

        public DraftCard(OperatorData data, OperatorRarity rarity, int cooldownRoundsRemaining = 0)
        {
            this.operatorData = data;
            this.rarity = rarity;
            this.cooldownRoundsRemaining = cooldownRoundsRemaining;
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
        private int rerollsRemaining = 3;

        public const int STARTING_REROLLS = 3;

        public IReadOnlyList<DraftCard> CurrentOfferedCards => currentOfferedCards;
        public int CurrentRound => currentRound;
        public int RerollsRemaining => rerollsRemaining;

        public event Action<IReadOnlyList<DraftCard>> OnCardsOffered;
        public event Action<DraftCard> OnCardSelected;
        public event Action<int> OnRerollCountChanged;

        /// <summary>
        /// Notifies listeners that a card has been selected (e.g. from the player's deck for deployment).
        /// </summary>
        public void NotifyCardSelected(DraftCard card)
        {
            if (card != null)
            {
                OnCardSelected?.Invoke(card);
            }
        }

        /// <summary>
        /// Reset state for a new stage (reroll count, round counter).
        /// </summary>
        public void ResetForNewStage()
        {
            currentRound = 0;
            rerollsRemaining = STARTING_REROLLS;
            currentOfferedCards.Clear();
            OnRerollCountChanged?.Invoke(rerollsRemaining);
        }

        /// <summary>
        /// Reroll the current draft offer. Costs 1 reroll.
        /// Returns the new offer, or null if no rerolls remain.
        /// </summary>
        public List<DraftCard> RerollOffer()
        {
            if (rerollsRemaining <= 0) return null;

            rerollsRemaining--;
            OnRerollCountChanged?.Invoke(rerollsRemaining);

            // Don't increment round on reroll — it's the same draft round, just re-shuffled
            currentRound--; // GenerateDraftOffer will increment it back
            return GenerateDraftOffer();
        }

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

            var selectedClasses = new HashSet<OperatorClass>();
            for (int i = 0; i < 3; i++)
            {
                var candidatesByRarity = new Dictionary<OperatorRarity, List<OperatorData>>();
                foreach (var op in availableOperatorPool)
                {
                    if (op == null || selectedClasses.Contains(op.operatorClass)) continue;
                    if (op.baseRarity < OperatorRarity.Star1 || op.baseRarity > OperatorRarity.Star3) continue;

                    if (!candidatesByRarity.TryGetValue(op.baseRarity, out var candidates))
                    {
                        candidates = new List<OperatorData>();
                        candidatesByRarity.Add(op.baseRarity, candidates);
                    }
                    candidates.Add(op);
                }

                if (candidatesByRarity.Count == 0) break;

                var availableRarities = new List<OperatorRarity>(candidatesByRarity.Keys);
                OperatorRarity chosenRarity = RollDraftRarity(availableRarities);
                var rarityCandidates = candidatesByRarity[chosenRarity];
                OperatorData chosenOp = rarityCandidates[UnityEngine.Random.Range(0, rarityCandidates.Count)];

                selectedClasses.Add(chosenOp.operatorClass);
                currentOfferedCards.Add(new DraftCard(chosenOp, chosenOp.baseRarity));
            }

            OnCardsOffered?.Invoke(currentOfferedCards);
            return currentOfferedCards;
        }

        /// <summary>
        /// Selects a rarity tier using the configured weights, considering only tiers
        /// with eligible operators. The selected operator keeps its configured rarity.
        /// </summary>
        private OperatorRarity RollDraftRarity(IReadOnlyList<OperatorRarity> availableRarities)
        {
            float totalWeight = 0f;
            foreach (var rarity in availableRarities)
            {
                totalWeight += GetRarityWeight(rarity);
            }

            if (totalWeight <= 0f)
            {
                return availableRarities[UnityEngine.Random.Range(0, availableRarities.Count)];
            }

            float roll = UnityEngine.Random.Range(0f, totalWeight);
            foreach (var rarity in availableRarities)
            {
                float weight = GetRarityWeight(rarity);
                if (weight <= 0f) continue;
                if (roll < weight) return rarity;
                roll -= weight;
            }

            return availableRarities[availableRarities.Count - 1];
        }

        private float GetRarityWeight(OperatorRarity rarity)
        {
            return Mathf.Max(0f, rarity switch
            {
                OperatorRarity.Star1 => star1Weight,
                OperatorRarity.Star2 => star2Weight,
                OperatorRarity.Star3 => star3Weight,
                _ => 0f
            });
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
