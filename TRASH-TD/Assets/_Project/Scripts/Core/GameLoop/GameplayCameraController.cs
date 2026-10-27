using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TrashTD.UI;

namespace TrashTD.Core.GameLoop
{
    /// <summary>
    /// Gameplay camera on top of the framing StageBootstrapper computes (the "home" view):
    ///   - Fully zoomed out: look-around. The camera drifts a little toward the cursor (or a held touch), smoothly,
    ///     so the player can peek at the map edges behind the HUD bars. On the HUD bars or outside the window it
    ///     glides slowly back to the centre.
    ///   - Zoomed in: map navigation like the stage-select map. Drag to pan (with inertia), the wheel zooms toward
    ///     the cursor, and the view stays within the map art. A left-drag only starts from the map (not UI) while no
    ///     card is being placed, and only after the pointer moves a few pixels, so clicks still select and deploy.
    ///     Right / middle drags always pan.
    ///   - Zoom: mouse wheel (pinch on touch screens) between the home view and <see cref="MaxZoom"/>.
    ///   - Shake: <see cref="Shake"/> adds a decaying positional shake (used when lives are lost).
    /// Input is ignored while paused, while the draft screen or results are up. Unscaled time throughout, so shakes
    /// still play out on the stopped results screen.
    /// </summary>
    public sealed class GameplayCameraController : MonoBehaviour
    {
        public static GameplayCameraController Instance { get; private set; }

        public const float MaxZoom = 2.25f;
        private const float ZoomStep = 0.15f;          // per wheel notch, as a fraction of the current zoom
        private const float DragZoomThreshold = 1.02f;  // above this (target) zoom the camera is navigated by dragging
        private const float LookNudge = 0.035f;         // look range at home zoom, as a fraction of the view height
        private const float LookDeadZone = 0.2f;        // cursor distance from the centre (0..1) that keeps the camera still
        private const float FollowSharpness = 4.5f;
        private const float ReturnSharpness = 1.6f;     // slower glide back to the centre (cursor on the HUD bars)
        private const float DragSharpness = 18f;
        private const float DragStartPixels = 8f;       // pointer travel before a press becomes a pan
        private const float InertiaDamping = 5f;
        private const float ZoomSharpness = 9f;
        private const float EdgeMargin = 0.3f;          // world units the view may go past the map art

        // HUD bars covering the view at the 1080 reference: top bar 90 px, deck bar 170 px
        private const float TopHudFraction = 90f / 1080f;
        private const float BottomHudFraction = 170f / 1080f;
        private static readonly Vector2 SpeedPanelSize = new Vector2(410f, 331f);

        private Camera cam;
        private Vector2 homePosition;
        private float homeSize;
        private Rect mapBounds;
        private bool initialized;

        private float zoom = 1f;
        private float targetZoom = 1f;
        private Vector2 offset;             // current camera offset from the home framing

        // Look-around (zoomed out)
        private Vector2 lookInput;          // -1..1 per axis, after dead zone and easing
        private bool returningHome;         // cursor is on the top bar / deck bar: glide slowly back to centre

        // Drag navigation (zoomed in)
        private Vector2 dragTarget;
        private Vector2 dragVelocity;       // world units / s, for inertia after release
        private bool pointerDown;
        private bool dragging;
        private Vector2 pressStart;
        private Vector2 lastPointer;
        private float lastDragTime;

        private float pinchDistance = -1f;
        private float shakeStrength;
        private float shakeTime;

        private GameplayHUDUI hud;
        private CardDraftOverlayUI draft;
        private StageBootstrapper bootstrapper;

        public float Zoom => zoom;
        private bool DragMode => targetZoom > DragZoomThreshold;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Sets the home view (position and orthographic size) and the map art bounds to look around in.</summary>
        public void Initialize(Camera camera, Vector2 position, float orthographicSize, Rect bounds)
        {
            cam = camera;
            homePosition = position;
            homeSize = Mathf.Max(0.1f, orthographicSize);
            mapBounds = bounds;
            zoom = targetZoom = 1f;
            offset = dragTarget = dragVelocity = Vector2.zero;
            lookInput = Vector2.zero;
            pointerDown = dragging = false;
            initialized = cam != null;
        }

        /// <summary>Adds a short shake. Strength is in world units at home zoom; overlapping shakes keep the strongest.</summary>
        public void Shake(float strength, float duration = 0.35f)
        {
            shakeStrength = Mathf.Max(shakeStrength * (shakeTime > 0f ? 1f : 0f), strength);
            shakeTime = Mathf.Max(shakeTime, duration);
        }

        public void ResetView()
        {
            targetZoom = 1f;
        }

        private void LateUpdate()
        {
            if (!initialized || cam == null) return;
            float dt = Time.unscaledDeltaTime;

            if (InputAllowed())
            {
                ReadInput(dt);
            }
            else
            {
                pinchDistance = -1f;
                pointerDown = dragging = false;
            }

            // Zoom eases toward its target
            zoom = Mathf.Lerp(zoom, targetZoom, 1f - Mathf.Exp(-ZoomSharpness * dt));
            float size = homeSize / zoom;
            float aspect = cam.aspect > 0f ? cam.aspect : 16f / 9f;
            Vector2 range = GetViewRange(size, aspect);

            if (DragMode)
            {
                // Drag navigation: inertia after release, kept inside the map
                if (!dragging && dragVelocity.sqrMagnitude > 1e-6f)
                {
                    dragTarget += dragVelocity * dt;
                    dragVelocity *= Mathf.Exp(-InertiaDamping * dt);
                }
                dragTarget = new Vector2(Mathf.Clamp(dragTarget.x, -range.x, range.x), Mathf.Clamp(dragTarget.y, -range.y, range.y));
                offset = Vector2.Lerp(offset, dragTarget, 1f - Mathf.Exp(-DragSharpness * dt));
            }
            else
            {
                // Look-around toward the cursor
                Vector2 target = Vector2.Scale(lookInput, range);
                float sharpness = returningHome ? ReturnSharpness : FollowSharpness;
                offset = Vector2.Lerp(offset, target, 1f - Mathf.Exp(-sharpness * dt));
                dragTarget = offset;
                dragVelocity = Vector2.zero;
            }

            // Shake decays quickly; scaled with the view so it reads the same at any zoom
            Vector2 shake = Vector2.zero;
            if (shakeTime > 0f)
            {
                shakeTime -= dt;
                float k = Mathf.Clamp01(shakeTime / 0.35f);
                float amplitude = shakeStrength * k * k * (size / homeSize);
                shake = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * amplitude;
                if (shakeTime <= 0f) shakeStrength = 0f;
            }

            Vector2 position = homePosition + GetZoomCentreShift(size) + offset + shake;
            cam.orthographicSize = size;
            cam.transform.position = new Vector3(position.x, position.y, cam.transform.position.z);
        }

        /// <summary>
        /// When zoomed in, the home framing (which keeps the playable area between the HUD bars centred on the map)
        /// shifts with the size so the zoom stays centred on the playable area instead of the screen centre.
        /// </summary>
        private Vector2 GetZoomCentreShift(float size)
        {
            float hudShift = (BottomHudFraction - TopHudFraction) * (homeSize - size);
            return new Vector2(0f, hudShift);
        }

        /// <summary>
        /// How far the view may move from the home framing per axis: a small nudge, plus whatever the zoom opens up
        /// between the visible playable area (between the HUD bars) and the map art.
        /// </summary>
        private Vector2 GetViewRange(float size, float aspect)
        {
            float nudge = LookNudge * size * 2f;
            float playHalfWidth = size * aspect;
            float playHalfHeight = size * (1f - TopHudFraction - BottomHudFraction);
            float mapHalfWidth = mapBounds.width * 0.5f + EdgeMargin;
            float mapHalfHeight = mapBounds.height * 0.5f + EdgeMargin;

            return new Vector2(
                nudge + Mathf.Max(0f, mapHalfWidth - playHalfWidth),
                nudge + Mathf.Max(0f, mapHalfHeight - playHalfHeight));
        }

        // ─────────────────────────────────────────────────────
        //  Input
        // ─────────────────────────────────────────────────────

        private void ReadInput(float dt)
        {
            if (Screen.width <= 0 || Screen.height <= 0) return;

            // Touch: two fingers pinch to zoom
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.touches.Count > 1 &&
                touchscreen.touches[0].press.isPressed && touchscreen.touches[1].press.isPressed)
            {
                float distance = Vector2.Distance(touchscreen.touches[0].position.ReadValue(), touchscreen.touches[1].position.ReadValue());
                if (pinchDistance > 0f && distance > 0f) SetTargetZoom(targetZoom * (distance / pinchDistance), null);
                pinchDistance = distance;
                pointerDown = dragging = false;
                return;
            }
            pinchDistance = -1f;

            // Pointer: a held touch, otherwise the mouse
            Vector2? pointer = null;
            bool pressed = false;
            bool pressedThisFrame = false;
            bool leftPress = false;
            if (touchscreen != null && touchscreen.primaryTouch.press.isPressed)
            {
                pointer = touchscreen.primaryTouch.position.ReadValue();
                pressed = true;
                pressedThisFrame = touchscreen.primaryTouch.press.wasPressedThisFrame;
                leftPress = pressedThisFrame;
            }

            Mouse mouse = Mouse.current;
            if (pointer == null && mouse != null)
            {
                pointer = mouse.position.ReadValue();
                pressed = mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed;
                leftPress = mouse.leftButton.wasPressedThisFrame;
                pressedThisFrame = leftPress || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame;

                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f && !PointerOverUi())
                {
                    float notches = Mathf.Sign(scroll) * Mathf.Max(1f, Mathf.Abs(scroll) / 120f);
                    SetTargetZoom(targetZoom * Mathf.Pow(1f + ZoomStep, notches), CrtDisplay.ScreenToPicture(pointer.Value));
                }
            }

            if (pointer == null) return;
            Vector2 picture = CrtDisplay.ScreenToPicture(pointer.Value);

            if (DragMode) ReadDrag(picture, pressed, pressedThisFrame, leftPress, dt);
            else ReadLook(picture, pressed);
        }

        /// <summary>Zoomed in: press on the map, move past the threshold, and the map follows the pointer.</summary>
        private void ReadDrag(Vector2 picture, bool pressed, bool pressedThisFrame, bool leftPress, float dt)
        {
            returningHome = false;
            lookInput = Vector2.zero;

            if (pressedThisFrame)
            {
                bool blocked = PointerOverUi() || (leftPress && IsPlacingCard());
                pointerDown = !blocked;
                dragging = false;
                pressStart = lastPointer = picture;
                if (pointerDown) dragVelocity = Vector2.zero;
            }

            if (pointerDown && pressed)
            {
                if (!dragging && (picture - pressStart).sqrMagnitude > DragStartPixels * DragStartPixels) dragging = true;
                if (dragging)
                {
                    float worldPerPixel = 2f * cam.orthographicSize / Screen.height;
                    Vector2 worldDelta = (picture - lastPointer) * worldPerPixel;
                    dragTarget -= worldDelta;
                    if (dt > 0f) dragVelocity = Vector2.Lerp(dragVelocity, -worldDelta / dt, 0.5f);
                    if (worldDelta.sqrMagnitude > 0f) lastDragTime = Time.unscaledTime;
                }
                lastPointer = picture;
                return;
            }

            if (!pressed)
            {
                // A pause before letting go cancels the fling
                if (dragging && Time.unscaledTime - lastDragTime > 0.08f) dragVelocity = Vector2.zero;
                pointerDown = dragging = false;
            }
        }

        /// <summary>Fully zoomed out: drift toward the cursor; glide home on the HUD bars or outside the window.</summary>
        private void ReadLook(Vector2 picture, bool pressed)
        {
            pointerDown = dragging = false;

            // Hold still while a button is held (placing / setting an operator's facing)
            if (pressed) return;

            bool outside = picture.x < 0f || picture.y < 0f || picture.x > Screen.width || picture.y > Screen.height;
            float heightFraction = picture.y / Screen.height;
            bool onHudBar = heightFraction < BottomHudFraction || heightFraction > 1f - TopHudFraction;

            // The deck bar's bottom-right speed panel stands taller than the bar (410 x 331 at 1080). HUD canvas
            // scale: ScaleWithScreenSize, 1920x1080 reference, width/height match 0.5.
            float uiScale = Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(Screen.width / 1920f, 2f), Mathf.Log(Screen.height / 1080f, 2f), 0.5f));
            if (picture.x > Screen.width - SpeedPanelSize.x * uiScale && picture.y < SpeedPanelSize.y * uiScale) onHudBar = true;
            returningHome = outside || onHudBar;
            if (returningHome)
            {
                lookInput = Vector2.zero;
                return;
            }

            var normalized = new Vector2(picture.x / Screen.width * 2f - 1f, picture.y / Screen.height * 2f - 1f);
            lookInput = new Vector2(ShapeAxis(normalized.x), ShapeAxis(normalized.y));
        }

        /// <summary>
        /// Changes the zoom target. With a cursor, a zoom that stays in drag mode keeps the point under the cursor
        /// roughly in place (like the stage-select map).
        /// </summary>
        private void SetTargetZoom(float value, Vector2? cursorPicture)
        {
            float previous = targetZoom;
            targetZoom = Mathf.Clamp(value, 1f, MaxZoom);
            if (!DragMode || Mathf.Approximately(previous, targetZoom)) return;

            if (previous <= DragZoomThreshold) dragTarget = offset;
            if (cursorPicture.HasValue)
            {
                float oldSize = homeSize / previous;
                float newSize = homeSize / targetZoom;
                float worldPerPixel = 2f * oldSize / Screen.height;
                Vector2 fromCentre = (cursorPicture.Value - new Vector2(Screen.width, Screen.height) * 0.5f) * worldPerPixel;
                dragTarget += fromCentre * (1f - newSize / oldSize);
            }
            dragVelocity = Vector2.zero;
        }

        /// <summary>Dead zone around the centre, then a smooth ramp to the edge.</summary>
        private static float ShapeAxis(float value)
        {
            float magnitude = Mathf.Clamp01((Mathf.Abs(value) - LookDeadZone) / (1f - LookDeadZone));
            float eased = magnitude * magnitude * (3f - 2f * magnitude);
            return Mathf.Sign(value) * eased;
        }

        private static bool PointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private bool IsPlacingCard()
        {
            if (bootstrapper == null) bootstrapper = FindAnyObjectByType<StageBootstrapper>();
            return bootstrapper != null && bootstrapper.IsPlacementActive;
        }

        private bool InputAllowed()
        {
            GameManager manager = GameManager.Instance;
            if (manager == null || manager.CurrentState != GamePlayState.Playing) return false;

            if (hud == null) hud = FindAnyObjectByType<GameplayHUDUI>();
            if (hud != null && (hud.IsPaused || hud.IsEnvironmentBriefingActive)) return false;

            if (draft == null) draft = FindAnyObjectByType<CardDraftOverlayUI>();
            if (draft != null && draft.IsShowing) return false;

            return Application.isFocused;
        }
    }
}
