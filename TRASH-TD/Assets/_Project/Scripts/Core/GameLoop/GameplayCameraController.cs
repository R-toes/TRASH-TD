using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TrashTD.UI;

namespace TrashTD.Core.GameLoop
{
    /// <summary>
    /// Gameplay camera on top of the framing StageBootstrapper computes (the "home" view):
    ///   - Look-around: the camera drifts a little toward the cursor (or a held touch), smoothly, so the player can
    ///     peek at the map edges behind the HUD bars. Zoomed in, the same cursor position reaches all the way to
    ///     the map's edges, so moving the cursor looks around the zoomed view.
    ///   - Zoom: mouse wheel (pinch on touch screens) between the home view and <see cref="MaxZoom"/>.
    ///   - Shake: <see cref="Shake"/> adds a decaying positional shake (used when lives are lost).
    /// Input is ignored while paused, while the draft screen or results are up, or with the pointer over UI.
    /// Everything runs on unscaled time so shakes still play out on the stopped results screen.
    /// </summary>
    public sealed class GameplayCameraController : MonoBehaviour
    {
        public static GameplayCameraController Instance { get; private set; }

        public const float MaxZoom = 2.25f;
        private const float ZoomStep = 0.15f;          // per wheel notch, as a fraction of the current zoom
        private const float LookNudge = 0.035f;         // extra look range at home zoom, as a fraction of the view height
        private const float LookDeadZone = 0.2f;        // cursor distance from the centre (0..1) that keeps the camera still
        private const float FollowSharpness = 4.5f;
        private const float ReturnSharpness = 1.6f;     // slower glide back to the centre (cursor on the HUD bars)
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
        private Vector2 lookInput;          // -1..1 per axis, after dead zone and easing
        private bool returningHome;         // cursor is on the top bar / deck bar: glide slowly back to centre
        private Vector2 offset;             // current camera offset from the home framing
        private float pinchDistance = -1f;

        private float shakeStrength;
        private float shakeTime;

        public float Zoom => zoom;

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
            offset = Vector2.zero;
            lookInput = Vector2.zero;
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

            if (InputAllowed()) ReadInput();
            else pinchDistance = -1f;

            // Zoom eases toward its target
            zoom = Mathf.Lerp(zoom, targetZoom, 1f - Mathf.Exp(-ZoomSharpness * dt));
            float size = homeSize / zoom;

            // Look-around offset: home nudge + whatever range the zoom opens up, per axis
            float aspect = cam.aspect > 0f ? cam.aspect : 16f / 9f;
            Vector2 range = GetLookRange(size, aspect);
            Vector2 target = Vector2.Scale(lookInput, range);
            float sharpness = returningHome ? ReturnSharpness : FollowSharpness;
            offset = Vector2.Lerp(offset, target, 1f - Mathf.Exp(-sharpness * dt));

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

        private Vector2 GetLookRange(float size, float aspect)
        {
            float nudge = LookNudge * size * 2f;

            // Visible playable area (between the HUD bars) vs the map art, per axis
            float playHalfWidth = size * aspect;
            float playHalfHeight = size * (1f - TopHudFraction - BottomHudFraction);
            float mapHalfWidth = mapBounds.width * 0.5f + EdgeMargin;
            float mapHalfHeight = mapBounds.height * 0.5f + EdgeMargin;

            return new Vector2(
                nudge + Mathf.Max(0f, mapHalfWidth - playHalfWidth),
                nudge + Mathf.Max(0f, mapHalfHeight - playHalfHeight));
        }

        private void ReadInput()
        {
            Vector2? pointer = null;

            // Touch: a held finger acts as the cursor; two fingers pinch to zoom
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.touches.Count > 1 &&
                touchscreen.touches[0].press.isPressed && touchscreen.touches[1].press.isPressed)
            {
                float distance = Vector2.Distance(touchscreen.touches[0].position.ReadValue(), touchscreen.touches[1].position.ReadValue());
                if (pinchDistance > 0f && distance > 0f)
                {
                    targetZoom = Mathf.Clamp(targetZoom * (distance / pinchDistance), 1f, MaxZoom);
                }
                pinchDistance = distance;
                return;
            }
            pinchDistance = -1f;

            if (touchscreen != null && touchscreen.primaryTouch.press.isPressed)
            {
                pointer = touchscreen.primaryTouch.position.ReadValue();
            }

            Mouse mouse = Mouse.current;
            if (pointer == null && mouse != null)
            {
                pointer = mouse.position.ReadValue();

                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f && !PointerOverUi())
                {
                    float notches = Mathf.Sign(scroll) * Mathf.Max(1f, Mathf.Abs(scroll) / 120f);
                    targetZoom = Mathf.Clamp(targetZoom * Mathf.Pow(1f + ZoomStep, notches), 1f, MaxZoom);
                }

                // Hold still while dragging (placing / setting an operator's facing)
                if (mouse.leftButton.isPressed || mouse.rightButton.isPressed) return;
            }

            if (pointer == null) return;
            Vector2 picture = CrtDisplay.ScreenToPicture(pointer.Value);
            if (Screen.width <= 0 || Screen.height <= 0) return;

            // Outside the window, or on the top bar / deck bar: glide slowly back to the default centre view
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

        private GameplayHUDUI hud;
        private CardDraftOverlayUI draft;

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
