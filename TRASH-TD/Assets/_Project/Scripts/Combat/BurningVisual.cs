using UnityEngine;
using TrashTD.Operators;

namespace TrashTD.Combat
{
    /// <summary>
    /// Burning visuals for an operator that has ignited (Stage 4 heat, 5 stacks): flames licking up from its
    /// body, rising sparks and dark smoke, a flickering orange glow over and behind the sprite, and bigger
    /// tongues of fire crowning the head. Flames start on the operator's real silhouette (read from the current
    /// sprite frame, like the ice effect) and burn as pixel-art particles on the same pixel grid as the stage art.
    /// Added and removed by OperatorBase.IsBurning, so retreating or dying puts it out. Runs on scaled time.
    /// </summary>
    public sealed class BurningVisual : MonoBehaviour
    {
        public const string GlowOverlayName = "BurningGlowOverlay";
        public const string HaloOverlayName = "BurningHaloOverlay";

        private const float FlameInterval = 0.07f;
        private const float CrownFlameInterval = 0.28f;
        private const float SparkInterval = 0.16f;
        private const float SmokeInterval = 0.45f;

        private static readonly Color Red = new Color(0.90f, 0.18f, 0.05f, 0.95f);
        private static readonly Color Orange = new Color(1.00f, 0.52f, 0.10f, 1f);
        private static readonly Color Yellow = new Color(1.00f, 0.83f, 0.25f, 1f);
        private static readonly Color White = new Color(1.00f, 0.97f, 0.72f, 1f);

        private static Sprite[] flameFrames;

        private SpriteRenderer target;
        private SpriteRenderer glow;   // Orange tint over the body
        private SpriteRenderer halo;   // Warm light behind it
        private float flameTimer;
        private float crownTimer;
        private float sparkTimer;
        private float smokeTimer;
        private float age;

        /// <summary>Starts or stops the burning effect on a game object (an operator).</summary>
        public static void SetBurning(GameObject owner, bool burning)
        {
            if (owner == null) return;

            BurningVisual visual = owner.GetComponent<BurningVisual>();
            if (burning)
            {
                if (visual == null) visual = owner.AddComponent<BurningVisual>();
                visual.enabled = true;
                visual.Ignite();
            }
            else if (visual != null)
            {
                visual.Extinguish();
            }
        }

        private void Ignite()
        {
            target = FindTargetRenderer();
            age = 0f;
            flameTimer = 0f;
            crownTimer = 0f;
            sparkTimer = 0f;
            smokeTimer = Random.Range(0f, SmokeInterval);

            if (glow == null) glow = CreateOverlay(GlowOverlayName);
            if (halo == null)
            {
                halo = CreateOverlay(HaloOverlayName);
                halo.sprite = CombatFxArt.GlowDisc(12);
            }
            glow.enabled = true;
            halo.enabled = true;
        }

        private void Extinguish()
        {
            enabled = false;
            if (glow != null) Destroy(glow.gameObject);
            if (halo != null) Destroy(halo.gameObject);
            glow = null;
            halo = null;
        }

        private void OnDestroy()
        {
            if (glow != null) Destroy(glow.gameObject);
            if (halo != null) Destroy(halo.gameObject);
        }

        private SpriteRenderer CreateOverlay(string overlayName)
        {
            var go = new GameObject(overlayName);
            go.transform.SetParent(transform, false);
            return go.AddComponent<SpriteRenderer>();
        }

        private SpriteRenderer FindTargetRenderer()
        {
            SpriteRenderer own = GetComponent<SpriteRenderer>();
            if (own != null) return own;

            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(false);
            for (int i = 0; i < renderers.Length; i++)
            {
                string rendererName = renderers[i].gameObject.name;
                if (renderers[i].sprite != null && !rendererName.Contains("Overlay") && !rendererName.StartsWith("StunStars"))
                    return renderers[i];
            }
            return null;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            age += dt;

            if (target == null) target = FindTargetRenderer();
            if (target == null || !target.enabled || !target.gameObject.activeInHierarchy) return;

            flameTimer -= dt;
            while (flameTimer <= 0f)
            {
                flameTimer += FlameInterval * Random.Range(0.7f, 1.3f);
                EmitFlame(crown: false);
            }

            crownTimer -= dt;
            if (crownTimer <= 0f)
            {
                crownTimer = CrownFlameInterval * Random.Range(0.8f, 1.3f);
                EmitFlame(crown: true);
            }

            sparkTimer -= dt;
            if (sparkTimer <= 0f)
            {
                sparkTimer = SparkInterval * Random.Range(0.6f, 1.4f);
                CombatFx.Sparks(BodyPoint(0.3f), new Color(1f, 0.55f, 0.12f), 1, 0.45f, 0.6f, gravity: -0.5f);
            }

            smokeTimer -= dt;
            if (smokeTimer <= 0f)
            {
                smokeTimer = SmokeInterval * Random.Range(0.8f, 1.4f);
                CombatFx.Smoke(BodyPoint(0.6f), new Color(0.16f, 0.14f, 0.13f, 0.5f), 1, 0.06f, 0.9f, 0.45f,
                    sortingOrder: CombatFx.TrailSortingOrder);
            }
        }

        private void LateUpdate()
        {
            if (glow == null || halo == null || target == null || target.sprite == null) return;

            bool visible = target.enabled && target.gameObject.activeInHierarchy;
            glow.enabled = visible;
            halo.enabled = visible;
            if (!visible) return;

            // Fire light is never steady: a slow throb plus a fast random flicker
            float flicker = 0.5f + 0.5f * Mathf.PerlinNoise(Time.time * 9f, GetInstanceHash());
            float throb = 0.5f + 0.5f * Mathf.Sin(age * 5.5f);

            // Orange tint hugging the sprite, lined up with it pixel for pixel
            Transform t = target.transform;
            glow.sprite = target.sprite;
            glow.flipX = target.flipX;
            glow.flipY = target.flipY;
            glow.sortingLayerID = target.sortingLayerID;
            glow.sortingOrder = target.sortingOrder + 1;
            glow.transform.SetPositionAndRotation(t.position, t.rotation);
            Vector3 lossy = t.lossyScale;
            Vector3 parent = glow.transform.parent != null ? glow.transform.parent.lossyScale : Vector3.one;
            glow.transform.localScale = new Vector3(
                parent.x != 0f ? lossy.x / parent.x : 1f,
                parent.y != 0f ? lossy.y / parent.y : 1f,
                1f);
            glow.color = new Color(1f, Mathf.Lerp(0.30f, 0.50f, flicker), 0.06f, 0.14f + 0.12f * throb + 0.08f * flicker);

            // Warm light spilling out behind the body
            halo.sortingLayerID = target.sortingLayerID;
            halo.sortingOrder = target.sortingOrder - 1;
            Vector3 center = target.bounds.center;
            halo.transform.SetPositionAndRotation(new Vector3(center.x, center.y, t.position.z), Quaternion.identity);
            float haloScale = Mathf.Lerp(1.5f, 1.9f, throb);
            halo.transform.localScale = Vector3.one * (haloScale / Mathf.Max(0.01f, transform.lossyScale.x));
            halo.color = new Color(1f, 0.42f, 0.08f, 0.16f + 0.14f * flicker);
        }

        private float GetInstanceHash() => (GetHashCode() & 0xFFFF) * 0.0137f;

        /// <summary>A flame rising off the body; crown flames start near the top of the silhouette and burn bigger.</summary>
        private void EmitFlame(bool crown)
        {
            Vector2 start = BodyPoint(crown ? 0.55f : 0.2f);
            bool big = crown || Random.value < 0.2f;
            float scale = big ? 1.4f : 1f;

            PixelParticles.Emit(new PixelParticle
            {
                Position = start,
                Velocity = new Vector2(Random.Range(-0.1f, 0.1f), Random.Range(0.16f, 0.36f)),
                Life = Random.Range(0.34f, 0.55f),
                Drag = 0.7f,
                StartColor = Color.white,
                EndColor = new Color(1f, 0.85f, 0.7f, 0f),
                StartScale = scale,
                EndScale = scale * 0.5f,
                Frames = GetFlameFrames(),
                SortingOrder = CombatFx.ImpactSortingOrder - 1
            });
        }

        /// <summary>
        /// A point on the operator's visible body. <paramref name="upperBias"/> 0 is anywhere, 1 is the top of the
        /// silhouette: the higher it is, the more candidates are tried and the highest one wins.
        /// </summary>
        private Vector2 BodyPoint(float upperBias)
        {
            Sprite sprite = target != null ? target.sprite : null;
            IceStatusVisual.Silhouette sil = sprite != null ? IceStatusVisual.GetSilhouette(sprite) : null;
            if (sil == null) return transform.position;

            // Several candidates compete on height, so a high bias lands near the head
            int wanted = 2 + Mathf.RoundToInt(upperBias * 8f);
            int accepted = 0;
            Vector2 bestLocal = Vector2.zero;
            float bestY = float.MinValue;
            bool found = false;
            for (int attempt = 0; attempt < 80 && accepted < wanted; attempt++)
            {
                int x = Random.Range(0, sil.Width);
                int y = Random.Range(0, sil.Height);
                if (!sil.Solid[y * sil.Width + x]) continue;

                accepted++;
                float score = y + Random.value * 2f;
                if (!found || score > bestY)
                {
                    bestY = score;
                    bestLocal = new Vector2(x + 0.5f, y + 0.5f);
                    found = true;
                }
            }
            if (!found) return target.bounds.center;

            float lx = (bestLocal.x - sil.Pivot.x) / sil.PixelsPerUnit * (target.flipX ? -1f : 1f);
            float ly = (bestLocal.y - sil.Pivot.y) / sil.PixelsPerUnit;
            return target.transform.TransformPoint(new Vector3(lx, ly, 0f));
        }

        // ─────────────────────────────────────────────────────
        //  Flame sprites (7x10 px, four flipbook frames: sway left, sway right, tall, dying)
        // ─────────────────────────────────────────────────────

        private static readonly string[][] FlameShapes =
        {
            new[] { "...R...", "...R...", "..ROR..", "..ROR..", ".RROOR.", ".ROOYOR", "RROYYOR", "ROOYWYR", "ROYYWYR", ".RROORR" },
            new[] { ".R.....", ".R.R...", ".ROR...", "RROR...", "RROOR..", ".ROOYR.", ".ROYYOR", "ROOYWYR", "ROYYWYR", ".RROORR" },
            new[] { "....R..", "...ROR.", "...ROR.", "..ROOR.", "..ROYR.", ".ROOYOR", ".ROYYOR", "ROOYWYR", "ROYYWYR", ".RROORR" },
            new[] { ".......", "...R...", "...R...", "..ROR..", "..ROR..", ".RROOR.", ".ROYOR.", "RROYYOR", "ROOYYOR", ".RROORR" }
        };

        private static Sprite[] GetFlameFrames()
        {
            if (flameFrames != null && flameFrames.Length == FlameShapes.Length && flameFrames[0] != null)
                return flameFrames;

            flameFrames = new Sprite[FlameShapes.Length];
            for (int f = 0; f < FlameShapes.Length; f++)
            {
                string[] rows = FlameShapes[f];
                int height = rows.Length;
                int width = rows[0].Length;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    name = "Burning Flame " + f,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                for (int row = 0; row < height; row++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Color c = rows[row][x] switch
                        {
                            'R' => Red,
                            'O' => Orange,
                            'Y' => Yellow,
                            'W' => White,
                            _ => Color.clear
                        };
                        texture.SetPixel(x, height - 1 - row, c);
                    }
                }
                texture.Apply();
                // Pivot at the foot of the flame, so it sits on the point it burns from
                flameFrames[f] = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.1f), CombatFxArt.PixelsPerUnit);
            }
            return flameFrames;
        }
    }
}
