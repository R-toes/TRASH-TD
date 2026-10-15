using System;
using System.Collections.Generic;
using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Systems
{
    /// <summary>
    /// Tracks the player's deck of chosen operators (up to 8 slots).
    /// Cards are added from the draft phase and consumed when deployed onto the grid.
    /// This represents the player's "hand" — available operators ready to be placed.
    /// </summary>
    public class PlayerDeck : MonoBehaviour
    {
        public static PlayerDeck Instance { get; private set; }

        public const int MAX_DECK_SIZE = 8;

        private readonly List<DraftCard> deckSlots = new List<DraftCard>(MAX_DECK_SIZE);
        private readonly Queue<DraftCard> pendingReturnedCards = new Queue<DraftCard>();

        public IReadOnlyList<DraftCard> DeckSlots => deckSlots;
        public int CardCount
        {
            get
            {
                int count = 0;
                foreach (var card in deckSlots)
                {
                    if (card != null) count++;
                }
                return count;
            }
        }
        public bool IsFull => CardCount >= MAX_DECK_SIZE;

        public event Action<IReadOnlyList<DraftCard>> OnDeckChanged;
        public event Action<DraftCard> OnCardAdded;
        public event Action<int> OnCardRemoved; // slot index
        public event Action<DraftCard> OnCardSelectedForDeployment;

        /// <summary>
        /// Select a card from the deck for deployment onto the grid.
        /// </summary>
        public void SelectCardForDeployment(int slotIndex)
        {
            var card = GetCard(slotIndex);
            if (card != null)
            {
                OnCardSelectedForDeployment?.Invoke(card);
            }
        }

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
        /// Add a draft card to the deck. A full deck only accepts a card that merges on arrival,
        /// since the merge frees the slot it would need. Returns false if the card can't be taken.
        /// </summary>
        public bool AddCard(DraftCard card)
        {
            if (card == null) return false;
            if (IsFull)
            {
                return RarityUpgradeSystem.Instance != null &&
                       RarityUpgradeSystem.Instance.TryMergeIncomingCard(this, card);
            }

            int emptySlot = -1;
            for (int i = 0; i < deckSlots.Count; i++)
            {
                if (deckSlots[i] != null) continue;

                emptySlot = i;
                break;
            }

            if (emptySlot >= 0)
            {
                deckSlots[emptySlot] = card;
            }
            else
            {
                deckSlots.Add(card);
            }

            OnCardAdded?.Invoke(card);
            OnDeckChanged?.Invoke(deckSlots);
            RarityUpgradeSystem.Instance?.ProcessAddedCard(this, card);
            return true;
        }

        public int GetCopyCount(OperatorData operatorData, OperatorRarity rarity)
        {
            if (operatorData == null) return 0;

            int count = 0;
            foreach (DraftCard card in deckSlots)
            {
                if (card != null && card.operatorData == operatorData && card.rarity == rarity)
                {
                    count++;
                }
            }

            return count;
        }

        public int GetCopyCount(string operatorName, OperatorRarity rarity)
        {
            if (string.IsNullOrEmpty(operatorName)) return 0;

            int count = 0;
            foreach (DraftCard card in deckSlots)
            {
                if (card != null && card.operatorData != null &&
                    card.operatorData.operatorName == operatorName && card.rarity == rarity)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// True if the card can be taken: the deck has a free slot, or the card merges on arrival.
        /// </summary>
        public bool CanAcceptCard(DraftCard card)
        {
            if (card == null) return false;
            return !IsFull ||
                   (RarityUpgradeSystem.Instance != null && RarityUpgradeSystem.Instance.WouldMergeIncomingCard(this, card));
        }

        /// <summary>
        /// Slots holding copies of the operator at this rarity that are off redeploy cooldown.
        /// Only these can take part in a rarity merge.
        /// </summary>
        public List<int> GetReadyCopySlots(OperatorData operatorData, OperatorRarity rarity)
        {
            var slots = new List<int>();
            if (operatorData == null) return slots;

            for (int i = 0; i < deckSlots.Count; i++)
            {
                DraftCard card = deckSlots[i];
                if (card != null && card.operatorData == operatorData && card.rarity == rarity &&
                    card.cooldownRoundsRemaining <= 0)
                {
                    slots.Add(i);
                }
            }

            return slots;
        }

        /// <summary>
        /// Removes the merged copies in the given slots. The upgraded card, if any, takes the first freed slot;
        /// it is null when the upgrade went to a deployed operator instead.
        /// </summary>
        public void ReplaceMergedCopies(IReadOnlyList<int> slots, DraftCard upgradedCard)
        {
            if (slots == null || slots.Count == 0) return;

            foreach (int slot in slots)
            {
                deckSlots[slot] = null;
                OnCardRemoved?.Invoke(slot);
            }

            if (upgradedCard != null)
            {
                deckSlots[slots[0]] = upgradedCard;
                OnCardAdded?.Invoke(upgradedCard);
            }

            foreach (int slot in slots)
            {
                FillVacatedSlotWithReturnedCard(slot);
            }

            OnDeckChanged?.Invoke(deckSlots);
        }

        public bool AddReturnedCard(DraftCard card)
        {
            if (card == null) return false;

            if (IsFull)
            {
                pendingReturnedCards.Enqueue(card);
                return true;
            }

            return AddCard(card);
        }

        /// <summary>
        /// Remove a card from the deck at the given slot index.
        /// Used when the operator is deployed onto the grid.
        /// </summary>
        public DraftCard RemoveCard(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= deckSlots.Count) return null;

            var card = deckSlots[slotIndex];
            deckSlots[slotIndex] = null;
            OnCardRemoved?.Invoke(slotIndex);
            FillVacatedSlotWithReturnedCard(slotIndex);
            OnDeckChanged?.Invoke(deckSlots);
            return card;
        }

        /// <summary>
        /// Move a card to another occupied deck position.
        /// </summary>
        public bool MoveCard(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= deckSlots.Count) return false;
            if (toIndex < 0 || toIndex >= MAX_DECK_SIZE) return false;

            DraftCard card = deckSlots[fromIndex];
            if (card == null) return false;
            if (fromIndex == toIndex) return true;

            while (deckSlots.Count <= toIndex)
            {
                deckSlots.Add(null);
            }

            if (deckSlots[toIndex] == null)
            {
                deckSlots[fromIndex] = null;
                deckSlots[toIndex] = card;
            }
            else if (fromIndex < toIndex)
            {
                for (int i = fromIndex; i < toIndex; i++)
                {
                    deckSlots[i] = deckSlots[i + 1];
                }
                deckSlots[toIndex] = card;
            }
            else
            {
                for (int i = fromIndex; i > toIndex; i--)
                {
                    deckSlots[i] = deckSlots[i - 1];
                }
                deckSlots[toIndex] = card;
            }

            OnDeckChanged?.Invoke(deckSlots);
            return true;
        }

        /// <summary>
        /// Remove a specific card instance from the deck (e.g. after deployment).
        /// </summary>
        public bool RemoveCard(DraftCard card)
        {
            if (card == null) return false;
            int index = deckSlots.IndexOf(card);
            if (index >= 0)
            {
                deckSlots[index] = null;
                OnCardRemoved?.Invoke(index);
                FillVacatedSlotWithReturnedCard(index);
                OnDeckChanged?.Invoke(deckSlots);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Get the card at a specific slot.
        /// </summary>
        public DraftCard GetCard(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= deckSlots.Count) return null;
            return deckSlots[slotIndex];
        }

        /// <summary>
        /// Clear the entire deck (e.g., stage reset).
        /// </summary>
        public void ClearDeck()
        {
            deckSlots.Clear();
            pendingReturnedCards.Clear();
            OnDeckChanged?.Invoke(deckSlots);
        }

        public void AdvanceRedeployCooldownsOneRound()
        {
            bool changed = false;
            var readiedCards = new List<DraftCard>();
            foreach (DraftCard card in deckSlots)
            {
                if (card == null || card.cooldownRoundsRemaining <= 0) continue;
                card.cooldownRoundsRemaining--;
                if (card.cooldownRoundsRemaining <= 0) readiedCards.Add(card);
                changed = true;
            }

            int pendingCount = pendingReturnedCards.Count;
            for (int i = 0; i < pendingCount; i++)
            {
                DraftCard card = pendingReturnedCards.Dequeue();
                if (card.cooldownRoundsRemaining > 0)
                {
                    card.cooldownRoundsRemaining--;
                    changed = true;
                }
                pendingReturnedCards.Enqueue(card);
            }

            if (changed) OnDeckChanged?.Invoke(deckSlots);

            // Cards skip merging while on cooldown, so check them again now that they're ready.
            foreach (DraftCard card in readiedCards)
            {
                if (deckSlots.Contains(card)) RarityUpgradeSystem.Instance?.ProcessReadiedCard(this, card);
            }
        }

        private void FillVacatedSlotWithReturnedCard(int preferredSlot)
        {
            if (pendingReturnedCards.Count == 0 || CardCount >= MAX_DECK_SIZE) return;

            int slot = preferredSlot >= 0 && preferredSlot < deckSlots.Count && deckSlots[preferredSlot] == null
                ? preferredSlot
                : deckSlots.FindIndex(existingCard => existingCard == null);

            if (slot < 0)
            {
                if (deckSlots.Count >= MAX_DECK_SIZE) return;
                slot = deckSlots.Count;
                deckSlots.Add(null);
            }

            DraftCard returnedCard = pendingReturnedCards.Dequeue();
            deckSlots[slot] = returnedCard;
            OnCardAdded?.Invoke(returnedCard);
            RarityUpgradeSystem.Instance?.ProcessAddedCard(this, returnedCard);
        }
    }
}
