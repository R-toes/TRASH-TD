using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Systems;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Shows what the Junkernaut protection aura covers (visual only; the rule lives in <see cref="JunkernautEnemy"/>).
    ///
    /// - <b>Aura zone:</b> each live Junkernaut's tile and its 8 neighbours are tinted amber on the Ground layer,
    ///   with a pulsing 2 px border around the union. Tiles covered by several Junkernauts are tinted deeper,
    ///   matching the stacking reduction (50% + 10% per extra Junkernaut).
    /// - <b>Protected enemies:</b> every enemy the aura currently protects
    ///   (<see cref="JunkernautEnemy.GetProtectingJunkernautCount"/>) gets a pixel shield beside its health bar,
    ///   with one pip per protecting Junkernaut (up to 5).
    /// - <b>Hits:</b> when the aura reduces a hit (<see cref="NotifyProtectedHit"/>, from
    ///   <c>EnemyBase.TryTakeAttackDamage</c>), that enemy's shield flashes white and punches.
    ///
    /// One manager per scene, created when a Junkernaut registers in Play Mode. The zone is rebuilt only when a
    /// Junkernaut changes tile; protected enemies are rescanned 10 times a second; everything is pooled.
    /// </summary>
    public sealed class JunkernautAuraVisual : MonoBehaviour
    {
        public static readonly Color AuraColor = new Color(1f, 0.74f, 0.24f);

        private const float ArtPixel = 1f / 32f;
        private const int ZoneFillOrder = 6;    // Ground layer: above map art (1), weather (3) and acid FX (5)
        private const int ZoneEdgeOrder = 7;
        private const int ShieldOrder = 48;     // Default layer: above units and health bars, below combat numbers (50)
        private const float ScanInterval = 0.1f;
        private const float FlashTime = 0.18f;
        private const int MaxPips = 5;

        private sealed class Marker
        {
            public Transform Root;
            public SpriteRenderer Shield;
            public SpriteRenderer ShieldFlash;
            public SpriteRenderer[] Pips;
            public EnemyBase Enemy;
            public SpriteRenderer Body;
            public int Count;
            public float Flash;
            public bool Seen;
        }

        private static JunkernautAuraVisual instance;
        private static readonly Vector2Int[] Directions =
        {
            Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right,
        };

        private readonly Dictionary<Vector2Int, int> zoneCells = new Dictionary<Vector2Int, int>();
        private readonly List<Vector2Int> junkernautCells = new List<Vector2Int>();
        private readonly List<Vector2Int> lastJunkernautCells = new List<Vector2Int>();
        private readonly List<SpriteRenderer> fills = new List<SpriteRenderer>();
        private readonly List<int> fillCounts = new List<int>();
        private readonly List<SpriteRenderer> edges = new List<SpriteRenderer>();
        private readonly Dictionary<EnemyBase, Marker> markers = new Dictionary<EnemyBase, Marker>();
        private readonly Stack<Marker> freeMarkers = new Stack<Marker>();
        private readonly List<EnemyBase> staleMarkers = new List<EnemyBase>();

        private int activeFills;
        private int activeEdges;
        private float scanTimer;
        private float pulseTime;
        private GridManager grid;
        private Transform zoneRoot;
        private Transform markerRoot;
        private Texture2D shieldTexture;
        private Texture2D shieldFlashTexture;
        private Sprite shieldSprite;
        private Sprite shieldFlashSprite;
        private Sprite texelSprite;

        /// <summary>Number of enemies currently showing a shield marker (for tests).</summary>
        public static int ProtectedMarkerCount => instance != null ? instance.markers.Count : 0;

        public static void EnsureInstance()
        {
            if (!Application.isPlaying || instance != null) return;
            instance = new GameObject("JunkernautAuraVisual").AddComponent<JunkernautAuraVisual>();
        }

        /// <summary>Called when the aura reduced a hit on <paramref name="enemy"/>: flashes its shield.</summary>
        public static void NotifyProtectedHit(EnemyBase enemy)
        {
            if (instance == null || enemy == null) return;
            if (instance.markers.TryGetValue(enemy, out Marker marker))
            {
                marker.Flash = FlashTime;
            }
            else
            {
                instance.scanTimer = ScanInterval; // show it on the next frame
            }
        }

        private void Awake()
        {
            zoneRoot = new GameObject("Zone").transform;
            zoneRoot.SetParent(transform, false);
            markerRoot = new GameObject("Markers").transform;
            markerRoot.SetParent(transform, false);
            BuildSprites();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            if (shieldSprite != null) Destroy(shieldSprite);
            if (shieldFlashSprite != null) Destroy(shieldFlashSprite);
            if (shieldFlashTexture != null) Destroy(shieldFlashTexture);
            if (texelSprite != null) Destroy(texelSprite);
            if (shieldTexture != null) Destroy(shieldTexture);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            pulseTime += dt;

            UpdateZone();

            scanTimer += dt;
            if (scanTimer >= ScanInterval)
            {
                scanTimer = 0f;
                ScanProtectedEnemies();
            }

            UpdateMarkers(dt);
            AnimateZone();
        }

        // ------------------------------------------------------------------
        // Aura zone
        // ------------------------------------------------------------------

        private void UpdateZone()
        {
            if (grid == null) grid = FindAnyObjectByType<GridManager>();

            junkernautCells.Clear();
            IReadOnlyList<JunkernautEnemy> active = JunkernautEnemy.ActiveJunkernauts;
            if (grid != null)
            {
                for (int i = 0; i < active.Count; i++)
                {
                    JunkernautEnemy junkernaut = active[i];
                    if (junkernaut == null || junkernaut.IsDead || junkernaut.IsInsideHole) continue;
                    junkernautCells.Add(grid.WorldToGridPosition(junkernaut.transform.position));
                }
            }

            if (SameCells(junkernautCells, lastJunkernautCells)) return;
            lastJunkernautCells.Clear();
            lastJunkernautCells.AddRange(junkernautCells);
            RebuildZone();
        }

        private static bool SameCells(List<Vector2Int> a, List<Vector2Int> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private void RebuildZone()
        {
            zoneCells.Clear();
            for (int i = 0; i < junkernautCells.Count; i++)
            {
                Vector2Int center = junkernautCells[i];
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        var cell = new Vector2Int(center.x + dx, center.y + dy);
                        if (grid != null && (cell.x < 0 || cell.y < 0 || cell.x >= grid.Width || cell.y >= grid.Height))
                            continue;
                        zoneCells.TryGetValue(cell, out int count);
                        zoneCells[cell] = count + 1;
                    }
                }
            }

            activeFills = 0;
            activeEdges = 0;
            float size = grid != null ? grid.CellSize : 1f;
            float thickness = 2f * ArtPixel;

            foreach (KeyValuePair<Vector2Int, int> pair in zoneCells)
            {
                Vector3 center = grid.GridToWorldPosition(pair.Key);

                SpriteRenderer fill = TakeRenderer(fills, ref activeFills, "Fill", ZoneFillOrder);
                fill.transform.position = new Vector3(center.x, center.y, 0f);
                fill.transform.localScale = new Vector3(size, size, 1f);
                SetFillCount(activeFills - 1, pair.Value);

                for (int d = 0; d < Directions.Length; d++)
                {
                    Vector2Int direction = Directions[d];
                    if (zoneCells.ContainsKey(pair.Key + direction)) continue;

                    SpriteRenderer edge = TakeRenderer(edges, ref activeEdges, "Edge", ZoneEdgeOrder);
                    float inset = size * 0.5f - ArtPixel * 2f;
                    edge.transform.position = new Vector3(center.x + direction.x * inset, center.y + direction.y * inset, 0f);
                    edge.transform.localScale = direction.x != 0
                        ? new Vector3(thickness, size, 1f)
                        : new Vector3(size, thickness, 1f);
                }
            }

            for (int i = activeFills; i < fills.Count; i++) fills[i].enabled = false;
            for (int i = activeEdges; i < edges.Count; i++) edges[i].enabled = false;
        }

        private void SetFillCount(int index, int count)
        {
            while (fillCounts.Count <= index) fillCounts.Add(0);
            fillCounts[index] = count;
        }

        private SpriteRenderer TakeRenderer(List<SpriteRenderer> pool, ref int used, string objectName, int order)
        {
            SpriteRenderer renderer;
            if (used < pool.Count)
            {
                renderer = pool[used];
            }
            else
            {
                var go = new GameObject(objectName);
                go.transform.SetParent(zoneRoot, false);
                renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = texelSprite;
                renderer.sortingLayerID = GridSorting.GroundLayerId;
                renderer.sortingOrder = order;
                pool.Add(renderer);
            }
            renderer.enabled = true;
            used++;
            return renderer;
        }

        private void AnimateZone()
        {
            if (activeFills == 0) return;

            float pulse = 0.5f + 0.5f * Mathf.Sin(pulseTime * 2.6f);
            for (int i = 0; i < activeFills; i++)
            {
                // Deeper tint where auras stack: 1 Junkernaut 0.10, +0.06 per extra one.
                float alpha = Mathf.Min(0.1f + 0.06f * (fillCounts[i] - 1), 0.34f) * (0.8f + 0.2f * pulse);
                fills[i].color = new Color(AuraColor.r, AuraColor.g, AuraColor.b, alpha);
            }

            Color edgeColor = new Color(AuraColor.r, AuraColor.g, AuraColor.b, 0.45f + 0.35f * pulse);
            for (int i = 0; i < activeEdges; i++) edges[i].color = edgeColor;
        }

        // ------------------------------------------------------------------
        // Protected enemies
        // ------------------------------------------------------------------

        private void ScanProtectedEnemies()
        {
            foreach (Marker marker in markers.Values) marker.Seen = false;

            EnemyManager manager = EnemyManager.Instance;
            if (manager != null && manager.ActiveEnemies != null && JunkernautEnemy.ActiveJunkernauts.Count > 0)
            {
                IReadOnlyList<EnemyBase> enemies = manager.ActiveEnemies;
                for (int i = 0; i < enemies.Count; i++)
                {
                    EnemyBase enemy = enemies[i];
                    if (enemy == null) continue;

                    int count = JunkernautEnemy.GetProtectingJunkernautCount(enemy);
                    if (count <= 0) continue;

                    if (!markers.TryGetValue(enemy, out Marker marker))
                    {
                        marker = TakeMarker(enemy);
                        markers.Add(enemy, marker);
                    }
                    marker.Seen = true;
                    if (marker.Count != count) SetPips(marker, count);
                }
            }

            staleMarkers.Clear();
            foreach (KeyValuePair<EnemyBase, Marker> pair in markers)
            {
                if (!pair.Value.Seen) staleMarkers.Add(pair.Key);
            }
            for (int i = 0; i < staleMarkers.Count; i++)
            {
                Marker marker = markers[staleMarkers[i]];
                markers.Remove(staleMarkers[i]);
                ReleaseMarker(marker);
            }
        }

        private void UpdateMarkers(float dt)
        {
            if (markers.Count == 0) return;

            staleMarkers.Clear();
            foreach (KeyValuePair<EnemyBase, Marker> pair in markers)
            {
                Marker marker = pair.Value;
                EnemyBase enemy = marker.Enemy;
                if (enemy == null || enemy.IsDead || enemy.IsInsideHole || !enemy.gameObject.activeInHierarchy)
                {
                    staleMarkers.Add(pair.Key);
                    continue;
                }

                // Beside the left end of the health bar (WorldHealthBar: 0.8 wide, sprite top + 0.15).
                Vector3 visual = enemy.VisualPosition;
                float top = marker.Body != null ? marker.Body.bounds.extents.y : 0.4f;
                Vector3 position = new Vector3(visual.x - 0.4f - 6f * ArtPixel, visual.y + top + 0.15f, 0f);
                position.x = Mathf.Round(position.x / ArtPixel) * ArtPixel;
                position.y = Mathf.Round(position.y / ArtPixel) * ArtPixel;
                marker.Root.position = position;

                // Hit flash: a white silhouette over the shield fades out while the marker punches back to size.
                float f = 0f;
                if (marker.Flash > 0f)
                {
                    marker.Flash = Mathf.Max(0f, marker.Flash - dt);
                    f = marker.Flash / FlashTime;
                }
                float scale = 1f + 0.35f * f;
                marker.Root.localScale = new Vector3(scale, scale, 1f);
                bool flashing = f > 0f;
                if (marker.ShieldFlash.enabled != flashing) marker.ShieldFlash.enabled = flashing;
                if (flashing) marker.ShieldFlash.color = new Color(1f, 1f, 1f, f);
            }

            for (int i = 0; i < staleMarkers.Count; i++)
            {
                Marker marker = markers[staleMarkers[i]];
                markers.Remove(staleMarkers[i]);
                ReleaseMarker(marker);
            }
        }

        private Marker TakeMarker(EnemyBase enemy)
        {
            Marker marker = freeMarkers.Count > 0 ? freeMarkers.Pop() : CreateMarker();
            marker.Enemy = enemy;
            marker.Body = FindBodyRenderer(enemy);
            marker.Count = 0;
            marker.Flash = 0f;
            marker.Root.gameObject.SetActive(true);
            return marker;
        }

        private void ReleaseMarker(Marker marker)
        {
            marker.Enemy = null;
            marker.Body = null;
            marker.Root.gameObject.SetActive(false);
            freeMarkers.Push(marker);
        }

        private Marker CreateMarker()
        {
            var root = new GameObject("ProtectedMarker").transform;
            root.SetParent(markerRoot, false);

            var marker = new Marker { Root = root, Pips = new SpriteRenderer[MaxPips] };
            marker.Shield = CreateSprite(root, "Shield", shieldSprite, Vector3.zero, Vector3.one, ShieldOrder);
            marker.ShieldFlash = CreateSprite(root, "ShieldFlash", shieldFlashSprite, Vector3.zero, Vector3.one, ShieldOrder + 1);
            marker.ShieldFlash.enabled = false;

            // Pips sit in a row under the shield: 2x2 px, 1 px apart.
            for (int i = 0; i < MaxPips; i++)
            {
                marker.Pips[i] = CreateSprite(root, "Pip", texelSprite, Vector3.zero,
                    new Vector3(2f * ArtPixel, 2f * ArtPixel, 1f), ShieldOrder);
                marker.Pips[i].color = AuraColor;
            }
            return marker;
        }

        private void SetPips(Marker marker, int count)
        {
            marker.Count = count;
            int shown = Mathf.Min(count, MaxPips);
            float step = 3f * ArtPixel;
            float start = -(shown - 1) * step * 0.5f;
            for (int i = 0; i < MaxPips; i++)
            {
                bool on = i < shown;
                marker.Pips[i].enabled = on;
                if (on) marker.Pips[i].transform.localPosition = new Vector3(start + i * step, -7f * ArtPixel, 0f);
            }
        }

        private static SpriteRenderer FindBodyRenderer(EnemyBase enemy)
        {
            SpriteRenderer[] renderers = enemy.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].enabled && renderers[i].sprite != null) return renderers[i];
            }
            return null;
        }

        private static SpriteRenderer CreateSprite(Transform parent, string objectName, Sprite sprite,
            Vector3 localPosition, Vector3 localScale, int order)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingLayerID = GridSorting.DefaultLayerId;
            renderer.sortingOrder = order;
            return renderer;
        }

        // ------------------------------------------------------------------
        // Generated art (owned by this instance, destroyed with it)
        // ------------------------------------------------------------------

        private void BuildSprites()
        {
            Texture2D white = Texture2D.whiteTexture;
            texelSprite = Sprite.Create(white, new Rect(0f, 0f, white.width, white.height),
                new Vector2(0.5f, 0.5f), white.width, 0, SpriteMeshType.FullRect);
            texelSprite.name = "JunkernautAuraTexel";

            // 9x10 heater shield: dark outline, amber left half with a highlight, darker right half and a rivet.
            string[] rows =
            {
                ".OOOOOOO.",
                "OHAAODDDO",
                "OHAAODDDO",
                "OAAARDDDO",
                "OAAAODDDO",
                "OAAAODDDO",
                ".OAAODDO.",
                "..OAODO..",
                "...OOO...",
                "....O....",
            };
            Color outline = new Color(0.12f, 0.07f, 0.03f, 1f);
            Color amber = AuraColor;
            Color dark = new Color(0.78f, 0.47f, 0.12f, 1f);
            Color highlight = new Color(1f, 0.93f, 0.62f, 1f);
            Color rivet = new Color(1f, 0.97f, 0.85f, 1f);

            int width = rows[0].Length;
            int height = rows.Length;
            shieldTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "JunkernautShield",
            };
            var pixels = new Color[width * height];
            var flashPixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                string row = rows[height - 1 - y];
                for (int x = 0; x < width; x++)
                {
                    Color c;
                    switch (row[x])
                    {
                        case 'O': c = outline; break;
                        case 'A': c = amber; break;
                        case 'D': c = dark; break;
                        case 'H': c = highlight; break;
                        case 'R': c = rivet; break;
                        default: c = Color.clear; break;
                    }
                    pixels[y * width + x] = c;
                    flashPixels[y * width + x] = c.a > 0f ? Color.white : Color.clear;
                }
            }
            shieldTexture.SetPixels(pixels);
            shieldTexture.Apply(false, false);
            shieldSprite = Sprite.Create(shieldTexture, new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f), 32f, 0, SpriteMeshType.FullRect);
            shieldSprite.name = "JunkernautShield";

            shieldFlashTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "JunkernautShieldFlash",
            };
            shieldFlashTexture.SetPixels(flashPixels);
            shieldFlashTexture.Apply(false, false);
            shieldFlashSprite = Sprite.Create(shieldFlashTexture, new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f), 32f, 0, SpriteMeshType.FullRect);
            shieldFlashSprite.name = "JunkernautShieldFlash";
        }
    }
}
