using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Combat
{
    /// <summary>What an AoE looks like on top of its area marker.</summary>
    public enum AoeStyle
    {
        Pulse,     // Plain shockwave and a few sparks
        Arts,      // Magic burst: sparks and twinkles
        Explosion, // Fireball, debris, embers and smoke
        JunkRocket,    // Basurocket: one bright fireball and a burst of colourful scrap
        HeavyOrdnance, // THE ERADICATOR: crimson fireball, shrapnel, black smoke column, scorch mark
        Heal,      // Rising heal crosses
        Bubble,    // Water droplets and pops
        Acid       // Corrosive splash: drips and fumes
    }

    /// <summary>
    /// Short-lived indicator that shows the exact area hit by an AoE attack, plus a styled effect.
    /// Circle blasts match world-radius splash: a dithered pixel disc with a crisp rim at the exact radius and
    /// a shockwave ring opening out to it. Cell blasts match grid-cell splash: each cell flashes with corner
    /// brackets, rippling out from the centre, with staggered explosions for the Explosion style.
    /// The area drawn is always the area that was hit. Runs on scaled time.
    /// </summary>
    public sealed class AoeBlastVisual : MonoBehaviour
    {
        private const float Lifetime = 0.45f;
        private const float HoldPortion = 0.35f;     // Full strength for this part of the life, then fade
        private const float ShockwaveDuration = 0.2f;
        private const float CellRippleDelay = 0.05f; // Per cell of distance from the blast centre
        private const int AreaSortingOrder = 25;
        private const int ShockwaveSortingOrder = 26;

        private sealed class Piece
        {
            public SpriteRenderer Renderer;
            public float Delay;
        }

        private readonly List<Piece> pieces = new List<Piece>();
        private SpriteRenderer shockwave;
        private Sprite[] shockwaveFrames;
        private Color blastColor;
        private float elapsed;
        private float lifetime = Lifetime;

        public static void PlayCircle(Vector3 center, float radius, Color color, AoeStyle style = AoeStyle.Pulse)
        {
            if (radius <= 0f) return;

            AoeBlastVisual visual = Create(center, color);
            int radiusPx = Mathf.Max(3, Mathf.RoundToInt(radius * CombatFxArt.PixelsPerUnit));
            visual.AddPiece(Vector3.zero, CombatFxArt.AreaCircle(radiusPx), 0f);

            var shockwaveObject = new GameObject("Shockwave");
            shockwaveObject.transform.SetParent(visual.transform, false);
            visual.shockwave = shockwaveObject.AddComponent<SpriteRenderer>();
            visual.shockwaveFrames = CombatFxArt.ShockwaveFrames(radiusPx);
            visual.shockwave.sprite = visual.shockwaveFrames[0];
            visual.shockwave.sortingOrder = ShockwaveSortingOrder;

            PlayCircleStyle(center, radius, color, style);
            visual.ApplyAnimation();
        }

        public static void PlayCells(IList<Vector3> cellCenters, float cellSize, Color color, AoeStyle style = AoeStyle.Pulse)
        {
            if (cellCenters == null || cellCenters.Count == 0 || cellSize <= 0f) return;

            Vector3 center = Vector3.zero;
            for (int i = 0; i < cellCenters.Count; i++)
                center += cellCenters[i];
            center /= cellCenters.Count;

            AoeBlastVisual visual = Create(center, color);
            float maxDelay = 0f;
            Sprite cellSprite = CombatFxArt.AreaCell();
            float cellScale = cellSize / (cellSprite.rect.width / CombatFxArt.PixelsPerUnit);
            for (int i = 0; i < cellCenters.Count; i++)
            {
                float delay = Vector2.Distance(cellCenters[i], center) / cellSize * CellRippleDelay;
                maxDelay = Mathf.Max(maxDelay, delay);
                Piece piece = visual.AddPiece(cellCenters[i] - center, cellSprite, delay);
                piece.Renderer.transform.localScale = Vector3.one * cellScale;
                PlayCellStyle(cellCenters[i], cellSize, color, style, delay, i == 0 || cellCenters.Count == 1);
            }
            visual.lifetime = Lifetime + maxDelay;
            visual.ApplyAnimation();
        }

        private static AoeBlastVisual Create(Vector3 center, Color color)
        {
            var visualObject = new GameObject("AoeBlastVisual");
            visualObject.transform.position = PixelParticles.Snap(center);
            AoeBlastVisual visual = visualObject.AddComponent<AoeBlastVisual>();
            visual.blastColor = color;
            return visual;
        }

        private Piece AddPiece(Vector3 localPosition, Sprite sprite, float delay)
        {
            var pieceObject = new GameObject("BlastPiece");
            pieceObject.transform.SetParent(transform, false);
            pieceObject.transform.localPosition = localPosition;

            var spriteRenderer = pieceObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            spriteRenderer.sortingOrder = AreaSortingOrder;
            var piece = new Piece { Renderer = spriteRenderer, Delay = delay };
            pieces.Add(piece);
            return piece;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            ApplyAnimation();
            if (elapsed >= lifetime)
                Destroy(gameObject);
        }

        private void ApplyAnimation()
        {
            // Area: flashes bright, holds at the given strength, then fades
            for (int i = 0; i < pieces.Count; i++)
            {
                Piece piece = pieces[i];
                float local = elapsed - piece.Delay;
                piece.Renderer.enabled = local >= 0f;
                if (local < 0f) continue;

                float t = Mathf.Clamp01(local / Lifetime);
                float flash = Mathf.Clamp01(1f - local / 0.06f);
                float fade = t < HoldPortion ? 1f : 1f - (t - HoldPortion) / (1f - HoldPortion);
                Color color = Color.Lerp(blastColor, Color.white, flash * 0.6f);
                // Capped so an opaque colour still leaves the units under the area readable
                color.a = Mathf.Clamp01(Mathf.Min(blastColor.a, 0.75f) + flash * 0.4f) * fade;
                piece.Renderer.color = color;
            }

            if (shockwave != null)
            {
                float t = Mathf.Clamp01(elapsed / ShockwaveDuration);
                shockwave.sprite = shockwaveFrames[Mathf.Min(shockwaveFrames.Length - 1, Mathf.FloorToInt(t * shockwaveFrames.Length))];
                Color ring = Color.Lerp(blastColor, Color.white, 0.5f);
                ring.a = 1f - Mathf.Clamp01((elapsed - ShockwaveDuration) / 0.12f);
                shockwave.color = ring;
            }
        }

        // ─────────────────────────────────────────────────────
        //  Styles
        // ─────────────────────────────────────────────────────

        private static void PlayCircleStyle(Vector2 center, float radius, Color color, AoeStyle style)
        {
            color.a = 1f;
            switch (style)
            {
                case AoeStyle.Explosion:
                    CombatFx.Explosion(center, radius * 0.6f);
                    // Secondary blasts out toward the edge, so a big radius reads as a big blast
                    int extra = Mathf.Clamp(Mathf.RoundToInt(radius * 2f), 0, 4);
                    for (int i = 0; i < extra; i++)
                    {
                        Vector2 offset = Random.insideUnitCircle.normalized * radius * Random.Range(0.4f, 0.7f);
                        CombatFx.Explosion(center + offset, radius * 0.3f, 0.06f + i * 0.05f, true);
                    }
                    break;

                case AoeStyle.JunkRocket:
                    CombatFx.JunkRocketBlast(center, radius);
                    break;

                case AoeStyle.HeavyOrdnance:
                    CombatFx.HeavyOrdnanceBlast(center, radius);
                    break;

                case AoeStyle.Arts:
                    CombatFx.Flash(center, color, true);
                    CombatFx.Sparks(center, color, Mathf.Clamp(Mathf.RoundToInt(radius * 14f), 6, 20), radius * 5f, 0.3f);
                    CombatFx.Crosses(center, color, Mathf.Clamp(Mathf.RoundToInt(radius * 4f), 2, 6), radius * 0.6f, false, 0.4f);
                    break;

                case AoeStyle.Heal:
                    CombatFx.Crosses(center, color, Mathf.Clamp(Mathf.RoundToInt(radius * 6f), 4, 10), radius * 0.75f, true, 0.8f);
                    CombatFx.Sparks(center, color, 8, radius * 2f, 0.5f, -0.6f);
                    break;

                case AoeStyle.Bubble:
                    CombatFx.Pop(center, color);
                    CombatFx.Droplets(center, Color.Lerp(color, Color.white, 0.3f), Mathf.Clamp(Mathf.RoundToInt(radius * 12f), 5, 14), radius * 4f);
                    break;

                case AoeStyle.Acid:
                    CombatFx.Droplets(center, color, Mathf.Clamp(Mathf.RoundToInt(radius * 14f), 6, 18), radius * 4.5f);
                    CombatFx.Smoke(center, new Color(color.r * 0.7f, color.g * 0.7f, color.b * 0.7f, 0.55f),
                        Mathf.Clamp(Mathf.RoundToInt(radius * 4f), 2, 6), radius * 0.5f, 0.7f, 0.3f);
                    break;

                default:
                    CombatFx.Sparks(center, color, Mathf.Clamp(Mathf.RoundToInt(radius * 10f), 4, 14), radius * 4f, 0.25f);
                    break;
            }
        }

        private static void PlayCellStyle(Vector2 cellCenter, float cellSize, Color color, AoeStyle style, float delay, bool primary)
        {
            color.a = 1f;
            switch (style)
            {
                case AoeStyle.Explosion:
                    CombatFx.Explosion(cellCenter, cellSize * (primary ? 0.5f : 0.36f), delay, !primary);
                    break;
                case AoeStyle.Arts:
                    CombatFx.Sparks(cellCenter, color, 5, cellSize * 2.5f, 0.25f, delay: delay);
                    break;
                case AoeStyle.Heal:
                    CombatFx.Crosses(cellCenter, color, 2, cellSize * 0.3f, true, 0.7f, delay);
                    break;
                case AoeStyle.Bubble:
                    CombatFx.Pop(cellCenter, color, delay);
                    CombatFx.Droplets(cellCenter, color, 4, cellSize * 2f, delay);
                    break;
                case AoeStyle.Acid:
                    CombatFx.Droplets(cellCenter, color, 5, cellSize * 2.2f, delay);
                    break;
                default:
                    CombatFx.Sparks(cellCenter, color, 4, cellSize * 2f, 0.2f, delay: delay);
                    break;
            }
        }
    }
}
