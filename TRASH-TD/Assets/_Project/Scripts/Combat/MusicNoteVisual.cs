using UnityEngine;
using TrashTD.Operators;

namespace TrashTD.Combat
{
    /// <summary>
    /// Flurmonica's music note flying to an ally: it leaves the flute with a little flourish, arcs up and over
    /// (higher for longer throws) with a soft glow and a trail of twinkles, swaying and spinning gently, and lands
    /// in a pop. On landing the same note appears in the ally's orbit (<see cref="HarmonyBuff"/>), so it looks like
    /// the note settles in to circle the ally. Purely visual: the stack is applied when Flurmonica casts.
    /// Follows the ally if it moves; runs on scaled time like the other projectiles.
    /// </summary>
    public sealed class MusicNoteVisual : MonoBehaviour
    {
        public const float FlightSeconds = 0.45f;

        private const float ArcPerTile = 0.22f;
        private const float MaxArc = 0.9f;
        private const float TrailInterval = 0.025f;
        private const int BodySortingOrder = 40;
        private const int HaloSortingOrder = 39;

        private Vector3 start;
        private OperatorBase target;
        private Vector3 lastEnd;
        private Color color;
        private float age;
        private float trailTimer;
        private SpriteRenderer body;
        private SpriteRenderer halo;

        public static void Fire(Vector3 start, OperatorBase target, Sprite note, Color color)
        {
            if (target == null || note == null) return;

            var noteObject = new GameObject("MusicNote");
            noteObject.transform.position = start;
            var visual = noteObject.AddComponent<MusicNoteVisual>();
            visual.start = start;
            visual.target = target;
            visual.lastEnd = HarmonyBuff.OrbitCenter(target);
            visual.color = color;

            visual.body = noteObject.AddComponent<SpriteRenderer>();
            visual.body.sprite = note;
            visual.body.sortingOrder = BodySortingOrder;

            var haloObject = new GameObject("Glow");
            haloObject.transform.SetParent(noteObject.transform, false);
            visual.halo = haloObject.AddComponent<SpriteRenderer>();
            visual.halo.sprite = CombatFxArt.GlowDisc(6);
            visual.halo.color = new Color(color.r, color.g, color.b, 0.35f);
            visual.halo.sortingOrder = HaloSortingOrder;

            // Flourish at the flute: a flash and a few twinkles thrown toward the target
            Vector2 toward = (Vector2)(visual.lastEnd - start);
            CombatFx.Flash(start, color);
            CombatFx.Sparks(start, color, 4, 1.4f, 0.22f, direction: toward, coneDegrees: 80f);
        }

        private void Update()
        {
            age += Time.deltaTime;
            if (target != null) lastEnd = HarmonyBuff.OrbitCenter(target);

            float t = Mathf.Clamp01(age / FlightSeconds);
            float eased = t * t * (3f - 2f * t);

            // Quadratic arc: the control point sits above the midpoint, higher for longer throws.
            float distance = Vector2.Distance(start, lastEnd);
            Vector3 control = (start + lastEnd) * 0.5f + Vector3.up * Mathf.Min(MaxArc, 0.25f + distance * ArcPerTile);
            float u = 1f - eased;
            Vector3 position = u * u * start + 2f * u * eased * control + eased * eased * lastEnd;
            transform.position = PixelParticles.Snap(position);

            // Pop in, a gentle sway, and a quick spin into the landing
            float scale = t < 0.15f ? Mathf.Lerp(0.3f, 1.2f, t / 0.15f) : Mathf.Lerp(1.2f, 0.9f, (t - 0.15f) / 0.85f);
            transform.localScale = Vector3.one * scale;
            float sway = Mathf.Sin(t * Mathf.PI * 3f) * 18f;
            float spin = t > 0.75f ? (t - 0.75f) / 0.25f * 360f : 0f;
            transform.rotation = Quaternion.Euler(0f, 0f, sway + spin);
            halo.color = new Color(color.r, color.g, color.b, 0.25f + 0.15f * Mathf.Sin(age * 30f));

            EmitTrail(position);

            if (t >= 1f)
            {
                CombatFx.Pop(lastEnd, color);
                CombatFx.Sparks(lastEnd, color, 6, 1.6f, 0.3f);
                CombatFx.Crosses(lastEnd, color, 2, 0.15f, false, 0.45f);
                Destroy(gameObject);
            }
        }

        /// <summary>Twinkles left behind the note that drift down a little and fade.</summary>
        private void EmitTrail(Vector3 position)
        {
            trailTimer -= Time.deltaTime;
            if (trailTimer > 0f) return;
            trailTimer = TrailInterval;

            PixelParticles.Emit(new PixelParticle
            {
                Position = (Vector2)position + Random.insideUnitCircle * 0.04f,
                Velocity = new Vector2(Random.Range(-0.08f, 0.08f), Random.Range(-0.15f, 0.05f)),
                Life = Random.Range(0.25f, 0.4f),
                Drag = 2f,
                StartColor = Color.Lerp(color, Color.white, 0.5f),
                EndColor = new Color(color.r, color.g, color.b, 0f),
                Sprite = Random.value < 0.25f ? CombatFxArt.Plus3() : CombatFxArt.Dot1(),
                SortingOrder = CombatFx.TrailSortingOrder
            });
        }
    }
}
