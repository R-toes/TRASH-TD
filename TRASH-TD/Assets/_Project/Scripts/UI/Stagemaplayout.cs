using UnityEngine;

namespace TrashTD.UI
{
    /// <summary>
    /// Put this on the map art's RectTransform. It scales and positions every StageMapNode from the
    /// map's current size, so the dots stay glued to their locations at any screen size.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class StageMapLayout : MonoBehaviour
    {
        [Tooltip("Reference map width for node scaling (1920 for 1080p canvas reference).")]
        [SerializeField] private float nativeMapWidth = 1920f;
        [Tooltip("Optional scale multiplier for node size.")]
        [SerializeField] private float nodeScaleMultiplier = 1f;
        [Tooltip("Leave empty to auto-find every StageMapNode under this object.")]
        [SerializeField] private StageMapNode[] nodes;

        /// <summary>
        /// Divides the marker scale while the map is zoomed (set by StageMapNavigator), so markers grow less than
        /// the map. 1 = markers scale with the map.
        /// </summary>
        public float ZoomCompensation { get; set; } = 1f;

        private void OnEnable() => Apply();

        private void OnRectTransformDimensionsChange()
        {
            if (isActiveAndEnabled) Apply();
        }

        [ContextMenu("Apply Layout")]
        public void Apply()
        {
            if (nodes == null || nodes.Length == 0) nodes = GetComponentsInChildren<StageMapNode>(true);

            float width = ((RectTransform)transform).rect.width;
            if (width <= 1f) width = 1920f;
            float mapWidth = nativeMapWidth > 600f ? nativeMapWidth : 1920f;
            float pixelScale = (width / mapWidth) * nodeScaleMultiplier / Mathf.Max(0.01f, ZoomCompensation);
            if (pixelScale <= 0.01f) pixelScale = 1f;

            foreach (var n in nodes)
            {
                if (n != null) n.ApplyLayout(pixelScale);
            }
        }
    }
}