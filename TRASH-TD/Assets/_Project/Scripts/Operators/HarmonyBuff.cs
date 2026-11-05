using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;

namespace TrashTD.Operators
{
    /// <summary>
    /// Flurmonica's Harmony buff on one ally: up to 3 stacks, each with its own 8 s timer. Each stack raises the
    /// ally's class stat by 4% (see <see cref="OperatorBase.SetHarmonyStacks"/>). Stacks from every Flurmonica
    /// share the same 3-stack cap. Added on the first stack; removed when the ally retreats.
    /// Timers pause between waves, like every other operator effect (<see cref="OperatorBase.IsCombatPaused"/>).
    ///
    /// Look: each stack keeps the music note Flurmonica sent. The notes circle the ally on a flat ellipse
    /// (passing behind it on the far side), hop to a beat and shed a sparkle now and then, so the ally looks like
    /// music is playing around it. A note pops in when its flight lands, blinks in its stack's last second and
    /// bursts when the stack runs out.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HarmonyBuff : MonoBehaviour
    {
        public const int MaxStacks = 3;
        public const float StackDuration = 8f;
        public const float BonusPerStack = 0.04f;

        // A stack this close to running out counts as already gone, so a cast landing in the same frame it
        // expires treats the ally as unbuffed (keeps the 4-allies-at-1-stack rotation stable across frame order).
        public const float ExpiringSoonSeconds = 0.1f;

        // Float slack for timers that should land on the same frame (stack expiry vs Flurmonica's cast).
        public const float TimerSlack = 0.0005f;

        public static readonly Color[] StackColors =
        {
            new Color(0.38f, 0.82f, 0.72f),
            new Color(0.98f, 0.91f, 0.72f),
            new Color(0.98f, 0.74f, 0.24f)
        };

        // ---- Orbit look ----
        private const string NotesObjectName = "HarmonyNotes";
        private const float OrbitSpeed = 1.7f;          // radians per second (one lap ≈ 3.7 s)
        private const float OrbitFlatten = 0.34f;       // ellipse height / width, a ring seen from above
        private const float BeatsPerSecond = 2.2f;
        private const float HopHeight = 0.07f;
        private const float SwayDegrees = 12f;
        private const float PopSeconds = 0.25f;
        private const float BlinkSeconds = 1f;
        private const float SparkleInterval = 0.45f;
        private const float RespaceRate = 5f;           // how fast notes slide to even spacing when the count changes

        private sealed class Stack
        {
            public float remaining;
            public int addedFrame;
            public Sprite note;
            public Color color;
            public float revealAt;
            public float phase;       // offset around the ring, eased toward even spacing
            public SpriteRenderer renderer;
        }

        private readonly List<Stack> stacks = new List<Stack>(MaxStacks);
        private OperatorBase owner;
        private SpriteRenderer body;
        private Transform notesRoot;
        private float sparkleTimer;

        public int Stacks => stacks.Count;

        public static HarmonyBuff GetOrAdd(OperatorBase target)
        {
            if (target == null) return null;
            HarmonyBuff buff = target.GetComponent<HarmonyBuff>();
            if (buff == null) buff = target.gameObject.AddComponent<HarmonyBuff>();
            buff.owner = target;
            return buff;
        }

        /// <summary>Stacks on the operator that still have more than <see cref="ExpiringSoonSeconds"/> left.</summary>
        public static int ActiveStacksOn(OperatorBase target)
        {
            HarmonyBuff buff = target != null ? target.GetComponent<HarmonyBuff>() : null;
            return buff != null ? buff.ActiveStacks : 0;
        }

        public int ActiveStacks
        {
            get
            {
                int count = 0;
                for (int i = 0; i < stacks.Count; i++)
                    if (stacks[i].remaining > ExpiringSoonSeconds) count++;
                return count;
            }
        }

        /// <summary>Middle of the ring the notes circle (world space): the ally's body, a little below centre.</summary>
        public static Vector3 OrbitCenter(OperatorBase target)
        {
            if (target == null) return Vector3.zero;
            Vector3 center = target.VisualCenter;
            float extentsY = target.BodyExtents.y;
            return new Vector3(center.x, center.y - extentsY * 0.12f, target.transform.position.z);
        }

        /// <summary>
        /// Adds one stack with a fresh 8 s timer. At 3 stacks it restarts the oldest stack's timer instead (that
        /// stack takes the new note). The note joins the orbit after <paramref name="revealDelay"/> seconds, when
        /// the flying note lands. With no note (tests), nothing is drawn. Returns the stack count afterwards.
        /// </summary>
        public int AddStack(Sprite note = null, Color color = default, float revealDelay = 0f)
        {
            for (int i = stacks.Count - 1; i >= 0; i--)
                if (stacks[i].remaining <= ExpiringSoonSeconds) RemoveStackAt(i, burst: true);

            Stack stack;
            if (stacks.Count < MaxStacks)
            {
                stack = new Stack { phase = stacks.Count > 0 ? stacks[stacks.Count - 1].phase + Mathf.PI : 0f };
                stacks.Add(stack);
            }
            else
            {
                stack = stacks[0];
                for (int i = 1; i < stacks.Count; i++)
                    if (stacks[i].remaining < stack.remaining) stack = stacks[i];
                if (stack.renderer != null) BurstNote(stack); // the old note gives way to the new one
            }

            stack.remaining = StackDuration;
            stack.addedFrame = Time.frameCount;
            stack.note = note;
            stack.color = color.a > 0f ? color : Color.white;
            stack.revealAt = Time.time + revealDelay;
            if (stack.renderer != null) stack.renderer.enabled = false;

            Apply();
            return stacks.Count;
        }

        /// <summary>Removes every stack and its stat bonus.</summary>
        public void Clear()
        {
            for (int i = stacks.Count - 1; i >= 0; i--) RemoveStackAt(i, burst: false);
            Apply();
        }

        /// <summary>Counts every stack down by dt and drops the expired ones (EditMode tests drive this directly).</summary>
        public void Tick(float dt) => Tick(dt, int.MinValue);

        private void Tick(float dt, int currentFrame)
        {
            if (stacks.Count == 0) return;

            int before = stacks.Count;
            for (int i = stacks.Count - 1; i >= 0; i--)
            {
                Stack stack = stacks[i];
                // A stack added earlier this frame starts counting next frame, so its 8 s run on the same
                // clock as the caster's 2 s cast timer whichever Update runs first.
                if (stack.addedFrame == currentFrame) continue;
                stack.remaining -= dt;
                if (stack.remaining <= TimerSlack) RemoveStackAt(i, burst: true);
            }

            if (stacks.Count != before) Apply();
        }

        private void Awake()
        {
            if (owner == null) owner = GetComponent<OperatorBase>();
        }

        private void Update()
        {
            if (owner == null || !owner.IsDeployed)
            {
                if (stacks.Count > 0) Clear();
                return;
            }

            if (!owner.IsCombatPaused) Tick(Time.deltaTime, Time.frameCount);
        }

        private void LateUpdate()
        {
            UpdateOrbit(Time.deltaTime);
        }

        private void OnDestroy()
        {
            if (notesRoot != null) Destroy(notesRoot.gameObject);
        }

        private void Apply()
        {
            if (owner == null) owner = GetComponent<OperatorBase>();
            if (owner != null) owner.SetHarmonyStacks(stacks.Count);
        }

        private void RemoveStackAt(int index, bool burst)
        {
            Stack stack = stacks[index];
            if (stack.renderer != null)
            {
                if (burst && stack.renderer.enabled) BurstNote(stack);
                Destroy(stack.renderer.gameObject);
            }
            stacks.RemoveAt(index);
        }

        // ============================
        // Orbiting notes
        // ============================

        private void UpdateOrbit(float dt)
        {
            if (stacks.Count == 0) return;
            if (body == null) body = GetComponent<SpriteRenderer>();

            Vector3 center = owner != null ? OrbitCenter(owner) : transform.position;
            float extentsX = owner != null ? owner.BodyExtents.x : (body != null && body.sprite != null ? Mathf.Min(0.5f, body.bounds.extents.x) : 0.5f);
            float radiusX = Mathf.Max(0.3f, extentsX * 0.9f);
            float radiusY = radiusX * OrbitFlatten;
            int bodyOrder = body != null ? body.sortingOrder : 0;
            float time = Time.time;
            float spin = time * OrbitSpeed;

            float oldestRemaining = float.MaxValue;
            for (int i = 0; i < stacks.Count; i++) oldestRemaining = Mathf.Min(oldestRemaining, stacks[i].remaining);

            for (int i = 0; i < stacks.Count; i++)
            {
                Stack stack = stacks[i];
                if (stack.note == null) continue;

                // Slide toward even spacing around the ring (1 note: anywhere; 2: opposite; 3: thirds).
                float slot = i * Mathf.PI * 2f / stacks.Count;
                float drift = Mathf.DeltaAngle(stack.phase * Mathf.Rad2Deg, slot * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                if (stacks.Count > 1) stack.phase += drift * Mathf.Min(1f, dt * RespaceRate);

                if (time < stack.revealAt)
                {
                    if (stack.renderer != null) stack.renderer.enabled = false;
                    continue;
                }

                SpriteRenderer renderer = EnsureNoteRenderer(stack);
                if (!renderer.enabled)
                {
                    renderer.enabled = true;
                    CombatFx.Pop(PixelParticles.Snap(renderer.transform.position), stack.color);
                }

                float angle = spin + stack.phase;
                float sin = Mathf.Sin(angle);
                bool behind = sin > 0f;                       // upper half of the ellipse is the far side
                float nearness = (1f - sin) * 0.5f;           // 0 far, 1 near

                // Each note hops on the beat, a little out of step with the others
                float beat = Mathf.Abs(Mathf.Sin((time * BeatsPerSecond + i * 0.33f) * Mathf.PI));
                Vector3 position = center + new Vector3(Mathf.Cos(angle) * radiusX, sin * radiusY + beat * HopHeight, 0f);
                renderer.transform.position = PixelParticles.Snap(position);
                renderer.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(time * 3.1f + i * 1.9f) * SwayDegrees);

                float age = time - stack.revealAt;
                float pop = age < PopSeconds
                    ? (age < PopSeconds * 0.6f ? Mathf.Lerp(0.2f, 1.25f, age / (PopSeconds * 0.6f)) : Mathf.Lerp(1.25f, 1f, (age - PopSeconds * 0.6f) / (PopSeconds * 0.4f)))
                    : 1f;
                float pulse = 1f + 0.12f * Mathf.Pow(beat, 6f);
                renderer.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.05f, nearness) * pop * pulse;
                renderer.sortingOrder = bodyOrder + (behind ? -1 : 3);

                float alpha = Mathf.Lerp(0.7f, 1f, nearness);
                bool expiring = Mathf.Approximately(stack.remaining, oldestRemaining) && stack.remaining < BlinkSeconds;
                if (expiring && Mathf.Repeat(time * 8f, 1f) < 0.5f) alpha *= 0.25f;
                renderer.color = new Color(1f, 1f, 1f, alpha);
            }

            EmitSparkle(dt);
        }

        /// <summary>Now and then a visible note sheds a pixel that drifts up and fades, like sound in the air.</summary>
        private void EmitSparkle(float dt)
        {
            sparkleTimer -= dt;
            if (sparkleTimer > 0f) return;
            sparkleTimer = SparkleInterval * Random.Range(0.7f, 1.3f);

            Stack source = stacks[Random.Range(0, stacks.Count)];
            if (source.renderer == null || !source.renderer.enabled) return;

            PixelParticles.Emit(new PixelParticle
            {
                Position = source.renderer.transform.position,
                Velocity = new Vector2(Random.Range(-0.12f, 0.12f), Random.Range(0.25f, 0.45f)),
                Life = Random.Range(0.5f, 0.8f),
                Drag = 0.6f,
                StartColor = Color.Lerp(source.color, Color.white, 0.4f),
                EndColor = new Color(source.color.r, source.color.g, source.color.b, 0f),
                Sprite = Random.value < 0.35f ? CombatFxArt.Plus3() : CombatFxArt.Dot1(),
                SortingOrder = CombatFx.ImpactSortingOrder
            });
        }

        private SpriteRenderer EnsureNoteRenderer(Stack stack)
        {
            if (notesRoot == null)
            {
                // Kept inside the ally's SortingGroup so notes on the far side pass behind its sprite.
                notesRoot = new GameObject(NotesObjectName).transform;
                notesRoot.SetParent(transform, false);
            }

            if (stack.renderer == null)
            {
                var noteObject = new GameObject("Note");
                noteObject.transform.SetParent(notesRoot, false);
                stack.renderer = noteObject.AddComponent<SpriteRenderer>();
                stack.renderer.enabled = false;
            }

            stack.renderer.sprite = stack.note;
            if (body != null) stack.renderer.sortingLayerID = body.sortingLayerID;
            return stack.renderer;
        }

        private static void BurstNote(Stack stack)
        {
            Vector2 position = stack.renderer.transform.position;
            CombatFx.Pop(position, stack.color);
            CombatFx.Sparks(position, stack.color, 4, 1.2f, 0.25f);
        }
    }
}
