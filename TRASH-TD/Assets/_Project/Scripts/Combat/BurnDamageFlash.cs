using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Combat
{
    /// <summary>
    /// Renders a fiery orange/red flash overlay on a target when taking burning damage.
    /// </summary>
    public sealed class BurnDamageFlash : MonoBehaviour
    {
        private static readonly Color BurnColor = new Color(1f, 0.35f, 0.05f, 0.95f);
        private const float FlashDuration = 0.35f;

        private readonly List<SpriteRenderer> sourceRenderers = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> overlayRenderers = new List<SpriteRenderer>();
        private float remainingFlashTime;

        public static void Flash(GameObject target)
        {
            if (target == null) return;

            BurnDamageFlash flash = target.GetComponent<BurnDamageFlash>();
            if (flash == null) flash = target.AddComponent<BurnDamageFlash>();
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
                if (source == null || source.gameObject.name.Contains("Overlay")) continue;

                GameObject overlayObject = new GameObject("BurnDamageOverlay");
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
                overlay.color = Color.clear;

                sourceRenderers.Add(source);
                overlayRenderers.Add(overlay);
            }
        }

        private void Update()
        {
            if (remainingFlashTime <= 0f) return;

            remainingFlashTime -= Time.deltaTime;
            float progress = Mathf.Clamp01(remainingFlashTime / FlashDuration);
            UpdateOverlayRenderers(progress);
        }

        private void UpdateOverlayRenderers(float progress)
        {
            Color color = BurnColor;
            color.a *= progress;

            for (int i = 0; i < overlayRenderers.Count; i++)
            {
                SpriteRenderer overlay = overlayRenderers[i];
                if (overlay == null) continue;

                if (i < sourceRenderers.Count && sourceRenderers[i] != null)
                {
                    overlay.sprite = sourceRenderers[i].sprite;
                    overlay.flipX = sourceRenderers[i].flipX;
                    overlay.flipY = sourceRenderers[i].flipY;
                }

                overlay.color = color;
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < overlayRenderers.Count; i++)
            {
                if (overlayRenderers[i] != null)
                {
                    Destroy(overlayRenderers[i].gameObject);
                }
            }
            overlayRenderers.Clear();
            sourceRenderers.Clear();
        }
    }
}
