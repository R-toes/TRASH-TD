using System;
using System.Collections.Generic;
using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Systems
{
    /// <summary>
    /// Tracks acquired duplicate copies of operators and manages rarity progression (GDD 1.6):
    /// - Rarities: 1★ to 5★.
    /// - Collecting 3 duplicate copies of a creature upgrades its rarity by one tier (e.g. 1★ -> 2★, up to 5★).
    /// </summary>
    public class RarityUpgradeSystem : MonoBehaviour
    {
        public static RarityUpgradeSystem Instance { get; private set; }

        public const int COPIES_REQUIRED_FOR_UPGRADE = 3;

        // Tracks duplicate copies owned: [OperatorName, [Rarity, Count]]
        private readonly Dictionary<string, Dictionary<OperatorRarity, int>> inventory =
            new Dictionary<string, Dictionary<OperatorRarity, int>>();

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
        /// Adds a drafted or acquired copy of an operator at a specific rarity tier.
        /// Automatically checks for 3 duplicates and upgrades if threshold reached.
        /// </summary>
        public void AddCardCopy(OperatorData opData, OperatorRarity rarity)
        {
            if (opData == null) return;

            string opName = opData.operatorName;
            if (!inventory.ContainsKey(opName))
            {
                inventory[opName] = new Dictionary<OperatorRarity, int>();
            }

            if (!inventory[opName].ContainsKey(rarity))
            {
                inventory[opName][rarity] = 0;
            }

            inventory[opName][rarity]++;
            UpdateHighestRarity(opData, rarity);

            OnCopyAdded?.Invoke(opData, rarity, inventory[opName][rarity]);

            // Check for upgrade: 3 duplicate copies trigger 1 tier upgrade
            CheckAndExecuteUpgrade(opData, rarity);
        }

        private void CheckAndExecuteUpgrade(OperatorData opData, OperatorRarity currentRarity)
        {
            string opName = opData.operatorName;

            // Maximum tier is 5★
            if (currentRarity >= OperatorRarity.Star5) return;

            while (inventory[opName].TryGetValue(currentRarity, out int count) && count >= COPIES_REQUIRED_FOR_UPGRADE)
            {
                inventory[opName][currentRarity] -= COPIES_REQUIRED_FOR_UPGRADE;

                OperatorRarity nextRarity = (OperatorRarity)((int)currentRarity + 1);
                if (!inventory[opName].ContainsKey(nextRarity))
                {
                    inventory[opName][nextRarity] = 0;
                }
                inventory[opName][nextRarity]++;

                UpdateHighestRarity(opData, nextRarity);
                OnOperatorUpgraded?.Invoke(opData, currentRarity, nextRarity);

                // Cascade upgrade if next tier now also has 3 copies
                currentRarity = nextRarity;
                if (currentRarity >= OperatorRarity.Star5) break;
            }
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
            if (inventory.TryGetValue(operatorName, out var rarities))
            {
                if (rarities.TryGetValue(rarity, out int count))
                {
                    return count;
                }
            }
            return 0;
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
            inventory.Clear();
            highestRarity.Clear();
        }
    }
}
