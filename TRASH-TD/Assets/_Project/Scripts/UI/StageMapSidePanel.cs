using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TrashTD.UI
{
    /// <summary>
    /// The machine panel down the left edge of the stage-select map (Resources/UI/panels/Stage Selector Map Panel,
    /// 183×810 art px, drawn at full screen height). Its joystick knob sits in the green dial.
    /// Grabbed (press anywhere on the dial and push), it is a real stick: the knob follows the pointer and the view
    /// travels that way at a speed set by how far it is pushed (StageMapNavigator.StickPan). Released, it shows the
    /// map navigation instead: it tilts toward where the view is heading (drag, throw, minimap jump, zoom toward the
    /// cursor), sinks and darkens while zooming in, and springs back to centre with a small wobble.
    /// The scroll wheel above it rolls with the zoom (up when zooming in), a pixel at a time. Unscaled time.
    /// </summary>
    public class StageMapSidePanel : MonoBehaviour
    {
        private const string PanelSpritePath = "UI/panels/Stage Selector Map Panel";
        private const string KnobSpritePath = "UI/panels/Stage Selector Map Panel joystick";

        private static readonly Vector2 ArtSize = new Vector2(183f, 810f);
        private static readonly Vector2 DialCentre = new Vector2(111.5f, 355.5f); // knob rest spot, art px from bottom-left
        private static readonly Color PressedTint = new Color(0.72f, 0.76f, 0.80f, 1f);

        private const float MaxTilt = 11f;      // art px the knob travels from the dial centre (stays inside the bezel)
        private const float TiltSpeed = 0.35f;  // view speed (map widths / s) that gives ~63% tilt
        private const float Stiffness = 260f;
        private const float Damping = 16f;      // under-damped: a little overshoot when it springs back
        private const float ZoomPress = 0.10f;  // press per unit of zoom rate (ln zoom / s)
        private const float MaxPress = 0.12f;   // fraction of the knob size it can sink by

        private const float DialRadius = 40f;     // art px: the grab area (the dial's bezel)
        private const float StickReach = 32f;     // art px of pointer travel from the dial centre for a full push
        private const float StickDeadZone = 0.12f;
        private const float StickMaxSpeed = 1200f; // canvas units / s the view travels at a full push
        private const float StickCurve = 1.5f;     // speed = push^curve, for fine control near the centre
        private const float GrabTint = 0.45f;      // how far toward PressedTint the knob darkens while held

        // Scroll wheel: the ridged strip above the joystick (art x 72–96, rows 218–324 from the top), a 4-row
        // repeat. Redrawn on top of the art and rolled by whole pixels as the zoom changes.
        private static readonly Rect WheelRidges = new Rect(72f, 485f, 25f, 107f); // art px from bottom-left
        private static readonly Color32[] WheelPattern = // bottom-up, from the art: dark edge, mid, highlight, gap
        {
            new Color32(0x41, 0x4B, 0x58, 0xFF),
            new Color32(0x5C, 0x68, 0x77, 0xFF),
            new Color32(0xC0, 0xCC, 0xD8, 0xFF),
            new Color32(0x0D, 0x10, 0x14, 0xFF),
        };
        private const float WheelPxPerZoomStep = 24f; // art px the ridges roll per unit of ln(zoom); ~4 px (one ridge) per wheel notch

        /// <summary>Set by MainMenuController once the navigator exists; the knob rests while it is null.</summary>
        public StageMapNavigator Navigator { get; set; }

        /// <summary>Raised when the stick is grabbed (for the click sound).</summary>
        public Action Grabbed;

        private RectTransform artRoot;
        private RectTransform knob;
        private Image knobImage;
        private Vector2 knobSize;
        private RawImage wheel;
        private float wheelRoll; // art px, positive = ridges rolled up (zooming in)
        private Canvas canvas;
        private Vector2 tilt;
        private Vector2 tiltVelocity;
        private float press;
        private Vector2 lastCentre;
        private float lastZoom;
        private bool hasLast;

        private bool held;
        private bool hovered;
        private Vector2 stick; // pointer push, magnitude 0–1

        /// <summary>Builds the panel on the left edge of <paramref name="parent"/>. Returns null if the art is missing.</summary>
        public static StageMapSidePanel Create(Transform parent)
        {
            Sprite panelSprite = LoadSprite(PanelSpritePath);
            if (panelSprite == null)
            {
                Debug.LogWarning($"[StageMapSidePanel] Missing sprite Resources/{PanelSpritePath}");
                return null;
            }

            var go = new GameObject("StageMapSidePanel", typeof(RectTransform), typeof(AspectRatioFitter));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;

            // Full height; width follows the art's aspect
            var fitter = go.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            fitter.aspectRatio = ArtSize.x / ArtSize.y;

            var panel = go.AddComponent<StageMapSidePanel>();
            panel.Build(panelSprite, LoadSprite(KnobSpritePath));
            return panel;
        }

        private void Build(Sprite panelSprite, Sprite knobSprite)
        {
            canvas = GetComponentInParent<Canvas>();

            // Children are laid out in art pixels; LateUpdate scales this root to the panel's height
            var rootGo = new GameObject("Art", typeof(RectTransform));
            rootGo.transform.SetParent(transform, false);
            artRoot = (RectTransform)rootGo.transform;
            artRoot.anchorMin = artRoot.anchorMax = artRoot.pivot = Vector2.zero;
            artRoot.anchoredPosition = Vector2.zero;
            artRoot.sizeDelta = ArtSize;

            Image plate = CreateImage(artRoot, "Panel", panelSprite, Color.white);
            plate.rectTransform.anchorMin = Vector2.zero;
            plate.rectTransform.anchorMax = Vector2.one;
            plate.rectTransform.offsetMin = plate.rectTransform.offsetMax = Vector2.zero;

            BuildWheel();

            if (knobSprite == null) return;

            knobSize = knobSprite.rect.size;

            // Hidden under the knob at rest; stays put so a tilt shows the socket
            Image socket = CreateImage(artRoot, "KnobSocket", knobSprite, new Color(0f, 0f, 0f, 0.5f));
            PlaceAtDial(socket.rectTransform, knobSize);

            knobImage = CreateImage(artRoot, "Knob", knobSprite, Color.white);
            knob = knobImage.rectTransform;
            PlaceAtDial(knob, knobSize);

            // Invisible grab area over the whole dial, so the stick is easy to catch
            Image hitArea = CreateImage(artRoot, "StickHitArea", null, new Color(1f, 1f, 1f, 0f));
            hitArea.raycastTarget = true;
            PlaceAtDial(hitArea.rectTransform, Vector2.one * (DialRadius * 2f));
            var input = hitArea.gameObject.AddComponent<StageMapStickInput>();
            input.Pressed = OnStickPressed;
            input.Moved = OnStickMoved;
            input.Released = Release;
            input.HoverChanged = value => hovered = value;
        }

        /// <summary>The ridge repeat as a 1×4 tiling texture over the wheel; LateUpdate scrolls its uvRect.</summary>
        private void BuildWheel()
        {
            var texture = new Texture2D(1, WheelPattern.Length, TextureFormat.RGBA32, false)
            {
                name = "StageMapWheelRidges",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };
            texture.SetPixels32(WheelPattern);
            texture.Apply();

            var go = new GameObject("ScrollWheel", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(artRoot, false);
            wheel = go.GetComponent<RawImage>();
            wheel.texture = texture;
            wheel.raycastTarget = false;
            RectTransform rt = wheel.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = WheelRidges.position;
            rt.sizeDelta = WheelRidges.size;
            ApplyWheel();
        }

        private void ApplyWheel()
        {
            // Rolling up moves the ridges up: sample lower in the repeat. Whole pixels only.
            float period = WheelPattern.Length;
            float roll = Mathf.Repeat(Mathf.Round(wheelRoll), period);
            wheel.uvRect = new Rect(0f, -roll / period, 1f, WheelRidges.height / period);
        }

        private static void PlaceAtDial(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = DialCentre;
        }

        private void OnEnable()
        {
            hasLast = false;
            tilt = tiltVelocity = Vector2.zero;
            press = 0f;
        }

        private void OnDisable()
        {
            held = false;
            hovered = false;
            stick = Vector2.zero;
        }

        // ─────────────────────────────────────────────────────
        //  Stick input
        // ─────────────────────────────────────────────────────

        private void OnStickPressed(Vector2 screenPosition)
        {
            if (knob == null || Navigator == null || !Navigator.BeginStick()) return;

            held = true;
            OnStickMoved(screenPosition);
            Grabbed?.Invoke();
        }

        private void OnStickMoved(Vector2 screenPosition)
        {
            if (!held) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(artRoot, CrtDisplay.ScreenToPicture(screenPosition), EventCamera(), out Vector2 local))
                return;

            stick = Vector2.ClampMagnitude((local - DialCentre) / StickReach, 1f);
        }

        private void Release()
        {
            held = false;
            stick = Vector2.zero;
        }

        private Camera EventCamera()
        {
            if (canvas == null) return null;
            Canvas root = canvas.rootCanvas;
            return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }

        // ─────────────────────────────────────────────────────
        //  Update
        // ─────────────────────────────────────────────────────

        private void Update()
        {
            if (!held || Navigator == null) return;

            // Speed from how far past the dead zone the stick is pushed
            float amount = stick.magnitude;
            if (amount <= StickDeadZone) return;
            float push = Mathf.Pow((amount - StickDeadZone) / (1f - StickDeadZone), StickCurve);
            Navigator.StickPan(stick / amount * (push * StickMaxSpeed), Time.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            if (artRoot == null) return;

            float height = ((RectTransform)transform).rect.height;
            if (height > 0f)
            {
                float scale = height / ArtSize.y;
                artRoot.localScale = new Vector3(scale, scale, 1f);
            }

            if (knob == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (dt <= 0f) return;

            // Read the navigator after it has moved this frame
            Vector2 push = Vector2.zero;
            float zoomRate = 0f;
            if (Navigator != null && Navigator.isActiveAndEnabled)
            {
                Vector2 centre = Navigator.ViewCentre;
                float zoom = Navigator.Zoom;
                if (hasLast)
                {
                    Vector2 velocity = (centre - lastCentre) / dt;
                    float speed = velocity.magnitude;
                    if (speed > 0.0001f) push = velocity / speed * (1f - Mathf.Exp(-speed / TiltSpeed));
                    if (lastZoom > 0f && zoom > 0f)
                    {
                        float zoomStep = Mathf.Log(zoom / lastZoom);
                        zoomRate = zoomStep / dt;
                        wheelRoll += zoomStep * WheelPxPerZoomStep;
                    }
                }

                lastCentre = centre;
                lastZoom = zoom;
                hasLast = true;
            }

            if (wheel != null) ApplyWheel();

            if (held)
            {
                // In the hand: the knob sits exactly where it is pushed
                tilt = stick * MaxTilt;
                tiltVelocity = Vector2.zero;
            }
            else
            {
                // Spring toward the push, or back to the middle once the view settles
                tiltVelocity += ((push * MaxTilt - tilt) * Stiffness - tiltVelocity * Damping) * dt;
                tilt += tiltVelocity * dt;
                tilt = Vector2.ClampMagnitude(tilt, MaxTilt);
            }

            float targetPress = Mathf.Clamp(zoomRate * ZoomPress, -MaxPress, MaxPress);
            press = Mathf.Lerp(press, targetPress, 1f - Mathf.Exp(-14f * dt));

            // Whole art pixels, like the rest of the pixel art. Hover lifts the knob a pixel to say it can be grabbed.
            knob.anchoredPosition = DialCentre + new Vector2(Mathf.Round(tilt.x), Mathf.Round(tilt.y));
            float sink = Mathf.Round(press * knobSize.y * 0.5f) - (hovered && !held ? 1f : 0f);
            knob.sizeDelta = knobSize - Vector2.one * (sink * 2f);
            float tint = Mathf.Max(Mathf.Clamp01(press / MaxPress), held ? GrabTint : 0f);
            knobImage.color = Color.Lerp(Color.white, PressedTint, tint);
        }

        private static Image CreateImage(Transform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Sprite LoadSprite(string path)
        {
            Sprite[] sprites = Resources.LoadAll<Sprite>(path);
            Sprite sprite = sprites != null && sprites.Length > 0 ? sprites[0] : Resources.Load<Sprite>(path);
            if (sprite != null && sprite.texture != null) sprite.texture.filterMode = FilterMode.Point;
            return sprite;
        }
    }

    /// <summary>Forwards presses, drags, releases and hover on the joystick's dial to StageMapSidePanel.</summary>
    public class StageMapStickInput : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Action<Vector2> Pressed;
        public Action<Vector2> Moved;
        public Action Released;
        public Action<bool> HoverChanged;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) Pressed?.Invoke(eventData.position);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) Released?.Invoke();
        }

        // Claims the drag so it doesn't go to anything else, and keeps tracking outside the dial
        public void OnBeginDrag(PointerEventData eventData) => Moved?.Invoke(eventData.position);
        public void OnDrag(PointerEventData eventData) => Moved?.Invoke(eventData.position);

        public void OnPointerEnter(PointerEventData eventData) => HoverChanged?.Invoke(true);
        public void OnPointerExit(PointerEventData eventData) => HoverChanged?.Invoke(false);
    }
}
