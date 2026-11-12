using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Combat
{
    public enum IceStatusState
    {
        None,
        Frost,  // Chilled: slowed, a light frost creeping over the body
        Ice     // Frozen: encased in ice
    }

    /// <summary>
    /// Pixel-art ice for the chilled and frozen status effects, drawn over an enemy on top of its blue tint.
    /// Both follow the enemy's actual silhouette (read from its current sprite frame, so they track the walk
    /// animation and mirroring) instead of a bounding box, and use the enemy's own pixel size.
    /// Chilled: a faint icy sheen and a sparse rime of frost flecks along the outline, heaviest near the feet,
    /// with an occasional cold sparkle. Frozen: a glassy ice shell hugging the body with outward ice crystals,
    /// glints and cracks; it snaps on with a burst and shatters when the freeze ends or the enemy dies.
    /// Runs on scaled time.
    /// </summary>
    public sealed class IceStatusVisual : MonoBehaviour
    {
        public const string ObjectName = "IceStatusVisual";

        private const int SortingOffset = 12; // Above the enemy, below the stun stars (+15)
        private const float FrostShimmerInterval = 0.45f;
        private const float SnapInDuration = 0.14f;
        private const int IcePixelsPerChunk = 170;    // Roughly one chunk of ice per this many body pixels
        private const float IceSkippedChunks = 0.25f; // Share of chunks left out so the body is not covered all over
        private const float IceSeamWidth = 1.7f;      // Gap between neighbouring chunks, in pixels
        private const int IceMinChunkPixels = 14;
        private const float FrozenOpacity = 0.66f; // Overall opacity of the ice shell once settled
        private const int IcePad = 7;   // Room around the sprite for the ice shell and its crystals
        private const int FrostPad = 2;

        private static readonly Color IceColor = new Color(0.55f, 0.85f, 1f, 1f);
        private static readonly Dictionary<Sprite, Silhouette> SilhouetteCache = new Dictionary<Sprite, Silhouette>();
        private static readonly Dictionary<(Sprite source, int kind), Sprite> SpriteCache = new Dictionary<(Sprite source, int kind), Sprite>();

        private sealed class Silhouette
        {
            public int Width;
            public int Height;
            public bool[] Solid;
            public Vector2 Pivot;   // In sprite pixels
            public float PixelsPerUnit;
        }

        private SpriteRenderer overlay;
        private SpriteRenderer targetRenderer;
        private IceStatusState state;
        private Sprite builtFor;
        private Sprite overlaySprite;
        private float stateTime;
        private float shimmerTimer;
        private int shimmerFrame;
        private float sparkleTimer;

        /// <summary>
        /// Puts the target into the given ice state, creating the overlay on first use. Returns the overlay
        /// (null if the target never needed one). Safe to call every frame with an unchanged state.
        /// </summary>
        public static IceStatusVisual SetState(GameObject target, IceStatusState newState, SpriteRenderer renderer, IceStatusVisual existing = null)
        {
            if (target == null) return existing;

            IceStatusVisual visual = existing;
            if (visual == null)
            {
                if (newState == IceStatusState.None) return null;

                var go = new GameObject(ObjectName);
                go.transform.SetParent(target.transform, false);
                visual = go.AddComponent<IceStatusVisual>();
                visual.overlay = go.AddComponent<SpriteRenderer>();
            }

            visual.Apply(newState, renderer);
            return visual;
        }

        public IceStatusState State => state;

        public void Apply(IceStatusState newState, SpriteRenderer renderer)
        {
            if (renderer != null) targetRenderer = renderer;
            if (newState == state) return;

            IceStatusState previous = state;
            state = newState;
            stateTime = 0f;
            shimmerTimer = 0f;
            shimmerFrame = 0;
            sparkleTimer = Random.Range(0.3f, 0.9f);
            builtFor = null; // force the overlay art to be rebuilt
            overlaySprite = null;

            if (previous == IceStatusState.Ice && newState != IceStatusState.Ice)
                Shatter();
            if (newState == IceStatusState.Ice)
                SnapFreezeBurst();

            overlay.enabled = newState != IceStatusState.None;
            if (newState != IceStatusState.None) Refresh();
        }

        private void LateUpdate()
        {
            if (state == IceStatusState.None || overlay == null) return;

            float dt = Time.deltaTime;
            stateTime += dt;

            if (state == IceStatusState.Frost)
            {
                shimmerTimer += dt;
                if (shimmerTimer >= FrostShimmerInterval)
                {
                    shimmerTimer -= FrostShimmerInterval;
                    shimmerFrame = (shimmerFrame + 1) % 2;
                    builtFor = null;
                }

                sparkleTimer -= dt;
                if (sparkleTimer <= 0f && overlay.enabled)
                {
                    sparkleTimer = Random.Range(1.1f, 2.2f);
                    CombatFx.Crosses(RandomBodyPoint(), new Color(0.75f, 0.93f, 1f), 1, 0.02f, false, 0.6f);
                }
            }

            Refresh();
        }

        /// <summary>Lays the overlay exactly over the target's current sprite, rebuilding the art when the frame changes.</summary>
        private void Refresh()
        {
            Sprite sprite = targetRenderer != null ? targetRenderer.sprite : null;
            bool visible = sprite != null && targetRenderer.enabled && targetRenderer.gameObject.activeInHierarchy;
            overlay.enabled = visible;
            if (!visible) return;

            if (sprite != builtFor || overlaySprite == null)
            {
                builtFor = sprite;
                overlaySprite = state == IceStatusState.Ice ? GetIce(sprite) : GetFrost(sprite, shimmerFrame);
            }
            // Some enemy animations assign their frame to every child renderer, this one included: put ours back
            if (overlay.sprite != overlaySprite) overlay.sprite = overlaySprite;

            // Same transform, flip and pixel size as the enemy's sprite, so the overlay lines up pixel for pixel
            Transform target = targetRenderer.transform;
            transform.SetPositionAndRotation(target.position, target.rotation);
            Vector3 lossy = target.lossyScale;
            Vector3 parent = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            Vector3 scale = new Vector3(
                parent.x != 0f ? lossy.x / parent.x : 1f,
                parent.y != 0f ? lossy.y / parent.y : 1f,
                parent.z != 0f ? lossy.z / parent.z : 1f);

            overlay.flipX = targetRenderer.flipX;
            overlay.flipY = targetRenderer.flipY;
            overlay.sortingLayerID = targetRenderer.sortingLayerID;
            overlay.sortingOrder = targetRenderer.sortingOrder + SortingOffset;

            if (state == IceStatusState.Ice)
            {
                // Snap-in: the shell lands slightly oversized and settles
                float t = Mathf.Clamp01(stateTime / SnapInDuration);
                transform.localScale = scale * Mathf.Lerp(1.12f, 1f, t * t);
                overlay.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.35f, FrozenOpacity, t));
            }
            else
            {
                transform.localScale = scale;
                overlay.color = new Color(1f, 1f, 1f, 0.55f + 0.12f * Mathf.Sin(stateTime * 2.4f));
            }
        }

        private Vector2 BodyCenter => targetRenderer != null ? (Vector2)targetRenderer.bounds.center : (Vector2)transform.position;

        /// <summary>A random point on the enemy's visible body (falls back to the sprite bounds).</summary>
        private Vector2 RandomBodyPoint()
        {
            Sprite sprite = targetRenderer != null ? targetRenderer.sprite : null;
            Silhouette sil = sprite != null ? GetSilhouette(sprite) : null;
            if (sil != null)
            {
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    int x = Random.Range(0, sil.Width);
                    int y = Random.Range(0, sil.Height);
                    if (!sil.Solid[y * sil.Width + x]) continue;

                    float lx = (x + 0.5f - sil.Pivot.x) / sil.PixelsPerUnit * (targetRenderer.flipX ? -1f : 1f);
                    float ly = (y + 0.5f - sil.Pivot.y) / sil.PixelsPerUnit;
                    return targetRenderer.transform.TransformPoint(new Vector3(lx, ly, 0f));
                }
            }
            return BodyCenter;
        }

        private void SnapFreezeBurst()
        {
            CombatFx.Sparks(BodyCenter, IceColor, 8, 2.2f, 0.25f, big: true);
        }

        private void Shatter()
        {
            Vector2 center = BodyCenter;
            float radius = targetRenderer != null
                ? Mathf.Max(0.45f, Mathf.Max(targetRenderer.bounds.size.x, targetRenderer.bounds.size.y) * 0.45f)
                : 0.6f;
            CombatFx.GlassShatter(center, radius, new Color(0.7f, 0.92f, 1f));
            CombatFx.Sparks(center, IceColor, 10, 3f, 0.35f, big: true);
        }

        // ─────────────────────────────────────────────────────
        //  Silhouette of a sprite frame
        // ─────────────────────────────────────────────────────

        /// <summary>
        /// The opaque pixels of a sprite frame. Read back through a render texture, so it works on sprites whose
        /// textures are not marked readable. Cached per sprite; null only if the sprite has no texture.
        /// </summary>
        private static Silhouette GetSilhouette(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null) return null;

            if (SilhouetteCache.TryGetValue(sprite, out Silhouette cached) && cached != null) return cached;

            int w = Mathf.Max(1, Mathf.RoundToInt(sprite.rect.width));
            int h = Mathf.Max(1, Mathf.RoundToInt(sprite.rect.height));
            var sil = new Silhouette
            {
                Width = w,
                Height = h,
                Solid = new bool[w * h],
                Pivot = sprite.pivot,
                PixelsPerUnit = sprite.pixelsPerUnit
            };

            if (!ReadOpaquePixels(sprite, sil)) FillEllipse(sil);
            SilhouetteCache[sprite] = sil;
            return sil;
        }

        private static bool ReadOpaquePixels(Sprite sprite, Silhouette sil)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return false;

            RenderTexture previous = RenderTexture.active;
            RenderTexture temp = null;
            Texture2D readback = null;
            try
            {
                Texture2D source = sprite.texture;
                Rect rect = sprite.textureRect;
                int rw = Mathf.RoundToInt(rect.width);
                int rh = Mathf.RoundToInt(rect.height);
                if (rw <= 0 || rh <= 0) return false;

                temp = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(source, temp);
                RenderTexture.active = temp;

                readback = new Texture2D(rw, rh, TextureFormat.RGBA32, false);
                readback.ReadPixels(new Rect(rect.x, rect.y, rw, rh), 0, 0);
                Color32[] pixels = readback.GetPixels32();

                // A tightly packed sprite is trimmed: its pixels sit at an offset inside the full sprite rect
                Vector2 offset = sprite.textureRectOffset;
                int ox = Mathf.RoundToInt(offset.x);
                int oy = Mathf.RoundToInt(offset.y);
                int solid = 0;
                for (int y = 0; y < rh; y++)
                {
                    for (int x = 0; x < rw; x++)
                    {
                        int dx = x + ox;
                        int dy = y + oy;
                        if (dx < 0 || dy < 0 || dx >= sil.Width || dy >= sil.Height) continue;
                        if (pixels[y * rw + x].a <= 40) continue;
                        sil.Solid[dy * sil.Width + dx] = true;
                        solid++;
                    }
                }
                return solid > 0;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[IceStatusVisual] Could not read {sprite.name}, using an oval silhouette: {ex.Message}");
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                if (temp != null) RenderTexture.ReleaseTemporary(temp);
                if (readback != null) Destroy(readback);
            }
        }

        private static void FillEllipse(Silhouette sil)
        {
            float cx = (sil.Width - 1) * 0.5f;
            float cy = (sil.Height - 1) * 0.5f;
            float rx = Mathf.Max(1f, sil.Width * 0.42f);
            float ry = Mathf.Max(1f, sil.Height * 0.42f);
            for (int y = 0; y < sil.Height; y++)
            {
                for (int x = 0; x < sil.Width; x++)
                {
                    float dx = (x - cx) / rx;
                    float dy = (y - cy) / ry;
                    sil.Solid[y * sil.Width + x] = dx * dx + dy * dy <= 1f;
                }
            }
        }

        // ─────────────────────────────────────────────────────
        //  Overlay art, cached per sprite frame
        // ─────────────────────────────────────────────────────

        private static Sprite GetIce(Sprite sprite)
        {
            return Cached((sprite, 0), () => BuildIce(sprite));
        }

        private static Sprite GetFrost(Sprite sprite, int variant)
        {
            return Cached((sprite, 1 + variant), () => BuildFrost(sprite, variant));
        }

        private static Sprite Cached((Sprite source, int kind) key, System.Func<Sprite> build)
        {
            // Domain reload may be off, so a cached sprite can be a destroyed reference from an earlier play session
            if (SpriteCache.TryGetValue(key, out Sprite sprite) && sprite != null) return sprite;
            sprite = build();
            SpriteCache[key] = sprite;
            return sprite;
        }

        /// <summary>The silhouette copied into a larger grid with a margin of <paramref name="pad"/> pixels.</summary>
        private static bool[] Padded(Silhouette sil, int pad, out int width, out int height)
        {
            width = sil.Width + pad * 2;
            height = sil.Height + pad * 2;
            var grid = new bool[width * height];
            for (int y = 0; y < sil.Height; y++)
            {
                for (int x = 0; x < sil.Width; x++)
                {
                    grid[(y + pad) * width + x + pad] = sil.Solid[y * sil.Width + x];
                }
            }
            return grid;
        }

        private static bool[] Dilate(bool[] mask, int width, int height, bool diagonals)
        {
            var result = (bool[])mask.Clone();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (mask[y * width + x]) continue;
                    bool near = At(mask, width, height, x - 1, y) || At(mask, width, height, x + 1, y) ||
                                At(mask, width, height, x, y - 1) || At(mask, width, height, x, y + 1);
                    if (!near && diagonals)
                    {
                        near = At(mask, width, height, x - 1, y - 1) || At(mask, width, height, x + 1, y - 1) ||
                               At(mask, width, height, x - 1, y + 1) || At(mask, width, height, x + 1, y + 1);
                    }
                    if (near) result[y * width + x] = true;
                }
            }
            return result;
        }

        private static bool At(bool[] mask, int width, int height, int x, int y)
        {
            return x >= 0 && y >= 0 && x < width && y < height && mask[y * width + x];
        }

        /// <summary>Distance (in pixels, roughly) from each masked pixel to the nearest unmasked one; 0 outside.</summary>
        private static int[] DistanceInside(bool[] mask, int width, int height)
        {
            const int Orthogonal = 3;
            const int Diagonal = 4;
            const int Far = 100000;
            var d = new int[width * height];
            for (int i = 0; i < d.Length; i++) d[i] = mask[i] ? Far : 0;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    if (d[i] == 0) continue;
                    int best = d[i];
                    best = Mathf.Min(best, Get(d, width, height, x - 1, y) + Orthogonal);
                    best = Mathf.Min(best, Get(d, width, height, x, y - 1) + Orthogonal);
                    best = Mathf.Min(best, Get(d, width, height, x - 1, y - 1) + Diagonal);
                    best = Mathf.Min(best, Get(d, width, height, x + 1, y - 1) + Diagonal);
                    d[i] = best;
                }
            }
            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = width - 1; x >= 0; x--)
                {
                    int i = y * width + x;
                    if (d[i] == 0) continue;
                    int best = d[i];
                    best = Mathf.Min(best, Get(d, width, height, x + 1, y) + Orthogonal);
                    best = Mathf.Min(best, Get(d, width, height, x, y + 1) + Orthogonal);
                    best = Mathf.Min(best, Get(d, width, height, x + 1, y + 1) + Diagonal);
                    best = Mathf.Min(best, Get(d, width, height, x - 1, y + 1) + Diagonal);
                    d[i] = best;
                }
            }

            for (int i = 0; i < d.Length; i++)
            {
                if (d[i] > 0) d[i] = Mathf.Max(1, Mathf.RoundToInt(d[i] / (float)Orthogonal));
            }
            return d;
        }

        private static int Get(int[] d, int width, int height, int x, int y)
        {
            // Off-grid counts as outside (distance 0), so shapes touching the edge still get a rim
            return x >= 0 && y >= 0 && x < width && y < height ? d[y * width + x] : 0;
        }

        private static Sprite BuildIce(Sprite sprite)
        {
            Silhouette sil = GetSilhouette(sprite);
            int pad = IcePad;
            bool[] body = Padded(sil, pad, out int w, out int h);

            var rng = new System.Random(StableHash(sprite.name) * 7919 + w * 31 + h);

            // Ice in several separate chunks that follow the silhouette, with gaps between them
            bool[] shell = BuildChunks(body, w, h, rng);
            int[] depth = DistanceInside(shell, w, h);

            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

            // Weighted centre of the body, used to aim the outward crystals
            float cxSum = 0f, cySum = 0f; int count = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (body[y * w + x]) { cxSum += x; cySum += y; count++; }
            float cx = count > 0 ? cxSum / count : w * 0.5f;
            float cy = count > 0 ? cySum / count : h * 0.5f;

            // Body of the shell: paler on top, bluer toward the feet, with a glassy lighter band just inside the rim
            for (int y = 0; y < h; y++)
            {
                float t = Mathf.Clamp01((y - pad) / (float)Mathf.Max(1, h - pad * 2));
                Color fill = Color.Lerp(new Color(0.40f, 0.70f, 0.98f, 0.50f), new Color(0.80f, 0.94f, 1f, 0.34f), t);
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!shell[i]) continue;
                    pixels[i] = depth[i] <= 3 ? new Color(0.86f, 0.97f, 1f, 0.52f) : fill;
                }
            }

            // Diagonal glints sweeping across the lit side
            int diagonal = w + h;
            int streakA = Mathf.RoundToInt(diagonal * 0.30f);
            int streakB = Mathf.RoundToInt(diagonal * 0.40f);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!shell[i] || depth[i] <= 2) continue;
                    int d = x + (h - 1 - y);
                    if (d >= streakA && d < streakA + 3) pixels[i] = new Color(1f, 1f, 1f, 0.58f);
                    else if (d == streakB) pixels[i] = new Color(1f, 1f, 1f, 0.46f);
                }
            }

            // Cracks: jagged walks down from the upper body
            for (int c = 0; c < 2; c++)
            {
                int x = Mathf.RoundToInt(cx) + (c == 0 ? 3 : -4) + rng.Next(-2, 3);
                int y = Mathf.RoundToInt(cy) + (h - pad * 2) / 4 + rng.Next(0, 3);
                int length = Mathf.Max(6, (h - pad * 2) / 3 + rng.Next(0, 4));
                for (int step = 0; step < length; step++)
                {
                    if (x >= 0 && y >= 0 && x < w && y < h && shell[y * w + x] && depth[y * w + x] > 1)
                    {
                        pixels[y * w + x] = new Color(1f, 1f, 1f, 0.82f);
                        if (x + 1 < w && shell[y * w + x + 1] && depth[y * w + x + 1] > 1 && pixels[y * w + x + 1].a < 0.7f)
                            pixels[y * w + x + 1] = new Color(0.3f, 0.6f, 0.9f, 0.6f);
                    }
                    y--;
                    x += rng.Next(-1, 2);
                }
            }

            // Rim: bright on the lit upper-left edges, deep blue on the shaded lower-right
            var rim = new List<Vector2Int>();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!shell[i] || depth[i] != 1) continue;
                    bool lit = !At(shell, w, h, x - 1, y) || !At(shell, w, h, x, y + 1);
                    pixels[i] = lit ? new Color(0.93f, 0.99f, 1f, 0.97f) : new Color(0.27f, 0.57f, 0.9f, 0.97f);
                    rim.Add(new Vector2Int(x, y));
                }
            }

            // Ice crystals bursting out of the shell, on the outline, pointing away from the body
            int crystals = Mathf.Clamp(rim.Count / 14, 6, 16);
            for (int c = 0; c < crystals && rim.Count > 0; c++)
            {
                Vector2Int start = rim[rng.Next(0, rim.Count)];
                Vector2 away = new Vector2(start.x - cx, start.y - cy);
                if (away.sqrMagnitude < 0.01f) away = Vector2.up;
                away.Normalize();
                // Snap to one of the eight pixel directions so the crystal stays crisp
                float snapped = Mathf.Round(Mathf.Atan2(away.y, away.x) / (Mathf.PI / 4f)) * (Mathf.PI / 4f);
                var dir = new Vector2Int(Mathf.RoundToInt(Mathf.Cos(snapped)), Mathf.RoundToInt(Mathf.Sin(snapped)));
                var side = new Vector2Int(-dir.y, dir.x);
                int length = rng.Next(3, pad);
                bool lit = dir.x < 0 || dir.y > 0;

                for (int k = 1; k <= length; k++)
                {
                    int px = start.x + dir.x * k;
                    int py = start.y + dir.y * k;
                    Color main = lit ? new Color(0.93f, 0.99f, 1f, 0.95f) : new Color(0.32f, 0.62f, 0.92f, 0.95f);
                    if (k == length) main = new Color(1f, 1f, 1f, 0.95f);
                    Put(pixels, w, h, px, py, main, shell);
                    // Wide at the base, tapering to a point
                    if (k <= length / 2 + 1 && k < length)
                        Put(pixels, w, h, px + side.x, py + side.y, new Color(0.62f, 0.86f, 1f, 0.8f), shell);
                }
            }

            return MakeSprite("Ice " + sprite.name, pixels, w, h, sil.Pivot + new Vector2(pad, pad), sil.PixelsPerUnit);
        }

        /// <summary>
        /// The ice for a body: a shell hugging the silhouette, broken into separate chunks. Seeds are scattered over
        /// the body and every shell pixel goes to its nearest seed; a gap is cut wherever two chunks meet (wobbly,
        /// so the seams look like fractures), and a few chunks are left out so parts of the body stay uncovered.
        /// Each remaining chunk is its own piece of ice with its own rim, so nothing is one solid mass.
        /// </summary>
        private static bool[] BuildChunks(bool[] body, int w, int h, System.Random rng)
        {
            bool[] hug = Dilate(Dilate(body, w, h, true), w, h, false);

            var bodyPixels = new List<Vector2Int>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (body[y * w + x]) bodyPixels.Add(new Vector2Int(x, y));
            if (bodyPixels.Count == 0) return hug;

            // Spread the seeds out: each new one is the best of a few random candidates, farthest from the others
            int seedCount = Mathf.Clamp(bodyPixels.Count / IcePixelsPerChunk, 4, 10);
            var seeds = new List<Vector2>(seedCount);
            seeds.Add(bodyPixels[rng.Next(0, bodyPixels.Count)]);
            while (seeds.Count < seedCount)
            {
                Vector2 best = Vector2.zero;
                float bestDistance = -1f;
                for (int candidate = 0; candidate < 8; candidate++)
                {
                    Vector2 point = bodyPixels[rng.Next(0, bodyPixels.Count)];
                    float nearest = float.MaxValue;
                    for (int s = 0; s < seeds.Count; s++) nearest = Mathf.Min(nearest, (point - seeds[s]).sqrMagnitude);
                    if (nearest > bestDistance) { bestDistance = nearest; best = point; }
                }
                seeds.Add(best);
            }

            // Leave a few chunks out entirely so the sprite is not blanketed
            var skipped = new bool[seedCount];
            int toSkip = Mathf.Max(1, Mathf.RoundToInt(seedCount * IceSkippedChunks));
            for (int i = 0; i < toSkip && seedCount - i > 3; i++) skipped[rng.Next(0, seedCount)] = true;

            // Assign shell pixels to chunks and cut the seams
            var chunkOf = new int[w * h];
            var chunkSize = new int[seedCount];
            var shell = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    chunkOf[i] = -1;
                    if (!hug[i]) continue;

                    float nearest = float.MaxValue, second = float.MaxValue;
                    int owner = 0;
                    var here = new Vector2(x, y);
                    for (int s = 0; s < seeds.Count; s++)
                    {
                        float d = (here - seeds[s]).magnitude;
                        if (d < nearest) { second = nearest; nearest = d; owner = s; }
                        else if (d < second) second = d;
                    }

                    // Seams get ragged by a per-pixel wobble, so they read as cracks rather than straight lines
                    float wobble = Mathf.Abs(((x * 73856093) ^ (y * 19349663)) % 100) / 100f;
                    if (second - nearest < IceSeamWidth + wobble * 1.2f) continue;
                    if (skipped[owner]) continue;

                    chunkOf[i] = owner;
                    chunkSize[owner]++;
                    shell[i] = true;
                }
            }

            // Drop specks that are too small to read as a piece of ice
            for (int i = 0; i < shell.Length; i++)
            {
                if (shell[i] && chunkSize[chunkOf[i]] < IceMinChunkPixels) shell[i] = false;
            }
            return shell;
        }
        private static void Put(Color[] pixels, int w, int h, int x, int y, Color color, bool[] skipInside)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int i = y * w + x;
            if (skipInside != null && skipInside[i]) return;
            pixels[i] = color;
        }

        private static Sprite BuildFrost(Sprite sprite, int variant)
        {
            Silhouette sil = GetSilhouette(sprite);
            int pad = FrostPad;
            bool[] body = Padded(sil, pad, out int w, out int h);
            int[] depth = DistanceInside(body, w, h);

            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;

            var rng = new System.Random(StableHash(sprite.name) * 31 + variant * 7919 + w);
            Color white = new Color(0.97f, 1f, 1f, 0.9f);
            Color pale = new Color(0.78f, 0.93f, 1f, 0.75f);
            Color blue = new Color(0.55f, 0.8f, 1f, 0.6f);

            // Barely-there icy sheen over the body, a little stronger at the outline
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!body[i]) continue;
                    pixels[i] = depth[i] <= 2 ? new Color(0.8f, 0.94f, 1f, 0.12f) : new Color(0.8f, 0.94f, 1f, 0.035f);
                }
            }

            // Frost flecks on the outline, thickest around the feet and thinning upward
            int bodyRows = Mathf.Max(1, h - pad * 2);
            for (int y = 0; y < h; y++)
            {
                float height01 = Mathf.Clamp01((y - pad) / (float)bodyRows); // 0 at the feet
                float chance = Mathf.Lerp(0.36f, 0.06f, height01);
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (!body[i] || depth[i] > 2) continue;
                    if (rng.NextDouble() > chance) continue;

                    Color c = rng.Next(0, 4) == 0 ? white : (rng.Next(0, 2) == 0 ? pale : blue);
                    pixels[i] = depth[i] == 1 ? c : new Color(c.r, c.g, c.b, c.a * 0.7f);

                    // A tiny rime tick sticking out past the outline now and then
                    if (depth[i] == 1 && rng.Next(0, 5) == 0)
                    {
                        int ox = !At(body, w, h, x - 1, y) ? -1 : (!At(body, w, h, x + 1, y) ? 1 : 0);
                        int oy = !At(body, w, h, x, y + 1) ? 1 : (!At(body, w, h, x, y - 1) ? -1 : 0);
                        Put(pixels, w, h, x + ox, y + oy, new Color(pale.r, pale.g, pale.b, 0.6f), body);
                    }
                }
            }

            // A few rime flecks deeper in, low on the body
            for (int i = 0; i < Mathf.Max(2, sil.Width * sil.Height / 220); i++)
            {
                int x = rng.Next(pad, w - pad);
                int y = pad + rng.Next(0, Mathf.Max(1, bodyRows / 2));
                if (body[y * w + x] && depth[y * w + x] > 2) pixels[y * w + x] = new Color(pale.r, pale.g, pale.b, 0.55f);
            }

            // One or two tiny snowflakes, only where they fit inside the body
            for (int i = 0; i < 2; i++)
            {
                int x = rng.Next(pad + 2, Mathf.Max(pad + 3, w - pad - 2));
                int y = rng.Next(pad + 2, Mathf.Max(pad + 3, h - pad - 2));
                if (!(At(body, w, h, x, y) && At(body, w, h, x - 1, y) && At(body, w, h, x + 1, y) &&
                      At(body, w, h, x, y - 1) && At(body, w, h, x, y + 1))) continue;
                pixels[y * w + x] = white;
                pixels[y * w + x - 1] = pale; pixels[y * w + x + 1] = pale;
                pixels[(y - 1) * w + x] = pale; pixels[(y + 1) * w + x] = pale;
            }

            return MakeSprite($"Frost {sprite.name} v{variant}", pixels, w, h, sil.Pivot + new Vector2(pad, pad), sil.PixelsPerUnit);
        }

        /// <summary>Same seed every run, so a sprite frame always gets the same frost and cracks.</summary>
        private static int StableHash(string text)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < text.Length; i++) hash = hash * 31 + text[i];
                return hash;
            }
        }

        private static Sprite MakeSprite(string name, Color[] pixels, int w, int h, Vector2 pivotPixels, float pixelsPerUnit)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, w, h), new Vector2(pivotPixels.x / w, pivotPixels.y / h), pixelsPerUnit);
        }
    }
}
