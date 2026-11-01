using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TrashTD.Development
{
    /// <summary>
    /// Hover and right-click callbacks for sandbox UI elements (hints, removing queued enemies).
    /// Implements no scroll or drag handlers, so scrolling over a tile still reaches its ScrollRect.
    /// </summary>
    public sealed class SandboxPointerEvents : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public Action Entered;
        public Action Exited;
        public Action RightClicked;

        public void OnPointerEnter(PointerEventData eventData) => Entered?.Invoke();

        public void OnPointerExit(PointerEventData eventData) => Exited?.Invoke();

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right) RightClicked?.Invoke();
        }

        private void OnDisable() => Exited?.Invoke();
    }
}
