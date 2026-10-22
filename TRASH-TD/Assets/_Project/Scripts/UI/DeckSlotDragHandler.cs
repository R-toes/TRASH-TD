using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TrashTD.Data;
using TrashTD.Systems;

namespace TrashTD.UI
{
    public sealed class DeckSlotDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private int slotIndex;
        private Func<bool> canDrag;
        private Action<int, Vector2> updateDrag;
        private Action<int, Vector2> finishDrag;
        private DraftCard card;
        private Color slotColor;
        private GameObject dragVisual;
        private RectTransform dragVisualRect;
        private bool isDragging;

        public void Bind(int index, Func<bool> dragAllowed, Action<int, Vector2> onDragUpdated, Action<int, Vector2> onDragFinished)
        {
            slotIndex = index;
            canDrag = dragAllowed;
            updateDrag = onDragUpdated;
            finishDrag = onDragFinished;
        }

        public void SetCard(DraftCard deckCard, Color color)
        {
            card = deckCard;
            slotColor = color;
        }

        // Positions handed to the drag callbacks are in the game picture (see CrtDisplay.ScreenToPicture),
        // so they work directly with Camera.ScreenToWorldPoint and the canvas camera.
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (canDrag == null || !canDrag()) return;

            isDragging = true;
            CreateDragVisual(CrtDisplay.ScreenToPicture(eventData.position));
        }

        public void OnDrag(PointerEventData eventData)
        {
            Vector2 picturePosition = CrtDisplay.ScreenToPicture(eventData.position);
            if (isDragging)
            {
                updateDrag?.Invoke(slotIndex, picturePosition);
            }

            if (dragVisualRect != null)
            {
                PlaceDragVisual(picturePosition);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (isDragging)
            {
                finishDrag?.Invoke(slotIndex, CrtDisplay.ScreenToPicture(eventData.position));
            }

            isDragging = false;
            if (dragVisual != null)
            {
                Destroy(dragVisual);
            }
        }

        /// <summary>Centres the drag visual on a picture position, for overlay and camera canvases alike.</summary>
        private void PlaceDragVisual(Vector2 picturePosition)
        {
            Canvas canvas = dragVisualRect.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            Camera canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    (RectTransform)canvas.transform, picturePosition, canvasCamera, out Vector3 worldPosition))
            {
                dragVisualRect.position = worldPosition;
            }
        }

        private void CreateDragVisual(Vector2 screenPosition)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            dragVisual = new GameObject("DeckCardDragVisual", typeof(RectTransform), typeof(Image));
            dragVisual.transform.SetParent(canvas.transform, false);
            dragVisualRect = dragVisual.GetComponent<RectTransform>();
            dragVisualRect.sizeDelta = GetComponent<RectTransform>().rect.size;
            PlaceDragVisual(screenPosition);

            Image image = dragVisual.GetComponent<Image>();
            bool hasPortrait = card != null && card.operatorData != null && card.operatorData.portrait != null;
            image.sprite = hasPortrait ? card.operatorData.portrait : GetComponent<Image>().sprite;
            image.color = hasPortrait ? Color.white : slotColor;
            image.raycastTarget = false;

            var labelObject = new GameObject("OperatorName", typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(dragVisual.transform, false);
            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(4f, 4f);
            labelRect.offsetMax = new Vector2(-4f, -4f);
            var label = labelObject.GetComponent<Text>();
            label.text = card != null && card.operatorData != null ? card.operatorData.operatorName : string.Empty;
            label.font = UIFontHelper.GetPixelFont();
            label.fontSize = 14;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
        }
    }
}