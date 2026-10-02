using System;
using System.Collections.Generic;
using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Systems
{
    /// <summary>
    /// Merges three matching deck cards into one card of the next rarity tier.
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

            OperatorData opData = card.operatorData;
            OperatorRarity currentRarity = card.rarity;
            OperatorRarity maxRarity = GetMaximumRarity(opData);
            UpdateHighestRarity(opData, currentRarity);
            OnCopyAdded?.Invoke(opData, currentRarity, deck.GetCopyCount(opData, currentRarity));

            while (currentRarity < maxRarity)
            {
                OperatorRarity nextRarity = (OperatorRarity)((int)currentRarity + 1);
                if (!deck.TryMergeCopies(opData, currentRarity, nextRarity, out _)) break;

                UpdateHighestRarity(opData, nextRarity);
                OnOperatorUpgraded?.Invoke(opData, currentRarity, nextRarity);
                currentRarity = nextRarity;
            }
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
