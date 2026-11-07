using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.UI;

namespace TrashTD.Combat
{
    /// <summary>
    /// Persistent overhead status tag that stays above a unit until dismissed.
    /// Used for:
    /// - Oculoid Carrier: "-1 BLOCK" on affected operators until SlipperyEffect expires.
    /// - Gooelist: "DUEL" on a dueling Gooelist until it is unblocked or killed.
    ///
    /// Look: a pixel-art tag (dark plate with a 1 px accent border and a 1 px accent tab underneath)
    /// holding the text with a 1 px drop shadow. It pops in, bobs 1 art pixel and squashes out on dismiss.
    /// Cost: 2 TextMeshes + 3 sprites per tag, one shared 1-texel sprite, and a static registry instead of
    /// GetComponentsInChildren lookups, so Show / Hide are cheap even when called often.
    /// </summary>
    public sealed class PersistentStatusText : MonoBehaviour
    {
        public const int StatusSortingOrder = 75; // Above operators, units, combat badges (60) and health bars (30)

        private const float ArtPixel = 1f / 32f;
        private const int FontSize = 24;
        private const float CharacterSize = 0.0521f;            // 1 em = 4 art pixels: "-1 BLOCK" is one tile wide
        private const float EmWorld = FontSize * CharacterSize * 0.1f;
        private const float PadX = 3f * ArtPixel;
        private const float PadY = 2f * ArtPixel;
        private const float PopInTime = 0.18f;
        private const float PopOutTime = 0.1f;

        private static readonly Color PlateColor = new Color(0.06f, 0.05f, 0.09f, 0.88f);
        private static readonly Color ShadowColor = new Color(0.02f, 0.01f, 0.04f, 1f);
        private static readonly Color DefaultAccent = new Color(1f, 0.78f, 0.3f);
        private static readonly List<PersistentStatusText> Registry = new List<PersistentStatusText>();
        private static Sprite texelSprite;

        private Transform targetTransform;
        private float verticalOffset = 0.7f;
        private string statusId;
        private TextMesh mainText;
        private TextMesh shadowText;
        private SpriteRenderer border;
        private SpriteRenderer plate;
        private SpriteRenderer tab;
        private Color baseColor;
        private Color accentColor;
        private float age;
        private float closeTimer = -1f;

        public string StatusId => statusId;
        public string Text => mainText != null ? mainText.text : string.Empty;
        public Color TextColor => baseColor;
        public Color AccentColor => accentColor;
        public bool IsClosing => closeTimer >= 0f;

        /// <summary>
        /// Shows (or updates) the tag with this <paramref name="id"/> on <paramref name="target"/>.
        /// <paramref name="accent"/> colours the border and tab; it defaults to amber.
        /// </summary>
        public static PersistentStatusText Show(
            GameObject target,
            string text,
            Color color,
            float yOffset = 0.7f,
            string id = "Status",
            Color? accent = null)
        {
            if (target == null) return null;

            PersistentStatusText existing = Find(target.transform, id);
            if (existing != null)
            {
                existing.verticalOffset = yOffset;
                existing.SetText(text, color);
                if (accent.HasValue) existing.SetAccent(accent.Value);
                return existing;
            }

            var go = new GameObject($"PersistentStatusText_{id}");
            go.transform.SetParent(target.transform, false);

            var comp = go.AddComponent<PersistentStatusText>();
            comp.Initialize(target.transform, text, color, yOffset, id, accent ?? DefaultAccent);
            return comp;
        }

        public static void Hide(GameObject target, string id = null)
        {
            if (target == null) return;
            Transform targetTransform = target.transform;
            for (int i = Registry.Count - 1; i >= 0; i--)
            {
                PersistentStatusText status = Registry[i];
                if (status == null)
                {
                    Registry.RemoveAt(i);
                    continue;
                }
                if (status.targetTransform == targetTransform &&
                    (string.IsNullOrEmpty(id) || status.statusId == id))
                {
                    status.Dismiss();
                }
            }
        }

        /// <summary>The open (not closing) tag with this id on the target, or null.</summary>
        public static PersistentStatusText Find(Transform target, string id)
        {
            for (int i = Registry.Count - 1; i >= 0; i--)
            {
                PersistentStatusText status = Registry[i];
                if (status == null)
                {
                    Registry.RemoveAt(i);
                    continue;
                }
                if (status.targetTransform == target && status.statusId == id) return status;
            }
            return null;
        }

        public void Initialize(Transform target, string text, Color color, float yOffset, string id)
        {
            Initialize(target, text, color, yOffset, id, DefaultAccent);
        }

        public void Initialize(Transform target, string text, Color color, float yOffset, string id, Color accent)
        {
            targetTransform = target;
            verticalOffset = yOffset;
            statusId = id;

            BuildVisuals();
            // Lift the tag out of the unit's SortingGroup onto the Default layer, above every unit.
            GridSorting.SortAboveUnits(gameObject, StatusSortingOrder);
            SetAccent(accent);
            SetText(text, color);
            Registry.Add(this);
            UpdateTransform();
        }

        public void SetText(string text, Color color)
        {
            baseColor = color;
            if (mainText == null) return;

            mainText.text = text;
            mainText.color = color;
            shadowText.text = text;
            LayoutPlate(text);
        }

        public void SetAccent(Color accent)
        {
            accentColor = accent;
            accent.a = 1f;
            if (border != null) border.color = accent;
            if (tab != null) tab.color = accent;
        }

        /// <summary>Squashes the tag out and destroys it. It stops counting as shown at once.</summary>
        public void Dismiss()
        {
            Registry.Remove(this);
            if (closeTimer >= 0f) return;

            if (!isActiveAndEnabled)
            {
                Destroy(gameObject);
                return;
            }
            closeTimer = 0f;
        }

        private void OnDestroy()
        {
            Registry.Remove(this);
        }

        private void LateUpdate()
        {
            if (targetTransform == null)
            {
                Destroy(gameObject);
                return;
            }

            age += Time.deltaTime;
            if (closeTimer >= 0f)
            {
                closeTimer += Time.deltaTime;
                if (closeTimer >= PopOutTime)
                {
                    Destroy(gameObject);
                    return;
                }
            }
            UpdateTransform();
        }

        private void UpdateTransform()
        {
            if (targetTransform == null) return;

            // 1 art-pixel hover bob, snapped to the art grid.
            float bob = Mathf.Sin(age * 3.5f) > 0f ? ArtPixel : 0f;
            Vector3 position = targetTransform.position + new Vector3(0f, verticalOffset + bob, 0f);
            position.x = Mathf.Round(position.x / ArtPixel) * ArtPixel;
            position.y = Mathf.Round(position.y / ArtPixel) * ArtPixel;
            transform.position = position;

            float scaleX = 1f;
            float scaleY = 1f;
            if (closeTimer >= 0f)
            {
                float e = 1f - Mathf.Clamp01(closeTimer / PopOutTime);
                scaleY = e;
                scaleX = 1f + (1f - e) * 0.3f;
            }
            else if (age < PopInTime)
            {
                float t = age / PopInTime;
                float pop = t < 0.55f ? Mathf.Lerp(0.2f, 1.15f, t / 0.55f) : Mathf.Lerp(1.15f, 1f, (t - 0.55f) / 0.45f);
                scaleX = pop;
                scaleY = pop;
            }

            // Undo the parent's scale (2x enemies) and facing flip so the tag is always the same size.
            Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            float px = Mathf.Approximately(parentScale.x, 0f) ? 1f : parentScale.x;
            float py = Mathf.Approximately(parentScale.y, 0f) ? 1f : parentScale.y;
            transform.localScale = new Vector3(scaleX / px, scaleY / py, 1f);
        }

        private void BuildVisuals()
        {
            Sprite sprite = GetTexelSprite();
            border = CreateSprite("Border", sprite, 0);
            tab = CreateSprite("Tab", sprite, 0);
            plate = CreateSprite("Plate", sprite, 1);
            plate.color = PlateColor;

            shadowText = CreateText("Shadow", new Vector3(0f, -ArtPixel, 0.001f), 2);
            shadowText.color = ShadowColor;
            mainText = CreateText("MainText", Vector3.zero, 3);
        }

        private void LayoutPlate(string text)
        {
            // Press Start 2P is monospaced: every glyph is 1 em wide.
            int length = string.IsNullOrEmpty(text) ? 1 : text.Length;
            float width = Snap(length * EmWorld + PadX * 2f);
            float height = Snap(EmWorld + PadY * 2f);

            plate.transform.localPosition = new Vector3(0f, 0f, 0.003f);
            plate.transform.localScale = new Vector3(width, height, 1f);
            border.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            border.transform.localScale = new Vector3(width + 2f * ArtPixel, height + 2f * ArtPixel, 1f);

            // A short accent tab under the plate, pointing at the unit.
            float tabWidth = Snap(Mathf.Min(width * 0.35f, 6f * ArtPixel));
            tab.transform.localPosition = new Vector3(0f, -(height * 0.5f + ArtPixel * 1.5f), 0.004f);
            tab.transform.localScale = new Vector3(tabWidth, ArtPixel, 1f);
        }

        private SpriteRenderer CreateSprite(string objectName, Sprite sprite, int sortingOrder)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingLayerID = GridSorting.DefaultLayerId;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private TextMesh CreateText(string objectName, Vector3 localPosition, int sortingOrder)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;

            TextMesh text = go.AddComponent<TextMesh>();
            UIFontHelper.Apply(text, FontStyle.Normal);
            text.fontSize = FontSize;
            text.characterSize = CharacterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.sortingLayerID = GridSorting.DefaultLayerId;
            renderer.sortingOrder = sortingOrder;
            return text;
        }

        private static float Snap(float value)
        {
            return Mathf.Max(ArtPixel, Mathf.Round(value / ArtPixel) * ArtPixel);
        }

        /// <summary>A 1x1 world-unit white sprite on the built-in white texture (never destroyed).</summary>
        private static Sprite GetTexelSprite()
        {
            if (texelSprite == null || texelSprite.texture == null)
            {
                Texture2D white = Texture2D.whiteTexture;
                texelSprite = Sprite.Create(white, new Rect(0f, 0f, white.width, white.height),
                    new Vector2(0.5f, 0.5f), white.width, 0, SpriteMeshType.FullRect);
                texelSprite.name = "StatusTexel";
            }
            return texelSprite;
        }
    }
}
