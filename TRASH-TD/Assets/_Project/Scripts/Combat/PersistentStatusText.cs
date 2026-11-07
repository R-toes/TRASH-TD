using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.UI;

namespace TrashTD.Combat
{
    /// <summary>
    /// Persistent overhead status text with a crisp 1-pixel black outline (retro pixel art style).
    /// Hovers above the target unit and stays visible until its duration ends / dismissed.
    /// Used for:
    /// - Oculoid Carrier: "-1 BLOCK" on affected operators until SlipperyEffect expires.
    /// - Hextension: "BOUND" on bound operators until bind duration ends.
    /// - Gooelist: "DUEL" on dueling Gooelist until unblocked / killed.
    /// </summary>
    public sealed class PersistentStatusText : MonoBehaviour
    {
        private Transform targetTransform;
        private float verticalOffset = 0.7f;
        private string statusId;
        private TextMesh mainText;
        private TextMesh[] outlineTexts;
        private Color baseColor;
        private float hoverTimer;

        public const int StatusSortingOrder = 75; // Higher layer than operators, units, combat badges (60), and health bars (30)
        private const int MainSortingOrder = 55;
        private const int OutlineSortingOrder = 54;
        private const float PixelOffset = 1f / 32f; // Exactly 1 map pixel in 32 PPU
        private const int DefaultFontSize = 24;
        private const float DefaultCharacterSize = 0.075f;

        private static readonly Vector3[] OutlineDirections = new Vector3[]
        {
            new Vector3(-PixelOffset, 0f, 0.001f),
            new Vector3(PixelOffset, 0f, 0.001f),
            new Vector3(0f, -PixelOffset, 0.001f),
            new Vector3(0f, PixelOffset, 0.001f),
            new Vector3(-PixelOffset, -PixelOffset, 0.001f),
            new Vector3(-PixelOffset, PixelOffset, 0.001f),
            new Vector3(PixelOffset, -PixelOffset, 0.001f),
            new Vector3(PixelOffset, PixelOffset, 0.001f),
        };

        public string StatusId => statusId;
        public string Text => mainText != null ? mainText.text : string.Empty;
        public Color TextColor => baseColor;

        public static PersistentStatusText Show(
            GameObject target,
            string text,
            Color color,
            float yOffset = 0.7f,
            string id = "Status")
        {
            if (target == null) return null;

            // Check if an existing PersistentStatusText with this ID already exists on target
            var existing = target.GetComponentsInChildren<PersistentStatusText>(true);
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != null && existing[i].statusId == id)
                {
                    existing[i].SetText(text, color);
                    existing[i].verticalOffset = yOffset;
                    return existing[i];
                }
            }

            var go = new GameObject($"PersistentStatusText_{id}");
            go.transform.SetParent(target.transform, false);

            var comp = go.AddComponent<PersistentStatusText>();
            comp.Initialize(target.transform, text, color, yOffset, id);
            return comp;
        }

        public static void Hide(GameObject target, string id = null)
        {
            if (target == null) return;
            var existing = target.GetComponentsInChildren<PersistentStatusText>(true);
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != null)
                {
                    if (string.IsNullOrEmpty(id) || existing[i].statusId == id)
                    {
                        existing[i].Dismiss();
                    }
                }
            }
        }

        public void Initialize(Transform target, string text, Color color, float yOffset, string id)
        {
            targetTransform = target;
            verticalOffset = yOffset;
            statusId = id;
            baseColor = color;

            SetupTextMesh(text, color);
            // Lift this status text out of the unit's SortingGroup and place it on the Default layer (above operators & units)
            GridSorting.SortAboveUnits(gameObject, StatusSortingOrder);
            UpdatePosition();
        }

        public void SetText(string text, Color color)
        {
            baseColor = color;
            if (mainText != null)
            {
                mainText.text = text;
                mainText.color = color;
            }
            if (outlineTexts != null)
            {
                for (int i = 0; i < outlineTexts.Length; i++)
                {
                    if (outlineTexts[i] != null)
                    {
                        outlineTexts[i].text = text;
                    }
                }
            }
        }

        private void SetupTextMesh(string text, Color color)
        {
            // 1. Black outline text meshes (8 cardinal + diagonal offsets)
            outlineTexts = new TextMesh[OutlineDirections.Length];
            for (int i = 0; i < OutlineDirections.Length; i++)
            {
                var outlineGo = new GameObject($"Outline_{i}");
                outlineGo.transform.SetParent(transform, false);
                outlineGo.transform.localPosition = OutlineDirections[i];

                var ot = outlineGo.AddComponent<TextMesh>();
                ot.text = text;
                UIFontHelper.Apply(ot, FontStyle.Bold);
                ot.fontSize = DefaultFontSize;
                ot.characterSize = DefaultCharacterSize;
                ot.anchor = TextAnchor.MiddleCenter;
                ot.alignment = TextAlignment.Center;
                ot.fontStyle = FontStyle.Bold;
                ot.color = Color.black;

                var mr = outlineGo.GetComponent<MeshRenderer>();
                mr.sortingLayerID = GridSorting.DefaultLayerId;
                mr.sortingOrder = OutlineSortingOrder;

                outlineTexts[i] = ot;
            }

            // 2. Main colored text mesh in center
            var mainGo = new GameObject("MainText");
            mainGo.transform.SetParent(transform, false);
            mainGo.transform.localPosition = Vector3.zero;

            mainText = mainGo.AddComponent<TextMesh>();
            mainText.text = text;
            UIFontHelper.Apply(mainText, FontStyle.Bold);
            mainText.fontSize = DefaultFontSize;
            mainText.characterSize = DefaultCharacterSize;
            mainText.anchor = TextAnchor.MiddleCenter;
            mainText.alignment = TextAlignment.Center;
            mainText.fontStyle = FontStyle.Bold;
            mainText.color = color;

            var mainMr = mainGo.GetComponent<MeshRenderer>();
            mainMr.sortingLayerID = GridSorting.DefaultLayerId;
            mainMr.sortingOrder = MainSortingOrder;
        }

        private void LateUpdate()
        {
            if (targetTransform == null)
            {
                Destroy(gameObject);
                return;
            }

            hoverTimer += Time.deltaTime;
            UpdatePosition();
        }

        private void UpdatePosition()
        {
            if (targetTransform == null) return;

            // Subtle 1-pixel hover bob
            float bob = Mathf.Sin(hoverTimer * 3.5f) * (0.5f * PixelOffset);
            transform.position = targetTransform.position + new Vector3(0f, verticalOffset + bob, 0f);
        }

        public void Dismiss()
        {
            Destroy(gameObject);
        }
    }
}
