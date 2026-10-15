using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Systems
{
    /// <summary>
    /// Merges three matching deck cards into one card of the next rarity tier.
    /// Deployed operators never take part; recall one to let its card merge.
    /// </summary>
    public class RarityUpgradeSystem : MonoBehaviour
    {
        public static RarityUpgradeSystem Instance { get; private set; }

        public const int COPIES_REQUIRED_FOR_UPGRADE = 3;

        // Tracks highest unlocked rarity tier for each operator
        private readonly Dictionary<string, OperatorRarity> highestRarity =
            new Dictionary<string, OperatorRarity>();

        public event Action<OperatorData, OperatorRarity, int> OnCopyAdded; // (data, rarity, currentCopiesAtTier)
        public event Action<OperatorData, OperatorRarity, OperatorRarity> OnOperatorUpgraded; // (data, oldRarity, newRarity)

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

        /// <summary>
        /// Processes a card after it enters the player's deck.
        /// </summary>
        public void ProcessAddedCard(PlayerDeck deck, DraftCard card)
        {
            if (deck == null || card == null || card.operatorData == null) return;

            UpdateHighestRarity(card.operatorData, card.rarity);
            OnCopyAdded?.Invoke(card.operatorData, card.rarity, deck.GetCopyCount(card.operatorData, card.rarity));
            MergeCopies(deck, card.operatorData, card.rarity, null);
        }

        /// <summary>
        /// True if this card, not yet in the deck, would merge immediately on arrival.
        /// </summary>
        public bool WouldMergeIncomingCard(PlayerDeck deck, DraftCard card)
        {
            if (deck == null || card == null || card.operatorData == null) return false;
            if (card.rarity >= GetMaximumRarity(card.operatorData)) return false;

            return deck.GetCopySlots(card.operatorData, card.rarity).Count >= COPIES_REQUIRED_FOR_UPGRADE - 1;
        }

        /// <summary>
        /// Merges a card without it taking a deck slot (used when the deck is full).
        /// </summary>
        public bool TryMergeIncomingCard(PlayerDeck deck, DraftCard card)
        {
            if (!WouldMergeIncomingCard(deck, card)) return false;

            UpdateHighestRarity(card.operatorData, card.rarity);
            OnCopyAdded?.Invoke(card.operatorData, card.rarity, deck.GetCopyCount(card.operatorData, card.rarity) + 1);
            MergeCopies(deck, card.operatorData, card.rarity, card);
            return true;
        }

        private void MergeCopies(PlayerDeck deck, OperatorData opData, OperatorRarity rarity, DraftCard incomingCard)
        {
            OperatorRarity maxRarity = GetMaximumRarity(opData);
            while (rarity < maxRarity)
            {
                OperatorRarity nextRarity = (OperatorRarity)((int)rarity + 1);
                if (!TryMergeOnce(deck, opData, rarity, nextRarity, incomingCard)) break;

                incomingCard = null;
                UpdateHighestRarity(opData, nextRarity);
                OnOperatorUpgraded?.Invoke(opData, rarity, nextRarity);
                rarity = nextRarity;
            }
        }

        /// <summary>
        /// Merges three deck copies (counting the incoming card, if any) into one of the next tier.
        /// Copies closest to ready are used first, and the upgraded card keeps the longest redeploy
        /// cooldown among them, so a recalled operator still serves its full cooldown.
        /// </summary>
        private static bool TryMergeOnce(
            PlayerDeck deck,
            OperatorData opData,
            OperatorRarity rarity,
            OperatorRarity nextRarity,
            DraftCard incomingCard)
        {
            int copiesFromDeck = COPIES_REQUIRED_FOR_UPGRADE - (incomingCard != null ? 1 : 0);

            List<int> slots = deck.GetCopySlots(opData, rarity);
            if (slots.Count < copiesFromDeck) return false;
            slots = slots.OrderBy(slot => deck.GetCard(slot).cooldownRoundsRemaining).Take(copiesFromDeck).ToList();

            int cooldownRounds = incomingCard != null ? incomingCard.cooldownRoundsRemaining : 0;
            foreach (int slot in slots)
            {
                cooldownRounds = Mathf.Max(cooldownRounds, deck.GetCard(slot).cooldownRoundsRemaining);
            }

            deck.ReplaceMergedCopies(slots, new DraftCard(opData, nextRarity, cooldownRounds));
            return true;
        }

        private static OperatorRarity GetMaximumRarity(OperatorData opData)
        {
            int maxRarity = Mathf.Min((int)OperatorRarity.Star5, (int)opData.baseRarity + 2);
            return (OperatorRarity)maxRarity;
        }

        private void UpdateHighestRarity(OperatorData opData, OperatorRarity rarity)
        {
            string opName = opData.operatorName;
            if (!highestRarity.ContainsKey(opName) || rarity > highestRarity[opName])
            {
                highestRarity[opName] = rarity;
            }
        }

        /// <summary>
        /// Gets the number of copies held for a given operator at a specific rarity.
        /// </summary>
        public int GetCopyCount(string operatorName, OperatorRarity rarity)
        {
            return PlayerDeck.Instance != null ? PlayerDeck.Instance.GetCopyCount(operatorName, rarity) : 0;
        }

        /// <summary>
        /// Gets the highest unlocked rarity for a given operator.
        /// </summary>
        public OperatorRarity GetHighestRarity(OperatorData opData)
        {
            if (opData == null) return OperatorRarity.Star1;
            return highestRarity.TryGetValue(opData.operatorName, out var r) ? r : opData.baseRarity;
        }

        /// <summary>
        /// Resets inventory (for new run / stage).
        /// </summary>
        public void ResetInventory()
        {
            highestRarity.Clear();
        }
    }
}
