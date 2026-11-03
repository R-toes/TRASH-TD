using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TrashTD.Systems;
using Random = UnityEngine.Random;

namespace TrashTD.UI
{
    /// <summary>
    /// Plays the deck-bar animation when three copies of a card merge (PlayerDeck.OnCardsMerged), on unscaled time:
    /// 1. Gather (0.2 s): ghosts of the three copies lift out of their slots and glow. A copy that merged without a
    ///    slot (full deck) drops in from above the result slot.
    /// 2. Fuse (0.38 s): they swoop into the result slot on little arcs, spinning and shrinking, trailing sparks.
    /// 3. Burst: a flash and a ring of pixel sparks in the new rarity's colour; the upgraded card pops in (the real slot
    ///    stays hidden until now) and a "RANK UP" label with the new stars rises above it.
    /// Merges queue and play one after another (a chain 1★→2★→3★ plays twice), and wait while the full draft screen
    /// covers the deck bar. The deck has already reached its final state when they play, so each source card is a
    /// snapshot of its deck slot taken when it merged (GameplayHUDUI.SnapshotDeckSlot). Snapshots stay on screen as
    /// placeholders until their merge plays; a card made by an earlier merge in the queue (the 2★ of a 1★→2★→3★
    /// chain) shows up only when that merge bursts. The snapshots themselves fly in, so they look like the real cards.
    /// </summary>
    public sealed class DeckMergeAnimator : MonoBehaviour
    {
        private const float GatherSeconds = 0.2f;
        private const float FuseSeconds = 0.38f;
        private const float PopSeconds = 0.4f;
        private const float LabelSeconds = 1.3f;
        private const float TrailInterval = 0.025f;

        /// <summary>Everything one queued merge needs, captured when it happened.</summary>
        public sealed class Request
        {
            public List<int> SourceSlots = new List<int>();   // -1 for a copy that had no slot
            public List<Sprite> SourcePortraits = new List<Sprite>();
            public List<DraftCard> SourceCards = new List<DraftCard>();
            /// <summary>Copies of the source deck slots as they looked at the merge (null where there is none).</summary>
            public List<GameObject> SourceSnapshots = new List<GameObject>();
            public DraftCard ResultCard;
            public int ResultSlot;
            public Sprite ResultPortrait;
            public int NewStars;
            public Color OldColor;
            public Color NewColor;
        }

        private sealed class Ghost
        {
            public RectTransform Rect;
            public Image Frame;
            public bool GlowOnly;   // a slot snapshot: Frame is a glow behind it, not its body
            public Vector2 Start;
            public float ArcHeight;
            public float Spin;
            public float TrailTimer;
        }

        private sealed class Spark
        {
            public RectTransform Rect;
            public Image Image;
            public Vector2 Velocity;
            public float Age;
            public float Lifetime;
            public Color Color;
            public float Gravity;
        }

        private RectTransform layer;
        private Func<int, RectTransform> getSlotRect;
        private Action<int, bool> setSlotHidden;
        private Func<bool> canPlay;

        private readonly Queue<Request> queue = new Queue<Request>();
        private readonly Dictionary<int, int> hiddenSlotCounts = new Dictionary<int, int>();
        private readonly List<Ghost> ghosts = new List<Ghost>();
        private readonly List<Spark> sparks = new List<Spark>();
        private readonly List<GameObject> transients = new List<GameObject>();

        /// <summary>A source card waiting in its slot (as a snapshot) for its merge to play.</summary>
        private sealed class Placeholder
        {
            public Request Owner;
            public int SourceIndex;
            public int Slot;
            public RectTransform Rect;
            public Request WaitFor; // an earlier merge that makes this card; it appears when that one bursts
            public bool Shown;
        }

        private readonly List<Placeholder> placeholders = new List<Placeholder>();

        private Request current;
        private float elapsed;
        private bool burst;
        private Vector2 target;
        private Vector2 slotSize;
        private RectTransform pop;
        private Image flash;
        private Image ring;
        private RectTransform label;
        private Text labelText;

        /// <param name="overlay">A full-canvas layer above the deck bar for the effects.</param>
        /// <param name="slotRect">The deck slot RectTransform for an index (null if none).</param>
        /// <param name="hideSlot">Hides (true) or shows (false) a deck slot's content.</param>
        /// <param name="mayPlay">False while the deck bar is covered (the full draft screen); the queue waits.</param>
        public void Initialize(RectTransform overlay, Func<int, RectTransform> slotRect, Action<int, bool> hideSlot, Func<bool> mayPlay)
        {
            layer = overlay;
            getSlotRect = slotRect;
            setSlotHidden = hideSlot;
            canPlay = mayPlay;
        }

        /// <summary>
        /// Queues a merge. Its result slot is hidden at once (the deck has already changed) and shown again when the
        /// last queued merge into that slot has burst.
        /// </summary>
        public void Enqueue(Request request)
        {
            if (request == null || layer == null) return;

            for (int i = 0; i < request.SourceSnapshots.Count; i++)
            {
                GameObject snapshot = request.SourceSnapshots[i];
                if (snapshot == null) continue;

                // Onto the effects layer, centre-anchored, exactly where the slot is
                var rect = snapshot.GetComponent<RectTransform>();
                Vector2 position = ToLayer(rect);
                rect.SetParent(layer, true);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = position;
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;

                var placeholder = new Placeholder
                {
                    Owner = request,
                    SourceIndex = i,
                    Slot = request.SourceSlots[i],
                    Rect = rect,
                    WaitFor = FindMergeMaking(i < request.SourceCards.Count ? request.SourceCards[i] : null)
                };
                placeholders.Add(placeholder);
                if (placeholder.WaitFor == null) Show(placeholder);
                else snapshot.SetActive(false);
            }

            Hide(request.ResultSlot);
            queue.Enqueue(request);
        }

        /// <summary>The queued (or playing, not yet burst) merge whose result is this card, if any.</summary>
        private Request FindMergeMaking(DraftCard card)
        {
            if (card == null) return null;
            Request found = null;
            if (current != null && !burst && ReferenceEquals(current.ResultCard, card)) found = current;
            foreach (Request queued in queue)
            {
                if (ReferenceEquals(queued.ResultCard, card)) found = queued;
            }
            return found;
        }

        /// <summary>Shows a waiting snapshot, hiding whatever the real slot holds now underneath it.</summary>
        private void Show(Placeholder placeholder)
        {
            placeholder.Shown = true;
            placeholder.Rect.gameObject.SetActive(true);
            Hide(placeholder.Slot);
        }

        private void Hide(int slot)
        {
            if (slot < 0) return;
            hiddenSlotCounts.TryGetValue(slot, out int count);
            hiddenSlotCounts[slot] = count + 1;
            setSlotHidden?.Invoke(slot, true);
        }

        private void OnDestroy()
        {
            ClearCurrent();
            foreach (Placeholder placeholder in placeholders)
            {
                if (placeholder.Rect != null) Destroy(placeholder.Rect.gameObject);
            }
            placeholders.Clear();
            foreach (int slot in hiddenSlotCounts.Keys) setSlotHidden?.Invoke(slot, false);
            hiddenSlotCounts.Clear();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            UpdateSparks(dt);

            if (current == null)
            {
                if (queue.Count == 0 || (canPlay != null && !canPlay())) return;
                Begin(queue.Dequeue());
                if (current == null) return;
            }

            elapsed += dt;
            if (!burst)
            {
                UpdateGhosts(dt);
                if (elapsed >= GatherSeconds + FuseSeconds) Burst();
            }
            else
            {
                UpdateAfterBurst();
            }
        }

        // ---------- Phases ----------

        private void Begin(Request request)
        {
            RectTransform resultRect = getSlotRect?.Invoke(request.ResultSlot);
            if (resultRect == null)
            {
                Release(request.ResultSlot);
                return;
            }

            current = request;
            elapsed = 0f;
            burst = false;
            layer.SetAsLastSibling();
            target = ToLayer(resultRect);
            slotSize = resultRect.rect.size;

            for (int i = 0; i < request.SourceSlots.Count; i++)
            {
                int index = i;
                Placeholder placeholder = placeholders.Find(p => p.Owner == request && p.SourceIndex == index);
                Ghost ghost;
                if (placeholder != null && placeholder.Rect != null)
                {
                    // The snapshot itself lifts out; the real slot underneath shows what it holds now
                    placeholders.Remove(placeholder);
                    if (placeholder.Shown) Release(placeholder.Slot);
                    placeholder.Rect.gameObject.SetActive(true);
                    ghost = GhostFromSnapshot(placeholder.Rect);
                }
                else
                {
                    RectTransform source = getSlotRect?.Invoke(request.SourceSlots[i]);
                    // A copy without a slot drops in from above the result slot
                    Vector2 start = source != null ? ToLayer(source) : target + new Vector2(0f, slotSize.y * 1.6f);
                    ghost = CreateGhost(request.SourcePortraits[i], request.OldColor, start);
                    if (source == null) ghost.Rect.GetComponent<CanvasGroup>().alpha = 0f;
                }

                Vector2 from = ghost.Start;
                ghost.ArcHeight = Mathf.Lerp(40f, 90f, Mathf.Clamp01(Mathf.Abs(from.x - target.x) / 600f));
                ghost.Spin = (from.x < target.x ? -1f : 1f) * Random.Range(160f, 260f);
                ghosts.Add(ghost);
            }
        }

        /// <summary>Turns a slot snapshot into a flying ghost, with a rarity-coloured glow behind it.</summary>
        private Ghost GhostFromSnapshot(RectTransform rect)
        {
            transients.Add(rect.gameObject);
            CanvasGroup group = rect.GetComponent<CanvasGroup>();
            if (group == null) group = rect.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.blocksRaycasts = false;

            var glowObject = new GameObject("MergeGlow", typeof(RectTransform), typeof(Image));
            var glowRect = glowObject.GetComponent<RectTransform>();
            glowRect.SetParent(rect, false);
            glowRect.SetAsFirstSibling();
            glowRect.anchorMin = Vector2.zero;
            glowRect.anchorMax = Vector2.one;
            glowRect.offsetMin = new Vector2(-6f, -6f);
            glowRect.offsetMax = new Vector2(6f, 6f);
            var glow = glowObject.GetComponent<Image>();
            glow.raycastTarget = false;
            glow.color = Color.clear;

            return new Ghost { Rect = rect, Frame = glow, GlowOnly = true, Start = rect.anchoredPosition, TrailTimer = TrailInterval };
        }

        private void UpdateGhosts(float dt)
        {
            for (int i = 0; i < ghosts.Count; i++)
            {
                Ghost ghost = ghosts[i];
                var group = ghost.Rect.GetComponent<CanvasGroup>();

                if (elapsed < GatherSeconds)
                {
                    // Lift out of the slot, glowing brighter, with a little shiver
                    float k = elapsed / GatherSeconds;
                    float lift = EaseOutBack(k) * 16f;
                    Vector2 shiver = new Vector2(Mathf.Sin(elapsed * 90f + i * 2f), 0f) * 2f * k;
                    ghost.Rect.anchoredPosition = ghost.Start + new Vector2(0f, lift) + shiver;
                    ghost.Rect.localScale = Vector3.one * Mathf.Lerp(1f, 1.08f, k);
                    Color glowing = Color.Lerp(current.OldColor, Color.white, 0.5f * k);
                    if (ghost.GlowOnly) glowing.a = 0.7f * k;
                    ghost.Frame.color = glowing;
                    group.alpha = Mathf.Max(group.alpha, k);
                    continue;
                }

                // Swoop in: accelerate along an arc, spin and shrink into the result slot
                float t = Mathf.Clamp01((elapsed - GatherSeconds) / FuseSeconds);
                float e = t * t * t;
                Vector2 from = ghost.Start + new Vector2(0f, 16f);
                Vector2 position = Vector2.Lerp(from, target, e) + new Vector2(0f, Mathf.Sin(t * Mathf.PI) * ghost.ArcHeight);
                ghost.Rect.anchoredPosition = position;
                ghost.Rect.localEulerAngles = new Vector3(0f, 0f, ghost.Spin * e);
                ghost.Rect.localScale = Vector3.one * Mathf.Lerp(1.08f, 0.35f, e);
                Color fusing = Color.Lerp(Color.Lerp(current.OldColor, Color.white, 0.5f), current.NewColor, t);
                if (ghost.GlowOnly) fusing.a = 0.7f + 0.3f * t;
                ghost.Frame.color = fusing;
                group.alpha = 1f;

                ghost.TrailTimer -= dt;
                if (ghost.TrailTimer <= 0f)
                {
                    ghost.TrailTimer = TrailInterval;
                    Color trail = Color.Lerp(current.OldColor, current.NewColor, t);
                    SpawnSpark(position, Random.insideUnitCircle * 40f, Random.Range(0.25f, 0.4f), trail, Random.Range(5f, 8f), 0f);
                }
            }
        }

        /// <summary>The copies fuse: flash, spark ring, the upgraded card pops in and the label rises.</summary>
        private void Burst()
        {
            burst = true;
            elapsed = 0f;
            foreach (Ghost ghost in ghosts) Destroy(ghost.Rect.gameObject);
            ghosts.Clear();

            // Cards this merge made for later merges appear in their slots now (before the result slot is released,
            // so it never shows for a frame)
            foreach (Placeholder placeholder in placeholders)
            {
                if (placeholder.WaitFor == current && !placeholder.Shown) Show(placeholder);
            }

            // The real slot comes back now (the pop copy covers it while it settles)
            Release(current.ResultSlot);

            flash = CreateImage("MergeFlash", Color.white, target, slotSize * 0.9f);
            flash.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
            ring = CreateImage("MergeRing", current.NewColor, target, slotSize * 0.9f);
            ring.rectTransform.localEulerAngles = new Vector3(0f, 0f, 45f);
            ring.gameObject.AddComponent<Outline>().effectColor = Color.white;
            ring.transform.SetAsFirstSibling();

            for (int i = 0; i < 22; i++)
            {
                float angle = i / 22f * Mathf.PI * 2f + Random.Range(-0.1f, 0.1f);
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Color color = i % 3 == 0 ? Color.white : current.NewColor;
                SpawnSpark(target, direction * Random.Range(220f, 420f), Random.Range(0.45f, 0.75f), color, Random.Range(6f, 11f), 700f);
            }

            Ghost card = CreateGhost(current.ResultPortrait, current.NewColor, target);
            pop = card.Rect;

            label = new GameObject("MergeLabel", typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
            label.SetParent(layer, false);
            label.sizeDelta = new Vector2(260f, 60f);
            label.anchoredPosition = target + new Vector2(0f, slotSize.y * 0.75f);
            transients.Add(label.gameObject);
            labelText = CreateLabelText(label, $"RANK UP\n<color=#{ColorUtility.ToHtmlStringRGB(current.NewColor)}>{new string('★', current.NewStars)}</color>");
        }

        private void UpdateAfterBurst()
        {
            float popK = Mathf.Clamp01(elapsed / PopSeconds);
            if (pop != null)
            {
                pop.localScale = Vector3.one * Mathf.LerpUnclamped(1.45f, 1f, EaseOutBack(popK));
                pop.GetComponent<CanvasGroup>().alpha = popK < 0.6f ? 1f : 1f - (popK - 0.6f) / 0.4f;
            }

            if (flash != null)
            {
                float k = Mathf.Clamp01(elapsed / 0.25f);
                flash.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.6f, k);
                flash.color = new Color(1f, 1f, 1f, 1f - k);
            }

            if (ring != null)
            {
                float k = Mathf.Clamp01(elapsed / 0.45f);
                ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.8f, 2.3f, 1f - (1f - k) * (1f - k));
                Color c = current.NewColor;
                c.a = 0.55f * (1f - k);
                ring.color = c;
            }

            if (label != null)
            {
                float k = Mathf.Clamp01(elapsed / LabelSeconds);
                label.anchoredPosition = target + new Vector2(0f, slotSize.y * 0.75f + 36f * EaseOutCubic(k));
                label.localScale = Vector3.one * (k < 0.15f ? Mathf.LerpUnclamped(0.4f, 1f, EaseOutBack(k / 0.15f)) : 1f);
                label.GetComponent<CanvasGroup>().alpha = k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f;
            }

            if (elapsed >= LabelSeconds) ClearCurrent();
        }

        private void ClearCurrent()
        {
            foreach (Ghost ghost in ghosts)
            {
                if (ghost.Rect != null) Destroy(ghost.Rect.gameObject);
            }
            ghosts.Clear();
            foreach (GameObject transient in transients)
            {
                if (transient != null) Destroy(transient);
            }
            transients.Clear();
            pop = null;
            flash = null;
            ring = null;
            label = null;
            labelText = null;
            current = null;
        }

        private void Release(int slot)
        {
            if (!hiddenSlotCounts.TryGetValue(slot, out int count)) return;
            if (count <= 1)
            {
                hiddenSlotCounts.Remove(slot);
                setSlotHidden?.Invoke(slot, false);
            }
            else
            {
                hiddenSlotCounts[slot] = count - 1;
            }
        }

        // ---------- Sparks ----------

        private void SpawnSpark(Vector2 position, Vector2 velocity, float lifetime, Color color, float size, float gravity)
        {
            Image image = CreateImage("MergeSpark", color, position, new Vector2(size, size), track: false);
            sparks.Add(new Spark
            {
                Rect = image.rectTransform, Image = image, Velocity = velocity, Lifetime = lifetime, Color = color, Gravity = gravity
            });
        }

        private void UpdateSparks(float dt)
        {
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                Spark spark = sparks[i];
                spark.Age += dt;
                float k = spark.Age / spark.Lifetime;
                if (k >= 1f || spark.Rect == null)
                {
                    if (spark.Rect != null) Destroy(spark.Rect.gameObject);
                    sparks.RemoveAt(i);
                    continue;
                }

                spark.Velocity.y -= spark.Gravity * dt;
                spark.Velocity *= 1f - 2.5f * dt; // drag
                // Whole canvas units, so the squares stay crisp like pixel art
                Vector2 position = spark.Rect.anchoredPosition + spark.Velocity * dt;
                spark.Rect.anchoredPosition = position;
                Color color = spark.Color;
                color.a = 1f - k * k;
                spark.Image.color = color;
            }
        }

        // ---------- Building blocks ----------

        /// <summary>A card ghost: a panel in the rarity colour with the portrait on it, like a deck card lifted out.</summary>
        private Ghost CreateGhost(Sprite portrait, Color color, Vector2 position)
        {
            var root = new GameObject("MergeCard", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Outline));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(layer, false);
            rect.sizeDelta = slotSize;
            rect.anchoredPosition = position;
            transients.Add(root);

            var frame = root.GetComponent<Image>();
            GameplayHUDUI.ApplyPanelContainer(root, GameplayHUDUI.PanelContainerType.Dark, color);
            frame.color = color;
            frame.raycastTarget = false;
            var outline = root.GetComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.8f);
            outline.effectDistance = new Vector2(2f, -2f);

            if (portrait != null)
            {
                var portraitObject = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
                var portraitRect = portraitObject.GetComponent<RectTransform>();
                portraitRect.SetParent(rect, false);
                portraitRect.anchorMin = new Vector2(0.1f, 0.12f);
                portraitRect.anchorMax = new Vector2(0.9f, 0.92f);
                portraitRect.offsetMin = Vector2.zero;
                portraitRect.offsetMax = Vector2.zero;
                var image = portraitObject.GetComponent<Image>();
                image.sprite = portrait;
                image.preserveAspect = true;
                image.raycastTarget = false;
            }

            return new Ghost { Rect = rect, Frame = frame, Start = position, TrailTimer = TrailInterval };
        }

        private Image CreateImage(string objectName, Color color, Vector2 position, Vector2 size, bool track = true)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(layer, false);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (track) transients.Add(go);
            return image;
        }

        private static Text CreateLabelText(RectTransform parent, string value)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var text = go.GetComponent<Text>();
            text.font = UIFontHelper.GetPixelFont();
            text.fontSize = 16;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.lineSpacing = 1.1f;
            text.color = Color.white;
            text.text = value;
            text.raycastTarget = false;
            var outline = go.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
            return text;
        }

        /// <summary>A point inside the layer for the centre of a RectTransform anywhere on the same canvas.</summary>
        private Vector2 ToLayer(RectTransform rect)
        {
            Vector3 world = rect.TransformPoint(rect.rect.center);
            Vector3 local = layer.InverseTransformPoint(world);
            return new Vector2(local.x, local.y);
        }

        private static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        private static float EaseOutCubic(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }
    }
}
