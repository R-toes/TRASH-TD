using UnityEngine;
using TrashTD.Data;
using TrashTD.UI;

namespace TrashTD.Combat
{
    public sealed class FloatingCombatNumber : MonoBehaviour
    {
        private const float Lifetime = 0.9f;
        private const float RiseDistance = 0.55f;

        private TextMesh label;
        private Vector3 startPosition;
        private Color baseColor;
        private float elapsed;

        public static void Show(Vector3 position, int amount, DamageType damageType, bool healing = false)
        {
            if (amount <= 0) return;

            GameObject numberObject = new GameObject("FloatingCombatNumber");
            numberObject.transform.position = position + Vector3.up * 0.35f + Vector3.right * Random.Range(-0.12f, 0.12f);

            TextMesh text = numberObject.AddComponent<TextMesh>();
            text.text = healing ? $"+{amount}" : $"-{amount}";
            UIFontHelper.Apply(text, FontStyle.Normal);
            text.fontSize = 24;
            text.characterSize = 0.075f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontStyle = FontStyle.Normal;
            text.color = healing
                ? new Color(0.25f, 1f, 0.38f)
                : damageType == DamageType.Arts ? new Color(0.72f, 0.38f, 1f) : Color.white;

            MeshRenderer meshRenderer = numberObject.GetComponent<MeshRenderer>();
            meshRenderer.sortingOrder = 50;

            FloatingCombatNumber animation = numberObject.AddComponent<FloatingCombatNumber>();
            animation.Initialize(text);
        }

        public static void ShowMiss(Vector3 position)
        {
            GameObject numberObject = new GameObject("FloatingCombatNumber");
            numberObject.transform.position = position + Vector3.up * 0.35f + Vector3.right * Random.Range(-0.12f, 0.12f);

            TextMesh text = numberObject.AddComponent<TextMesh>();
            text.text = "MISS";
            UIFontHelper.Apply(text, FontStyle.Normal);
            text.fontSize = 24;
            text.characterSize = 0.075f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontStyle = FontStyle.Bold;
            text.color = new Color(1f, 0.85f, 0.35f);

            MeshRenderer meshRenderer = numberObject.GetComponent<MeshRenderer>();
            meshRenderer.sortingOrder = 50;

            FloatingCombatNumber animation = numberObject.AddComponent<FloatingCombatNumber>();
            animation.Initialize(text);
        }

        public static void ShowText(Vector3 position, string message, Color color)
        {
            GameObject numberObject = new GameObject("FloatingCombatNumber");
            numberObject.transform.position = position + Vector3.up * 0.35f + Vector3.right * Random.Range(-0.12f, 0.12f);

            TextMesh text = numberObject.AddComponent<TextMesh>();
            text.text = message;
            UIFontHelper.Apply(text, FontStyle.Normal);
            text.fontSize = 24;
            text.characterSize = 0.075f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontStyle = FontStyle.Bold;
            text.color = color;

            MeshRenderer meshRenderer = numberObject.GetComponent<MeshRenderer>();
            meshRenderer.sortingOrder = 50;

            FloatingCombatNumber animation = numberObject.AddComponent<FloatingCombatNumber>();
            animation.Initialize(text);
        }

        private void Initialize(TextMesh text)
        {
            label = text;
            startPosition = transform.position;
            baseColor = label.color;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / Lifetime);
            transform.position = startPosition + Vector3.up * (RiseDistance * normalizedTime);
            label.color = new Color(baseColor.r, baseColor.g, baseColor.b, 1f - normalizedTime);

            if (elapsed >= Lifetime)
            {
                Destroy(gameObject);
            }
        }
    }

    /// <summary>
    /// Briefly overlays a pulsing acid-green tint on a unit when it takes trap damage.
    /// </summary>
    public sealed class AcidDamageFlash : MonoBehaviour
    {
        private static readonly Color AcidColor = new Color(0.2f, 1f, 0.05f, 1f);
        private const float FlashDuration = 0.3f;

        private readonly System.Collections.Generic.List<SpriteRenderer> sourceRenderers =
            new System.Collections.Generic.List<SpriteRenderer>();
        private readonly System.Collections.Generic.List<SpriteRenderer> overlayRenderers =
            new System.Collections.Generic.List<SpriteRenderer>();
        private float remainingFlashTime;

        public static void Flash(GameObject target)
        {
            if (target == null) return;

            AcidDamageFlash flash = target.GetComponent<AcidDamageFlash>();
            if (flash == null) flash = target.AddComponent<AcidDamageFlash>();
            flash.StartFlash();
        }

        private void StartFlash()
        {
            if (sourceRenderers.Count == 0)
            {
                CacheRenderers();
            }

            remainingFlashTime = FlashDuration;
            UpdateOverlayRenderers(1f);
        }

        private void CacheRenderers()
        {
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
            foreach (SpriteRenderer source in renderers)
            {
                if (source == null) continue;

                GameObject overlayObject = new GameObject("AcidDamageOverlay");
                overlayObject.transform.SetParent(source.transform, false);

                SpriteRenderer overlay = overlayObject.AddComponent<SpriteRenderer>();
                overlay.sprite = source.sprite;
                overlay.flipX = source.flipX;
                overlay.flipY = source.flipY;
                overlay.sortingLayerID = source.sortingLayerID;
                overlay.sortingOrder = source.sortingOrder + 1;
                overlay.maskInteraction = source.maskInteraction;
                overlay.drawMode = source.drawMode;
                overlay.size = source.size;
                overlay.tileMode = source.tileMode;
                overlay.color = Color.clear;
                overlay.enabled = false;

                sourceRenderers.Add(source);
                overlayRenderers.Add(overlay);
            }
        }

        private void Update()
        {
            if (remainingFlashTime <= 0f) return;

            remainingFlashTime = Mathf.Max(0f, remainingFlashTime - Time.deltaTime);
            float progress = 1f - remainingFlashTime / FlashDuration;
            float fade = Mathf.Sin(progress * Mathf.PI);
            float pulse = 0.4f + 0.6f * (0.5f + 0.5f * Mathf.Sin(progress * Mathf.PI * 8f));
            UpdateOverlayRenderers(fade * pulse);

            if (remainingFlashTime <= 0f)
            {
                for (int i = 0; i < overlayRenderers.Count; i++)
                {
                    if (overlayRenderers[i] != null) overlayRenderers[i].enabled = false;
                }
            }
        }

        private void UpdateOverlayRenderers(float intensity)
        {
            for (int i = 0; i < sourceRenderers.Count; i++)
            {
                SpriteRenderer source = sourceRenderers[i];
                SpriteRenderer overlay = overlayRenderers[i];
                if (source == null || overlay == null) continue;

                overlay.sprite = source.sprite;
                overlay.flipX = source.flipX;
                overlay.flipY = source.flipY;
                overlay.sortingLayerID = source.sortingLayerID;
                overlay.sortingOrder = source.sortingOrder + 1;
                overlay.maskInteraction = source.maskInteraction;
                overlay.drawMode = source.drawMode;
                overlay.size = source.size;
                overlay.tileMode = source.tileMode;
                overlay.color = new Color(AcidColor.r, AcidColor.g, AcidColor.b, AcidColor.a * intensity);
                overlay.enabled = source.enabled && source.gameObject.activeInHierarchy && intensity > 0f;
            }
        }
    }
}