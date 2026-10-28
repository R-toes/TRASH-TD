using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Combat
{
    /// <summary>
    /// Hit reaction for operators and enemies: the sprite flashes white for a moment, shakes a little and an
    /// impact burst pops on it, all scaled by how hard the hit was (its share of max HP). Small ticks (acid
    /// rain, burning, trap tiles) don't react, and they keep their own coloured flashes.
    ///
    /// The shake never moves the unit as far as game logic is concerned: the offset is added in LateUpdate,
    /// after every Update has run, and removed in Update, which runs before every other script's Update
    /// (execution order -10000). Movement, range checks, targeting and projectile origins all see the true
    /// position; only rendering sees the offset. Play mode only: EditMode tests never get one.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class HitReaction : MonoBehaviour
    {
        private const float MinDamageFraction = 0.02f;
        private const float FullStrengthFraction = 0.12f;
        private const float FlashDuration = 0.12f;
        private const float ShakeDuration = 0.16f;
        private const float MaxShakePixels = 2.5f;
        private const float BurstRetriggerInterval = 0.08f; // rapid ticks (lasers) don't stack bursts

        // The flash is drawn over the sprite at this opacity, so the sprite shows through as a glow (1 = solid colour)
        private static readonly Color FlashColor = Color.white;
        private const float FlashOpacity = 0.5f;

        private static Material flashMaterial;

        private readonly List<SpriteRenderer> sources = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> overlays = new List<SpriteRenderer>();
        private float flashTime;
        private float shakeTime;
        private float strength;
        private float shakeSign = 1f;
        private float lastBurstTime = -1f;
        private bool offsetApplied;
        private Vector3 appliedOffset;
        private Vector3 positionWithOffset;

        /// <summary>
        /// React to a hit of <paramref name="damage"/> on a unit with <paramref name="maxHP"/>.
        /// <paramref name="center"/> is where the impact burst pops (the visible sprite's centre).
        /// </summary>
        public static void Play(GameObject unit, Vector3 center, int damage, int maxHP)
        {
            if (!Application.isPlaying || unit == null || damage <= 0 || maxHP <= 0) return;

            float fraction = damage / (float)maxHP;
            if (fraction < MinDamageFraction) return;

            HitReaction reaction = unit.GetComponent<HitReaction>();
            if (reaction == null) reaction = unit.AddComponent<HitReaction>();
            reaction.Trigger(center, Mathf.Clamp01(fraction / FullStrengthFraction));
        }

        private void Trigger(Vector3 center, float hitStrength)
        {
            if (sources.Count == 0) CacheRenderers();

            strength = flashTime > 0f ? Mathf.Max(strength, hitStrength) : hitStrength;
            flashTime = FlashDuration;
            shakeTime = ShakeDuration;
            enabled = true;
            UpdateOverlays(1f);

            if (Time.time - lastBurstTime < BurstRetriggerInterval) return;
            lastBurstTime = Time.time;
            PlayImpactBurst(center, hitStrength);
        }

        /// <summary>A white star and sparks on the unit; heavy hits add a bigger flash and a ring.</summary>
        private static void PlayImpactBurst(Vector2 center, float hitStrength)
        {
            bool heavy = hitStrength >= 0.6f;
            CombatFx.Flash(center, Color.white, heavy);
            CombatFx.Sparks(center, Color.white, 2 + Mathf.RoundToInt(4f * hitStrength), 1.6f + 1.6f * hitStrength, 0.14f);
            if (heavy) CombatFx.Pop(center, Color.white);
        }

        /// <summary>The unit's own sprite: the root renderer and, for raised sprites, the "EnemyVisual" child.</summary>
        private void CacheRenderers()
        {
            AddSource(GetComponent<SpriteRenderer>());
            Transform visual = transform.Find("EnemyVisual");
            if (visual != null) AddSource(visual.GetComponent<SpriteRenderer>());
        }

        private void AddSource(SpriteRenderer source)
        {
            if (source == null) return;

            Material material = GetFlashMaterial();
            if (material == null) return; // shader missing: shake and burst still play

            var overlayObject = new GameObject("HitFlashOverlay");
            overlayObject.transform.SetParent(source.transform, false);
            SpriteRenderer overlay = overlayObject.AddComponent<SpriteRenderer>();
            overlay.sharedMaterial = material;
            overlay.enabled = false;
            sources.Add(source);
            overlays.Add(overlay);
        }

        private void Update()
        {
            RemoveShakeOffset();
        }

        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;

            if (flashTime > 0f)
            {
                flashTime -= deltaTime;
                UpdateOverlays(Mathf.Clamp01(flashTime / FlashDuration));
            }

            if (shakeTime > 0f)
            {
                shakeTime -= deltaTime;
                if (shakeTime > 0f)
                {
                    // Side to side each frame, dying down; whole art pixels keep the sprite crisp
                    float decay = shakeTime / ShakeDuration;
                    float pixels = Mathf.Max(1f, Mathf.Round(MaxShakePixels * strength * decay));
                    shakeSign = -shakeSign;
                    ApplyShakeOffset(new Vector3(pixels * CombatFxArt.Pixel * shakeSign, 0f, 0f));
                }
            }

            if (flashTime <= 0f && shakeTime <= 0f)
            {
                UpdateOverlays(0f);
                enabled = false;
            }
        }

        private void OnDisable()
        {
            RemoveShakeOffset();
        }

        private void OnDestroy()
        {
            for (int i = 0; i < overlays.Count; i++)
            {
                if (overlays[i] != null) Destroy(overlays[i].gameObject);
            }
            overlays.Clear();
            sources.Clear();
        }

        private void ApplyShakeOffset(Vector3 offset)
        {
            transform.position += offset;
            appliedOffset = offset;
            positionWithOffset = transform.position;
            offsetApplied = true;
        }

        private void RemoveShakeOffset()
        {
            if (!offsetApplied) return;
            offsetApplied = false;

            // If something else placed the unit since (a teleport, a respawn), its position is already clean.
            if ((transform.position - positionWithOffset).sqrMagnitude < 0.0000001f)
                transform.position -= appliedOffset;
        }

        private void UpdateOverlays(float alpha)
        {
            for (int i = 0; i < overlays.Count; i++)
            {
                SpriteRenderer source = sources[i];
                SpriteRenderer overlay = overlays[i];
                if (source == null || overlay == null) continue;

                bool visible = alpha > 0f && source.enabled && source.gameObject.activeInHierarchy && source.sprite != null;
                overlay.enabled = visible;
                if (!visible) continue;

                // Code that recolours every child sprite (Framiliar's grayscale) may have swapped the material
                if (overlay.sharedMaterial != flashMaterial) overlay.sharedMaterial = flashMaterial;
                overlay.sprite = source.sprite;
                overlay.flipX = source.flipX;
                overlay.flipY = source.flipY;
                overlay.sortingLayerID = source.sortingLayerID;
                overlay.sortingOrder = source.sortingOrder + 1;
                overlay.maskInteraction = source.maskInteraction;
                // Ease out: stays bright for the first frames, then fades
                overlay.color = new Color(FlashColor.r, FlashColor.g, FlashColor.b,
                    FlashOpacity * Mathf.SmoothStep(0f, 1f, alpha) * Mathf.Min(1f, source.color.a + 0.2f));
            }
        }

        private static Material GetFlashMaterial()
        {
            if (flashMaterial != null) return flashMaterial;

            Shader shader = Shader.Find("TrashTD/SpriteFlash");
            if (shader == null) shader = Resources.Load<Shader>("Shaders/SpriteFlash");
            if (shader == null) return null;

            flashMaterial = new Material(shader) { name = "HitFlash" };
            return flashMaterial;
        }
    }
}
