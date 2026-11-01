using UnityEngine;
using UnityEngine.UI;
using TrashTD.Data;

namespace TrashTD.UI
{
    /// <summary>
    /// A looping pixel diagram of one placement scheme (ControlSettings), used by Settings → Controls and the
    /// tutorial's control picker. A cursor drags a card onto the middle of a 3x3 patch of tiles, then acts out the
    /// scheme: arrows + PLACE (Precise), hold and drag (Swipe), or one arrow tap (Quick). A caption under the
    /// tiles names each step. Built from UI Images, runs on unscaled time (Settings opens while paused), and
    /// never takes clicks, so the option card around it stays clickable.
    /// </summary>
    public sealed class PlacementSchemeDiagram : MonoBehaviour
    {
        private const float Tile = 34f;
        private const float GridY = 14f;        // grid centre, from the diagram centre
        private const float LoopSeconds = 4.4f;
        private const float DropTime = 0.95f;

        private static readonly Color TileColor = new Color(0.05f, 0.13f, 0.08f, 1f);
        private static readonly Color LaneColor = new Color(0.09f, 0.2f, 0.13f, 1f);
        private static readonly Color TileLine = new Color(0.22f, 0.91f, 0.5f, 0.28f);
        private static readonly Color RangeColor = new Color(0.15f, 1f, 0.28f, 0.9f);
        private static readonly Color ArrowFill = new Color(0.07f, 0.2f, 0.12f, 1f);
        private static readonly Color Bright = new Color(0.32f, 1f, 0.64f, 1f);
        private static readonly Color PlaceColor = new Color(0.12f, 0.58f, 0.28f, 1f);
        private static readonly Color CancelColor = new Color(0.55f, 0.16f, 0.15f, 1f);
        private static readonly Color CaptionColor = new Color(0.45f, 0.78f, 0.56f, 1f);

        private PlacementControlScheme scheme;
        private RectTransform card, operatorRect, cursor, clickFlash, rangeTile, facingArrow, trail, placedFlash;
        private Image operatorImage, clickFlashImage, placedFlashImage, trailImage;
        private readonly RectTransform[] arrows = new RectTransform[4];
        private readonly Image[] arrowImages = new Image[4];
        private RectTransform placeButton, cancelButton;
        private Text caption;
        private float startTime;

        // Facing order used by the arrows: right, up, left, down
        private static readonly Vector2[] Directions = { Vector2.right, Vector2.up, Vector2.left, Vector2.down };
        private static readonly float[] Rotations = { 0f, 90f, 180f, -90f };

        public static PlacementSchemeDiagram Create(Transform parent, PlacementControlScheme scheme, Vector2 size)
        {
            var root = new GameObject("PlacementSchemeDiagram", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            root.GetComponent<RectTransform>().sizeDelta = size;
            var diagram = root.AddComponent<PlacementSchemeDiagram>();
            diagram.Build(scheme, size);
            return diagram;
        }

        private void OnEnable() => startTime = Time.unscaledTime;

        private void Build(PlacementControlScheme value, Vector2 size)
        {
            scheme = value;
            Sprite icon = GameplayHUDUI.GetClassIconSprite(OperatorClass.Guard);
            Sprite arrowIcon = GameplayHUDUI.LoadIconSprite("icon_play");

            // 3x3 tiles; the middle row is the enemy lane
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    Image tile = MakeImage("Tile", y == 0 ? LaneColor : TileColor, TilePosition(x, y), new Vector2(Tile - 2f, Tile - 2f));
                    Outline outline = tile.gameObject.AddComponent<Outline>();
                    outline.effectColor = TileLine;
                    outline.effectDistance = new Vector2(1f, -1f);
                }
            }

            rangeTile = MakeImage("Range", new Color(RangeColor.r, RangeColor.g, RangeColor.b, 0.18f), Vector2.zero, new Vector2(Tile - 4f, Tile - 4f)).rectTransform;
            Outline rangeOutline = rangeTile.gameObject.AddComponent<Outline>();
            rangeOutline.effectColor = RangeColor;
            rangeOutline.effectDistance = new Vector2(2f, -2f);

            placedFlashImage = MakeImage("PlacedFlash", new Color(Bright.r, Bright.g, Bright.b, 0f), TilePosition(0, 0), new Vector2(Tile, Tile));
            placedFlash = placedFlashImage.rectTransform;

            operatorImage = MakeImage("Operator", Color.white, TilePosition(0, 0), new Vector2(24f, 24f));
            operatorImage.sprite = icon;
            operatorImage.preserveAspect = true;
            operatorRect = operatorImage.rectTransform;

            Image facingImage = MakeImage("Facing", Bright, Vector2.zero, new Vector2(9f, 9f));
            facingImage.sprite = arrowIcon;
            facingArrow = facingImage.rectTransform;

            trailImage = MakeImage("Trail", new Color(Bright.r, Bright.g, Bright.b, 0.55f), Vector2.zero, new Vector2(0f, 3f));
            trail = trailImage.rectTransform;
            trail.pivot = new Vector2(0f, 0.5f);

            // Arrow buttons just outside the operator's tile
            for (int i = 0; i < 4; i++)
            {
                Image button = MakeImage("Arrow", ArrowFill, TilePosition(0, 0) + Directions[i] * Tile * 0.92f, new Vector2(18f, 18f));
                Outline outline = button.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(Bright.r, Bright.g, Bright.b, 0.7f);
                outline.effectDistance = new Vector2(1f, -1f);
                Image glyph = MakeImage("Glyph", Bright, Vector2.zero, new Vector2(10f, 10f), button.transform);
                glyph.sprite = arrowIcon;
                glyph.rectTransform.localEulerAngles = new Vector3(0f, 0f, Rotations[i]);
                arrows[i] = button.rectTransform;
                arrowImages[i] = button;
            }

            if (scheme == PlacementControlScheme.Precise)
            {
                placeButton = MakeMiniButton("PLACE", PlaceColor, TilePosition(0, 0) + new Vector2(Tile * 2.05f, 9f));
                cancelButton = MakeMiniButton("CANCEL", CancelColor, TilePosition(0, 0) + new Vector2(Tile * 2.05f, -9f));
            }

            card = MakeImage("Card", new Color(0.1f, 0.25f, 0.16f, 1f), Vector2.zero, new Vector2(22f, 28f)).rectTransform;
            Outline cardOutline = card.gameObject.AddComponent<Outline>();
            cardOutline.effectColor = Bright;
            cardOutline.effectDistance = new Vector2(1f, -1f);
            Image cardIcon = MakeImage("Icon", Color.white, Vector2.zero, new Vector2(16f, 16f), card);
            cardIcon.sprite = icon;
            cardIcon.preserveAspect = true;

            clickFlashImage = MakeImage("Click", new Color(1f, 1f, 1f, 0f), Vector2.zero, new Vector2(10f, 10f));
            clickFlash = clickFlashImage.rectTransform;
            Outline flashOutline = clickFlash.gameObject.AddComponent<Outline>();
            flashOutline.effectColor = new Color(1f, 1f, 1f, 0.8f);
            flashOutline.effectDistance = new Vector2(1f, -1f);

            Image cursorImage = MakeImage("Cursor", Color.white, Vector2.zero, new Vector2(16f, 16f));
            cursorImage.sprite = GameplayHUDUI.LoadIconSprite("cursor_arrow");
            cursorImage.preserveAspect = true;
            cursor = cursorImage.rectTransform;
            cursor.pivot = new Vector2(0.1f, 0.9f); // the arrow's tip

            caption = new GameObject("Caption", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            caption.transform.SetParent(transform, false);
            caption.font = UIFontHelper.GetPixelFont();
            caption.fontSize = 8;
            caption.alignment = TextAnchor.MiddleCenter;
            caption.color = CaptionColor;
            caption.horizontalOverflow = HorizontalWrapMode.Overflow;
            caption.raycastTarget = false;
            RectTransform captionRect = caption.rectTransform;
            captionRect.anchoredPosition = new Vector2(0f, -size.y * 0.5f + 8f);
            captionRect.sizeDelta = new Vector2(size.x, 14f);

            startTime = Time.unscaledTime;
            Animate(0f);
        }

        private void Update() => Animate((Time.unscaledTime - startTime) % LoopSeconds);

        private void Animate(float t)
        {
            Vector2 center = TilePosition(0, 0);
            Vector2 cardStart = center + new Vector2(-Tile * 2.2f, -Tile * 1.75f);
            Vector2 leftArrow = center + Directions[2] * Tile * 0.92f;

            // Shared opening: drag the card onto the middle tile
            bool dropped = t >= DropTime;
            Vector2 cardPos = Vector2.Lerp(cardStart, center, Ease(Mathf.Clamp01((t - 0.15f) / 0.75f)));
            card.gameObject.SetActive(!dropped);
            card.anchoredPosition = cardPos;
            Vector2 cursorPos = cardPos + new Vector2(5f, -4f);

            int facing = 0; // right
            bool placed = false;
            float arrowsShown = 0f;
            float placedAt = -1f;
            float clickAt = -1f;
            string step = "DRAG OR CLICK A CARD";
            trail.gameObject.SetActive(false);

            if (dropped)
            {
                step = "DROP IT ON A TILE";
                switch (scheme)
                {
                    case PlacementControlScheme.Precise:
                    {
                        arrowsShown = Mathf.Clamp01((t - DropTime) / 0.15f);
                        Vector2 place = placeButton.anchoredPosition;
                        if (t < 1.75f) cursorPos = Vector2.Lerp(center, leftArrow, Ease(Mathf.Clamp01((t - 1.15f) / 0.6f)));
                        else if (t < 2.5f) cursorPos = Vector2.Lerp(leftArrow, place, Ease(Mathf.Clamp01((t - 1.95f) / 0.55f)));
                        else cursorPos = place;
                        if (t >= 1.15f) step = "TAP AN ARROW";
                        if (t >= 1.8f) { facing = 2; step = "THEN PRESS PLACE"; }
                        clickAt = t >= 2.6f ? 2.6f : 1.8f;
                        if (t >= 2.6f) { placed = true; placedAt = 2.6f; arrowsShown = 0f; }
                        break;
                    }
                    case PlacementControlScheme.Swipe:
                    {
                        Vector2 dragEnd = center + new Vector2(-Tile * 1.1f, 0f);
                        if (t < 1.3f) cursorPos = Vector2.Lerp(center + new Vector2(4f, -6f), center, Ease(Mathf.Clamp01((t - DropTime) / 0.3f)));
                        else if (t < 2.15f) cursorPos = Vector2.Lerp(center, dragEnd, Ease(Mathf.Clamp01((t - 1.35f) / 0.7f)));
                        else cursorPos = dragEnd;
                        if (t >= 1.25f && t < 2.2f)
                        {
                            step = "HOLD AND DRAG";
                            ShowTrail(center, cursorPos);
                        }
                        if (cursorPos.x < center.x - Tile * 0.2f) facing = 2;
                        clickAt = 1.3f;
                        if (t >= 2.2f) { placed = true; placedAt = 2.2f; }
                        break;
                    }
                    case PlacementControlScheme.Quick:
                    {
                        arrowsShown = Mathf.Clamp01((t - DropTime) / 0.15f);
                        cursorPos = t < 1.8f ? Vector2.Lerp(center, leftArrow, Ease(Mathf.Clamp01((t - 1.15f) / 0.6f))) : leftArrow;
                        if (t >= 1.15f) step = "TAP AN ARROW";
                        clickAt = 1.85f;
                        if (t >= 1.85f) { facing = 2; placed = true; placedAt = 1.85f; arrowsShown = 0f; }
                        break;
                    }
                }
                if (placed) step = "PLACED!";
            }

            cursor.anchoredPosition = cursorPos;
            caption.text = step;

            // Operator: a see-through preview until placed, then a little settle bounce
            operatorRect.gameObject.SetActive(dropped);
            float sincePlaced = placed ? t - placedAt : -1f;
            operatorImage.color = new Color(1f, 1f, 1f, placed ? 1f : 0.6f);
            float bounce = sincePlaced >= 0f && sincePlaced < 0.25f ? Mathf.Sin(sincePlaced / 0.25f * Mathf.PI) * 4f : 0f;
            operatorRect.anchoredPosition = center + new Vector2(0f, bounce);

            // Facing marker and the range tile in front
            facingArrow.gameObject.SetActive(dropped);
            rangeTile.gameObject.SetActive(dropped);
            facingArrow.anchoredPosition = center + Directions[facing] * Tile * 0.36f;
            facingArrow.localEulerAngles = new Vector3(0f, 0f, Rotations[facing]);
            rangeTile.anchoredPosition = center + Directions[facing] * Tile;

            for (int i = 0; i < 4; i++)
            {
                arrows[i].gameObject.SetActive(arrowsShown > 0f);
                arrows[i].localScale = Vector3.one * Mathf.Lerp(0.4f, 1f, arrowsShown);
                arrowImages[i].color = i == facing && !placed ? new Color(0.12f, 0.45f, 0.26f, 1f) : ArrowFill;
            }
            if (placeButton != null)
            {
                placeButton.gameObject.SetActive(arrowsShown > 0f);
                cancelButton.gameObject.SetActive(arrowsShown > 0f);
            }

            // A press: a small square that grows and fades at the cursor tip
            float sinceClick = clickAt >= 0f ? t - clickAt : -1f;
            bool flashing = sinceClick >= 0f && sinceClick < 0.3f;
            clickFlash.gameObject.SetActive(flashing);
            if (flashing)
            {
                float k = sinceClick / 0.3f;
                clickFlash.anchoredPosition = cursorPos;
                clickFlash.localScale = Vector3.one * Mathf.Lerp(0.6f, 2f, k);
                clickFlashImage.GetComponent<Outline>().effectColor = new Color(1f, 1f, 1f, 1f - k);
            }
            cursor.localScale = Vector3.one * (flashing && sinceClick < 0.12f ? 0.85f : 1f);

            // Placed: the tile flashes bright once
            float flash = sincePlaced >= 0f && sincePlaced < 0.45f ? 1f - sincePlaced / 0.45f : 0f;
            placedFlashImage.color = new Color(Bright.r, Bright.g, Bright.b, flash * 0.55f);
        }

        private void ShowTrail(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            if (delta.magnitude < 2f) return;
            trail.gameObject.SetActive(true);
            trail.anchoredPosition = from;
            trail.sizeDelta = new Vector2(delta.magnitude, 3f);
            trail.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        private static float Ease(float k) => 1f - (1f - k) * (1f - k);

        private static Vector2 TilePosition(int x, int y) => new Vector2(x * Tile, GridY + y * Tile);

        private RectTransform MakeMiniButton(string label, Color color, Vector2 position)
        {
            Image button = MakeImage(label, color, position, new Vector2(44f, 15f));
            Text text = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(button.transform, false);
            text.font = UIFontHelper.GetPixelFont();
            text.fontSize = 6;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = label;
            text.rectTransform.sizeDelta = new Vector2(44f, 15f);
            return button.rectTransform;
        }

        private Image MakeImage(string name, Color color, Vector2 position, Vector2 size, Transform parent = null)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent != null ? parent : transform, false);
            Image image = obj.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            RectTransform rect = image.rectTransform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return image;
        }
    }

    /// <summary>
    /// One selectable control-scheme card: name, the animated diagram and a short description, in the green
    /// terminal style of the settings panel. Shared by Settings → Controls and the tutorial's control picker.
    /// </summary>
    public sealed class PlacementSchemeOption
    {
        private static readonly Color CardFill = new Color(0.035f, 0.09f, 0.06f, 1f);
        private static readonly Color SelectedFill = new Color(0.07f, 0.22f, 0.13f, 1f);
        private static readonly Color Line = new Color(0.22f, 0.91f, 0.5f, 0.45f);
        private static readonly Color Bright = new Color(0.32f, 1f, 0.64f, 1f);
        private static readonly Color Label = new Color(0.45f, 0.78f, 0.56f, 1f);

        public PlacementControlScheme Scheme { get; private set; }
        public Button Button { get; private set; }

        private Image background;
        private Outline outline;
        private Text nameText;
        private GameObject selectedTag;

        public static PlacementSchemeOption Create(Transform parent, PlacementControlScheme scheme, Vector2 size)
        {
            var option = new PlacementSchemeOption { Scheme = scheme };
            var root = new GameObject(ControlSettings.GetName(scheme) + "Option", typeof(RectTransform), typeof(Image), typeof(Button));
            root.transform.SetParent(parent, false);
            root.GetComponent<RectTransform>().sizeDelta = size;
            option.background = root.GetComponent<Image>();
            option.background.color = CardFill;
            option.Button = root.GetComponent<Button>();
            option.Button.transition = Selectable.Transition.None;
            option.Button.navigation = new Navigation { mode = Navigation.Mode.None };
            option.outline = root.AddComponent<Outline>();
            option.outline.effectDistance = new Vector2(2f, -2f);

            option.nameText = MakeText(root.transform, ControlSettings.GetName(scheme), 14, Bright, TextAnchor.MiddleCenter,
                new Vector2(0f, size.y * 0.5f - 20f), new Vector2(size.x - 16f, 24f));

            PlacementSchemeDiagram diagram = PlacementSchemeDiagram.Create(root.transform, scheme, new Vector2(size.x - 20f, 150f));
            diagram.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, size.y * 0.5f - 40f - 75f);

            Text description = MakeText(root.transform, ControlSettings.GetDescription(scheme), 9, Label, TextAnchor.UpperCenter,
                new Vector2(0f, size.y * 0.5f - 200f), new Vector2(size.x - 24f, 60f));
            description.horizontalOverflow = HorizontalWrapMode.Wrap;
            description.verticalOverflow = VerticalWrapMode.Overflow;
            description.lineSpacing = 1.5f;
            description.rectTransform.pivot = new Vector2(0.5f, 1f);

            option.selectedTag = new GameObject("SelectedTag", typeof(RectTransform), typeof(Image));
            option.selectedTag.transform.SetParent(root.transform, false);
            Image tag = option.selectedTag.GetComponent<Image>();
            tag.color = Bright;
            tag.raycastTarget = false;
            RectTransform tagRect = tag.rectTransform;
            tagRect.anchoredPosition = new Vector2(0f, -size.y * 0.5f + 14f);
            tagRect.sizeDelta = new Vector2(96f, 18f);
            MakeText(option.selectedTag.transform, "SELECTED", 8, new Color(0.02f, 0.1f, 0.05f, 1f), TextAnchor.MiddleCenter,
                Vector2.zero, new Vector2(96f, 18f));

            option.SetSelected(false);
            return option;
        }

        public void SetSelected(bool selected)
        {
            // A MenuButtonFeedback on the card owns its colour (hover/press derive from the base)
            Color fill = selected ? SelectedFill : CardFill;
            MenuButtonFeedback feedback = Button.GetComponent<MenuButtonFeedback>();
            if (feedback != null) feedback.SetBaseColor(fill);
            else background.color = fill;
            outline.effectColor = selected ? Bright : Line;
            nameText.color = selected ? Bright : Label;
            selectedTag.SetActive(selected);
        }

        private static Text MakeText(Transform parent, string value, int size, Color color, TextAnchor alignment, Vector2 position, Vector2 box)
        {
            Text text = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            text.font = UIFontHelper.GetPixelFont();
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = value;
            text.rectTransform.anchoredPosition = position;
            text.rectTransform.sizeDelta = box;
            return text;
        }
    }
}
