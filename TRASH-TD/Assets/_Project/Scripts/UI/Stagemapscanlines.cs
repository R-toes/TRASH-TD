using UnityEngine;
using UnityEngine.UI;

namespace TrashTD.UI
{
    /// <summary>
    /// Optional CRT scanline overlay for the map. Put it on a RawImage that stretches over the map art
    /// (Raycast Target off). One scanline period is drawn per source-art pixel row, matching the baked CRT look.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class StageMapScanlines : MonoBehaviour
    {
        [Tooltip("Number of pixel rows in the source art (270 for the 480x270 map).")]
        [SerializeField] private int sourceRows = 270;
        [SerializeField, Range(0f, 1.5f)] private float strength = 1f;

        private Texture2D tex;

        private void Awake()
        {
            // 4 rows per source pixel: bright, slightly dim, dim, dark (alpha of black)
            float[] a = { 0f, 0.03f, 0.12f, 0.30f };
            tex = new Texture2D(1, 4, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
            for (int i = 0; i < 4; i++) tex.SetPixel(0, i, new Color(0f, 0f, 0f, Mathf.Clamp01(a[i] * strength)));
            tex.Apply();

            var raw = GetComponent<RawImage>();
            raw.texture = tex;
            raw.uvRect = new Rect(0f, 0f, 1f, sourceRows);
            raw.raycastTarget = false;
        }

        private void OnDestroy()
        {
            if (tex != null) Destroy(tex);
        }
    }
}