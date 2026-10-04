using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TrashTD.UI
{
    /// <summary>
    /// Hover / press / keyboard-focus feedback for menu buttons and cards:
    /// smooth scale, tint, outline glow, a springy click punch and a "denied" wobble.
    /// Works with or without a Button on the same object. Uses unscaled time.
    /// </summary>
    [DisallowMultipleComponent]
    public class MenuButtonFeedback : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        IPointerClickHandler, ISubmitHandler, ISelectHandler, IDeselectHandler
    {
        private const float WobbleDuration = 0.38f;
        private const float PunchDuration = 0.6f;

        [Header("Scale")]
        public float hoverScale = 1.04f;
        public float pressScale = 0.96f;
        /// <summary>Resting scale (used by the difficulty picker to enlarge the selected card).</summary>
        public float restScale = 1f;
        /// <summary>Extra multiplier used by entrance animations.</summary>
        public float introScale = 1f;
        public float smoothing = 16f;

        [Header("Tint")]
        public float hoverBrighten = 0.12f;
        public float pressDarken = 0.18f;

        [Header("Glow")]
        public Outline glowOutline;
        public Color glowColor = new Color(0.18f, 0.82f, 0.45f, 0.9f);
        public bool glowAlways;

        public bool hoverEnabled = true;
        public bool respondToFocus = true;

        /// <summary>Raised when the pointer enters (sound hook).</summary>
        public System.Action Hovered;
        /// <summary>Raised on click / submit (sound hook).</summary>
        public System.Action Clicked;

        private Graphic graphic;
        private Selectable selectable;
        private Color baseColor = Color.white;
        private bool hovered;
        private bool pressed;
        private bool focused;
        private float scale = 1f;
        private float glow;
        private bool punching;
        private float punchTime;
        private float wobbleTime;

        private void Awake()
        {
            graphic = GetComponent<Graphic>();
            selectable = GetComponent<Selectable>();
            if (graphic != null) baseColor = graphic.color;
            scale = restScale;
        }

        /// <summary>Changes the color the button rests at (hover/press are derived from it).</summary>
        public void SetBaseColor(Color color)
        {
            baseColor = color;
        }

        /// <summary>Quick side-to-side shake, used for locked / denied interactions.</summary>
        public void Wobble()
        {
            wobbleTime = WobbleDuration;
        }

        private bool IsInteractable()
        {
            return selectable == null || selectable.IsInteractable();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float k = 1f - Mathf.Exp(-smoothing * dt);

            bool active = hoverEnabled && IsInteractable();
            bool hot = active && (hovered || (respondToFocus && focused));

            // Scale
            float targetScale = restScale;
            if (active && pressed) targetScale *= pressScale;
            else if (hot) targetScale *= hoverScale;
            scale = Mathf.Lerp(scale, targetScale, k);

            float punchOffset = 0f;
            if (punching)
            {
                punchTime += dt;
                if (punchTime >= PunchDuration) punching = false;
                else punchOffset = Mathf.Sin(punchTime * 28f) * Mathf.Exp(-punchTime * 9f) * 0.09f;
            }

            transform.localScale = Vector3.one * (scale * introScale + punchOffset);

            // Tint
            if (graphic != null)
            {
                Color target = baseColor;
                if (active && pressed) target = Color.Lerp(baseColor, Color.black, pressDarken);
                else if (hot) target = Color.Lerp(baseColor, Color.white, hoverBrighten);
                target.a = baseColor.a;
                graphic.color = Color.Lerp(graphic.color, target, k);
            }

            // Glow
            if (glowOutline != null)
            {
                float glowTarget = (glowAlways || hot) ? 1f : 0f;
                glow = Mathf.Lerp(glow, glowTarget, k);
                Color c = glowColor;
                c.a = glowColor.a * glow;
                if (glowOutline.effectColor != c) glowOutline.effectColor = c;
            }

            // Wobble
            if (wobbleTime > 0f)
            {
                wobbleTime -= dt;
                float w = Mathf.Max(0f, wobbleTime) / WobbleDuration;
                float angle = wobbleTime > 0f ? Mathf.Sin((1f - w) * 42f) * 4f * w : 0f;
                transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        private void Trigger()
        {
            punching = true;
            punchTime = 0f;
            Clicked?.Invoke();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            if (hoverEnabled && IsInteractable()) Hovered?.Invoke();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            pressed = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pressed = true;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pressed = false;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && IsInteractable()) Trigger();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (IsInteractable()) Trigger();
        }

        public void OnSelect(BaseEventData eventData)
        {
            focused = true;
        }

        public void OnDeselect(BaseEventData eventData)
        {
            focused = false;
        }

        private void OnDisable()
        {
            // Panels get deactivated without a pointer-exit, so clear state here.
            hovered = false;
            pressed = false;
            focused = false;
            punching = false;
            wobbleTime = 0f;
            glow = 0f;
            scale = restScale;

            transform.localScale = Vector3.one * restScale;
            transform.localRotation = Quaternion.identity;

            if (graphic != null) graphic.color = baseColor;

            if (glowOutline != null)
            {
                Color c = glowColor;
                c.a = glowAlways ? glowColor.a : 0f;
                glowOutline.effectColor = c;
            }
        }
    }
}