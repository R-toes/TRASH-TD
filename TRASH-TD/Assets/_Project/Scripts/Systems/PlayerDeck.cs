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
        /// Add a draft card to the deck. Returns false if deck is full.
        /// </summary>
        public bool AddCard(DraftCard card)
        {
            if (card == null || IsFull) return false;

            for (int i = 0; i < deckSlots.Count; i++)
            {
                if (deckSlots[i] != null) continue;

                deckSlots[i] = card;
                OnCardAdded?.Invoke(card);
                OnDeckChanged?.Invoke(deckSlots);
                return true;
            }

            deckSlots.Add(card);
            OnCardAdded?.Invoke(card);
            OnDeckChanged?.Invoke(deckSlots);
            return true;
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
            OnDeckChanged?.Invoke(deckSlots);
        }
    }
}
