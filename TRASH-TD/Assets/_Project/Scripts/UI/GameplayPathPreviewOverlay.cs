using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;

namespace TrashTD.UI
{
    /// <summary>
    /// In-game enemy route preview, toggled by the HUD's PREVIEW PATH button. Each route is a trail of faint
    /// pixel-art chevrons (&gt; &gt; &gt;, the shape of the Stage 4 conveyor arrows) marching from spawn to exit,
    /// in colours sampled from the stage's own art (StageThemes). Spawn and exit tiles get breathing corner brackets.
    ///
    /// Chevrons belong to grid steps (cell → next cell), not to routes: every step carries one per Spacing, all
    /// marching in lockstep, so arrows flow through corners and merges, and a corridor several routes share gets a
    /// single trail whose chevrons alternate between those routes' colours. Steps walked both ways are pushed apart
    /// sideways. A route that jumps (a Stage 5 hole) has no chevrons across the gap: its trail fades into the jump
    /// and back in after it. Drawn on the stage's pixel grid (visualTilePixelSize per tile) and never rotated, so the
    /// pixels stay crisp. Sits above the map art and below units, lit like the map. Unscaled time.
    /// </summary>
    public sealed class GameplayPathPreviewOverlay : MonoBehaviour
    {
        private const int SortingOrder = 3;        // map art is 1–2, units 4+
        private const float ChevronAlpha = 0.5f;
        private const float BracketAlpha = 0.6f;
        private const float ShadowAlpha = 0.55f;   // 1 px drop shadow under every pixel, so trails read on light ground
        private const float Spacing = 0.5f;        // tiles between chevrons (16 art px, like the Stage 4 belts)
        private const float FlowSpeed = 0.75f;     // tiles / s
        private const float RevealSpeed = 16f;     // tiles / s the trails draw in from the spawns when shown
        private const float HideDuration = 0.2f;
        private const float BracketBeat = 0.45f;   // s per bracket frame
        private const int LaneShiftPx = 6;         // art px sideways for steps walked in both directions
        private const int DefaultTilePixels = 32;

        private static readonly Color ExitColor = Hex(0xF43636); // danger red, from the Stage 2 art

        /// <summary>Route colours per stage, sampled from each map's art; the first one is the main route.</summary>
        private static readonly Dictionary<string, Color[]> StageThemes = new Dictionary<string, Color[]>
        {
            // Landfill Outskirts: hazard-sign yellow, puddle blue, sand cream, rust
            ["STAGE_01"] = new[] { Hex(0xFFC824), Hex(0x9BD7E6), Hex(0xF0D495), Hex(0xAD5A14) },
            // Scrapyard Junction: hazard yellow, rust orange, cream, glass blue
            ["STAGE_02"] = new[] { Hex(0xFFC824), Hex(0xFC6D28), Hex(0xECCB7E), Hex(0x9BD7E6) },
            // Railyard Crossing: toxic-slime green, hazard yellow, rail rust
            ["STAGE_03"] = new[] { Hex(0x7DFF00), Hex(0xFFC824), Hex(0x945747) },
            // Industrial Factory: molten orange, signal green, coolant blue, amber
            ["STAGE_04"] = new[] { Hex(0xFF8F00), Hex(0x5AD26E), Hex(0x8FC8D8), Hex(0xFFBB38) },
            // Boulevard: road-paint yellow, crosswalk white, window blue, rust, sign blue, amber
            ["STAGE_05"] = new[] { Hex(0xFFC824), Hex(0xFFFFFF), Hex(0x9BD7E6), Hex(0xB6672A), Hex(0x4189AC), Hex(0xE4B340) },
            // Meltdown: acid green, hazard yellow, barrel orange, glass blue
            ["STAGE_06"] = new[] { Hex(0xBFDA40), Hex(0xFFC824), Hex(0xD27633), Hex(0x9BD7E6) },
        };

        // Right-pointing chevron, 2 px stroke, 5×10 (x along the arrow, row from the top), as on the Stage 4 belts
        private static readonly Vector2Int[] ChevronShape =
        {
            new Vector2Int(0, 0),
            new Vector2Int(0, 1), new Vector2Int(1, 1),
            new Vector2Int(1, 2), new Vector2Int(2, 2),
            new Vector2Int(2, 3), new Vector2Int(3, 3),
            new Vector2Int(3, 4), new Vector2Int(4, 4),
            new Vector2Int(3, 5), new Vector2Int(4, 5),
            new Vector2Int(2, 6), new Vector2Int(3, 6),
            new Vector2Int(1, 7), new Vector2Int(2, 7),
            new Vector2Int(0, 8), new Vector2Int(1, 8),
            new Vector2Int(0, 9),
        };

        private sealed class Step
        {
            public Vector2Int fromCell;
            public Vector2Int toCell;
            public Vector3 from;           // world centre of the start cell
            public Vector2 direction;      // world, normalised
            public float length;           // tiles
            public Vector3 laneShift;      // world
            public int ordinal;            // position along the direction, so colours run on through straight corridors
            public readonly List<Color> colors = new List<Color>(); // the routes using this step, in route order
            public float revealAt;         // tiles from the nearest spawn to the step's start
            public bool fadeIn;            // nothing leads into it (a spawn, or just after a jump)
            public bool fadeOut;           // nothing leads on from it (the exit, or into a jump)
            public SpriteRenderer[] chevrons;
        }

        private sealed class Bracket
        {
            public SpriteRenderer renderer;
            public Color color;
            public float revealAt;
            public float phase;
        }

        private GridManager gridManager;
        private StageData stageData;

        private GameObject visualRoot;
        private readonly List<Step> steps = new List<Step>();
        private readonly List<Bracket> brackets = new List<Bracket>();
        private readonly List<Sprite> createdSprites = new List<Sprite>();
        private readonly Sprite[] chevronSprites = new Sprite[4]; // right, left, up, down
        private readonly Sprite[] bracketSprites = new Sprite[2]; // wide, tight
        private float pixelsPerUnit = DefaultTilePixels;

        private static readonly Dictionary<int, Texture2D> textureCache = new Dictionary<int, Texture2D>();

        private bool isVisible;
        private bool hiding;
        private float hideTime;
        private float animTime;

        public bool IsVisible => isVisible;

        public static GameplayPathPreviewOverlay Create(Transform parent, Canvas canvas, GridManager grid, StageData stage)
        {
            var go = new GameObject("GameplayPathPreviewOverlay");
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var overlay = go.AddComponent<GameplayPathPreviewOverlay>();
            overlay.Initialize(grid, stage);
            go.SetActive(false);
            return overlay;
        }

        public void Initialize(GridManager grid, StageData stage)
        {
            gridManager = grid;
            stageData = stage;

            if (visualRoot == null)
            {
                visualRoot = new GameObject("VisualRoot");
                visualRoot.transform.SetParent(transform, false);
            }
        }

        public void SetStage(StageData stage, GridManager grid)
        {
            stageData = stage;
            if (grid != null) gridManager = grid;
            if (isVisible)
            {
                Rebuild();
            }
        }

        public void Toggle()
        {
            SetVisible(!isVisible);
        }

        public void SetVisible(bool visible)
        {
            if (isVisible == visible) return;
            isVisible = visible;

            if (isVisible)
            {
                hiding = false;
                gameObject.SetActive(true);
                animTime = 0f;
                Rebuild();
            }
            else
            {
                // Fade out, then clear (Update)
                hiding = true;
                hideTime = 0f;
            }
        }

        private void ClearVisuals()
        {
            steps.Clear();
            brackets.Clear();

            foreach (Sprite sprite in createdSprites)
            {
                if (sprite != null) Destroy(sprite);
            }
            createdSprites.Clear();

            if (visualRoot != null)
            {
                for (int i = visualRoot.transform.childCount - 1; i >= 0; i--)
                {
                    Destroy(visualRoot.transform.GetChild(i).gameObject);
                }
            }
        }

        private void OnDestroy()
        {
            ClearVisuals();
        }

        // ─────────────────────────────────────────────────────
        //  Build
        // ─────────────────────────────────────────────────────

        public void Rebuild()
        {
            ClearVisuals();

            if (gridManager == null) gridManager = FindFirstObjectByType<GridManager>();

            var bootstrapper = FindFirstObjectByType<TrashTD.Core.GameLoop.StageBootstrapper>();
            if (stageData == null)
            {
                if (bootstrapper != null && bootstrapper.stageData != null) stageData = bootstrapper.stageData;
                if (stageData == null && TrashTD.Core.GameLoop.GameManager.Instance != null)
                {
                    stageData = TrashTD.Core.GameLoop.GameManager.Instance.CurrentStage;
                }
            }

            if (stageData == null || gridManager == null)
            {
                Debug.LogWarning($"[GameplayPathPreview] Missing dependency: stageData={stageData != null}, gridManager={gridManager != null}");
                return;
            }

            if (visualRoot == null)
            {
                visualRoot = new GameObject("VisualRoot");
                visualRoot.transform.SetParent(transform, false);
            }

            List<(int spawn, List<Vector2Int> cells)> cellRoutes = StageMapPreview.ComputeRoutes(stageData);
            if (cellRoutes.Count == 0)
            {
                Debug.LogWarning("[GameplayPathPreview] No enemy routes found for stage: " + stageData.stageId);
                return;
            }

            // The sandbox shows whichever map is selected, so theme by the environment, not stageData.stageId
            string themeId = bootstrapper != null && !string.IsNullOrEmpty(bootstrapper.EnvironmentStageId)
                ? bootstrapper.EnvironmentStageId
                : stageData.stageId;
            StageThemes.TryGetValue(themeId ?? string.Empty, out Color[] theme);

            float cellSize = gridManager.CellSize;
            int tilePixels = stageData.visualTilePixelSize > 0 ? stageData.visualTilePixelSize : DefaultTilePixels;
            pixelsPerUnit = tilePixels / cellSize;
            CreateSprites(tilePixels);

            float longestRoute = BuildSteps(cellRoutes, theme);
            BuildBrackets(cellRoutes, theme, longestRoute);

            // Apply the first frame now, so nothing flashes at full strength
            Update();
        }

        /// <summary>Turns the routes into shared grid steps with chevrons. Returns the longest route in tiles.</summary>
        private float BuildSteps(List<(int spawn, List<Vector2Int> cells)> cellRoutes, Color[] theme)
        {
            var byKey = new Dictionary<(Vector2Int, Vector2Int), Step>();
            var entered = new HashSet<Vector2Int>();
            var left = new HashSet<Vector2Int>();
            float longest = 0f;

            for (int r = 0; r < cellRoutes.Count; r++)
            {
                List<Vector2Int> cells = cellRoutes[r].cells;
                if (cells == null) continue;

                Color color = RouteColor(theme, r);
                float distance = 0f;
                for (int i = 1; i < cells.Count; i++)
                {
                    Vector2Int a = cells[i - 1];
                    Vector2Int b = cells[i];
                    Vector2Int d = b - a;
                    if (d == Vector2Int.zero) continue;
                    if (Mathf.Abs(d.x) > 1 || Mathf.Abs(d.y) > 1) continue; // a jump (hole): no chevrons across it

                    if (!byKey.TryGetValue((a, b), out Step step))
                    {
                        Vector3 from = gridManager.GridToWorldPosition(a);
                        Vector3 to = gridManager.GridToWorldPosition(b);
                        step = new Step
                        {
                            fromCell = a,
                            toCell = b,
                            from = new Vector3(from.x, from.y, -0.05f),
                            direction = ((Vector2)(to - from)).normalized,
                            length = ((Vector2)d).magnitude,
                            ordinal = a.x * d.x + a.y * d.y,
                            revealAt = distance
                        };
                        byKey.Add((a, b), step);
                        steps.Add(step);
                    }

                    step.revealAt = Mathf.Min(step.revealAt, distance);
                    if (!step.colors.Contains(color)) step.colors.Add(color);
                    left.Add(a);
                    entered.Add(b);
                    distance += ((Vector2)d).magnitude;
                }

                longest = Mathf.Max(longest, distance);
            }

            foreach (Step step in steps)
            {
                step.fadeIn = !entered.Contains(step.fromCell);
                step.fadeOut = !left.Contains(step.toCell);

                // Walked both ways: each direction keeps to its own right, like traffic
                if (byKey.ContainsKey((step.toCell, step.fromCell)))
                {
                    Vector2 right = new Vector2(step.direction.y, -step.direction.x);
                    step.laneShift = (Vector3)Snap(right * (LaneShiftPx / pixelsPerUnit));
                }

                Sprite sprite = chevronSprites[DirectionIndex(step.direction)];
                int count = Mathf.CeilToInt(step.length / Spacing - 0.001f);
                step.chevrons = new SpriteRenderer[count];
                for (int m = 0; m < count; m++)
                {
                    step.chevrons[m] = CreateRenderer("Chevron", sprite, step.from);
                }
            }

            return longest;
        }

        private void BuildBrackets(List<(int spawn, List<Vector2Int> cells)> cellRoutes, Color[] theme, float longestRoute)
        {
            if (stageData.spawnPoints != null)
            {
                var spawnColors = new Dictionary<int, Color>();
                for (int r = 0; r < cellRoutes.Count; r++)
                {
                    if (!spawnColors.ContainsKey(cellRoutes[r].spawn)) spawnColors[cellRoutes[r].spawn] = RouteColor(theme, r);
                }

                for (int s = 0; s < stageData.spawnPoints.Length; s++)
                {
                    Color color = spawnColors.TryGetValue(s, out Color routeColor) ? routeColor : ExitColor;
                    AddBracket(stageData.spawnPoints[s], color, 0f, s * 0.37f);
                }
            }

            // Exits light up when the trails reach them
            if (stageData.exitPoints != null)
            {
                for (int e = 0; e < stageData.exitPoints.Length; e++)
                {
                    AddBracket(stageData.exitPoints[e], ExitColor, longestRoute, 0.5f + e * 0.37f);
                }
            }
        }

        private void AddBracket(Vector2Int cell, Color color, float revealAt, float phase)
        {
            Vector3 world = gridManager.GridToWorldPosition(cell);
            brackets.Add(new Bracket
            {
                renderer = CreateRenderer("Bracket", bracketSprites[0], new Vector3(world.x, world.y, -0.05f)),
                color = color,
                revealAt = revealAt,
                phase = phase
            });
        }

        private SpriteRenderer CreateRenderer(string name, Sprite sprite, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(visualRoot.transform, false);
            go.transform.position = position;
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = SortingOrder;
            renderer.color = Color.clear;
            return renderer;
        }

        private static Color RouteColor(Color[] theme, int routeIndex)
        {
            return theme != null && theme.Length > 0
                ? theme[routeIndex % theme.Length]
                : StageMapPreview.GetRouteColor(routeIndex);
        }

        // ─────────────────────────────────────────────────────
        //  Animation
        // ─────────────────────────────────────────────────────

        private void Update()
        {
            if (!isVisible && !hiding) return;

            float dt = Time.unscaledDeltaTime;
            animTime += dt;

            float visibility = 1f;
            if (hiding)
            {
                hideTime += dt;
                visibility = 1f - hideTime / HideDuration;
                if (visibility <= 0f)
                {
                    hiding = false;
                    ClearVisuals();
                    gameObject.SetActive(false);
                    return;
                }
            }

            float reveal = animTime * RevealSpeed;
            float flow = animTime * FlowSpeed;
            float offset = Mathf.Repeat(flow, Spacing);
            int passed = Mathf.FloorToInt(flow / Spacing); // chevrons that have moved on by one slot
            int perTile = Mathf.Max(1, Mathf.RoundToInt(1f / Spacing));
            float cellSize = gridManager != null ? gridManager.CellSize : 1f;

            foreach (Step step in steps)
            {
                int colorCount = step.colors.Count;
                for (int m = 0; m < step.chevrons.Length; m++)
                {
                    SpriteRenderer chevron = step.chevrons[m];
                    float u = offset + m * Spacing; // tiles from the step's start
                    if (u >= step.length)
                    {
                        chevron.enabled = false;
                        continue;
                    }

                    float alpha = ChevronAlpha * visibility * Mathf.Clamp01((reveal - step.revealAt - u) / Spacing);
                    if (step.fadeIn) alpha *= Mathf.Clamp01(u / Spacing);
                    if (step.fadeOut) alpha *= Mathf.Clamp01((step.length - u) / Spacing);
                    chevron.enabled = alpha > 0.002f;
                    if (!chevron.enabled) continue;

                    // Same id as the chevron moves along (and into the next step of a straight corridor), so its
                    // colour sticks to it; shared steps alternate the routes' colours.
                    int id = step.ordinal * perTile + m - passed;
                    Color color = colorCount > 0 ? step.colors[((id % colorCount) + colorCount) % colorCount] : Color.white;
                    color.a = alpha;
                    chevron.color = color;
                    chevron.transform.position = step.from + step.laneShift + (Vector3)Snap(step.direction * (u * cellSize));
                }
            }

            foreach (Bracket bracket in brackets)
            {
                float alpha = BracketAlpha * visibility * Mathf.Clamp01(reveal - bracket.revealAt);
                bracket.renderer.enabled = alpha > 0.002f;
                if (!bracket.renderer.enabled) continue;

                // Two-frame breathing: the corners step in and back out
                int frame = Mathf.FloorToInt(animTime / BracketBeat + bracket.phase) & 1;
                bracket.renderer.sprite = bracketSprites[frame];
                Color color = bracket.color;
                color.a = alpha;
                bracket.renderer.color = color;
            }
        }

        /// <summary>Rounds a world offset to whole art pixels.</summary>
        private Vector2 Snap(Vector2 offset)
        {
            return new Vector2(Mathf.Round(offset.x * pixelsPerUnit), Mathf.Round(offset.y * pixelsPerUnit)) / pixelsPerUnit;
        }

        private static int DirectionIndex(Vector2 direction)
        {
            if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)) return direction.x >= 0f ? 0 : 1;
            return direction.y >= 0f ? 2 : 3;
        }

        private static Color Hex(int rgb)
        {
            return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        }

        // ─────────────────────────────────────────────────────
        //  Pixel art
        // ─────────────────────────────────────────────────────

        private void CreateSprites(int tilePixels)
        {
            for (int d = 0; d < 4; d++)
            {
                chevronSprites[d] = CreateSprite(GetChevronTexture(d));
            }

            bracketSprites[0] = CreateSprite(GetBracketTexture(tilePixels, 2));
            bracketSprites[1] = CreateSprite(GetBracketTexture(tilePixels, 4));
        }

        private Sprite CreateSprite(Texture2D texture)
        {
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f),
                pixelsPerUnit, 0, SpriteMeshType.FullRect);
            createdSprites.Add(sprite);
            return sprite;
        }

        /// <summary>Chevron facing right (0), left (1), up (2) or down (3), white with a drop shadow; tinted per route.</summary>
        private static Texture2D GetChevronTexture(int direction)
        {
            int key = 100 + direction;
            if (textureCache.TryGetValue(key, out Texture2D cached) && cached != null) return cached;

            bool horizontal = direction < 2;
            int width = horizontal ? 6 : 12;
            int height = horizontal ? 12 : 8;
            var body = new HashSet<Vector2Int>();
            foreach (Vector2Int p in ChevronShape)
            {
                // p.x runs along the arrow (0 = tail side, 4 = tip), p.y across it from the top
                int across = 10 - p.y; // 1..10, bottom-up
                switch (direction)
                {
                    case 0: body.Add(new Vector2Int(p.x, across)); break;          // right
                    case 1: body.Add(new Vector2Int(5 - p.x, across)); break;      // left
                    case 2: body.Add(new Vector2Int(across, 2 + p.x)); break;      // up
                    default: body.Add(new Vector2Int(across, 6 - p.x)); break;     // down
                }
            }

            Texture2D texture = PaintWithShadow(width, height, body);
            textureCache[key] = texture;
            return texture;
        }

        /// <summary>Tile-sized frame of four L corners (arms 7 px, 2 px thick), <paramref name="inset"/> px in from the edges.</summary>
        private static Texture2D GetBracketTexture(int size, int inset)
        {
            int key = -(size * 16 + inset); // chevrons use small positive keys
            if (textureCache.TryGetValue(key, out Texture2D cached) && cached != null) return cached;

            const int arm = 7;
            const int thickness = 2;
            var body = new HashSet<Vector2Int>();
            int lo = inset;
            int hi = size - 1 - inset;
            foreach (int cx in new[] { lo, hi })
            {
                foreach (int cy in new[] { lo, hi })
                {
                    int sx = cx == lo ? 1 : -1;
                    int sy = cy == lo ? 1 : -1;
                    for (int i = 0; i < arm; i++)
                    {
                        for (int t = 0; t < thickness; t++)
                        {
                            body.Add(new Vector2Int(cx + sx * i, cy + sy * t));
                            body.Add(new Vector2Int(cx + sx * t, cy + sy * i));
                        }
                    }
                }
            }

            Texture2D texture = PaintWithShadow(size, size, body);
            textureCache[key] = texture;
            return texture;
        }

        /// <summary>White pixels for <paramref name="body"/>, with a dark 1 px shadow under any that has open space below.</summary>
        private static Texture2D PaintWithShadow(int width, int height, HashSet<Vector2Int> body)
        {
            var pixels = new Color32[width * height];
            var shadow = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(ShadowAlpha * 255f));
            foreach (Vector2Int p in body)
            {
                var below = new Vector2Int(p.x, p.y - 1);
                if (below.y >= 0 && !body.Contains(below) && below.x >= 0 && below.x < width) pixels[below.y * width + below.x] = shadow;
            }
            foreach (Vector2Int p in body)
            {
                if (p.x >= 0 && p.x < width && p.y >= 0 && p.y < height) pixels[p.y * width + p.x] = new Color32(255, 255, 255, 255);
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "PathPreviewPixelArt",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
