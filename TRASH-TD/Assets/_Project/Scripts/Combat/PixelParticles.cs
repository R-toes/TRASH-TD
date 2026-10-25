using UnityEngine;

namespace TrashTD.Combat
{
    /// <summary>One pixel particle. Set the fields you need; the rest default to a still, white, 1x dot.</summary>
    public struct PixelParticle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public float Life;
        public float Delay;        // Seconds before it appears
        public float Gravity;      // Units/s² pulling down (negative floats up)
        public float Drag;         // Velocity damping per second
        public Color StartColor;
        public Color EndColor;     // Lerped over the life (alpha 0 = fade out)
        public float StartScale;   // 0 = 1
        public float EndScale;     // 0 = StartScale
        public Sprite Sprite;
        public Sprite[] Frames;    // Optional flipbook played once over the life (overrides Sprite)
        public float Rotation;     // Degrees
        public int SortingOrder;
    }

    /// <summary>
    /// Pooled sprite particles for combat effects (projectile trails, impacts, explosions, debris).
    /// One manager per scene, created on first use. Positions snap to the stage-art pixel grid so the
    /// effects stay crisp next to the pixel art. Runs on scaled time: it follows game speed and freezes
    /// while paused, like the combat it decorates.
    /// </summary>
    public sealed class PixelParticles : MonoBehaviour
    {
        private const int MaxParticles = 800;

        private sealed class Slot
        {
            public PixelParticle Data;
            public SpriteRenderer Renderer;
            public float Age;
            public bool Active;
        }

        private static PixelParticles instance;

        private Slot[] slots = new Slot[0];
        private int count;
        private int nextSlot;

        public static void Emit(PixelParticle particle)
        {
            if (particle.Life <= 0f) return;
            if (instance == null)
            {
                instance = new GameObject("PixelParticles").AddComponent<PixelParticles>();
            }
            instance.Add(particle);
        }

        private void Add(PixelParticle particle)
        {
            Slot slot = TakeSlot();
            slot.Data = particle;
            slot.Age = -Mathf.Max(0f, particle.Delay);
            slot.Active = true;

            SpriteRenderer renderer = slot.Renderer;
            renderer.sprite = particle.Frames != null && particle.Frames.Length > 0 ? particle.Frames[0] : particle.Sprite;
            renderer.sortingOrder = particle.SortingOrder;
            renderer.transform.rotation = Quaternion.Euler(0f, 0f, particle.Rotation);
            renderer.enabled = false;
            Apply(slot, 0f);
        }

        /// <summary>A free slot, growing the pool up to the cap; past it, the oldest slot is reused.</summary>
        private Slot TakeSlot()
        {
            for (int i = 0; i < count; i++)
            {
                int index = (nextSlot + i) % count;
                if (!slots[index].Active)
                {
                    nextSlot = (index + 1) % count;
                    return slots[index];
                }
            }

            if (count < MaxParticles)
            {
                if (count == slots.Length) System.Array.Resize(ref slots, Mathf.Min(MaxParticles, Mathf.Max(64, slots.Length * 2)));
                var go = new GameObject("Particle");
                go.transform.SetParent(transform, false);
                slots[count] = new Slot { Renderer = go.AddComponent<SpriteRenderer>() };
                return slots[count++];
            }

            Slot reused = slots[nextSlot];
            nextSlot = (nextSlot + 1) % count;
            return reused;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            for (int i = 0; i < count; i++)
            {
                Slot slot = slots[i];
                if (!slot.Active) continue;

                slot.Age += deltaTime;
                if (slot.Age >= slot.Data.Life)
                {
                    slot.Active = false;
                    slot.Renderer.enabled = false;
                    continue;
                }
                if (slot.Age < 0f) continue;

                ref PixelParticle p = ref slot.Data;
                p.Velocity.y -= p.Gravity * deltaTime;
                if (p.Drag > 0f) p.Velocity *= Mathf.Exp(-p.Drag * deltaTime);
                p.Position += p.Velocity * deltaTime;
                Apply(slot, slot.Age / p.Life);
            }
        }

        private static void Apply(Slot slot, float t)
        {
            ref PixelParticle p = ref slot.Data;
            SpriteRenderer renderer = slot.Renderer;
            if (slot.Age < 0f) return;

            renderer.enabled = true;
            renderer.color = Color.Lerp(p.StartColor, p.EndColor, t);

            float start = p.StartScale > 0f ? p.StartScale : 1f;
            float end = p.EndScale > 0f ? p.EndScale : start;
            renderer.transform.localScale = Vector3.one * Mathf.Lerp(start, end, t);
            renderer.transform.position = Snap(p.Position);

            if (p.Frames != null && p.Frames.Length > 0)
            {
                renderer.sprite = p.Frames[Mathf.Min(p.Frames.Length - 1, Mathf.FloorToInt(t * p.Frames.Length))];
            }
        }

        public static Vector3 Snap(Vector2 position) => new Vector3(
            Mathf.Round(position.x * CombatFxArt.PixelsPerUnit) / CombatFxArt.PixelsPerUnit,
            Mathf.Round(position.y * CombatFxArt.PixelsPerUnit) / CombatFxArt.PixelsPerUnit,
            0f);
    }
}
