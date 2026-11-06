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

        public Vector2Int[] GetRangePattern()
        {
            if (operatorData == null) return null;
            return operatorData.GetRangePatternForRarity(rarity);
        }

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
    /// - Rarity odds step up as waves are cleared (<see cref="DraftOddsTable"/>).
    /// </summary>
    public class CardDraftSystem : MonoBehaviour
    {
        [Header("Draft Pool Configuration")]
        [Tooltip("Master pool of all available OperatorData assets eligible for drafting")]
        [SerializeField] private List<OperatorData> availableOperatorPool = new List<OperatorData>();

        [Header("Rarity Odds")]
        [Tooltip("Optional. Empty = Resources/DraftOddsTable, or the built-in default table without it.")]
        [SerializeField] private DraftOddsTable oddsTable;

        private readonly List<DraftCard> currentOfferedCards = new List<DraftCard>(3);
        // Set by the tutorial: the next draft offers exactly these operators instead of random ones
        private readonly List<OperatorData> scriptedOffer = new List<OperatorData>(3);
        private int currentRound = 0;
        private int rerollsRemaining = 3;
        private int wavesCleared;

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

        /// <summary>
        /// The first draft of a stage run (rerolls included). It deals 1★ operators only, so restarting the stage
        /// to fish for an early 3★ doesn't pay off.
        /// </summary>
        public bool IsOpeningDraft => currentRound == 1;
        public int RerollsRemaining => rerollsRemaining;

        /// <summary>Waves cleared so far in this stage run; picks the row of the odds table.</summary>
        public int WavesCleared => wavesCleared;

        // Loaded lazily: EditMode tests add this component without running Awake.
        public DraftOddsTable OddsTable
        {
            get
            {
                if (oddsTable == null) oddsTable = DraftOddsTable.Load();
                return oddsTable;
            }
        }

        /// <summary>Replaces the odds table (null = load the default again).</summary>
        public void SetOddsTable(DraftOddsTable table)
        {
            oddsTable = table;
        }

        /// <summary>Set before each draft (the draft overlay passes GameManager's wave index during Card Pick).</summary>
        public void SetWavesCleared(int count)
        {
            wavesCleared = Mathf.Max(0, count);
        }

        /// <summary>
        /// Odds the current offer was rolled with: 100% 1★ in the opening draft, otherwise the odds table's row
        /// for <see cref="WavesCleared"/>. Tiers with no eligible operator left are skipped when rolling, but
        /// these are the odds shown to the player.
        /// </summary>
        public DraftOdds GetCurrentOdds()
        {
            return IsOpeningDraft ? new DraftOdds(100f, 0f, 0f) : OddsTable.GetOdds(wavesCleared);
        }
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
            wavesCleared = 0;
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
        /// upgradeTiers adds bonus upgrade levels to the drawn cards (e.g. 1 = 1x upgraded card, 2 = 2x upgraded card).
        /// </summary>
        public int DrawRandomCardsToDeck(int count, int upgradeTiers = 0, OperatorRarity? targetRarity = null)
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
                OperatorRarity cardRarity = (OperatorRarity)Mathf.Clamp((int)chosenOperator.baseRarity + upgradeTiers, 1, 5);
                var card = new DraftCard(chosenOperator, cardRarity);
                if (!PlayerDeck.Instance.AddReturnedCard(card))
                {
                    Debug.LogError($"CardDraftSystem: Failed to add drawn card for {chosenOperator.operatorName} to the player's deck.");
                    continue;
                }

                drawnCount++;
            }

            return drawnCount;
        }

        public int DrawRandomCardsToDeck(int count, OperatorRarity targetRarity)
        {
            return DrawRandomCardsToDeck(count, 0, targetRarity);
        }

        /// <summary>
        /// Generates a new draft offering of 3 cards respecting all GDD constraints:
        /// - Exactly 3 cards
        /// - No duplicate classes
        /// - 1★ to 3★ only (1★ only in the opening draft, <see cref="IsOpeningDraft"/>)
        /// - No Medics or Specialists in the opening draft (<see cref="IsExcludedFromOpeningDraft"/>)
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
            OperatorRarity maxRarity = IsOpeningDraft ? OperatorRarity.Star1 : OperatorRarity.Star3;
            for (int i = 0; i < 3; i++)
            {
                var candidatesByRarity = new Dictionary<OperatorRarity, List<OperatorData>>();
                foreach (var op in availableOperatorPool)
                {
                    if (op == null || selectedClasses.Contains(op.operatorClass)) continue;
                    if (op.baseRarity < OperatorRarity.Star1 || op.baseRarity > maxRarity) continue;
                    if (IsOpeningDraft && IsExcludedFromOpeningDraft(op.operatorClass)) continue;

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
        /// Classes left out of the opening draft so the first picks are operators that can hold a lane on their own.
        /// </summary>
        public static bool IsExcludedFromOpeningDraft(OperatorClass operatorClass)
        {
            return operatorClass == OperatorClass.Medic || operatorClass == OperatorClass.Specialist;
        }

        /// <summary>
        /// Selects a rarity tier using the odds table's current row, considering only tiers
        /// with eligible operators. The selected operator keeps its configured rarity.
        /// </summary>
        private OperatorRarity RollDraftRarity(IReadOnlyList<OperatorRarity> availableRarities)
        {
            DraftOdds odds = OddsTable.GetOdds(wavesCleared);
            float totalWeight = 0f;
            foreach (var rarity in availableRarities)
            {
                totalWeight += odds.Get(rarity);
            }

            if (totalWeight <= 0f)
            {
                return availableRarities[UnityEngine.Random.Range(0, availableRarities.Count)];
            }

            float roll = UnityEngine.Random.Range(0f, totalWeight);
            foreach (var rarity in availableRarities)
            {
                float weight = odds.Get(rarity);
                if (weight <= 0f) continue;
                if (roll < weight) return rarity;
                roll -= weight;
            }

            return availableRarities[availableRarities.Count - 1];
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
