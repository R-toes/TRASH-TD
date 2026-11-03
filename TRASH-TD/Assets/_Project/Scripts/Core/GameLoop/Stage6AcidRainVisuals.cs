using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;

namespace TrashTD.Core.GameLoop
{
    /// <summary>
    /// Stage 6 (The Pale) acid storm. Visual only: the damage stays in StageBootstrapper
    /// (ApplyStage6AcidRainDamage), which calls PlayAcidTick on every damage tick.
    /// - Acid rain: slanted streaks with a bright head and a fading tail, blown left to right. The storm
    ///   swings between a drizzle and a downpour. Each drop splashes on the ground (a dash beneath the units),
    ///   throws up a couple of droplets, and now and then hisses off a wisp of fume.
    /// - Toxic fog: sickly green puffs (Stage 1's mist sprites, tinted) in two layers, thin over the enemy lane
    ///   and thick off the grid, plus a green cast over the whole map.
    /// - Acid pools (Trap tiles): a pulsing green glow and bubbles that swell and pop.
    /// - Storm flashes: a green flicker over the map every 14-28 s.
    /// - Damage ticks: a gust of heavier rain and a pulse of the green cast, so the hit reads as the rain.
    /// Everything is clipped to the map art and runs on scaled time, so it freezes while paused.
    /// Generated sprites belong to this instance and are destroyed with it (domain reload is off).
    /// </summary>
    public sealed class Stage6AcidRainVisuals : MonoBehaviour
    {
        private const int TintSortingOrder = 2;       // Ground layer; map artwork is 1
        private const int GroundSortingOrder = 3;     // Ground layer, beneath every unit
        private const int FrontFogSortingOrder = 11;  // Above ground units and foreground art (10), below air units (12)
        private const int FlashSortingOrder = 23;     // Default layer, above units, below the rain
        private const int RainSortingOrder = 24;      // Above units, below projectiles (40)

        private const int MaxRainDrops = 260;
        private const int MaxSpray = 140;
        private const int MaxFumes = 28;
        private const int MaxBubbles = 36;

        private const float DrizzleIntensity = 0.6f;
        private const float DownpourIntensity = 1f;
        private const float IntensityChangePerSecond = 0.15f;
        private const float TickGustSeconds = 0.6f;
        private const float TickGustBoost = 0.45f;     // extra intensity during a damage-tick gust
        private const float SplashDuration = 0.14f;
        private const float FumeChance = 0.16f;
        private const float SprayGravity = 9f;         // tiles / s²
        private const int MistSpriteVariants = 3;

        private static readonly Color RainColor = new Color(0.66f, 1f, 0.22f, 0.62f);
        private static readonly Color HeavyRainColor = new Color(0.86f, 1f, 0.5f, 0.85f);
        private static readonly Color SplashColor = new Color(0.58f, 1f, 0.2f, 0.65f);
        private static readonly Color SprayColor = new Color(0.78f, 1f, 0.38f, 0.9f);
        private static readonly Color FumeColor = new Color(0.62f, 0.76f, 0.42f, 1f);
        private static readonly Color FogColor = new Color(0.56f, 0.74f, 0.36f, 1f);
        private static readonly Color TintColor = new Color(0.22f, 0.45f, 0.06f, 1f);
        private static readonly Color FlashColor = new Color(0.78f, 1f, 0.6f, 1f);
        private static readonly Color PoolGlowColor = new Color(0.45f, 1f, 0.15f, 1f);
        private static readonly Color BubbleColor = new Color(0.72f, 1f, 0.32f, 0.9f);

        private enum Storm { Drizzle, Downpour }

        private sealed class FogLayer
        {
            public string Name;
            public int Count;
            public int SortingOrder;
            public Vector2 WidthRange;
            public Vector2 HeightRange;
            public Vector2 AlphaRange;
            public Vector2 SpeedRange;
            public float DensityExponent; // > 1 thins the layer out faster over the lane
        }

        private static readonly FogLayer[] FogLayers =
        {
            new FogLayer
            {
                Name = "Back", Count = 12, SortingOrder = GroundSortingOrder,
                WidthRange = new Vector2(2.6f, 4.2f), HeightRange = new Vector2(0.9f, 1.5f),
                AlphaRange = new Vector2(0.30f, 0.48f), SpeedRange = new Vector2(0.10f, 0.22f), DensityExponent = 1f
            },
            new FogLayer
            {
                Name = "Front", Count = 9, SortingOrder = FrontFogSortingOrder,
                WidthRange = new Vector2(2.2f, 3.6f), HeightRange = new Vector2(0.7f, 1.2f),
                AlphaRange = new Vector2(0.22f, 0.36f), SpeedRange = new Vector2(0.16f, 0.3f), DensityExponent = 2.2f
            }
        };

        private sealed class FogPuff
        {
            public FogLayer Layer;
            public Transform Transform;
            public SpriteRenderer Renderer;
            public Vector3 BaseScale;
            public float BaseY;
            public float BaseAlpha;
            public float Speed;
            public float HalfWidth;
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

        /// <summary>A short-lived bit: a droplet thrown from a splash or a popping bubble, or a rising fume wisp.</summary>
        private sealed class Particle
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public bool Active;
            public Vector2 Velocity;
            public float Age;
            public float Lifetime;
            public float Size;
        }

        private sealed class Bubble
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public bool Active;
            public float Age;
            public float Lifetime;
            public float MaxSize;
            public float Delay; // waits this long before the next bubble rises
        }

        private GridManager gridManager;
        private StageData stageData;
        private float cellSize;
        private float pixelSize;
        private Vector2 artMin;
        private Vector2 artMax;
        private Vector2 gridMin;
        private float[,] fogDensityField; // Per cell, with a 1-tile border ring of full density

        private readonly List<FogPuff> fogPuffs = new List<FogPuff>();
        private readonly List<RainDrop> rainDrops = new List<RainDrop>();
        private readonly List<Particle> sprays = new List<Particle>();
        private readonly List<Particle> fumes = new List<Particle>();
        private readonly List<Bubble> bubbles = new List<Bubble>();
        private readonly List<SpriteRenderer> poolGlows = new List<SpriteRenderer>();
        private readonly List<Vector3> poolCells = new List<Vector3>();
        private SpriteRenderer tint;
        private SpriteRenderer flash;

        private Storm storm = Storm.Drizzle;
        private float stormTimeLeft;
        private float rainIntensity;
        private Vector2 rainVelocity;
        private float tickGustTimer;
        private float tickPulse;          // 1 on a damage tick, fades quickly
        private float flashTimer;         // counts down to the next storm flash
        private float flashElapsed = -1f; // time into the current flash, < 0 when none
        private float sprayBudget;        // spray particles still allowed this frame

        private Sprite whiteSprite;
        private Sprite streakSprite;
        private Sprite ringSprite;
        private Sprite glowSprite;
        private Sprite[] mistSprites;

        public void Initialize(GridManager grid, StageData stage)
        {
            gridManager = grid;
            stageData = stage;
            if (gridManager == null || stageData == null) return;

            cellSize = gridManager.CellSize;
            pixelSize = cellSize / 32f;
            CalculateBounds();
            BuildFogDensityField();
            CreateSprites();

            CreateArtMask();
            CreateTintAndFlash();
            CreateFog();
            CreateAcidPools();
            CreateRainDrops();
            CreateParticles();

            rainIntensity = DrizzleIntensity;
            stormTimeLeft = Random.Range(18f, 30f);
            flashTimer = Random.Range(8f, 16f);
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
            // whiteSprite wraps Texture2D.whiteTexture, which is built in: destroy the sprite only
            if (whiteSprite != null) Destroy(whiteSprite);
            DestroyGeneratedSprite(streakSprite);
            DestroyGeneratedSprite(ringSprite);
            DestroyGeneratedSprite(glowSprite);
            if (mistSprites != null)
            {
                foreach (Sprite sprite in mistSprites) DestroyGeneratedSprite(sprite);
            }
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
            UpdateParticles(sprays, deltaTime, true);
            UpdateParticles(fumes, deltaTime, false);
            UpdateAcidPools(deltaTime);
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
                _ => 0.25f              // Enemy lane, melee ground, spawns, exits, holes
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

        private SpriteRenderer CreateMapCover(string objectName, bool onGround, int sortingOrder)
        {
            var coverObject = new GameObject(objectName);
            coverObject.transform.SetParent(transform, false);
            coverObject.transform.position = (Vector3)((artMin + artMax) * 0.5f);
            coverObject.transform.localScale = new Vector3(artMax.x - artMin.x, artMax.y - artMin.y, 1f);

            var renderer = coverObject.AddComponent<SpriteRenderer>();
            renderer.sprite = whiteSprite;
            if (onGround) GridSorting.PlaceOnGround(renderer);
            renderer.sortingOrder = sortingOrder;
            renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            renderer.color = Color.clear;
            return renderer;
        }

        private void CreateTintAndFlash()
        {
            tint = CreateMapCover("Stage6ToxicCast", true, TintSortingOrder);
            flash = CreateMapCover("Stage6StormFlash", false, FlashSortingOrder);
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
                    var puffObject = new GameObject($"FogPuff_{i}");
                    puffObject.transform.SetParent(layerRoot.transform, false);

                    var renderer = puffObject.AddComponent<SpriteRenderer>();
                    renderer.sprite = mistSprites[i % mistSprites.Length];
                    if (layer.SortingOrder == GroundSortingOrder) GridSorting.PlaceOnGround(renderer);
                    renderer.sortingOrder = layer.SortingOrder;
                    renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

                    var puff = new FogPuff { Layer = layer, Transform = puffObject.transform, Renderer = renderer };
                    ResetFogPuff(puff, true);
                    fogPuffs.Add(puff);
                }
            }
        }

        private void ResetFogPuff(FogPuff puff, bool anywhere)
        {
            FogLayer layer = puff.Layer;
            float width = Random.Range(layer.WidthRange.x, layer.WidthRange.y) * cellSize;
            float height = Random.Range(layer.HeightRange.x, layer.HeightRange.y) * cellSize;
            puff.HalfWidth = width * 0.5f;

            // The mist sprites are 1 x 0.5 units
            puff.BaseScale = new Vector3(width, height * 2f, 1f);
            puff.Transform.localScale = puff.BaseScale;
            puff.Renderer.flipX = Random.value < 0.5f;

            // Drifts right with the storm wind; new puffs roll in from the left edge
            float x = anywhere
                ? Random.Range(artMin.x - puff.HalfWidth, artMax.x + puff.HalfWidth)
                : artMin.x - puff.HalfWidth;
            puff.BaseY = Random.Range(artMin.y, artMax.y);
            puff.Transform.position = new Vector3(x, puff.BaseY, 0f);

            puff.BaseAlpha = Random.Range(layer.AlphaRange.x, layer.AlphaRange.y);
            puff.Speed = Random.Range(layer.SpeedRange.x, layer.SpeedRange.y) * cellSize;
            puff.Phase = Random.Range(0f, Mathf.PI * 2f);
            puff.FadeIn = anywhere ? 1f : 0f;
        }

        /// <summary>A glow under each acid pool (Trap tile) and a pool of bubbles that rise in them.</summary>
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
                    poolCells.Add(cell.WorldPosition);

                    var glowObject = new GameObject($"PoolGlow_{x}_{y}");
                    glowObject.transform.SetParent(poolRoot.transform, false);
                    glowObject.transform.position = cell.WorldPosition;
                    Vector2 glowSize = glowSprite.bounds.size;
                    glowObject.transform.localScale = new Vector3(
                        cellSize * 1.3f / glowSize.x, cellSize * 1.0f / glowSize.y, 1f);

                    var renderer = glowObject.AddComponent<SpriteRenderer>();
                    renderer.sprite = glowSprite;
                    GridSorting.PlaceOnGround(renderer);
                    renderer.sortingOrder = TintSortingOrder;
                    renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                    renderer.color = Color.clear;
                    poolGlows.Add(renderer);
                }
            }

            if (poolCells.Count == 0) return;

            for (int i = 0; i < MaxBubbles; i++)
            {
                var bubbleObject = new GameObject($"Bubble_{i}");
                bubbleObject.transform.SetParent(poolRoot.transform, false);
                var renderer = bubbleObject.AddComponent<SpriteRenderer>();
                renderer.sprite = ringSprite;
                GridSorting.PlaceOnGround(renderer);
                renderer.sortingOrder = GroundSortingOrder;
                renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                bubbleObject.SetActive(false);
                // Staggered so the pools don't all bubble at once
                bubbles.Add(new Bubble { Transform = bubbleObject.transform, Renderer = renderer, Delay = Random.Range(0f, 2.5f) });
            }
        }

        private void CreateRainDrops()
        {
            var rainRoot = new GameObject("Stage6AcidRain");
            rainRoot.transform.SetParent(transform, false);

            for (int i = 0; i < MaxRainDrops; i++)
            {
                var dropObject = new GameObject($"AcidDrop_{i}");
                dropObject.transform.SetParent(rainRoot.transform, false);

                var renderer = dropObject.AddComponent<SpriteRenderer>();
                renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                dropObject.SetActive(false);

                rainDrops.Add(new RainDrop { Transform = dropObject.transform, Renderer = renderer });
            }
        }

        private void CreateParticles()
        {
            var sprayRoot = new GameObject("Stage6AcidSpray");
            sprayRoot.transform.SetParent(transform, false);
            for (int i = 0; i < MaxSpray; i++)
            {
                sprays.Add(CreateParticle(sprayRoot.transform, $"Spray_{i}", whiteSprite, false, RainSortingOrder));
            }

            var fumeRoot = new GameObject("Stage6AcidFumes");
            fumeRoot.transform.SetParent(transform, false);
            for (int i = 0; i < MaxFumes; i++)
            {
                fumes.Add(CreateParticle(fumeRoot.transform, $"Fume_{i}", mistSprites[i % mistSprites.Length], false, FrontFogSortingOrder));
            }
        }

        private static Particle CreateParticle(Transform parent, string objectName, Sprite sprite, bool onGround, int sortingOrder)
        {
            var particleObject = new GameObject(objectName);
            particleObject.transform.SetParent(parent, false);
            var renderer = particleObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            if (onGround) GridSorting.PlaceOnGround(renderer);
            renderer.sortingOrder = sortingOrder;
            renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            particleObject.SetActive(false);
            return new Particle { Transform = particleObject.transform, Renderer = renderer };
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

        // ---------- Per-frame updates ----------

        private void UpdateFog(float deltaTime)
        {
            float time = Time.time;
            for (int i = 0; i < fogPuffs.Count; i++)
            {
                FogPuff puff = fogPuffs[i];
                Vector3 position = puff.Transform.position;
                position.x += puff.Speed * deltaTime;
                if (position.x > artMax.x + puff.HalfWidth)
                {
                    ResetFogPuff(puff, false);
                    position = puff.Transform.position;
                }

                // Slow churn: swell, flatten and bob out of step with the neighbours
                float wave = time * 0.4f + puff.Phase;
                position.y = puff.BaseY + Mathf.Sin(wave * 0.7f) * cellSize * 0.07f;
                puff.Transform.position = position;
                puff.Transform.localScale = new Vector3(
                    puff.BaseScale.x * (1f + 0.07f * Mathf.Sin(wave)),
                    puff.BaseScale.y * (1f + 0.1f * Mathf.Sin(wave * 1.3f + 1f)),
                    1f);

                puff.FadeIn = Mathf.Min(1f, puff.FadeIn + deltaTime * 0.5f);

                float density = Mathf.Pow(GetFogDensity(position), puff.Layer.DensityExponent);
                float breathe = 0.8f + 0.2f * Mathf.Sin(wave * 0.9f + 2f);
                Color color = FogColor;
                color.a = puff.BaseAlpha * density * breathe * puff.FadeIn * (0.85f + 0.25f * rainIntensity);
                puff.Renderer.color = color;
            }
        }

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
                    // The dash spreads as it fades
                    drop.Transform.localScale = new Vector3(pixelSize * Mathf.Lerp(5f, 3f, k), pixelSize, 1f);

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
            float streakWidth = streakSprite.bounds.size.x;
            float streakHeight = streakSprite.bounds.size.y;
            drop.Transform.localScale = new Vector3(
                pixelSize / streakWidth * (drop.Heavy ? 2f : 1f),
                pixelSize * lengthPixels / streakHeight,
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
            Vector3 position = drop.Transform.position;
            position.y = drop.LandY;
            drop.Transform.position = position;
            drop.Transform.rotation = Quaternion.identity;
            drop.Transform.localScale = new Vector3(pixelSize * 3f, pixelSize, 1f);
            drop.Renderer.sprite = whiteSprite;
            GridSorting.PlaceOnGround(drop.Renderer);
            drop.Renderer.sortingOrder = GroundSortingOrder;
            drop.Renderer.color = SplashColor;

            int droplets = drop.Heavy ? 3 : (Random.value < 0.6f ? 2 : 1);
            for (int i = 0; i < droplets; i++) EmitSpray(position, 1f);
            if (Random.value < FumeChance * (drop.Heavy ? 2f : 1f)) EmitFume(position, 0.25f);
        }

        private static void DeactivateRainDrop(RainDrop drop)
        {
            drop.Active = false;
            drop.SplashTimer = 0f;
            drop.Transform.gameObject.SetActive(false);
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
            particle.Size = pixelSize * (Random.value < 0.25f ? 2f : 1f);
            particle.Transform.position = origin;
            particle.Transform.localScale = new Vector3(particle.Size, particle.Size, 1f);
            particle.Renderer.color = SprayColor;
            particle.Transform.gameObject.SetActive(true);
        }

        /// <summary>A small wisp of acid fume rising from where a drop landed or a bubble popped.</summary>
        private void EmitFume(Vector3 origin, float sizeTiles)
        {
            Particle particle = TakeIdle(fumes);
            if (particle == null) return;

            particle.Active = true;
            particle.Age = 0f;
            particle.Lifetime = Random.Range(1.0f, 1.6f);
            particle.Velocity = new Vector2(Random.Range(0.05f, 0.25f), Random.Range(0.25f, 0.45f)) * cellSize;
            particle.Size = sizeTiles * cellSize * Random.Range(0.8f, 1.2f);
            particle.Transform.position = origin;
            particle.Transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(-20f, 20f));
            particle.Renderer.flipX = Random.value < 0.5f;
            particle.Transform.gameObject.SetActive(true);
        }

        private static Particle TakeIdle(List<Particle> pool)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (!pool[i].Active) return pool[i];
            }
            return null;
        }

        /// <summary>Sprays fly ballistically and blink out; fumes rise, swell and fade.</summary>
        private void UpdateParticles(List<Particle> pool, float deltaTime, bool ballistic)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                Particle particle = pool[i];
                if (!particle.Active) continue;

                particle.Age += deltaTime;
                float k = particle.Age / particle.Lifetime;
                if (k >= 1f)
                {
                    particle.Active = false;
                    particle.Transform.gameObject.SetActive(false);
                    continue;
                }

                if (ballistic) particle.Velocity.y -= SprayGravity * cellSize * deltaTime;
                Vector3 position = particle.Transform.position;
                position.x += particle.Velocity.x * deltaTime;
                position.y += particle.Velocity.y * deltaTime;
                particle.Transform.position = position;

                if (ballistic)
                {
                    Color color = SprayColor;
                    color.a *= 1f - k * k;
                    particle.Renderer.color = color;
                    continue;
                }

                // Mist sprites are 1 x 0.5 units: swell from small to full size while fading in then out
                float size = particle.Size * Mathf.Lerp(0.4f, 1.3f, k);
                particle.Transform.localScale = new Vector3(size, size, 1f);
                Color fume = FumeColor;
                fume.a = 0.42f * Mathf.Sin(k * Mathf.PI);
                particle.Renderer.color = fume;
            }
        }

        private void UpdateAcidPools(float deltaTime)
        {
            float time = Time.time;
            for (int i = 0; i < poolGlows.Count; i++)
            {
                // Each pool breathes out of step with the others; damage ticks flare them all
                float pulse = 0.5f + 0.5f * Mathf.Sin(time * 1.6f + i * 1.7f);
                Color glow = PoolGlowColor;
                glow.a = 0.16f + 0.12f * pulse + 0.25f * tickPulse;
                poolGlows[i].color = glow;
            }

            for (int i = 0; i < bubbles.Count; i++)
            {
                Bubble bubble = bubbles[i];
                if (!bubble.Active)
                {
                    bubble.Delay -= deltaTime;
                    if (bubble.Delay <= 0f) StartBubble(bubble);
                    continue;
                }

                bubble.Age += deltaTime;
                float k = bubble.Age / bubble.Lifetime;
                if (k >= 1f)
                {
                    PopBubble(bubble);
                    continue;
                }

                // Swells slowly, then bulges just before it pops
                float size = bubble.MaxSize * Mathf.Lerp(0.25f, 1f, Mathf.Pow(k, 0.7f));
                bubble.Transform.localScale = new Vector3(size, size * 0.8f, 1f);
                Color color = BubbleColor;
                color.a *= Mathf.Clamp01(k * 4f);
                bubble.Renderer.color = color;
            }
        }

        private void StartBubble(Bubble bubble)
        {
            Vector3 cell = poolCells[Random.Range(0, poolCells.Count)];
            bubble.Transform.position = cell + new Vector3(
                Random.Range(-0.38f, 0.38f) * cellSize,
                Random.Range(-0.32f, 0.32f) * cellSize,
                0f);
            bubble.Active = true;
            bubble.Age = 0f;
            bubble.Lifetime = Random.Range(0.6f, 1.3f);
            // The ring sprite is 1 unit across: 3 to 7 art pixels wide
            bubble.MaxSize = pixelSize * Random.Range(3, 8);
            bubble.Transform.localScale = Vector3.zero;
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
            if (Random.value < 0.35f) EmitFume(position, 0.3f);
        }

        private void UpdateTintAndFlash(float deltaTime)
        {
            if (tint != null)
            {
                Color cast = TintColor;
                cast.a = 0.10f + 0.04f * rainIntensity + 0.10f * tickPulse;
                tint.color = cast;
            }

            if (flash == null) return;

            if (flashElapsed < 0f)
            {
                flashTimer -= deltaTime;
                if (flashTimer <= 0f)
                {
                    flashElapsed = 0f;
                    // Downpours flash more often
                    flashTimer = storm == Storm.Downpour ? Random.Range(9f, 18f) : Random.Range(14f, 28f);
                }
            }

            float strength = 0f;
            if (flashElapsed >= 0f)
            {
                flashElapsed += deltaTime;
                // A double flicker, then a slow fade: on, off, on brighter, fade out
                float t = flashElapsed;
                if (t < 0.07f) strength = 0.55f;
                else if (t < 0.14f) strength = 0.1f;
                else if (t < 0.22f) strength = 1f;
                else strength = Mathf.Max(0f, 1f - (t - 0.22f) / 0.55f);
                if (t >= 0.77f) flashElapsed = -1f;
            }

            Color light = FlashColor;
            light.a = 0.2f * strength;
            flash.color = light;
        }

        // ---------- Generated sprites ----------

        private void CreateSprites()
        {
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            streakSprite = CreateStreakSprite();
            ringSprite = CreateRingSprite();
            glowSprite = CreateGlowSprite();

            mistSprites = new Sprite[MistSpriteVariants];
            for (int i = 0; i < MistSpriteVariants; i++)
            {
                mistSprites[i] = Stage1WeatherVisuals.CreateMistSprite(6061 + i * 977);
            }
        }

        /// <summary>A 1x8 streak: a faint tail at the top brightening to a solid head at the bottom.</summary>
        private static Sprite CreateStreakSprite()
        {
            const int height = 8;
            var texture = new Texture2D(1, height, TextureFormat.RGBA32, false)
            {
                name = "Stage6 Acid Streak",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int y = 0; y < height; y++)
            {
                float fromHead = (float)y / (height - 1); // 0 at the bottom (head), 1 at the top (tail)
                texture.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.Lerp(1f, 0.12f, fromHead)));
            }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, 1f, height), new Vector2(0.5f, 0f), height, 0, SpriteMeshType.FullRect);
        }

        /// <summary>A 7x7 pixel ring with a highlight on its upper left, for bubbles.</summary>
        private static Sprite CreateRingSprite()
        {
            const int size = 7;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Stage6 Bubble Ring",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            float centre = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                    Color color = Color.clear;
                    if (d > 2.2f && d <= 3.3f) color = new Color(1f, 1f, 1f, 0.9f);       // rim
                    else if (d <= 2.2f) color = new Color(1f, 1f, 1f, 0.18f);             // thin film
                    if (x == 2 && y == 4) color = Color.white;                             // glint
                    texture.SetPixel(x, y, color);
                }
            }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        }

        /// <summary>A soft ellipse that fades to nothing at its edge, for the pool glows.</summary>
        private static Sprite CreateGlowSprite()
        {
            const int width = 32;
            const int height = 16;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Stage6 Pool Glow",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width * 2f - 1f;
                    float v = (y + 0.5f) / height * 2f - 1f;
                    float d = Mathf.Sqrt(u * u + v * v);
                    float alpha = d >= 1f ? 0f : Mathf.SmoothStep(0f, 1f, 1f - d);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply();
            // FullRect so the faint rim isn't trimmed into a polygon (see Stage1WeatherVisuals.CreateMistSprite)
            return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), width, 0, SpriteMeshType.FullRect);
        }
    }
}
