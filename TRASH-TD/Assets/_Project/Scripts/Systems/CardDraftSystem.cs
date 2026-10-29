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
        public int heatStacks;
        /// <summary>
        /// HP the operator had when it was retreated, restored exactly on redeploy. -1 = full HP
        /// (a fresh card). Nothing heals a benched card.
        /// </summary>
        public int savedHP;

        public DraftCard(OperatorData data, OperatorRarity rarity, int cooldownRoundsRemaining = 0, int heatStacks = 0, int savedHP = -1)
        {
            this.operatorData = data;
            this.rarity = rarity;
            this.cooldownRoundsRemaining = cooldownRoundsRemaining;
            this.heatStacks = heatStacks;
            this.savedHP = savedHP;
        }

        public bool HasSavedHP => savedHP >= 0;

        /// <summary>Saved HP as a fraction of this card's max HP (1 for a fresh card).</summary>
        public float GetHealthRatio()
        {
            if (!HasSavedHP || operatorData == null) return 1f;
            int maxHP = operatorData.GetScaledHP(rarity);
            return maxHP > 0 ? Mathf.Clamp01((float)savedHP / maxHP) : 1f;
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
        [SerializeField] private float star1Weight = 85f;
        [Tooltip("Relative weight for 2★ offers")]
        [SerializeField] private float star2Weight = 12f;
        [Tooltip("Relative weight for 3★ offers")]
        [SerializeField] private float star3Weight = 3f;

        private readonly List<DraftCard> currentOfferedCards = new List<DraftCard>(3);
        // Set by the tutorial: the next draft offers exactly these operators instead of random ones
        private readonly List<OperatorData> scriptedOffer = new List<OperatorData>(3);
        private int currentRound = 0;
        private int rerollsRemaining = 3;

        public const int STARTING_REROLLS = 3;

        public static CardDraftSystem Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public IReadOnlyList<DraftCard> CurrentOfferedCards => currentOfferedCards;
        public int CurrentRound => currentRound;
        public int RerollsRemaining => rerollsRemaining;
#if UNITY_EDITOR || DEBUG
        public IReadOnlyList<OperatorData> AvailableOperatorPool => availableOperatorPool;
#endif

        public event Action<IReadOnlyList<DraftCard>> OnCardsOffered;

        /// <summary>
        /// Makes the next <see cref="GenerateDraftOffer"/> offer exactly these operators, at base rarity, in this
        /// order (used once, then drafts are random again). The tutorial uses it to deal fixed hands.
        /// </summary>
        public void SetScriptedOffer(IEnumerable<OperatorData> operators)
        {
            scriptedOffer.Clear();
            if (operators == null) return;
            foreach (OperatorData op in operators)
            {
                if (op != null) scriptedOffer.Add(op);
            }
        }
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

#if UNITY_EDITOR || DEBUG
        public bool SetDeveloperOffer(OperatorData operatorData)
        {
            if (operatorData == null || availableOperatorPool == null ||
                !availableOperatorPool.Contains(operatorData))
                return false;

            currentOfferedCards.Clear();
            currentOfferedCards.Add(new DraftCard(operatorData, operatorData.baseRarity));
            OnCardsOffered?.Invoke(currentOfferedCards);
            return true;
        }
#endif

        /// <summary>
        /// Draw random eligible operator cards directly into the player's deck.
        /// Cards can be queued by PlayerDeck when all deck slots are occupied.
        /// If targetRarity is specified, draws only operators matching that base rarity.
        /// </summary>
        public int DrawRandomCardsToDeck(int count, OperatorRarity? targetRarity = null)
        {
            if (count <= 0) return 0;

            if (PlayerDeck.Instance == null)
            {
                Debug.LogError("CardDraftSystem: Cannot draw operator cards because no PlayerDeck is active.");
                return 0;
            }

            var eligibleOperators = new List<OperatorData>();
            if (availableOperatorPool == null)
            {
                Debug.LogError("CardDraftSystem: Cannot draw operator cards because the operator pool is unavailable.");
                return 0;
            }

            foreach (OperatorData op in availableOperatorPool)
            {
                if (op == null) continue;

                if (targetRarity.HasValue)
                {
                    if (op.baseRarity == targetRarity.Value)
                    {
                        eligibleOperators.Add(op);
                    }
                }
                else
                {
                    if (op.baseRarity >= OperatorRarity.Star1 && op.baseRarity <= OperatorRarity.Star3)
                    {
                        eligibleOperators.Add(op);
                    }
                }
            }

            if (eligibleOperators.Count == 0)
            {
                Debug.LogError($"CardDraftSystem: Cannot draw operator cards because the operator pool has no eligible operators{(targetRarity.HasValue ? $" for rarity {targetRarity.Value}" : "")}.");
                return 0;
            }

            int drawnCount = 0;
            for (int i = 0; i < count; i++)
            {
                OperatorData chosenOperator = eligibleOperators[UnityEngine.Random.Range(0, eligibleOperators.Count)];
                var card = new DraftCard(chosenOperator, chosenOperator.baseRarity);
                if (!PlayerDeck.Instance.AddReturnedCard(card))
                {
                    Debug.LogError($"CardDraftSystem: Failed to add drawn card for {chosenOperator.operatorName} to the player's deck.");
                    continue;
                }

                drawnCount++;
            }

            return drawnCount;
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

            if (scriptedOffer.Count > 0)
            {
                foreach (OperatorData op in scriptedOffer) currentOfferedCards.Add(new DraftCard(op, op.baseRarity));
                scriptedOffer.Clear();
                OnCardsOffered?.Invoke(currentOfferedCards);
                return currentOfferedCards;
            }

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

            return true;
        }
    }
}
