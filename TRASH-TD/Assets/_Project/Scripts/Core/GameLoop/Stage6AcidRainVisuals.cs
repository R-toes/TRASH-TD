using System;
using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;
using Random = UnityEngine.Random;

namespace TrashTD.Core.GameLoop
{
    /// <summary>
    /// Stage 6 (The Pale) acid storm. The rain damage stays in StageBootstrapper (ApplyStage6AcidRainDamage),
    /// which calls PlayAcidTick on every damage tick. Everything here is visual except the downpour puddles.
    /// - Acid rain: slanted streaks with a bright head and a fading tail, blown left to right. The storm swings
    ///   between a drizzle and a downpour. Drops splash on the ground, throw up droplets and sometimes hiss off fumes.
    /// - Toxic fog: pixel-art clouds (dithered edges, never scaled, snapped to the art grid, like Stage 3's dust) in
    ///   two layers, thin over the enemy lane and thick off the grid, plus a green cast over the map.
    /// - Acid pools (Trap tiles): animated pixel-art acid that joins up across neighbouring pool tiles, with bubbles.
    /// - Storm flashes: a pixel lightning bolt in the sky with a flash from the top of the map, sometimes striking twice.
    /// - Downpour puddles (gameplay): during a downpour, while the acid is live (a wave, or the sandbox), acid seeps up
    ///   on random tiles. A puddle builds up for 1.6 s, hurts like a Trap tile for 6 s (GridCell.HasAcidPuddle, so
    ///   operators and enemies take TrapTileRules damage; acid-tile-immune enemies don't), then fades out over 1.6 s.
    /// Everything is clipped to the map art and runs on scaled time, so it freezes while paused.
    /// Generated sprites belong to this instance and are destroyed with it (domain reload is off).
    /// </summary>
    public sealed class Stage6AcidRainVisuals : MonoBehaviour
    {
        // Ground layer (beneath every unit), back to front
        private const int TintSortingOrder = 2;       // map artwork is 1
        private const int BoltSortingOrder = 3;
        private const int PoolSortingOrder = 4;
        private const int GroundFxSortingOrder = 5;   // splash dashes, bubbles
        private const int BackFogSortingOrder = 6;
        // Default layer
        private const int FrontFogSortingOrder = 11;  // above ground units and foreground art (10), below air units (12)
        private const int FlashSortingOrder = 23;     // above units, below the rain
        private const int RainSortingOrder = 24;      // above units, below projectiles (40)

        private const int ArtPixelsPerTile = 32;
        private const int MaxRainDrops = 260;
        private const int MaxSpray = 140;
        private const int MaxFumes = 32;
        private const int MaxBubbles = 40;

        private const float DrizzleIntensity = 0.6f;
        private const float DownpourIntensity = 1f;
        private const float IntensityChangePerSecond = 0.15f;
        private const float TickGustSeconds = 0.6f;
        private const float TickGustBoost = 0.45f;    // extra intensity during a damage-tick gust
        private const float SplashDuration = 0.14f;
        private const float FumeChance = 0.16f;
        private const float SprayGravity = 9f;        // tiles / s²
        private const float PoolFrameSeconds = 0.2f;

        // Downpour puddles
        public const float PuddleGrowSeconds = 1.6f;
        public const float PuddleActiveSeconds = 6f;
        public const float PuddleFadeSeconds = 1.6f;
        private const int MaxPuddles = 3;                         // at once, not counting ones fading out
        private const float PuddleDownpourThreshold = 0.85f;      // rain intensity needed for puddles to form
        private const int PuddleVariants = 3;
        private const int PuddleGrowthStages = 5;
        private const int PoolFrames = 4;

        private static readonly Color RainColor = new Color(0.66f, 1f, 0.22f, 0.62f);
        private static readonly Color HeavyRainColor = new Color(0.86f, 1f, 0.5f, 0.85f);
        private static readonly Color SplashColor = new Color(0.58f, 1f, 0.2f, 0.65f);
        private static readonly Color SprayColor = new Color(0.78f, 1f, 0.38f, 0.9f);
        private static readonly Color TintColor = new Color(0.22f, 0.45f, 0.06f, 1f);
        private static readonly Color FlashColor = new Color(0.82f, 1f, 0.66f, 1f);
        private static readonly Color BubbleColor = new Color(0.75f, 1f, 0.35f, 0.95f);

        // Pixel-art fog tones (lit from above) and acid tones
        private static readonly Color FogLight = new Color(0.74f, 0.88f, 0.5f);
        private static readonly Color FogMid = new Color(0.54f, 0.7f, 0.34f);
        private static readonly Color FogShadow = new Color(0.36f, 0.48f, 0.22f);
        private static readonly Color AcidRim = new Color(0.14f, 0.34f, 0.05f, 0.95f);
        private static readonly Color AcidShallow = new Color(0.6f, 0.96f, 0.2f, 0.92f);
        private static readonly Color AcidBody = new Color(0.38f, 0.8f, 0.1f, 0.88f);
        private static readonly Color AcidDeep = new Color(0.25f, 0.6f, 0.07f, 0.9f);
        private static readonly Color AcidGlint = new Color(0.9f, 1f, 0.62f, 1f);

        /// <summary>Fog cloud sizes in art pixels; clouds never scale, so they stay crisp.</summary>
        private static readonly Vector2Int[] FogSizes =
        {
            new Vector2Int(56, 22),
            new Vector2Int(72, 28),
            new Vector2Int(88, 32),
            new Vector2Int(104, 38)
        };

        /// <summary>A fume wisp grows through these sizes instead of scaling.</summary>
        private static readonly Vector2Int[] FumeSizes =
        {
            new Vector2Int(10, 7),
            new Vector2Int(14, 10),
            new Vector2Int(19, 13)
        };

        private enum Storm { Drizzle, Downpour }

        private sealed class FogLayer
        {
            public string Name;
            public int Count;
            public int SortingOrder;
            public bool OnGround;
            public Vector2 AlphaRange;
            public Vector2 SpeedRange;
            public float DensityExponent; // > 1 thins the layer out faster over the lane
        }

        private static readonly FogLayer[] FogLayers =
        {
            new FogLayer
            {
                Name = "Back", Count = 14, SortingOrder = BackFogSortingOrder, OnGround = true,
                AlphaRange = new Vector2(0.45f, 0.65f), SpeedRange = new Vector2(0.12f, 0.24f), DensityExponent = 1f
            },
            new FogLayer
            {
                Name = "Front", Count = 10, SortingOrder = FrontFogSortingOrder, OnGround = false,
                AlphaRange = new Vector2(0.35f, 0.5f), SpeedRange = new Vector2(0.18f, 0.32f), DensityExponent = 2.2f
            }
        };

        private sealed class FogCloud
        {
            public FogLayer Layer;
            public Transform Transform;
            public SpriteRenderer Renderer;
            public float X;
            public float BaseY;
            public float HalfWidth;
            public float BaseAlpha;
            public float Speed;
            public float Phase;
            public float FadeIn;
        }

        private sealed class RainDrop
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public bool Active;
            public bool Heavy;
            public float LandY;
            public float SplashTimer; // > 0 while showing its splash
        }

        /// <summary>A droplet thrown from a splash or a popping bubble, or a rising fume wisp.</summary>
        private sealed class Particle
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public bool Active;
            public Vector2 Velocity;
            public Vector2 Position; // unsnapped; the transform is snapped to the art grid
            public float Age;
            public float Lifetime;
        }

        private sealed class Bubble
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public bool Active;
            public float Age;
            public float Lifetime;
            public int MaxStage;  // largest ring sprite it reaches before popping
            public float Delay;   // waits this long before the next bubble rises
        }

        private sealed class Pool
        {
            public SpriteRenderer Renderer;
            public Sprite[] Frames;
            public float Phase;
        }

        private enum PuddleState { Idle, Growing, Active, Fading }

        private sealed class Puddle
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public PuddleState State;
            public GridCell Cell;
            public int Variant;
            public float StateTime;
            public float FumeTimer;
        }

        private GridManager gridManager;
        private StageData stageData;
        private Func<bool> isAcidActive;
        private float cellSize;
        private float pixelSize;
        private Vector2 artMin;
        private Vector2 artMax;
        private Vector2 gridMin;
        private float[,] fogDensityField; // per cell, with a 1-tile border ring of full density

        private readonly List<FogCloud> fogClouds = new List<FogCloud>();
        private readonly List<RainDrop> rainDrops = new List<RainDrop>();
        private readonly List<Particle> sprays = new List<Particle>();
        private readonly List<Particle> fumes = new List<Particle>();
        private readonly List<Bubble> bubbles = new List<Bubble>();
        private readonly List<Pool> pools = new List<Pool>();
        private readonly List<Vector3> poolCentres = new List<Vector3>();
        private readonly List<Puddle> puddles = new List<Puddle>();
        private readonly List<GridCell> puddleCandidates = new List<GridCell>();
        private readonly List<Vector3> bubbleSites = new List<Vector3>();
        private SpriteRenderer tint;
        private SpriteRenderer skyFlash;
        private SpriteRenderer bolt;

        private Storm storm = Storm.Drizzle;
        private float stormTimeLeft;
        private float rainIntensity;
        private Vector2 rainVelocity;
        private float tickGustTimer;
        private float tickPulse;           // 1 on a damage tick, fades quickly
        private float flashTimer;          // counts down to the next storm flash
        private float flashElapsed = -1f;  // time into the current flash, < 0 when none
        private bool flashStrikesTwice;
        private float puddleTimer;
        private float sprayBudget;         // droplets still allowed this frame

        private Sprite whiteSprite;
        private Sprite streakSprite;
        private Sprite skyFlashSprite;
        private Sprite boltSprite;
        private Sprite[] fogSprites;
        private Sprite[] fumeSprites;
        private Sprite[] ringSprites;
        private Sprite[][] puddleGrowth;   // [variant][stage], small to full
        private Sprite[][] puddleShimmer;  // [variant][frame]
        private readonly List<Sprite> generatedSprites = new List<Sprite>();

        public void Initialize(GridManager grid, StageData stage, Func<bool> acidActive = null)
        {
            gridManager = grid;
            stageData = stage;
            isAcidActive = acidActive;
            if (gridManager == null || stageData == null) return;

            cellSize = gridManager.CellSize;
            pixelSize = cellSize / ArtPixelsPerTile;
            CalculateBounds();
            BuildFogDensityField();
            CreateSprites();

            CreateArtMask();
            CreateTintAndFlash();
            CreateFog();
            CreateAcidPools();
            CreatePuddles();
            CreateRainDrops();
            CreateParticles();

            rainIntensity = DrizzleIntensity;
            stormTimeLeft = Random.Range(18f, 30f);
            flashTimer = Random.Range(8f, 16f);
            puddleTimer = Random.Range(1f, 2.5f);
            UpdateRainVelocity();
            PrefillRain();
        }

        /// <summary>The acid just hurt everyone (StageBootstrapper's damage tick): a gust and a green pulse.</summary>
        public void PlayAcidTick()
        {
            tickGustTimer = TickGustSeconds;
            tickPulse = 1f;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < puddles.Count; i++)
            {
                if (puddles[i].Cell != null) puddles[i].Cell.HasAcidPuddle = false;
            }

            // whiteSprite wraps Texture2D.whiteTexture, which is built in: destroy the sprite only
            if (whiteSprite != null) Destroy(whiteSprite);
            DestroyGeneratedSprite(boltSprite);
            foreach (Sprite sprite in generatedSprites) DestroyGeneratedSprite(sprite);
            generatedSprites.Clear();
        }

        private static void DestroyGeneratedSprite(Sprite sprite)
        {
            if (sprite == null) return;
            if (sprite.texture != null) Destroy(sprite.texture);
            Destroy(sprite);
        }

        private void Update()
        {
            if (gridManager == null) return;

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            UpdateStorm(deltaTime);
            sprayBudget = 12f; // caps how many droplets a heavy frame throws up
            UpdateFog(deltaTime);
            UpdateRain(deltaTime);
            UpdatePuddles(deltaTime);
            UpdatePools(deltaTime);
            UpdateSprays(deltaTime);
            UpdateFumes(deltaTime);
            UpdateTintAndFlash(deltaTime);
        }

        // ---------- Storm cycle ----------

        private void UpdateStorm(float deltaTime)
        {
            stormTimeLeft -= deltaTime;
            if (stormTimeLeft <= 0f)
            {
                storm = storm == Storm.Drizzle ? Storm.Downpour : Storm.Drizzle;
                stormTimeLeft = storm == Storm.Drizzle ? Random.Range(18f, 30f) : Random.Range(12f, 22f);
            }

            float target = storm == Storm.Downpour ? DownpourIntensity : DrizzleIntensity;
            rainIntensity = Mathf.MoveTowards(rainIntensity, target, IntensityChangePerSecond * deltaTime);

            if (tickGustTimer > 0f) tickGustTimer = Mathf.Max(0f, tickGustTimer - deltaTime);
            tickPulse = Mathf.MoveTowards(tickPulse, 0f, deltaTime * 2.2f);
            UpdateRainVelocity();
        }

        /// <summary>Current strength including a damage-tick gust (up to ~1.45).</summary>
        private float EffectiveIntensity =>
            rainIntensity + TickGustBoost * Mathf.Clamp01(tickGustTimer / TickGustSeconds);

        /// <summary>Blown left to right; heavier rain falls faster and slants more.</summary>
        private void UpdateRainVelocity()
        {
            float k = Mathf.Clamp01(EffectiveIntensity);
            rainVelocity = new Vector2(Mathf.Lerp(0.7f, 1.6f, k), Mathf.Lerp(-8.5f, -11.5f, k)) * cellSize;
        }

        // ---------- Bounds and fog density ----------

        private void CalculateBounds()
        {
            gridMin = gridManager.GridToWorldPosition(0, 0) - new Vector3(cellSize * 0.5f, cellSize * 0.5f, 0f);
            Vector2 gridMax = gridMin + new Vector2(stageData.gridWidth, stageData.gridHeight) * cellSize;
            artMin = gridMin;
            artMax = gridMax;

            // The artwork can be larger than the grid
            if (stageData.mapVisualSprite != null)
            {
                float pixelPerTile = stageData.visualTilePixelSize > 0 ? stageData.visualTilePixelSize : 32f;
                Vector2 spriteMin = gridMin + stageData.visualTileOffset * cellSize;
                Vector2 spriteMax = spriteMin + stageData.mapVisualSprite.rect.size / pixelPerTile * cellSize;
                artMin = Vector2.Min(artMin, spriteMin);
                artMax = Vector2.Max(artMax, spriteMax);
            }
        }

        private void BuildFogDensityField()
        {
            int width = stageData.gridWidth;
            int height = stageData.gridHeight;
            fogDensityField = new float[width + 2, height + 2];

            for (int x = 0; x < width + 2; x++)
            {
                for (int y = 0; y < height + 2; y++)
                {
                    bool insideGrid = x >= 1 && x <= width && y >= 1 && y <= height;
                    fogDensityField[x, y] = insideGrid ? GetTileFogDensity(stageData.GetTile(x - 1, y - 1)) : 1f;
                }
            }
        }

        private static float GetTileFogDensity(TileType tile)
        {
            return tile switch
            {
                TileType.HighGround => 0.5f,
                TileType.Blocked => 0.85f,
                TileType.Trap => 0.75f, // fumes gather over the acid pools
                _ => 0.25f              // enemy lane, melee ground, spawns, exits, holes
            };
        }

        /// <summary>0-1 fog thickness at a world position: 1 off the grid, lowest over the enemy lane.</summary>
        private float GetFogDensity(Vector2 worldPosition)
        {
            int width = stageData.gridWidth;
            int height = stageData.gridHeight;

            float fx = Mathf.Clamp((worldPosition.x - gridMin.x) / cellSize - 0.5f, -1f, width) + 1f;
            float fy = Mathf.Clamp((worldPosition.y - gridMin.y) / cellSize - 0.5f, -1f, height) + 1f;

            int x0 = Mathf.Min(Mathf.FloorToInt(fx), width);
            int y0 = Mathf.Min(Mathf.FloorToInt(fy), height);
            float tx = fx - x0;
            float ty = fy - y0;

            float bottom = Mathf.Lerp(fogDensityField[x0, y0], fogDensityField[x0 + 1, y0], tx);
            float top = Mathf.Lerp(fogDensityField[x0, y0 + 1], fogDensityField[x0 + 1, y0 + 1], tx);
            return Mathf.Lerp(bottom, top, ty);
        }

        /// <summary>Snaps to the art pixel grid, so pixel-art sprites never land between pixels.</summary>
        private Vector3 Snap(float x, float y) => new Vector3(
            Mathf.Round(x / pixelSize) * pixelSize,
            Mathf.Round(y / pixelSize) * pixelSize,
            0f);

        // ---------- Scene objects ----------

        /// <summary>Clips everything to the map artwork so nothing draws over the void around it.</summary>
        private void CreateArtMask()
        {
            var maskObject = new GameObject("Stage6StormMask");
            maskObject.transform.SetParent(transform, false);
            maskObject.transform.position = (Vector3)((artMin + artMax) * 0.5f);
            maskObject.transform.localScale = new Vector3(artMax.x - artMin.x, artMax.y - artMin.y, 1f);
            maskObject.AddComponent<SpriteMask>().sprite = whiteSprite;
        }

        private SpriteRenderer CreateRenderer(Transform parent, string objectName, Sprite sprite, bool onGround, int sortingOrder)
        {
            var rendererObject = new GameObject(objectName);
            rendererObject.transform.SetParent(parent, false);
            var renderer = rendererObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            if (onGround) GridSorting.PlaceOnGround(renderer);
            renderer.sortingOrder = sortingOrder;
            renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            return renderer;
        }

        private void CreateTintAndFlash()
        {
            Vector3 centre = (Vector3)((artMin + artMax) * 0.5f);
            Vector3 size = new Vector3(artMax.x - artMin.x, artMax.y - artMin.y, 1f);

            tint = CreateRenderer(transform, "Stage6ToxicCast", whiteSprite, true, TintSortingOrder);
            tint.transform.position = centre;
            tint.transform.localScale = size;
            tint.color = Color.clear;

            // The flash comes from the sky: strongest at the top of the map, gone by the middle
            skyFlash = CreateRenderer(transform, "Stage6StormFlash", skyFlashSprite, false, FlashSortingOrder);
            skyFlash.transform.position = centre;
            skyFlash.transform.localScale = new Vector3(
                size.x / skyFlashSprite.bounds.size.x, size.y / skyFlashSprite.bounds.size.y, 1f);
            skyFlash.color = Color.clear;

            bolt = CreateRenderer(transform, "Stage6LightningBolt", null, true, BoltSortingOrder);
            bolt.transform.localScale = Vector3.one * cellSize;
            bolt.enabled = false;
        }

        private void CreateFog()
        {
            for (int l = 0; l < FogLayers.Length; l++)
            {
                FogLayer layer = FogLayers[l];
                var layerRoot = new GameObject($"Stage6Fog_{layer.Name}");
                layerRoot.transform.SetParent(transform, false);

                for (int i = 0; i < layer.Count; i++)
                {
                    SpriteRenderer renderer = CreateRenderer(layerRoot.transform, $"FogCloud_{i}", null, layer.OnGround, layer.SortingOrder);
                    renderer.transform.localScale = Vector3.one * cellSize; // 32 px per tile: one texture pixel per art pixel

                    var cloud = new FogCloud { Layer = layer, Transform = renderer.transform, Renderer = renderer };
                    ResetFogCloud(cloud, true);
                    fogClouds.Add(cloud);
                }
            }
        }

        private void ResetFogCloud(FogCloud cloud, bool anywhere)
        {
            FogLayer layer = cloud.Layer;
            Sprite sprite = fogSprites[Random.Range(0, fogSprites.Length)];
            cloud.Renderer.sprite = sprite;
            cloud.Renderer.flipX = Random.value < 0.5f;
            cloud.HalfWidth = sprite.bounds.extents.x * cellSize;

            // Drifts right with the storm wind; new clouds roll in from the left edge
            cloud.X = anywhere
                ? Random.Range(artMin.x - cloud.HalfWidth, artMax.x + cloud.HalfWidth)
                : artMin.x - cloud.HalfWidth;
            cloud.BaseY = Random.Range(artMin.y, artMax.y);
            cloud.BaseAlpha = Random.Range(layer.AlphaRange.x, layer.AlphaRange.y);
            cloud.Speed = Random.Range(layer.SpeedRange.x, layer.SpeedRange.y) * cellSize;
            cloud.Phase = Random.Range(0f, Mathf.PI * 2f);
            cloud.FadeIn = anywhere ? 1f : 0f;
        }

        /// <summary>Animated pixel-art acid on each Trap tile. Neighbouring pool tiles join into one pool.</summary>
        private void CreateAcidPools()
        {
            var poolRoot = new GameObject("Stage6AcidPools");
            poolRoot.transform.SetParent(transform, false);

            for (int x = 0; x < stageData.gridWidth; x++)
            {
                for (int y = 0; y < stageData.gridHeight; y++)
                {
                    GridCell cell = gridManager.GetCell(x, y);
                    if (cell == null || cell.TileType != TileType.Trap) continue;

                    bool left = IsTrap(x - 1, y), right = IsTrap(x + 1, y), down = IsTrap(x, y - 1), up = IsTrap(x, y + 1);
                    // Grid y may run the other way to world y: orient the open sides by the neighbours' world positions
                    if (!IsWorldAbove(x, y + 1, cell)) (up, down) = (down, up);

                    // Noise in world pixels with one shared seed, so joined pool tiles line up at their seams
                    Vector2Int noiseOffset = new Vector2Int(
                        Mathf.RoundToInt(cell.WorldPosition.x / cellSize) * ArtPixelsPerTile,
                        Mathf.RoundToInt(cell.WorldPosition.y / cellSize) * ArtPixelsPerTile);
                    var frames = new Sprite[PoolFrames];
                    for (int f = 0; f < PoolFrames; f++)
                    {
                        frames[f] = Track(CreateAcidSprite(left, right, down, up, 3f, 1f, 7001, noiseOffset, f, $"Stage6 Pool {x},{y} #{f}"));
                    }

                    SpriteRenderer renderer = CreateRenderer(poolRoot.transform, $"AcidPool_{x}_{y}", frames[0], true, PoolSortingOrder);
                    renderer.transform.position = Snap(cell.WorldPosition.x, cell.WorldPosition.y);
                    renderer.transform.localScale = Vector3.one * cellSize;
                    pools.Add(new Pool { Renderer = renderer, Frames = frames, Phase = Random.Range(0f, PoolFrames * PoolFrameSeconds) });
                    poolCentres.Add(cell.WorldPosition);
                }
            }

            for (int i = 0; i < MaxBubbles; i++)
            {
                SpriteRenderer renderer = CreateRenderer(poolRoot.transform, $"Bubble_{i}", ringSprites[0], true, GroundFxSortingOrder);
                renderer.transform.localScale = Vector3.one * cellSize;
                renderer.gameObject.SetActive(false);
                // Staggered so the pools don't all bubble at once
                bubbles.Add(new Bubble { Transform = renderer.transform, Renderer = renderer, Delay = Random.Range(0f, 2.5f) });
            }
        }

        private bool IsTrap(int x, int y)
        {
            GridCell cell = gridManager.GetCell(x, y);
            return cell != null && cell.TileType == TileType.Trap;
        }

        private bool IsWorldAbove(int x, int y, GridCell from)
        {
            Vector3 other = gridManager.GridToWorldPosition(x, y);
            return other.y > from.WorldPosition.y;
        }

        /// <summary>Downpour puddles: the candidate tiles and a small pool of renderers.</summary>
        private void CreatePuddles()
        {
            for (int x = 0; x < stageData.gridWidth; x++)
            {
                for (int y = 0; y < stageData.gridHeight; y++)
                {
                    GridCell cell = gridManager.GetCell(x, y);
                    if (cell == null) continue;
                    // Ground and high ground; never pools, spawns, exits, holes or blocked tiles
                    if (cell.TileType == TileType.LowGround || cell.TileType == TileType.EnemyPath || cell.TileType == TileType.HighGround)
                        puddleCandidates.Add(cell);
                }
            }

            var puddleRoot = new GameObject("Stage6AcidPuddles");
            puddleRoot.transform.SetParent(transform, false);
            for (int i = 0; i < MaxPuddles + 2; i++)
            {
                SpriteRenderer renderer = CreateRenderer(puddleRoot.transform, $"AcidPuddle_{i}", null, true, PoolSortingOrder);
                renderer.transform.localScale = Vector3.one * cellSize;
                renderer.gameObject.SetActive(false);
                puddles.Add(new Puddle { Transform = renderer.transform, Renderer = renderer, State = PuddleState.Idle });
            }
        }

        private void CreateRainDrops()
        {
            var rainRoot = new GameObject("Stage6AcidRain");
            rainRoot.transform.SetParent(transform, false);

            for (int i = 0; i < MaxRainDrops; i++)
            {
                SpriteRenderer renderer = CreateRenderer(rainRoot.transform, $"AcidDrop_{i}", streakSprite, false, RainSortingOrder);
                renderer.gameObject.SetActive(false);
                rainDrops.Add(new RainDrop { Transform = renderer.transform, Renderer = renderer });
            }
        }

        private void CreateParticles()
        {
            var sprayRoot = new GameObject("Stage6AcidSpray");
            sprayRoot.transform.SetParent(transform, false);
            for (int i = 0; i < MaxSpray; i++)
            {
                SpriteRenderer renderer = CreateRenderer(sprayRoot.transform, $"Spray_{i}", whiteSprite, false, RainSortingOrder);
                renderer.gameObject.SetActive(false);
                sprays.Add(new Particle { Transform = renderer.transform, Renderer = renderer });
            }

            var fumeRoot = new GameObject("Stage6AcidFumes");
            fumeRoot.transform.SetParent(transform, false);
            for (int i = 0; i < MaxFumes; i++)
            {
                SpriteRenderer renderer = CreateRenderer(fumeRoot.transform, $"Fume_{i}", fumeSprites[0], false, FrontFogSortingOrder);
                renderer.transform.localScale = Vector3.one * cellSize;
                renderer.gameObject.SetActive(false);
                fumes.Add(new Particle { Transform = renderer.transform, Renderer = renderer });
            }
        }

        /// <summary>Scatters drops through the air so the stage doesn't open with an empty sky.</summary>
        private void PrefillRain()
        {
            int wantedDrops = Mathf.RoundToInt(rainIntensity * MaxRainDrops);
            for (int i = 0; i < wantedDrops && i < rainDrops.Count; i++)
            {
                SpawnRainDrop(rainDrops[i], true);
            }
        }

        // ---------- Fog ----------

        private void UpdateFog(float deltaTime)
        {
            float time = Time.time;
            float thickness = 0.85f + 0.25f * rainIntensity;
            for (int i = 0; i < fogClouds.Count; i++)
            {
                FogCloud cloud = fogClouds[i];
                cloud.X += cloud.Speed * deltaTime;
                if (cloud.X > artMax.x + cloud.HalfWidth) ResetFogCloud(cloud, false);

                // A slow roll, out of step with the neighbours
                float y = cloud.BaseY + Mathf.Sin(time * 0.5f + cloud.Phase) * cellSize * 0.06f;
                cloud.Transform.position = Snap(cloud.X, y);

                cloud.FadeIn = Mathf.Min(1f, cloud.FadeIn + deltaTime * 0.5f);
                float density = Mathf.Pow(GetFogDensity(new Vector2(cloud.X, y)), cloud.Layer.DensityExponent);
                float breathe = 0.85f + 0.15f * Mathf.Sin(time * 0.9f + cloud.Phase * 2f);

                Color color = Color.white;
                color.a = cloud.BaseAlpha * density * thickness * breathe * cloud.FadeIn;
                cloud.Renderer.color = color;
            }
        }

        // ---------- Rain ----------

        private void UpdateRain(float deltaTime)
        {
            int wantedDrops = Mathf.Min(MaxRainDrops, Mathf.RoundToInt(EffectiveIntensity / (DownpourIntensity + TickGustBoost) * MaxRainDrops));

            for (int i = 0; i < rainDrops.Count; i++)
            {
                RainDrop drop = rainDrops[i];
                if (!drop.Active)
                {
                    if (i < wantedDrops) SpawnRainDrop(drop, false);
                    continue;
                }

                if (drop.SplashTimer > 0f)
                {
                    drop.SplashTimer -= deltaTime;
                    float k = Mathf.Clamp01(drop.SplashTimer / SplashDuration);
                    Color splash = SplashColor;
                    splash.a *= k;
                    drop.Renderer.color = splash;
                    // The dash spreads as it fades (whole art pixels)
                    drop.Transform.localScale = new Vector3(pixelSize * Mathf.Round(Mathf.Lerp(5f, 3f, k)), pixelSize, 1f);

                    if (drop.SplashTimer <= 0f)
                    {
                        // Drops past the current count stop here, so the rain thins out gradually
                        if (i < wantedDrops) SpawnRainDrop(drop, false);
                        else DeactivateRainDrop(drop);
                    }
                    continue;
                }

                Vector3 position = drop.Transform.position;
                float speed = drop.Heavy ? 1.15f : 1f;
                position.x += rainVelocity.x * speed * deltaTime;
                position.y += rainVelocity.y * speed * deltaTime;
                drop.Transform.position = position;

                if (position.y <= drop.LandY) StartSplash(drop);
            }
        }

        private void SpawnRainDrop(RainDrop drop, bool midAir)
        {
            drop.Active = true;
            drop.SplashTimer = 0f;
            drop.Heavy = Random.value < 0.15f;
            drop.Transform.gameObject.SetActive(true);

            // Drops fall diagonally to the right, so spawn them further left to cover the whole map
            float fallTime = (artMax.y - artMin.y) / Mathf.Max(0.01f, -rainVelocity.y);
            float slant = rainVelocity.x * fallTime;
            float startY = midAir
                ? Random.Range(artMin.y, artMax.y + cellSize)
                : artMax.y + Random.Range(0f, cellSize);
            drop.Transform.position = new Vector3(Random.Range(artMin.x - slant, artMax.x), startY, 0f);
            drop.LandY = Random.Range(artMin.y, Mathf.Min(artMax.y, startY));

            float lengthPixels = Mathf.Round(Mathf.Lerp(4f, 7f, Mathf.Clamp01(EffectiveIntensity))) + (drop.Heavy ? 2f : 0f);
            Vector2 streakSize = streakSprite.bounds.size;
            drop.Transform.localScale = new Vector3(
                pixelSize / streakSize.x * (drop.Heavy ? 2f : 1f),
                pixelSize * lengthPixels / streakSize.y,
                1f);
            drop.Transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(rainVelocity.x, -rainVelocity.y) * Mathf.Rad2Deg);
            drop.Renderer.sprite = streakSprite;
            drop.Renderer.sortingLayerID = GridSorting.DefaultLayerId;
            drop.Renderer.sortingOrder = RainSortingOrder;
            drop.Renderer.color = drop.Heavy ? HeavyRainColor : RainColor;
        }

        /// <summary>The drop becomes a short dash on the ground beneath the units, throws droplets and maybe a fume.</summary>
        private void StartSplash(RainDrop drop)
        {
            drop.SplashTimer = SplashDuration;
            Vector3 position = Snap(drop.Transform.position.x, drop.LandY);
            drop.Transform.position = position;
            drop.Transform.rotation = Quaternion.identity;
            drop.Transform.localScale = new Vector3(pixelSize * 3f, pixelSize, 1f);
            drop.Renderer.sprite = whiteSprite;
            GridSorting.PlaceOnGround(drop.Renderer);
            drop.Renderer.sortingOrder = GroundFxSortingOrder;
            drop.Renderer.color = SplashColor;

            int droplets = drop.Heavy ? 3 : (Random.value < 0.6f ? 2 : 1);
            for (int i = 0; i < droplets; i++) EmitSpray(position, 1f);
            if (Random.value < FumeChance * (drop.Heavy ? 2f : 1f)) EmitFume(position);
        }

        private static void DeactivateRainDrop(RainDrop drop)
        {
            drop.Active = false;
            drop.SplashTimer = 0f;
            drop.Transform.gameObject.SetActive(false);
        }

        // ---------- Droplets and fumes ----------

        private static Particle TakeIdle(List<Particle> pool)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (!pool[i].Active) return pool[i];
            }
            return null;
        }

        /// <summary>A single acid droplet thrown up and out; it arcs and falls back.</summary>
        private void EmitSpray(Vector3 origin, float strength)
        {
            if (sprayBudget < 1f) return;
            Particle particle = TakeIdle(sprays);
            if (particle == null) return;
            sprayBudget -= 1f;

            particle.Active = true;
            particle.Age = 0f;
            particle.Lifetime = Random.Range(0.18f, 0.32f);
            particle.Velocity = new Vector2(Random.Range(-0.9f, 0.9f), Random.Range(0.9f, 1.7f) * strength) * cellSize;
            particle.Position = origin;
            float size = pixelSize * (Random.value < 0.25f ? 2f : 1f);
            particle.Transform.position = origin;
            particle.Transform.localScale = new Vector3(size, size, 1f);
            particle.Renderer.color = SprayColor;
            particle.Transform.gameObject.SetActive(true);
        }

        /// <summary>A wisp of acid fume rising from where a drop landed, a bubble popped or a puddle seethes.</summary>
        private void EmitFume(Vector3 origin)
        {
            Particle particle = TakeIdle(fumes);
            if (particle == null) return;

            particle.Active = true;
            particle.Age = 0f;
            particle.Lifetime = Random.Range(1.0f, 1.6f);
            particle.Velocity = new Vector2(Random.Range(0.05f, 0.25f), Random.Range(0.25f, 0.45f)) * cellSize;
            particle.Position = origin;
            particle.Renderer.flipX = Random.value < 0.5f;
            particle.Renderer.sprite = fumeSprites[0];
            particle.Transform.position = Snap(origin.x, origin.y);
            particle.Transform.gameObject.SetActive(true);
        }

        private void UpdateSprays(float deltaTime)
        {
            for (int i = 0; i < sprays.Count; i++)
            {
                Particle particle = sprays[i];
                if (!particle.Active) continue;

                particle.Age += deltaTime;
                float k = particle.Age / particle.Lifetime;
                if (k >= 1f)
                {
                    particle.Active = false;
                    particle.Transform.gameObject.SetActive(false);
                    continue;
                }

                particle.Velocity.y -= SprayGravity * cellSize * deltaTime;
                particle.Position += particle.Velocity * deltaTime;
                particle.Transform.position = Snap(particle.Position.x, particle.Position.y);

                Color color = SprayColor;
                color.a *= 1f - k * k;
                particle.Renderer.color = color;
            }
        }

        /// <summary>Fumes rise and drift, growing through the fume sprites (no scaling) as they fade in then out.</summary>
        private void UpdateFumes(float deltaTime)
        {
            for (int i = 0; i < fumes.Count; i++)
            {
                Particle particle = fumes[i];
                if (!particle.Active) continue;

                particle.Age += deltaTime;
                float k = particle.Age / particle.Lifetime;
                if (k >= 1f)
                {
                    particle.Active = false;
                    particle.Transform.gameObject.SetActive(false);
                    continue;
                }

                particle.Position += particle.Velocity * deltaTime;
                particle.Transform.position = Snap(particle.Position.x, particle.Position.y);
                particle.Renderer.sprite = fumeSprites[Mathf.Min(fumeSprites.Length - 1, (int)(k * fumeSprites.Length))];
                Color color = Color.white;
                color.a = 0.75f * Mathf.Sin(k * Mathf.PI);
                particle.Renderer.color = color;
            }
        }

        // ---------- Acid pools and bubbles ----------

        private void UpdatePools(float deltaTime)
        {
            float time = Time.time;
            for (int i = 0; i < pools.Count; i++)
            {
                Pool pool = pools[i];
                int frame = (int)((time + pool.Phase) / PoolFrameSeconds) % pool.Frames.Length;
                pool.Renderer.sprite = pool.Frames[frame];
                // Each pool breathes out of step with the others; damage ticks brighten them all
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * 1.6f + i * 1.7f);
                float shade = Mathf.Clamp01(0.84f + 0.1f * pulse + 0.16f * tickPulse);
                pool.Renderer.color = new Color(shade, shade, shade, 1f);
            }

            // Bubbles rise in the fixed pools and in any live puddle
            bubbleSites.Clear();
            bubbleSites.AddRange(poolCentres);
            for (int i = 0; i < puddles.Count; i++)
            {
                if (puddles[i].State == PuddleState.Active) bubbleSites.Add(puddles[i].Cell.WorldPosition);
            }

            for (int i = 0; i < bubbles.Count; i++)
            {
                Bubble bubble = bubbles[i];
                if (!bubble.Active)
                {
                    bubble.Delay -= deltaTime;
                    if (bubble.Delay <= 0f && bubbleSites.Count > 0) StartBubble(bubble);
                    continue;
                }

                bubble.Age += deltaTime;
                float k = bubble.Age / bubble.Lifetime;
                if (k >= 1f)
                {
                    PopBubble(bubble);
                    continue;
                }

                // Swells through the ring sprites (no scaling), then pops
                int stage = Mathf.Min(bubble.MaxStage, (int)(k * (bubble.MaxStage + 1)));
                bubble.Renderer.sprite = ringSprites[stage];
                Color color = BubbleColor;
                color.a *= Mathf.Clamp01(k * 4f);
                bubble.Renderer.color = color;
            }
        }

        private void StartBubble(Bubble bubble)
        {
            Vector3 site = bubbleSites[Random.Range(0, bubbleSites.Count)];
            bubble.Transform.position = Snap(
                site.x + Random.Range(-0.3f, 0.3f) * cellSize,
                site.y + Random.Range(-0.26f, 0.26f) * cellSize);
            bubble.Active = true;
            bubble.Age = 0f;
            bubble.Lifetime = Random.Range(0.6f, 1.3f);
            bubble.MaxStage = Random.Range(1, ringSprites.Length);
            bubble.Renderer.sprite = ringSprites[0];
            bubble.Transform.gameObject.SetActive(true);
        }

        private void PopBubble(Bubble bubble)
        {
            Vector3 position = bubble.Transform.position;
            bubble.Active = false;
            bubble.Transform.gameObject.SetActive(false);
            // Faster bubbling during a downpour and right after a damage tick
            bubble.Delay = Random.Range(0.3f, 1.8f) / (0.6f + rainIntensity + tickPulse);

            EmitSpray(position, 0.6f);
            if (Random.value < 0.35f) EmitFume(position);
        }

        // ---------- Downpour puddles ----------

        private void UpdatePuddles(float deltaTime)
        {
            bool acidLive = isAcidActive == null || isAcidActive();

            if (acidLive && storm == Storm.Downpour && rainIntensity >= PuddleDownpourThreshold)
            {
                puddleTimer -= deltaTime;
                if (puddleTimer <= 0f)
                {
                    puddleTimer = Random.Range(2.5f, 4.5f);
                    TrySpawnPuddle();
                }
            }

            for (int i = 0; i < puddles.Count; i++)
            {
                Puddle puddle = puddles[i];
                if (puddle.State == PuddleState.Idle) continue;

                // The wave ended (or hazards went off): every puddle drains away and stops hurting
                if (!acidLive && puddle.State != PuddleState.Fading) BeginFade(puddle);

                puddle.StateTime += deltaTime;
                switch (puddle.State)
                {
                    case PuddleState.Growing:
                    {
                        float k = Mathf.Clamp01(puddle.StateTime / PuddleGrowSeconds);
                        int stage = Mathf.Min(PuddleGrowthStages - 1, (int)(k * PuddleGrowthStages));
                        puddle.Renderer.sprite = puddleGrowth[puddle.Variant][stage];
                        puddle.Renderer.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.45f, 1f, k));
                        if (k >= 1f)
                        {
                            puddle.State = PuddleState.Active;
                            puddle.StateTime = 0f;
                            puddle.Cell.HasAcidPuddle = true;
                            Vector3 centre = puddle.Cell.WorldPosition;
                            for (int s = 0; s < 4; s++) EmitSpray(centre, 0.8f);
                            EmitFume(centre);
                        }
                        break;
                    }
                    case PuddleState.Active:
                    {
                        Sprite[] frames = puddleShimmer[puddle.Variant];
                        puddle.Renderer.sprite = frames[(int)(puddle.StateTime / PoolFrameSeconds) % frames.Length];
                        float shade = Mathf.Clamp01(0.88f + 0.12f * tickPulse);
                        puddle.Renderer.color = new Color(shade, shade, shade, 1f);

                        // Seethes: a fume now and then
                        puddle.FumeTimer -= deltaTime;
                        if (puddle.FumeTimer <= 0f)
                        {
                            puddle.FumeTimer = Random.Range(0.6f, 1.2f);
                            Vector3 centre = puddle.Cell.WorldPosition;
                            EmitFume(centre + new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(-0.2f, 0.2f), 0f) * cellSize);
                        }

                        if (puddle.StateTime >= PuddleActiveSeconds) BeginFade(puddle);
                        break;
                    }
                    case PuddleState.Fading:
                    {
                        float k = Mathf.Clamp01(puddle.StateTime / PuddleFadeSeconds);
                        // Shrinks back through the growth stages as it soaks away
                        int stage = Mathf.Clamp((int)((1f - k) * PuddleGrowthStages), 0, PuddleGrowthStages - 1);
                        puddle.Renderer.sprite = puddleGrowth[puddle.Variant][stage];
                        puddle.Renderer.color = new Color(1f, 1f, 1f, 1f - k);
                        if (k >= 1f)
                        {
                            puddle.State = PuddleState.Idle;
                            puddle.Cell = null;
                            puddle.Transform.gameObject.SetActive(false);
                        }
                        break;
                    }
                }
            }
        }

        private void TrySpawnPuddle()
        {
            int live = 0;
            for (int i = 0; i < puddles.Count; i++)
            {
                if (puddles[i].State == PuddleState.Growing || puddles[i].State == PuddleState.Active) live++;
            }
            if (live >= MaxPuddles || puddleCandidates.Count == 0) return;

            Puddle free = null;
            for (int i = 0; i < puddles.Count; i++)
            {
                if (puddles[i].State == PuddleState.Idle) { free = puddles[i]; break; }
            }
            if (free == null) return;

            // A few tries for a tile without a puddle already on it
            for (int attempt = 0; attempt < 8; attempt++)
            {
                GridCell cell = puddleCandidates[Random.Range(0, puddleCandidates.Count)];
                if (HasPuddle(cell)) continue;

                free.Cell = cell;
                free.State = PuddleState.Growing;
                free.StateTime = 0f;
                free.FumeTimer = Random.Range(0.2f, 0.6f);
                free.Variant = Random.Range(0, PuddleVariants);
                free.Transform.position = Snap(cell.WorldPosition.x, cell.WorldPosition.y);
                free.Renderer.flipX = Random.value < 0.5f;
                free.Renderer.sprite = puddleGrowth[free.Variant][0];
                free.Transform.gameObject.SetActive(true);
                return;
            }
        }

        private bool HasPuddle(GridCell cell)
        {
            for (int i = 0; i < puddles.Count; i++)
            {
                if (puddles[i].State != PuddleState.Idle && puddles[i].Cell == cell) return true;
            }
            return false;
        }

        private static void BeginFade(Puddle puddle)
        {
            puddle.State = PuddleState.Fading;
            puddle.StateTime = 0f;
            if (puddle.Cell != null) puddle.Cell.HasAcidPuddle = false;
        }

        // ---------- Tint and storm flashes ----------

        private void UpdateTintAndFlash(float deltaTime)
        {
            float strength = UpdateFlash(deltaTime);

            if (tint != null)
            {
                Color cast = TintColor;
                cast.a = 0.10f + 0.04f * rainIntensity + 0.10f * tickPulse + 0.10f * strength;
                tint.color = cast;
            }

            if (skyFlash != null)
            {
                Color light = FlashColor;
                light.a = 0.32f * strength;
                skyFlash.color = light;
            }
        }

        /// <summary>
        /// Runs the current flash: a faint pre-flicker, the strike (bolt and sky flash, flickering), and a fading
        /// afterglow; sometimes a second strike follows. Returns the sky flash strength 0-1.
        /// </summary>
        private float UpdateFlash(float deltaTime)
        {
            if (flashElapsed < 0f)
            {
                flashTimer -= deltaTime;
                if (flashTimer > 0f) return 0f;

                // Downpours flash more often
                flashTimer = storm == Storm.Downpour ? Random.Range(9f, 18f) : Random.Range(14f, 28f);
                flashElapsed = 0f;
                flashStrikesTwice = Random.value < 0.45f;
                CreateBolt();
            }

            flashElapsed += deltaTime;
            float t = flashElapsed;
            float flicker = 0.8f + 0.2f * Mathf.PerlinNoise(t * 40f, 0.5f);
            float strength;
            float boltAlpha;

            if (t < 0.05f) { strength = 0.3f; boltAlpha = 0f; }
            else if (t < 0.1f) { strength = 0.04f; boltAlpha = 0f; }
            else if (t < 0.3f) { strength = flicker; boltAlpha = t < 0.16f || t > 0.2f ? 1f : 0.35f; }
            else
            {
                strength = Mathf.Exp(-(t - 0.3f) * 4.5f);
                boltAlpha = Mathf.Clamp01(1f - (t - 0.3f) / 0.18f) * 0.7f;
            }

            if (flashStrikesTwice && t >= 0.5f && t < 0.62f)
            {
                strength = Mathf.Max(strength, 0.75f * flicker);
                boltAlpha = 0.9f;
            }

            if (bolt != null)
            {
                bolt.enabled = boltAlpha > 0.01f;
                bolt.color = new Color(1f, 1f, 1f, boltAlpha);
            }

            if (t >= 1.2f)
            {
                flashElapsed = -1f;
                if (bolt != null) bolt.enabled = false;
                return 0f;
            }
            return Mathf.Clamp01(strength);
        }

        /// <summary>
        /// A fresh pixel lightning bolt for this flash: a jagged 1 px core with a soft 1 px glow and a fork or two,
        /// striking down from the top of the map somewhere across it.
        /// </summary>
        private void CreateBolt()
        {
            if (bolt == null) return;

            int artHeightPx = Mathf.Max(32, Mathf.RoundToInt((artMax.y - artMin.y) / pixelSize));
            int height = Mathf.RoundToInt(artHeightPx * Random.Range(0.35f, 0.6f));
            const int width = 48;
            var core = new bool[width, height];

            int x = width / 2;
            int y = height - 1;
            var forkStarts = new List<Vector2Int>();
            while (y > 0)
            {
                int nextY = Mathf.Max(0, y - Random.Range(4, 9));
                int nextX = Mathf.Clamp(x + Random.Range(-5, 6), 2, width - 3);
                DrawLine(core, x, y, nextX, nextY);
                if (forkStarts.Count < 2 && y < height - 6 && Random.value < 0.25f) forkStarts.Add(new Vector2Int(x, y));
                x = nextX;
                y = nextY;
            }

            foreach (Vector2Int start in forkStarts)
            {
                int fx = start.x, fy = start.y;
                int direction = Random.value < 0.5f ? -1 : 1;
                int steps = Random.Range(2, 5);
                for (int s = 0; s < steps && fy > 0; s++)
                {
                    int nextY = Mathf.Max(0, fy - Random.Range(3, 7));
                    int nextX = Mathf.Clamp(fx + direction * Random.Range(2, 6), 1, width - 2);
                    DrawLine(core, fx, fy, nextX, nextY);
                    fx = nextX;
                    fy = nextY;
                }
            }

            var pixels = new Color[width * height];
            Color coreColor = new Color(0.94f, 1f, 0.84f, 1f);
            Color glowColor = new Color(0.62f, 1f, 0.4f, 0.45f);
            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    if (core[px, py]) { pixels[py * width + px] = coreColor; continue; }
                    bool nearCore = (px > 0 && core[px - 1, py]) || (px < width - 1 && core[px + 1, py]) ||
                                    (py > 0 && core[px, py - 1]) || (py < height - 1 && core[px, py + 1]);
                    pixels[py * width + px] = nearCore ? glowColor : Color.clear;
                }
            }

            DestroyGeneratedSprite(boltSprite);
            boltSprite = MakeSprite(pixels, width, height, "Stage6 Lightning Bolt", new Vector2(0.5f, 1f));
            bolt.sprite = boltSprite;

            float halfWidth = width * 0.5f * pixelSize;
            float boltX = Random.Range(artMin.x + halfWidth, artMax.x - halfWidth);
            bolt.transform.position = Snap(boltX, artMax.y);
            bolt.flipX = Random.value < 0.5f;
        }

        private static void DrawLine(bool[,] grid, int x0, int y0, int x1, int y1)
        {
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;
            while (true)
            {
                if (x0 >= 0 && x0 < grid.GetLength(0) && y0 >= 0 && y0 < grid.GetLength(1)) grid[x0, y0] = true;
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * error;
                if (e2 >= dy) { error += dy; x0 += sx; }
                if (e2 <= dx) { error += dx; y0 += sy; }
            }
        }

        // ---------- Generated sprites ----------

        private Sprite Track(Sprite sprite)
        {
            generatedSprites.Add(sprite);
            return sprite;
        }

        /// <summary>A point-filtered pixel-art sprite at 32 px per tile (draw it at localScale = cellSize).</summary>
        private static Sprite MakeSprite(Color[] pixels, int width, int height, string name, Vector2 pivot)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, width, height), pivot, ArtPixelsPerTile, 0, SpriteMeshType.FullRect);
        }

        private void CreateSprites()
        {
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            streakSprite = Track(CreateStreakSprite());
            skyFlashSprite = Track(CreateSkyFlashSprite());

            fogSprites = new Sprite[FogSizes.Length];
            for (int i = 0; i < FogSizes.Length; i++)
                fogSprites[i] = Track(CreateFogSprite(FogSizes[i].x, FogSizes[i].y, 6100 + i * 613, 1f));

            fumeSprites = new Sprite[FumeSizes.Length];
            for (int i = 0; i < FumeSizes.Length; i++)
                fumeSprites[i] = Track(CreateFogSprite(FumeSizes[i].x, FumeSizes[i].y, 6900 + i * 211, 1.15f));

            ringSprites = new[] { Track(CreateRingSprite(3)), Track(CreateRingSprite(5)), Track(CreateRingSprite(7)) };

            puddleGrowth = new Sprite[PuddleVariants][];
            puddleShimmer = new Sprite[PuddleVariants][];
            for (int v = 0; v < PuddleVariants; v++)
            {
                int seed = 8300 + v * 577;
                puddleGrowth[v] = new Sprite[PuddleGrowthStages];
                for (int s = 0; s < PuddleGrowthStages; s++)
                {
                    float coverage = (s + 1f) / PuddleGrowthStages;
                    puddleGrowth[v][s] = Track(CreateAcidSprite(false, false, false, false, 6f, coverage, seed, Vector2Int.zero, 0, $"Stage6 Puddle {v} grow {s}"));
                }

                puddleShimmer[v] = new Sprite[3];
                for (int f = 0; f < 3; f++)
                    puddleShimmer[v][f] = Track(CreateAcidSprite(false, false, false, false, 6f, 1f, seed, Vector2Int.zero, f, $"Stage6 Puddle {v} #{f}"));
            }
        }

        /// <summary>A 1x8 streak: a faint tail at the top brightening to a solid head at the bottom.</summary>
        private static Sprite CreateStreakSprite()
        {
            const int height = 8;
            var pixels = new Color[height];
            for (int y = 0; y < height; y++)
            {
                float fromHead = (float)y / (height - 1); // 0 at the bottom (head), 1 at the top (tail)
                pixels[y] = new Color(1f, 1f, 1f, Mathf.Lerp(1f, 0.12f, fromHead));
            }
            return MakeSprite(pixels, 1, height, "Stage6 Acid Streak", new Vector2(0.5f, 0f));
        }

        /// <summary>A sky glow for the storm flash: full at the top, fading out by the middle (dithered bands).</summary>
        private static Sprite CreateSkyFlashSprite()
        {
            const int width = 16;
            const int height = 64;
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height; // 0 bottom, 1 top
                float alpha = Mathf.Clamp01((v - 0.35f) / 0.65f);
                alpha = alpha * alpha;
                for (int x = 0; x < width; x++)
                {
                    // An ordered dither between bands keeps the falloff from turning into blocks on the CRT
                    float threshold = ((x & 1) * 2 + (y & 1)) / 4f * 0.12f;
                    pixels[y * width + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha + threshold - 0.06f));
                }
            }
            return MakeSprite(pixels, width, height, "Stage6 Sky Flash", new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// A rounded, billowing pixel-art fog cloud. Lumpy body eroded by noise into puffs, then quantised into a
        /// solid core, a lighter body, a checker dither and a sparse dither at the edge (no soft gradient, so it
        /// can't turn into flat translucent rectangles). Lit from above: top surfaces light, undersides shadow.
        /// </summary>
        private static Sprite CreateFogSprite(int width, int height, int seed, float lumpScale)
        {
            var random = new System.Random(seed);
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

            var lumps = new Vector4[6];
            for (int i = 0; i < lumps.Length; i++)
            {
                lumps[i] = new Vector4(Range(0.22f, 0.78f), Range(0.35f, 0.62f),
                    Range(0.13f, 0.24f) * lumpScale, Range(0.22f, 0.34f) * lumpScale);
            }
            float ox = Range(0f, 100f);
            float oy = Range(0f, 100f);

            var density = new float[width, height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float v = (y + 0.5f) / height;

                    float shape = 0f;
                    for (int i = 0; i < lumps.Length; i++)
                    {
                        float dx = (u - lumps[i].x) / lumps[i].z;
                        float dy = (v - lumps[i].y) / lumps[i].w;
                        shape += Mathf.Exp(-(dx * dx + dy * dy) * 1.6f);
                    }
                    float body = 1f - Mathf.Exp(-shape * 1.25f);

                    // Round, billowy noise (not wind-stretched like the Stage 3 sand)
                    float billow = Mathf.PerlinNoise(ox + u * 4f, oy + v * 3f) * 0.6f +
                                   Mathf.PerlinNoise(ox + u * 9f + 13f, oy + v * 7f + 5f) * 0.4f;
                    float d = body * Mathf.Lerp(0.6f, 1.15f, billow);

                    float edgeX = Mathf.Min(u, 1f - u) * 2f;
                    float edgeY = Mathf.Min(v, 1f - v) * 2f;
                    d *= Mathf.SmoothStep(0f, 1f, Mathf.Min(edgeX * 3f, 1f)) * Mathf.SmoothStep(0f, 1f, Mathf.Min(edgeY * 3f, 1f));
                    density[x, y] = d;
                }
            }

            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float d = density[x, y];
                    float alpha;
                    if (d > 0.62f) alpha = 0.85f;
                    else if (d > 0.44f) alpha = 0.68f;
                    else if (d > 0.3f) alpha = (x + y) % 2 == 0 ? 0.55f : 0f;
                    else if (d > 0.2f) alpha = x % 2 == 0 && y % 2 == 0 ? 0.4f : 0f;
                    else alpha = 0f;

                    if (alpha <= 0f)
                    {
                        pixels[y * width + x] = Color.clear;
                        continue;
                    }

                    float above = density[x, Mathf.Min(height - 1, y + 2)];
                    float below = density[x, Mathf.Max(0, y - 2)];
                    Color tone = d - above > 0.06f ? FogLight : below < d - 0.06f ? FogShadow : FogMid;
                    tone.a = alpha;
                    pixels[y * width + x] = tone;
                }
            }
            return MakeSprite(pixels, width, height, $"Stage6 Fog {width}x{height}", new Vector2(0.5f, 0.5f));
        }

        /// <summary>A pixel ring of the given size with a glint on its upper left, for bubbles.</summary>
        private static Sprite CreateRingSprite(int size)
        {
            var pixels = new Color[size * size];
            float centre = (size - 1) * 0.5f;
            float outer = centre + 0.35f;
            float inner = Mathf.Max(0.2f, centre - 0.85f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                    Color color = Color.clear;
                    if (d <= outer && d > inner) color = new Color(1f, 1f, 1f, 0.95f); // rim
                    else if (d <= inner) color = new Color(1f, 1f, 1f, 0.2f);          // thin film
                    pixels[y * size + x] = color;
                }
            }
            if (size >= 5) pixels[(size - 2) * size + 1] = Color.white; // glint
            return MakeSprite(pixels, size, size, $"Stage6 Bubble {size}", new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// One tile (32x32) of pixel-art acid. The shape is a rounded blob with a wobbly edge, inset from the tile
        /// edges, except towards open sides (neighbouring pool tiles), where it runs past the edge so pools join up.
        /// Tones: a dark rim, a bright shallow band, the body, a dithered deeper middle and per-frame surface glints.
        /// coverage below 1 shrinks the blob towards its middle (puddles growing and drying).
        /// </summary>
        private static Sprite CreateAcidSprite(bool openLeft, bool openRight, bool openDown, bool openUp,
            float baseInset, float coverage, int seed, Vector2Int noiseOffset, int frame, string name)
        {
            const int size = ArtPixelsPerTile;
            var random = new System.Random(seed);
            float ox = (float)random.NextDouble() * 100f;
            float oy = (float)random.NextDouble() * 100f;
            const float cornerRadius = 7f;

            float insetL = openLeft ? -6f : baseInset;
            float insetR = openRight ? -6f : baseInset;
            float insetB = openDown ? -6f : baseInset;
            float insetT = openUp ? -6f : baseInset;
            // Grows from the middle: coverage 1 is the full shape, lower values need a deeper field to show
            float threshold = (1f - coverage) * 11f;

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float left = x - insetL, right = (size - 1 - x) - insetR;
                    float bottom = y - insetB, top = (size - 1 - y) - insetT;
                    float field = Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top));

                    // Round the corners between two closed sides
                    if (!openLeft && !openDown) field = Mathf.Min(field, CornerField(left, bottom, cornerRadius));
                    if (!openRight && !openDown) field = Mathf.Min(field, CornerField(right, bottom, cornerRadius));
                    if (!openLeft && !openUp) field = Mathf.Min(field, CornerField(left, top, cornerRadius));
                    if (!openRight && !openUp) field = Mathf.Min(field, CornerField(right, top, cornerRadius));

                    // A wobbly, organic edge
                    int wx = x + noiseOffset.x, wy = y + noiseOffset.y;
                    field += (Mathf.PerlinNoise(ox + wx * 0.19f, oy + wy * 0.19f) - 0.5f) * 6f;
                    field -= threshold;

                    if (field < 0f)
                    {
                        pixels[y * size + x] = Color.clear;
                        continue;
                    }

                    Color color;
                    if (field < 1.2f) color = AcidRim;
                    else if (field < 3.2f) color = AcidShallow;
                    else if (field < 6.5f) color = AcidBody;
                    else if (field < 8f) color = (wx + wy) % 2 == 0 ? AcidDeep : AcidBody;
                    else color = AcidDeep;

                    // Surface glints that move between frames
                    if (field > 2.5f)
                    {
                        float glint = Mathf.PerlinNoise(ox + wx * 0.33f + frame * 2.3f, oy + wy * 0.55f + frame * 0.9f);
                        if (glint > 0.8f) color = AcidGlint;
                        else if (glint > 0.74f && (wx + wy + frame) % 2 == 0) color = AcidShallow;
                    }
                    pixels[y * size + x] = color;
                }
            }
            return MakeSprite(pixels, size, size, name, new Vector2(0.5f, 0.5f));
        }

        /// <summary>Distance inside a rounded corner: positive inside the quarter circle, matching the edge distances.</summary>
        private static float CornerField(float a, float b, float radius)
        {
            if (a >= radius || b >= radius) return float.MaxValue;
            float dx = radius - a;
            float dy = radius - b;
            return radius - Mathf.Sqrt(dx * dx + dy * dy);
        }
    }
}
