using System;
using System.Collections.Generic;
using UnityEngine;
using TrashTD.Data;
using TrashTD.Operators;
using TrashTD.UI;

namespace TrashTD.Systems
{
    /// <summary>
    /// Merges three matching copies of an operator into one of the next rarity tier.
    /// Ready deck cards count, plus at most one deployed copy, which is upgraded in place.
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

            // A card on redeploy cooldown (a retreated operator) waits until it's ready before merging,
            // so it never pulls ready copies into its cooldown.
            if (card.cooldownRoundsRemaining > 0) return;
            MergeCopies(deck, card.operatorData, card.rarity, null);
        }

        /// <summary>
        /// Merges a deck card whose redeploy cooldown just ended.
        /// </summary>
        public void ProcessReadiedCard(PlayerDeck deck, DraftCard card)
        {
            if (deck == null || card == null || card.operatorData == null || card.cooldownRoundsRemaining > 0) return;
            MergeCopies(deck, card.operatorData, card.rarity, null);
        }

        /// <summary>
        /// True if this card, not yet in the deck, would merge immediately on arrival.
        /// </summary>
        public bool WouldMergeIncomingCard(PlayerDeck deck, DraftCard card)
        {
            if (deck == null || card == null || card.operatorData == null || card.cooldownRoundsRemaining > 0) return false;
            if (card.rarity >= GetMaximumRarity(card.operatorData)) return false;

            int availableCopies = deck.GetReadyCopySlots(card.operatorData, card.rarity).Count +
                                  (FindDeployedCopy(card.operatorData, card.rarity) != null ? 1 : 0);
            return availableCopies >= COPIES_REQUIRED_FOR_UPGRADE - 1;
        }

        /// <summary>
        /// Merges a card straight from the draft without it taking a deck slot (used when the deck is full).
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
        /// Merges three copies into one of the next tier. Copies come from ready deck cards, the incoming card
        /// (if any) and at most one deployed operator. A deployed operator that takes part is upgraded where it
        /// stands, so merging never removes anything from the field.
        /// </summary>
        private static bool TryMergeOnce(
            PlayerDeck deck,
            OperatorData opData,
            OperatorRarity rarity,
            OperatorRarity nextRarity,
            DraftCard incomingCard)
        {
            OperatorBase fieldCopy = FindDeployedCopy(opData, rarity);
            int copiesFromDeck = COPIES_REQUIRED_FOR_UPGRADE - (incomingCard != null ? 1 : 0) - (fieldCopy != null ? 1 : 0);

            List<int> slots = deck.GetReadyCopySlots(opData, rarity);
            if (slots.Count < copiesFromDeck) return false;
            slots.RemoveRange(copiesFromDeck, slots.Count - copiesFromDeck);

            if (fieldCopy != null)
            {
                deck.ReplaceMergedCopies(slots, null);
                fieldCopy.UpgradeRarity(nextRarity);
                OperatorUpgradeBadge.Refresh(fieldCopy);
            }
            else
            {
                deck.ReplaceMergedCopies(slots, new DraftCard(opData, nextRarity));
            }

            return true;
        }

        private static OperatorBase FindDeployedCopy(OperatorData opData, OperatorRarity rarity)
        {
            if (OperatorManager.Instance == null) return null;

            foreach (OperatorBase op in OperatorManager.Instance.DeployedOperators)
            {
                if (op != null && op.IsDeployed && op.Data == opData && op.CurrentRarity == rarity) return op;
            }

            return null;
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
