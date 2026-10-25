using UnityEngine;

namespace TrashTD.Combat
{
    /// <summary>
    /// Telegraph for incoming enemy ordnance (THE ERADICATOR's rockets): a faint dithered fill and a target ring
    /// on the blast area that blinks faster as impact nears, with a second ring closing in on it. Lasts until
    /// the rocket lands. Runs on scaled time.
    /// </summary>
    public sealed class BlastWarningVisual : MonoBehaviour
    {
        private const int SortingOrder = 25; // AoE area layer

        private SpriteRenderer fill;
        private SpriteRenderer ring;
        private SpriteRenderer closing;
        private Sprite[] closingFrames;
        private Color color;
        private float duration;
        private float elapsed;

        public static void Play(Vector3 center, float radius, Color color, float duration)
        {
            if (radius <= 0f || duration <= 0f) return;

            var visualObject = new GameObject("BlastWarningVisual");
            visualObject.transform.position = PixelParticles.Snap(center);
            var visual = visualObject.AddComponent<BlastWarningVisual>();

            int radiusPx = Mathf.Max(3, Mathf.RoundToInt(radius * CombatFxArt.PixelsPerUnit));
            visual.fill = visual.AddRenderer("Fill", CombatFxArt.AreaCircle(radiusPx));
            visual.ring = visual.AddRenderer("Ring", CombatFxArt.TargetRing(radiusPx));
            visual.closingFrames = CombatFxArt.ShockwaveFrames(radiusPx);
            visual.closing = visual.AddRenderer("Closing", visual.closingFrames[visual.closingFrames.Length - 1]);
            visual.color = color;
            visual.duration = duration;
            visual.Apply();
        }

        private SpriteRenderer AddRenderer(string objectName, Sprite sprite)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(transform, false);
            var spriteRenderer = child.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            spriteRenderer.sortingOrder = SortingOrder;
            return spriteRenderer;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            if (elapsed >= duration)
            {
                Destroy(gameObject);
                return;
            }

            Apply();
        }

        private void Apply()
        {
            float t = Mathf.Clamp01(elapsed / duration);

            // Blinks faster as the rocket closes in
            float blinkHz = Mathf.Lerp(4f, 12f, t);
            bool lit = Mathf.Repeat(elapsed * blinkHz, 1f) < 0.6f;

            Color ringColor = Color.Lerp(color, Color.white, lit ? 0.35f : 0f);
            ringColor.a = lit ? 0.95f : 0.45f;
            ring.color = ringColor;

            Color fillColor = color;
            fillColor.a = Mathf.Lerp(0.12f, 0.3f, t);
            fill.color = fillColor;

            // The closing ring shrinks from the full radius to the centre
            int frame = Mathf.Clamp(closingFrames.Length - 1 - Mathf.FloorToInt(t * closingFrames.Length), 0, closingFrames.Length - 1);
            closing.sprite = closingFrames[frame];
            Color closingColor = color;
            closingColor.a = 0.8f;
            closing.color = closingColor;
        }
    }
}
