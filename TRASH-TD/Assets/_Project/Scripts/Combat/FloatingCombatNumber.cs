using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.UI;

namespace TrashTD.Combat
{
    /// <summary>
    /// Floating combat popups: damage / heal numbers, MISS and short callouts ("DODGE!", "DUEL").
    ///
    /// One pooled manager draws every popup (no GameObject, component or Update per hit):
    /// - Popups are recycled from a fixed pool (<see cref="MaxPopups"/>); when it is full the oldest one is reused.
    /// - Hits of the same kind on the same unit within <see cref="MergeWindow"/> add up in one number
    ///   that punches bigger instead of stacking a new one (DoT ticks, rapid fire, lasers, multi-hits).
    ///   Pass an anchor (the damaged unit) to enable this; position-only calls never merge.
    /// - The same callout on the same unit (or the same spot) refreshes instead of repeating.
    /// - Text changes only on spawn / merge; the animation only moves and scales transforms.
    /// Look: Press Start 2P with a 1-pixel dark outline and a 2-pixel drop shadow, coloured by damage type,
    /// slammed in, risen on an ease-out, snapped to the art-pixel grid and squashed out at the end.
    /// Outside Play Mode (EditMode tests) each popup is a plain, unanimated TextMesh object instead.
    /// </summary>
    public sealed class FloatingCombatNumber : MonoBehaviour
    {
        public const int MaxPopups = 64;
        public const float MergeWindow = 0.45f;
        private const float CalloutRefreshWindow = 0.6f;
        private const float CalloutMergeDistance = 0.4f;

        private const int FontSize = 24;
        private const float CharacterSize = 0.075f;
        private const float ArtPixel = 1f / 32f;
        private const int OutlineSortingOrder = 50;
        private const int MainSortingOrder = 51;
        private const float ExitTime = 0.14f;
        private const float SpawnHeight = 0.35f;
        private const float StackStep = 0.16f;

        public static readonly Color PhysicalColor = new Color(1f, 0.96f, 0.88f);
        public static readonly Color ArtsColor = new Color(0.8f, 0.52f, 1f);
        public static readonly Color TrueColor = new Color(1f, 0.6f, 0.28f);
        public static readonly Color HealColor = new Color(0.38f, 1f, 0.5f);
        public static readonly Color MissColor = new Color(0.72f, 0.78f, 0.88f);
        private static readonly Color OutlineColor = new Color(0.05f, 0.03f, 0.08f, 1f);

        // 4 cardinal 1 px outlines + a 2 px drop shadow under the glyphs.
        private static readonly Vector3[] OutlineOffsets =
        {
            new Vector3(-ArtPixel, 0f, 0.001f),
            new Vector3(ArtPixel, 0f, 0.001f),
            new Vector3(0f, ArtPixel, 0.001f),
            new Vector3(0f, -ArtPixel, 0.001f),
            new Vector3(0f, -2f * ArtPixel, 0.002f),
        };

        private enum PopupKind { Physical, Arts, True, Heal, Miss, Callout }

        private sealed class Popup
        {
            public Transform Root;
            public TextMesh Main;
            public TextMesh[] Outlines;
            public PopupKind Kind;
            public Object Anchor;
            public string Message;
            public int Amount;
            public Vector3 BasePosition;
            public float Drift;
            public float Age;
            public float Life;
            public float RiseTime;
            public float RiseDistance;
            public float BaseScale;
            public float PunchAge;
            public float PunchStrength;
            public bool Active;
        }

        private static FloatingCombatNumber instance;
        private static string[] damageStrings;
        private static string[] healStrings;
        private const int CachedNumberCount = 1000;

        private readonly List<Popup> active = new List<Popup>(MaxPopups);
        private readonly Stack<Popup> free = new Stack<Popup>(MaxPopups);
        private int created;
        private int driftSign = 1;

        /// <summary>Popups currently on screen (for tests and the sandbox).</summary>
        public static int ActiveCount => instance != null ? instance.active.Count : 0;

        public static void Show(Vector3 position, int amount, DamageType damageType, bool healing = false)
        {
            Show(position, amount, damageType, healing, null);
        }

        /// <summary>
        /// Damage (or heal) number. <paramref name="anchor"/> is the unit that was hit: hits of the same kind
        /// on it within <see cref="MergeWindow"/> are added to one number.
        /// </summary>
        public static void Show(Vector3 position, int amount, DamageType damageType, bool healing, Object anchor)
        {
            if (amount <= 0) return;
            PopupKind kind = healing ? PopupKind.Heal
                : damageType == DamageType.Arts ? PopupKind.Arts
                : damageType == DamageType.True ? PopupKind.True
                : PopupKind.Physical;

            FloatingCombatNumber manager = GetInstance();
            if (manager == null)
            {
                ShowInEditMode(position, FormatNumber(amount, healing), ColorFor(kind));
                return;
            }
            manager.SpawnNumber(position, amount, kind, anchor);
        }

        public static void ShowMiss(Vector3 position)
        {
            ShowMiss(position, null);
        }

        public static void ShowMiss(Vector3 position, Object anchor)
        {
            FloatingCombatNumber manager = GetInstance();
            if (manager == null)
            {
                ShowInEditMode(position, "MISS", MissColor);
                return;
            }
            manager.SpawnCallout(position, "MISS", MissColor, PopupKind.Miss, anchor);
        }

        public static void ShowText(Vector3 position, string message, Color color)
        {
            ShowText(position, message, color, null);
        }

        public static void ShowText(Vector3 position, string message, Color color, Object anchor)
        {
            if (string.IsNullOrEmpty(message)) return;
            FloatingCombatNumber manager = GetInstance();
            if (manager == null)
            {
                ShowInEditMode(position, message, color);
                return;
            }
            manager.SpawnCallout(position, message, color, PopupKind.Callout, anchor);
        }

        private static FloatingCombatNumber GetInstance()
        {
            if (!Application.isPlaying) return null;
            if (instance == null)
            {
                instance = new GameObject("FloatingCombatNumbers").AddComponent<FloatingCombatNumber>();
            }
            return instance;
        }

        /// <summary>
        /// Outside Play Mode (EditMode tests) there is no manager: a popup is a plain static TextMesh object named
        /// "FloatingCombatNumber", as before the pooled version, so tests can find its text and clean it up.
        /// </summary>
        private static void ShowInEditMode(Vector3 position, string message, Color color)
        {
            var go = new GameObject("FloatingCombatNumber");
            go.transform.position = position + Vector3.up * SpawnHeight;
            TextMesh text = go.AddComponent<TextMesh>();
            UIFontHelper.Apply(text, FontStyle.Normal);
            text.fontSize = FontSize;
            text.characterSize = CharacterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.text = message;
            color.a = 1f;
            text.color = color;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        // ------------------------------------------------------------------
        // Spawning
        // ------------------------------------------------------------------

        private void SpawnNumber(Vector3 position, int amount, PopupKind kind, Object anchor)
        {
            if (!ReferenceEquals(anchor, null))
            {
                for (int i = active.Count - 1; i >= 0; i--)
                {
                    Popup existing = active[i];
                    if (!ReferenceEquals(existing.Anchor, anchor) || existing.Kind != kind || existing.Age > MergeWindow) continue;

                    existing.Amount += amount;
                    SetText(existing, FormatNumber(existing.Amount, kind == PopupKind.Heal));
                    existing.BaseScale = Mathf.Max(existing.BaseScale, ScaleForAmount(existing.Amount));
                    existing.Life = Mathf.Max(existing.Life, existing.Age + 0.6f);
                    existing.PunchAge = 0f;
                    existing.PunchStrength = 0.35f;
                    return;
                }
            }

            Popup popup = Acquire();
            popup.Kind = kind;
            popup.Anchor = anchor;
            popup.Message = null;
            popup.Amount = amount;
            popup.Life = kind == PopupKind.Heal ? 0.95f : 0.85f;
            popup.RiseTime = 0.5f;
            popup.RiseDistance = kind == PopupKind.Heal ? 0.4f : 0.5f;
            popup.BaseScale = ScaleForAmount(amount);
            popup.PunchStrength = 0.6f;
            Place(popup, position, anchor);
            SetText(popup, FormatNumber(amount, kind == PopupKind.Heal));
            SetColor(popup, ColorFor(kind));
            Activate(popup);
        }

        private void SpawnCallout(Vector3 position, string message, Color color, PopupKind kind, Object anchor)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Popup existing = active[i];
                if (existing.Kind != kind || existing.Age > CalloutRefreshWindow || existing.Message != message) continue;

                bool sameUnit = !ReferenceEquals(anchor, null)
                    ? ReferenceEquals(existing.Anchor, anchor)
                    : (existing.BasePosition - position).sqrMagnitude <= CalloutMergeDistance * CalloutMergeDistance;
                if (!sameUnit) continue;

                existing.Life = Mathf.Max(existing.Life, existing.Age + 0.55f);
                existing.PunchAge = 0f;
                existing.PunchStrength = 0.3f;
                return;
            }

            Popup popup = Acquire();
            popup.Kind = kind;
            popup.Anchor = anchor;
            popup.Message = message;
            popup.Amount = 0;
            bool miss = kind == PopupKind.Miss;
            popup.Life = miss ? 0.6f : 1.05f;
            popup.RiseTime = miss ? 0.35f : 0.45f;
            popup.RiseDistance = miss ? 0.22f : 0.3f;
            popup.BaseScale = miss ? 0.85f : 1f;
            popup.PunchStrength = 1f;
            Place(popup, position, anchor);
            SetText(popup, message);
            SetColor(popup, color);
            Activate(popup);
        }

        private void Place(Popup popup, Vector3 position, Object anchor)
        {
            // Popups already showing on this unit push the new one up a step so they don't overlap.
            int stacked = 0;
            if (!ReferenceEquals(anchor, null))
            {
                for (int i = 0; i < active.Count; i++)
                {
                    if (ReferenceEquals(active[i].Anchor, anchor) && active[i].Age < active[i].RiseTime) stacked++;
                }
            }

            driftSign = -driftSign;
            popup.Drift = driftSign * Random.Range(0.03f, 0.12f);
            popup.BasePosition = position
                + Vector3.up * (SpawnHeight + StackStep * Mathf.Min(stacked, 3))
                + Vector3.right * Random.Range(-0.1f, 0.1f);
            popup.Age = 0f;
            popup.PunchAge = 0f;
        }

        private void Activate(Popup popup)
        {
            popup.Active = true;
            active.Add(popup);
            if (!popup.Root.gameObject.activeSelf) popup.Root.gameObject.SetActive(true);
            Animate(popup);
        }

        private Popup Acquire()
        {
            if (free.Count > 0) return free.Pop();
            if (created < MaxPopups) return CreatePopup();

            // Pool exhausted: recycle the oldest popup on screen.
            Popup oldest = active[0];
            active.RemoveAt(0);
            oldest.Active = false;
            return oldest;
        }

        private Popup CreatePopup()
        {
            created++;
            var root = new GameObject("FloatingCombatNumber").transform;
            root.SetParent(transform, false);

            var popup = new Popup
            {
                Root = root,
                Outlines = new TextMesh[OutlineOffsets.Length],
            };

            for (int i = 0; i < OutlineOffsets.Length; i++)
            {
                popup.Outlines[i] = CreateText(root, "Outline", OutlineOffsets[i], OutlineSortingOrder);
                popup.Outlines[i].color = OutlineColor;
            }
            popup.Main = CreateText(root, "Text", Vector3.zero, MainSortingOrder);
            root.gameObject.SetActive(false);
            return popup;
        }

        private static TextMesh CreateText(Transform parent, string name, Vector3 localPosition, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            TextMesh text = go.AddComponent<TextMesh>();
            UIFontHelper.Apply(text, FontStyle.Normal);
            text.fontSize = FontSize;
            text.characterSize = CharacterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.sortingLayerID = GridSorting.DefaultLayerId;
            renderer.sortingOrder = sortingOrder;
            return text;
        }

        private static void SetText(Popup popup, string message)
        {
            popup.Main.text = message;
            for (int i = 0; i < popup.Outlines.Length; i++) popup.Outlines[i].text = message;
        }

        private static void SetColor(Popup popup, Color color)
        {
            color.a = 1f;
            popup.Main.color = color;
        }

        private static Color ColorFor(PopupKind kind)
        {
            switch (kind)
            {
                case PopupKind.Arts: return ArtsColor;
                case PopupKind.True: return TrueColor;
                case PopupKind.Heal: return HealColor;
                case PopupKind.Miss: return MissColor;
                default: return PhysicalColor;
            }
        }

        /// <summary>Bigger hits draw bigger: 1x up to 1.45x at 300+.</summary>
        private static float ScaleForAmount(int amount)
        {
            return 1f + 0.45f * Mathf.Clamp01((amount - 30) / 270f);
        }

        private static string FormatNumber(int amount, bool healing)
        {
            if (amount >= CachedNumberCount) return healing ? "+" + amount : "-" + amount;

            string[] cache = healing ? healStrings : damageStrings;
            if (cache == null)
            {
                cache = new string[CachedNumberCount];
                if (healing) healStrings = cache; else damageStrings = cache;
            }
            return cache[amount] ?? (cache[amount] = (healing ? "+" : "-") + amount);
        }

        // ------------------------------------------------------------------
        // Animation
        // ------------------------------------------------------------------

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Popup popup = active[i];
                popup.Age += dt;
                popup.PunchAge += dt;
                if (popup.Age >= popup.Life)
                {
                    Release(popup);
                    active.RemoveAt(i);
                    continue;
                }
                Animate(popup);
            }
        }

        private static void Animate(Popup popup)
        {
            float rise = Mathf.Clamp01(popup.Age / popup.RiseTime);
            rise = 1f - (1f - rise) * (1f - rise) * (1f - rise);

            Vector3 position = popup.BasePosition;
            position.x += popup.Drift * rise;
            position.y += popup.RiseDistance * rise;
            if (popup.Kind == PopupKind.Miss && popup.Age < 0.18f)
            {
                position.x += Mathf.Sin(popup.Age * 70f) * ArtPixel * 1.5f * (1f - popup.Age / 0.18f);
            }
            position.x = Mathf.Round(position.x / ArtPixel) * ArtPixel;
            position.y = Mathf.Round(position.y / ArtPixel) * ArtPixel;
            popup.Root.position = position;

            float scaleX = popup.BaseScale;
            float scaleY = popup.BaseScale;

            if (popup.Kind == PopupKind.Callout && popup.Age < 0.16f && popup.PunchStrength >= 1f)
            {
                // Callouts grow in with an overshoot: 0.3 -> 1.2 -> 1.
                float pop = popup.Age < 0.08f
                    ? Mathf.Lerp(0.3f, 1.2f, popup.Age / 0.08f)
                    : Mathf.Lerp(1.2f, 1f, (popup.Age - 0.08f) / 0.08f);
                scaleX *= pop;
                scaleY *= pop;
            }
            else
            {
                // Numbers slam in big and settle; merges and refreshes punch again.
                float u = Mathf.Clamp01(popup.PunchAge / 0.14f);
                float punch = 1f + Mathf.Min(popup.PunchStrength, 0.6f) * (1f - u) * (1f - u);
                scaleX *= punch;
                scaleY *= punch;
            }

            float remaining = popup.Life - popup.Age;
            if (remaining < ExitTime)
            {
                float e = Mathf.Clamp01(remaining / ExitTime);
                scaleY *= e;
                scaleX *= 1f + (1f - e) * 0.3f;
            }

            popup.Root.localScale = new Vector3(scaleX, scaleY, 1f);
        }

        private static void Release(Popup popup)
        {
            popup.Active = false;
            popup.Anchor = null;
            popup.Message = null;
            popup.Root.gameObject.SetActive(false);
            if (instance != null) instance.free.Push(popup);
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