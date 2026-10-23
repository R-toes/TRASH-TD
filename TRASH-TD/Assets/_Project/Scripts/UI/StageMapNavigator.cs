using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TrashTD.UI
{
    /// <summary>
    /// Map-style navigation for the stage-select map, on the map container (the content). Scroll zooms toward the
    /// cursor, dragging pans with inertia, double-click zooms in; +/- zoom and R / Home resets. The view never shows
    /// past the map edges. Also builds a HUD: an overview minimap (visible area, stage dots, cursor grid reference;
    /// click or drag to jump) and a zoom bar. Markers grow only a little with zoom (StageMapLayout.ZoomCompensation).
    /// Pointer positions go through CrtDisplay.ScreenToPicture (curved CRT glass). Unscaled time.
    /// </summary>
    public class StageMapNavigator : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler, IPointerClickHandler
    {
        private const float MinZoom = 1f;
        private const float MaxZoom = 3f;
        private const float ScrollStep = 1.2f;       // zoom factor per wheel notch
        private const float DoubleClickZoom = 1.8f;
        private const float KeyZoomPerSecond = 1.8f;  // zoom factor per second while +/- is held
        private const float Smoothing = 12f;
        private const float InertiaDamping = 5f;
        private const float MarkerGrowth = 0.35f;     // markers scale with zoom^MarkerGrowth
        private const float FollowMargin = 160f;      // keep a focused marker this far inside the screen edges

        private static readonly Color Accent = new Color(0.22f, 0.91f, 0.50f, 1f);
        private static readonly Color AccentBright = new Color(0.32f, 1.00f, 0.64f, 1f);
        private static readonly Color TerminalText = new Color(0.45f, 0.78f, 0.56f, 1f);
        private static readonly Color TerminalLine = new Color(0.22f, 0.91f, 0.50f, 0.45f);
        private static readonly Color PlateColor = new Color(0.010f, 0.030f, 0.020f, 0.86f);
        private static readonly Color SelectedGlow = new Color(0.98f, 1.00f, 0.65f, 1f);
        private static readonly Color LockedGrey = new Color(0.38f, 0.44f, 0.50f, 0.85f);

        /// <summary>When it returns true (dossier open, loading...), keyboard and pointer navigation is ignored.</summary>
        public Func<bool> InputBlocked;

        /// <summary>The minimap + zoom bar, for entrance animations.</summary>
        public RectTransform HudRoot { get; private set; }

        private RectTransform content;
        private RectTransform viewport;
        private StageMapLayout layout;
        private Canvas canvas;
        private IList<StageMapNode> nodes;

        private float zoom = MinZoom;
        private float targetZoom = MinZoom;
        private Vector2 pan;
        private Vector2 targetPan;
        private Vector2 velocity;
        private bool dragging;
        private Vector2 lastDragPoint;
        private float lastDragTime;

        private RectTransform minimap;
        private RectTransform minimapView;
        private Text coordsText;
        private Text zoomText;
        private readonly List<Image> minimapDots = new List<Image>();

        private static Sprite frameSprite;

        // ─────────────────────────────────────────────────────
        //  Setup
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// <paramref name="viewportRect"/> is the full-screen area the map is seen through (its pivot must be centred);
        /// the HUD is built under <paramref name="hudParent"/>; <paramref name="styleButton"/> styles the zoom buttons
        /// and attaches their feedback/sounds.
        /// </summary>
        public void Initialize(RectTransform viewportRect, StageMapLayout mapLayout, Sprite mapSprite, Transform hudParent,
            IList<StageMapNode> stageNodes, Action<Button> styleButton)
        {
            viewport = viewportRect;
            layout = mapLayout;
            nodes = stageNodes;
            content = (RectTransform)transform;
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(0.5f, 0.5f);
            canvas = GetComponentInParent<Canvas>();

            BuildHud(mapSprite, hudParent, styleButton);
            ResetView(true);
        }

        public void ResetView(bool instant)
        {
            targetZoom = MinZoom;
            targetPan = Vector2.zero;
            velocity = Vector2.zero;
            if (instant)
            {
                zoom = targetZoom;
                pan = targetPan;
                ApplyView();
            }
        }

        /// <summary>Pans (keeping the zoom) so the marker sits comfortably inside the screen. No-op when it already does.</summary>
        public void EnsureVisible(StageMapNode node)
        {
            if (node == null || targetZoom <= MinZoom + 0.01f) return;

            Vector2 view = ViewSize();
            Vector2 point = ContentPoint(NodeNormalized(node), targetPan, targetZoom);
            Vector2 limit = Vector2.Max(Vector2.zero, view * 0.5f - Vector2.one * FollowMargin);
            Vector2 inside = new Vector2(Mathf.Clamp(point.x, -limit.x, limit.x), Mathf.Clamp(point.y, -limit.y, limit.y));
            if (inside == point) return;

            velocity = Vector2.zero;
            targetPan = ClampPan(targetPan + inside - point, targetZoom);
        }

        // ─────────────────────────────────────────────────────
        //  Pointer input (events bubble up from the markers too)
        // ─────────────────────────────────────────────────────

        public void OnScroll(PointerEventData eventData)
        {
            if (IsBlocked() || Mathf.Approximately(eventData.scrollDelta.y, 0f)) return;
            if (!TryViewportPoint(eventData.position, out Vector2 point)) return;

            float factor = eventData.scrollDelta.y > 0f ? ScrollStep : 1f / ScrollStep;
            ZoomAbout(point, targetZoom * factor);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (IsBlocked() || eventData.clickCount != 2) return;
            if (!TryViewportPoint(eventData.position, out Vector2 point)) return;

            // Double-click on open map: zoom in there, or back out once fully zoomed
            if (targetZoom >= MaxZoom - 0.01f) ResetView(false);
            else ZoomAbout(point, targetZoom * DoubleClickZoom);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (IsBlocked() || !TryViewportPoint(eventData.position, out lastDragPoint)) return;
            dragging = true;
            velocity = Vector2.zero;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || !TryViewportPoint(eventData.position, out Vector2 point)) return;

            Vector2 delta = point - lastDragPoint;
            lastDragPoint = point;

            // 1:1 while dragging; remember the speed for the throw
            targetPan = ClampPan(targetPan + delta, targetZoom);
            pan = ClampPan(pan + delta, zoom);
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
            velocity = Vector2.Lerp(velocity, delta / dt, 0.5f);
            lastDragTime = Time.unscaledTime;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            dragging = false;
            // Held still before letting go: no throw
            if (Time.unscaledTime - lastDragTime > 0.08f) velocity = Vector2.zero;
        }

        /// <summary>Minimap click/drag: centre the view on that point of the map.</summary>
        private void JumpToMinimapPoint(Vector2 screenPosition)
        {
            if (IsBlocked() || minimap == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(minimap, CrtDisplay.ScreenToPicture(screenPosition), EventCamera(), out Vector2 local))
                return;

            Rect rect = minimap.rect;
            var normalized = new Vector2(Mathf.InverseLerp(rect.xMin, rect.xMax, local.x), Mathf.InverseLerp(rect.yMin, rect.yMax, local.y));
            if (targetZoom <= MinZoom + 0.01f) targetZoom = 1.8f; // jumping only makes sense zoomed in
            velocity = Vector2.zero;
            targetPan = ClampPan(-(normalized - new Vector2(0.5f, 0.5f)) * (ViewSize() * targetZoom), targetZoom);
        }

        // ─────────────────────────────────────────────────────
        //  Update
        // ─────────────────────────────────────────────────────

        private void Update()
        {
            if (viewport == null) return;
            float dt = Time.unscaledDeltaTime;

            if (!IsBlocked()) HandleKeys(dt);

            // Throw after a drag
            if (!dragging && velocity.sqrMagnitude > 1f)
            {
                targetPan = ClampPan(targetPan + velocity * dt, targetZoom);
                velocity *= Mathf.Exp(-InertiaDamping * dt);
            }

            float k = 1f - Mathf.Exp(-Smoothing * dt);
            zoom = Mathf.Abs(zoom - targetZoom) < 0.0005f ? targetZoom : Mathf.Lerp(zoom, targetZoom, k);
            if (!dragging) pan = Vector2.Lerp(pan, targetPan, k);
            pan = ClampPan(pan, zoom);

            ApplyView();
            UpdateHud();
        }

        private void HandleKeys(float dt)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            float direction = 0f;
            if (keyboard.equalsKey.isPressed || keyboard.numpadPlusKey.isPressed) direction += 1f;
            if (keyboard.minusKey.isPressed || keyboard.numpadMinusKey.isPressed) direction -= 1f;
            if (direction != 0f) ZoomAbout(Vector2.zero, targetZoom * Mathf.Pow(KeyZoomPerSecond, direction * dt));

            if (keyboard.rKey.wasPressedThisFrame || keyboard.homeKey.wasPressedThisFrame) ResetView(false);
        }

        /// <summary>Zooms keeping the map point under <paramref name="viewportPoint"/> (centre-origin) in place.</summary>
        private void ZoomAbout(Vector2 viewportPoint, float newZoom)
        {
            newZoom = Mathf.Clamp(newZoom, MinZoom, MaxZoom);
            targetPan = ClampPan(viewportPoint - (viewportPoint - targetPan) * (newZoom / targetZoom), newZoom);
            targetZoom = newZoom;
            velocity = Vector2.zero;
        }

        private void ApplyView()
        {
            // Compensation first: resizing the content makes StageMapLayout re-apply the marker scale
            if (layout != null) layout.ZoomCompensation = Mathf.Pow(zoom, 1f - MarkerGrowth);
            content.sizeDelta = ViewSize() * zoom;
            content.anchoredPosition = pan;
        }

        private void UpdateHud()
        {
            if (minimap == null) return;

            // Visible area in map-normalised coordinates (origin bottom-left)
            Vector2 view = ViewSize();
            Vector2 size = view * zoom;
            Vector2 min = (-view * 0.5f - pan) / size + new Vector2(0.5f, 0.5f);
            Vector2 max = (view * 0.5f - pan) / size + new Vector2(0.5f, 0.5f);
            minimapView.anchorMin = min;
            minimapView.anchorMax = max;
            minimapView.offsetMin = Vector2.zero;
            minimapView.offsetMax = Vector2.zero;

            zoomText.text = zoom.ToString("0.0") + "X";

            for (int i = 0; i < minimapDots.Count && i < nodes.Count; i++)
            {
                StageMapNode node = nodes[i];
                if (node == null) continue;
                minimapDots[i].color = node.IsLocked ? LockedGrey : (node.IsSelectedStage ? SelectedGlow : AccentBright);
            }

            // Grid reference of the cursor over the map
            Mouse mouse = Mouse.current;
            string coords = "--.-- --.--";
            if (mouse != null && !IsBlocked() && TryViewportPoint(mouse.position.ReadValue(), out Vector2 point))
            {
                Vector2 m = (point - pan) / size + new Vector2(0.5f, 0.5f);
                if (m.x >= 0f && m.x <= 1f && m.y >= 0f && m.y <= 1f)
                {
                    coords = $"{m.x * 100f:00.00} {(1f - m.y) * 100f:00.00}";
                }
            }
            coordsText.text = coords;
        }

        // ─────────────────────────────────────────────────────
        //  Math helpers
        // ─────────────────────────────────────────────────────

        private Vector2 ViewSize()
        {
            Vector2 size = viewport != null ? viewport.rect.size : Vector2.zero;
            return size.x < 1f || size.y < 1f ? new Vector2(1920f, 1080f) : size;
        }

        /// <summary>Largest pan that keeps the map covering the screen at <paramref name="atZoom"/>.</summary>
        private Vector2 ClampPan(Vector2 value, float atZoom)
        {
            Vector2 limit = ViewSize() * (atZoom - 1f) * 0.5f;
            return new Vector2(Mathf.Clamp(value.x, -limit.x, limit.x), Mathf.Clamp(value.y, -limit.y, limit.y));
        }

        private Vector2 ContentPoint(Vector2 normalized, Vector2 atPan, float atZoom)
        {
            return atPan + (normalized - new Vector2(0.5f, 0.5f)) * (ViewSize() * atZoom);
        }

        /// <summary>Node position in map-normalised coordinates with the origin at the bottom-left.</summary>
        private static Vector2 NodeNormalized(StageMapNode node)
        {
            Vector2 p = node.NormalizedPosition;
            return new Vector2(p.x, 1f - p.y);
        }

        /// <summary>Screen position → viewport point (origin at its centre), through the curved CRT picture.</summary>
        private bool TryViewportPoint(Vector2 screenPosition, out Vector2 point)
        {
            point = Vector2.zero;
            if (viewport == null) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, CrtDisplay.ScreenToPicture(screenPosition), EventCamera(), out Vector2 local))
                return false;
            point = local - viewport.rect.center;
            return true;
        }

        private Camera EventCamera()
        {
            if (canvas == null) return null;
            Canvas root = canvas.rootCanvas;
            return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }

        private bool IsBlocked()
        {
            return !isActiveAndEnabled || (InputBlocked != null && InputBlocked());
        }

        // ─────────────────────────────────────────────────────
        //  HUD: minimap + zoom bar
        // ─────────────────────────────────────────────────────

        private const float HudWidth = 256f;
        private const float MinimapHeight = 144f;

        private void BuildHud(Sprite mapSprite, Transform hudParent, Action<Button> styleButton)
        {
            var hudGo = new GameObject("MapNavigatorHud", typeof(RectTransform));
            hudGo.transform.SetParent(hudParent, false);
            HudRoot = (RectTransform)hudGo.transform;
            Place(HudRoot, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-60f, -88f), new Vector2(HudWidth, 240f));

            // Header: label + cursor grid reference
            Text label = CreateText(HudRoot, "Label", "MAP OVERVIEW", 10, TextAnchor.MiddleLeft, TerminalText);
            Place(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(HudWidth, 18f));
            coordsText = CreateText(HudRoot, "Coords", string.Empty, 10, TextAnchor.MiddleRight, AccentBright);
            Place(coordsText.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(HudWidth, 18f));

            // Minimap: the map art, stage dots and the visible-area frame; click or drag to jump
            Image plate = CreateImage(HudRoot, "MinimapPlate", PlateColor);
            minimap = plate.rectTransform;
            Place(minimap, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -24f), new Vector2(HudWidth, MinimapHeight));
            plate.raycastTarget = true;
            AddCornerBrackets(minimap, 12f, 2f, TerminalLine, -5f);
            var input = plate.gameObject.AddComponent<StageMapMinimapInput>();
            input.Pressed = JumpToMinimapPoint;

            if (mapSprite != null)
            {
                Image art = CreateImage(minimap, "Art", new Color(0.75f, 0.85f, 0.78f, 0.9f));
                art.sprite = mapSprite;
                Stretch(art.rectTransform, 2f);
            }

            if (nodes != null)
            {
                foreach (StageMapNode node in nodes)
                {
                    Image dot = CreateImage(minimap, "Dot", AccentBright);
                    Vector2 n = node != null ? NodeNormalized(node) : Vector2.zero;
                    dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = n;
                    dot.rectTransform.sizeDelta = new Vector2(6f, 6f);
                    minimapDots.Add(dot);
                }
            }

            Image view = CreateImage(minimap, "ViewFrame", AccentBright);
            view.sprite = GetFrameSprite();
            view.type = Image.Type.Sliced;
            view.pixelsPerUnitMultiplier = 0.5f; // 2 px border
            minimapView = view.rectTransform;
            Image viewFill = CreateImage(minimapView, "Fill", new Color(AccentBright.r, AccentBright.g, AccentBright.b, 0.08f));
            Stretch(viewFill.rectTransform, 0f);

            // Zoom bar: [-] 1.0X [+] [RESET]
            const float rowY = -24f - MinimapHeight - 12f;
            Button zoomOut = CreateButton(HudRoot, "ZoomOut", "-", styleButton);
            Place((RectTransform)zoomOut.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, rowY), new Vector2(40f, 40f));
            zoomOut.onClick.AddListener(() => ZoomAbout(Vector2.zero, targetZoom / 1.5f));

            Image readout = CreateImage(HudRoot, "ZoomReadout", PlateColor);
            Place(readout.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(46f, rowY), new Vector2(80f, 40f));
            zoomText = CreateText(readout.rectTransform, "Zoom", "1.0X", 13, TextAnchor.MiddleCenter, AccentBright);
            Stretch(zoomText.rectTransform, 0f);

            Button zoomIn = CreateButton(HudRoot, "ZoomIn", "+", styleButton);
            Place((RectTransform)zoomIn.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(132f, rowY), new Vector2(40f, 40f));
            zoomIn.onClick.AddListener(() => ZoomAbout(Vector2.zero, targetZoom * 1.5f));

            Button reset = CreateButton(HudRoot, "ZoomReset", "RESET", styleButton);
            Place((RectTransform)reset.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(178f, rowY), new Vector2(78f, 40f));
            reset.onClick.AddListener(() => ResetView(false));

            Text hint = CreateText(HudRoot, "Hint", "SCROLL ZOOM / DRAG PAN / R RESET", 9, TextAnchor.MiddleRight,
                new Color(TerminalText.r, TerminalText.g, TerminalText.b, 0.6f));
            Place(hint.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, rowY - 52f), new Vector2(HudWidth, 16f));
        }

        private static Button CreateButton(Transform parent, string name, string label, Action<Button> style)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Text text = CreateText(go.transform, "Label", label, 14, TextAnchor.MiddleCenter, AccentBright);
            Stretch(text.rectTransform, 0f);
            Button button = go.GetComponent<Button>();
            style?.Invoke(button);
            return button;
        }

        private static Text CreateText(Transform parent, string name, string content, int size, TextAnchor alignment, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = UIFontHelper.GetPixelFont();
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
        }

        private static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        private static void AddCornerBrackets(RectTransform target, float length, float thickness, Color color, float inset)
        {
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2(i % 2, i / 2);
                var inward = new Vector2(corner.x < 0.5f ? 1f : -1f, corner.y < 0.5f ? 1f : -1f);
                foreach (Vector2 size in new[] { new Vector2(length, thickness), new Vector2(thickness, length) })
                {
                    Image bar = CreateImage(target, "CornerBracket", color);
                    bar.rectTransform.anchorMin = bar.rectTransform.anchorMax = bar.rectTransform.pivot = corner;
                    bar.rectTransform.sizeDelta = size;
                    bar.rectTransform.anchoredPosition = inward * inset;
                }
            }
        }

        /// <summary>3x3 white frame with a 1 px border, sliced into a hollow rectangle.</summary>
        private static Sprite GetFrameSprite()
        {
            if (frameSprite != null) return frameSprite;

            var tex = new Texture2D(3, 3, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    tex.SetPixel(x, y, x == 1 && y == 1 ? Color.clear : Color.white);
                }
            }
            tex.Apply();
            frameSprite = Sprite.Create(tex, new Rect(0f, 0f, 3f, 3f), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(1f, 1f, 1f, 1f));
            return frameSprite;
        }
    }

    /// <summary>Forwards presses and drags on the minimap to StageMapNavigator.</summary>
    public class StageMapMinimapInput : MonoBehaviour, IPointerDownHandler, IDragHandler, IBeginDragHandler
    {
        public Action<Vector2> Pressed;

        public void OnPointerDown(PointerEventData eventData) => Pressed?.Invoke(eventData.position);
        public void OnBeginDrag(PointerEventData eventData) => Pressed?.Invoke(eventData.position);
        public void OnDrag(PointerEventData eventData) => Pressed?.Invoke(eventData.position);
    }
}
