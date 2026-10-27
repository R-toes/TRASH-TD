using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;

namespace TrashTD.Core.GameLoop
{
    /// <summary>
    /// Stage 3 (Railyard Drain) sandstorm. Visual only: the sandstorm's gameplay (attack intervals x0.95,
    /// 10% miss) lives in StageCombatModifiers.
    /// - Pixel-art dust clouds (point filtered, 3 sand tones, dithered edges, lit from above) blow left to
    ///   right in two layers: a back layer on the ground beneath the units, and a front layer above them.
    ///   Both follow a per-tile density field: thick off the grid, thin over the enemy lane. The front layer
    ///   thins out much faster, so the path stays readable.
    /// - 1 px wind streaks whip across above the units, and sand grains hop along the ground.
    /// - The wind comes in gusts: a steady breeze, short strong gusts and the odd lull. Gust strength drives
    ///   cloud speed and thickness, streak and grain counts, and a warm haze over the map.
    /// Everything is snapped to the 32 px-per-tile art grid. Runs on scaled time, so it freezes while paused.
    /// </summary>
    public sealed class Stage3SandstormVisuals : MonoBehaviour
    {
        private const int HazeSortingOrder = 2;       // Map artwork is 1
        private const int GroundSortingOrder = 3;     // On the Ground sorting layer, beneath every unit
        private const int FrontDustSortingOrder = 11; // Above ground units and foreground art (10), below air units (12)
        private const int StreakSortingOrder = 24;    // Above units, below projectiles (40)

        private const int ArtPixelsPerTile = 32;
        private const int MaxStreaks = 110;
        private const int MaxGrains = 80;

        private const float LullIntensity = 0.2f;
        private const float BreezeIntensity = 0.5f;
        private const float GustIntensity = 1f;
        private const float GustRisePerSecond = 0.6f; // Gusts hit quickly...
        private const float GustFallPerSecond = 0.25f; // ...and die down slowly
        private const float LullChance = 0.3f;

        private const float HazeMinAlpha = 0.05f;
        private const float HazeMaxAlpha = 0.18f;

        private static readonly Color HazeColor = new Color(0.86f, 0.62f, 0.32f, 1f);
        private static readonly Color StreakColor = new Color(1f, 0.9f, 0.7f, 1f);
        private static readonly Color GrainColor = new Color(0.8f, 0.62f, 0.38f, 1f);
        private static readonly Color DustLight = new Color(0.97f, 0.86f, 0.64f);
        private static readonly Color DustMid = new Color(0.86f, 0.69f, 0.45f);
        private static readonly Color DustShadow = new Color(0.68f, 0.51f, 0.32f);

        /// <summary>Cloud sizes in art pixels (width, height); clouds never scale, so they stay crisp.</summary>
        private static readonly Vector2Int[] CloudSizes =
        {
            new Vector2Int(64, 22),
            new Vector2Int(80, 28),
            new Vector2Int(96, 32),
            new Vector2Int(112, 38),
            new Vector2Int(128, 44)
        };

        private enum Wind { Lull, Breeze, Gust }

        /// <summary>One depth layer of dust clouds. Speeds are in cells per second at full gust.</summary>
        private sealed class DustLayer
        {
            public string Name;
            public int Count;
            public int SortingOrder;
            public Vector2 AlphaRange;
            public Vector2 SpeedRange;
            public float DensityExponent; // > 1 thins the layer out faster over the walkable tiles
        }

        private static readonly DustLayer[] DustLayers =
        {
            new DustLayer
            {
                Name = "Back", Count = 16, SortingOrder = GroundSortingOrder,
                AlphaRange = new Vector2(0.5f, 0.75f), SpeedRange = new Vector2(1.1f, 1.8f), DensityExponent = 1f
            },
            new DustLayer
            {
                Name = "Front", Count = 12, SortingOrder = FrontDustSortingOrder,
                AlphaRange = new Vector2(0.4f, 0.6f), SpeedRange = new Vector2(1.8f, 2.8f), DensityExponent = 2f
            }
        };

        private sealed class DustCloud
        {
            public DustLayer Layer;
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

        private sealed class Streak
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public bool Active;
            public Vector2 Position;
            public float Speed;
            public float Drop;
            public float Length;
            public float Alpha;
            public float Phase;
        }

        private sealed class Grain
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public bool Active;
            public Vector2 Origin;
            public float Speed;
            public float HopHeight;
            public float HopRate;
            public float Age;
            public float Life;
        }

        private GridManager gridManager;
        private StageData stageData;

        private float cellSize;
        private float pixelSize;
        private Vector2 artMin;
        private Vector2 artMax;
        private Vector2 gridMin;
        private float[,] densityField; // Per cell, with a 1-tile border ring of full density

        private readonly List<DustCloud> clouds = new List<DustCloud>();
        private readonly List<Streak> streaks = new List<Streak>();
        private readonly List<Grain> grains = new List<Grain>();
        private SpriteRenderer haze;

        private readonly Queue<(Wind wind, float duration)> forecast = new Queue<(Wind, float)>();
        private Wind wind = Wind.Breeze;
        private float windTimeLeft;
        private float intensity;

        private static Sprite whiteSprite;
        private static Sprite[] cloudSprites;

        public float WindIntensity => intensity;

        public void Initialize(GridManager grid, StageData stage)
        {
            gridManager = grid;
            stageData = stage;
            if (gridManager == null || stageData == null) return;

            cellSize = gridManager.CellSize;
            pixelSize = cellSize / ArtPixelsPerTile;
            CalculateBounds();
            BuildDensityField();
            intensity = BreezeIntensity;

            CreateArtMask();
            CreateHaze();
            CreateClouds();
            CreateStreaks();
            CreateGrains();

            AdvanceForecast();
        }

        private void Update()
        {
            if (gridManager == null) return;

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            UpdateWind(deltaTime);
            UpdateClouds(deltaTime);
            UpdateStreaks(deltaTime);
            UpdateGrains(deltaTime);

            if (haze != null)
            {
                Color color = HazeColor;
                color.a = Mathf.Lerp(HazeMinAlpha, HazeMaxAlpha, intensity);
                haze.color = color;
            }
        }

        // ---------- Wind cycle ----------

        private void UpdateWind(float deltaTime)
        {
            windTimeLeft -= deltaTime;
            if (windTimeLeft <= 0f) AdvanceForecast();

            float target = wind switch
            {
                Wind.Gust => GustIntensity,
                Wind.Lull => LullIntensity,
                _ => BreezeIntensity
            };
            float rate = target > intensity ? GustRisePerSecond : GustFallPerSecond;
            intensity = Mathf.MoveTowards(intensity, target, rate * deltaTime);
        }

        private void AdvanceForecast()
        {
            if (forecast.Count == 0) PlanNextWind();

            (wind, windTimeLeft) = forecast.Dequeue();
        }

        /// <summary>A steady breeze broken by short gusts; now and then the wind drops to a lull.</summary>
        private void PlanNextWind()
        {
            forecast.Enqueue((Wind.Breeze, Random.Range(8f, 16f)));
            forecast.Enqueue((Wind.Gust, Random.Range(4f, 8f)));

            if (Random.value < LullChance)
            {
                forecast.Enqueue((Wind.Breeze, Random.Range(4f, 8f)));
                forecast.Enqueue((Wind.Lull, Random.Range(4f, 7f)));
            }
        }

        /// <summary>0.5 in a lull, 1 in a breeze, ~1.6 in a full gust.</summary>
        private float WindSpeedFactor => 0.3f + intensity * 1.3f;

        // ---------- Bounds and dust density ----------

        private void CalculateBounds()
        {
            gridMin = gridManager.GridToWorldPosition(0, 0) - new Vector3(cellSize * 0.5f, cellSize * 0.5f, 0f);
            Vector2 gridMax = gridMin + new Vector2(stageData.gridWidth, stageData.gridHeight) * cellSize;
            artMin = gridMin;
            artMax = gridMax;

            if (stageData.mapVisualSprite != null)
            {
                float pixelPerTile = stageData.visualTilePixelSize > 0 ? stageData.visualTilePixelSize : 32f;
                Vector2 spriteMin = gridMin + stageData.visualTileOffset * cellSize;
                Vector2 spriteMax = spriteMin + stageData.mapVisualSprite.rect.size / pixelPerTile * cellSize;
                artMin = Vector2.Min(artMin, spriteMin);
                artMax = Vector2.Max(artMax, spriteMax);
            }
        }

        private void BuildDensityField()
        {
            int width = stageData.gridWidth;
            int height = stageData.gridHeight;
            densityField = new float[width + 2, height + 2];

            for (int x = 0; x < width + 2; x++)
            {
                for (int y = 0; y < height + 2; y++)
                {
                    bool insideGrid = x >= 1 && x <= width && y >= 1 && y <= height;
                    densityField[x, y] = insideGrid ? GetTileDustDensity(stageData.GetTile(x - 1, y - 1)) : 1f;
                }
            }
        }

        private static float GetTileDustDensity(TileType tile)
        {
            return tile switch
            {
                TileType.HighGround => 0.6f,
                TileType.Blocked => 0.85f,
                _ => 0.3f // Enemy lane, melee ground, spawns, exits, traps, holes
            };
        }

        /// <summary>0-1 dust thickness at a world position: 1 off the grid, lowest over the enemy lane.</summary>
        private float GetDustDensity(Vector2 worldPosition)
        {
            int width = stageData.gridWidth;
            int height = stageData.gridHeight;

            float fx = Mathf.Clamp((worldPosition.x - gridMin.x) / cellSize - 0.5f, -1f, width) + 1f;
            float fy = Mathf.Clamp((worldPosition.y - gridMin.y) / cellSize - 0.5f, -1f, height) + 1f;

            int x0 = Mathf.Min(Mathf.FloorToInt(fx), width);
            int y0 = Mathf.Min(Mathf.FloorToInt(fy), height);
            float tx = fx - x0;
            float ty = fy - y0;

            float bottom = Mathf.Lerp(densityField[x0, y0], densityField[x0 + 1, y0], tx);
            float top = Mathf.Lerp(densityField[x0, y0 + 1], densityField[x0 + 1, y0 + 1], tx);
            return Mathf.Lerp(bottom, top, ty);
        }

        private Vector3 Snap(float x, float y) => new Vector3(
            Mathf.Round(x / pixelSize) * pixelSize,
            Mathf.Round(y / pixelSize) * pixelSize,
            0f);

        // ---------- Scene objects ----------

        /// <summary>Clips the storm to the map artwork so nothing draws over the void around it.</summary>
        private void CreateArtMask()
        {
            var maskObject = new GameObject("Stage3SandstormMask");
            maskObject.transform.SetParent(transform, false);
            maskObject.transform.position = (Vector3)((artMin + artMax) * 0.5f);
            maskObject.transform.localScale = new Vector3(artMax.x - artMin.x, artMax.y - artMin.y, 1f);

            var mask = maskObject.AddComponent<SpriteMask>();
            mask.sprite = GetWhiteSprite();
        }

        private void CreateHaze()
        {
            var hazeObject = new GameObject("Stage3SandHaze");
            hazeObject.transform.SetParent(transform, false);
            hazeObject.transform.position = (Vector3)((artMin + artMax) * 0.5f);
            hazeObject.transform.localScale = new Vector3(artMax.x - artMin.x, artMax.y - artMin.y, 1f);

            haze = hazeObject.AddComponent<SpriteRenderer>();
            haze.sprite = GetWhiteSprite();
            GridSorting.PlaceOnGround(haze);
            haze.sortingOrder = HazeSortingOrder;
            haze.color = Color.clear;
        }

        private void CreateClouds()
        {
            Sprite[] sprites = GetCloudSprites();

            for (int l = 0; l < DustLayers.Length; l++)
            {
                DustLayer layer = DustLayers[l];
                var layerRoot = new GameObject($"Stage3Dust_{layer.Name}");
                layerRoot.transform.SetParent(transform, false);

                for (int i = 0; i < layer.Count; i++)
                {
                    var cloudObject = new GameObject($"DustCloud_{i}");
                    cloudObject.transform.SetParent(layerRoot.transform, false);
                    cloudObject.transform.localScale = Vector3.one * cellSize;

                    var renderer = cloudObject.AddComponent<SpriteRenderer>();
                    if (layer.SortingOrder == GroundSortingOrder) GridSorting.PlaceOnGround(renderer);
                    renderer.sortingOrder = layer.SortingOrder;
                    renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

                    var cloud = new DustCloud { Layer = layer, Transform = cloudObject.transform, Renderer = renderer };
                    ResetCloud(cloud, sprites, true);
                    clouds.Add(cloud);
                }
            }
        }

        private void ResetCloud(DustCloud cloud, Sprite[] sprites, bool anywhere)
        {
            DustLayer layer = cloud.Layer;
            Sprite sprite = sprites[Random.Range(0, sprites.Length)];
            cloud.Renderer.sprite = sprite;
            cloud.Renderer.flipX = Random.value < 0.5f;
            cloud.HalfWidth = sprite.bounds.extents.x * cellSize;

            // Blown left to right; new clouds roll in from the left edge.
            cloud.X = anywhere
                ? Random.Range(artMin.x - cloud.HalfWidth, artMax.x + cloud.HalfWidth)
                : artMin.x - cloud.HalfWidth;
            cloud.BaseY = Random.Range(artMin.y, artMax.y);
            cloud.BaseAlpha = Random.Range(layer.AlphaRange.x, layer.AlphaRange.y);
            cloud.Speed = Random.Range(layer.SpeedRange.x, layer.SpeedRange.y) * cellSize;
            cloud.Phase = Random.Range(0f, Mathf.PI * 2f);
            cloud.FadeIn = anywhere ? 1f : 0f;
        }

        private void CreateStreaks()
        {
            var root = new GameObject("Stage3WindStreaks");
            root.transform.SetParent(transform, false);
            Sprite sprite = GetWhiteSprite();

            for (int i = 0; i < MaxStreaks; i++)
            {
                var streakObject = new GameObject($"Streak_{i}");
                streakObject.transform.SetParent(root.transform, false);

                var renderer = streakObject.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = StreakSortingOrder;
                renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                streakObject.SetActive(false);

                var streak = new Streak { Transform = streakObject.transform, Renderer = renderer };
                streaks.Add(streak);
                if (i < WantedStreaks()) SpawnStreak(streak, true);
            }
        }

        private void CreateGrains()
        {
            var root = new GameObject("Stage3SandGrains");
            root.transform.SetParent(transform, false);
            Sprite sprite = GetWhiteSprite();

            for (int i = 0; i < MaxGrains; i++)
            {
                var grainObject = new GameObject($"Grain_{i}");
                grainObject.transform.SetParent(root.transform, false);
                grainObject.transform.localScale = new Vector3(pixelSize, pixelSize, 1f);

                var renderer = grainObject.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                GridSorting.PlaceOnGround(renderer);
                renderer.sortingOrder = GroundSortingOrder;
                renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                grainObject.SetActive(false);

                grains.Add(new Grain { Transform = grainObject.transform, Renderer = renderer });
            }
        }

        private int WantedStreaks() => Mathf.RoundToInt(MaxStreaks * Mathf.Lerp(0.12f, 1f, intensity));

        private int WantedGrains() => Mathf.RoundToInt(MaxGrains * Mathf.Lerp(0.05f, 1f, intensity));

        // ---------- Per-frame updates ----------

        private void UpdateClouds(float deltaTime)
        {
            Sprite[] sprites = GetCloudSprites();
            float speedFactor = WindSpeedFactor;
            float thickness = Mathf.Lerp(0.55f, 1f, intensity);
            float time = Time.time;

            for (int i = 0; i < clouds.Count; i++)
            {
                DustCloud cloud = clouds[i];
                cloud.X += cloud.Speed * speedFactor * deltaTime;
                if (cloud.X > artMax.x + cloud.HalfWidth)
                {
                    ResetCloud(cloud, sprites, false);
                }

                // Clouds roll: a slow bob, out of step with their neighbours.
                float y = cloud.BaseY + Mathf.Sin(time * 0.8f + cloud.Phase) * cellSize * 0.06f;
                cloud.Transform.position = Snap(cloud.X, y);

                cloud.FadeIn = Mathf.Min(1f, cloud.FadeIn + deltaTime * 0.6f);
                float density = Mathf.Pow(GetDustDensity(new Vector2(cloud.X, y)), cloud.Layer.DensityExponent);
                float breathe = 0.85f + 0.15f * Mathf.Sin(time * 1.3f + cloud.Phase * 2f);

                Color color = Color.white;
                color.a = cloud.BaseAlpha * density * thickness * breathe * cloud.FadeIn;
                cloud.Renderer.color = color;
            }
        }

        private void UpdateStreaks(float deltaTime)
        {
            int wanted = WantedStreaks();
            float speedFactor = WindSpeedFactor;

            for (int i = 0; i < streaks.Count; i++)
            {
                Streak streak = streaks[i];
                if (!streak.Active)
                {
                    if (i < wanted) SpawnStreak(streak, false);
                    continue;
                }

                streak.Position.x += streak.Speed * speedFactor * deltaTime;
                streak.Position.y -= streak.Drop * deltaTime;
                float wobble = Mathf.Sin(Time.time * 6f + streak.Phase) * pixelSize * 1.5f;

                if (streak.Position.x - streak.Length > artMax.x || streak.Position.y < artMin.y)
                {
                    // Streaks past the current count stop here, so a dying gust thins out gradually.
                    if (i < wanted) SpawnStreak(streak, false);
                    else Deactivate(streak);
                    continue;
                }

                streak.Transform.position = Snap(streak.Position.x - streak.Length * 0.5f, streak.Position.y + wobble);
                Color color = StreakColor;
                color.a = streak.Alpha * Mathf.Lerp(0.6f, 1f, intensity);
                streak.Renderer.color = color;
            }
        }

        private void SpawnStreak(Streak streak, bool anywhere)
        {
            streak.Active = true;
            streak.Transform.gameObject.SetActive(true);

            float lengthPixels = Mathf.Round(Random.Range(3f, 6f) + intensity * Random.Range(2f, 6f));
            streak.Length = lengthPixels * pixelSize;
            streak.Position = new Vector2(
                anywhere ? Random.Range(artMin.x, artMax.x) : artMin.x - Random.Range(0f, cellSize * 2f),
                Random.Range(artMin.y, artMax.y + cellSize));
            streak.Speed = Random.Range(5f, 8f) * cellSize;
            streak.Drop = Random.Range(0.2f, 0.6f) * cellSize;
            streak.Alpha = Random.Range(0.25f, 0.55f);
            streak.Phase = Random.Range(0f, Mathf.PI * 2f);
            streak.Transform.localScale = new Vector3(streak.Length, pixelSize, 1f);
        }

        private static void Deactivate(Streak streak)
        {
            streak.Active = false;
            streak.Transform.gameObject.SetActive(false);
        }

        private void UpdateGrains(float deltaTime)
        {
            int wanted = WantedGrains();
            float speedFactor = WindSpeedFactor;

            for (int i = 0; i < grains.Count; i++)
            {
                Grain grain = grains[i];
                if (!grain.Active)
                {
                    if (i < wanted) SpawnGrain(grain);
                    continue;
                }

                grain.Age += deltaTime;
                if (grain.Age >= grain.Life)
                {
                    if (i < wanted) SpawnGrain(grain);
                    else
                    {
                        grain.Active = false;
                        grain.Transform.gameObject.SetActive(false);
                    }
                    continue;
                }

                // Saltation: grains skip along the ground in little hops.
                float x = grain.Origin.x + grain.Speed * speedFactor * grain.Age;
                float hop = Mathf.Abs(Mathf.Sin(grain.Age * grain.HopRate)) * grain.HopHeight;
                grain.Transform.position = Snap(x, grain.Origin.y + hop);

                float t = grain.Age / grain.Life;
                Color color = GrainColor;
                color.a = 0.85f * Mathf.Min(1f, t * 6f) * Mathf.Min(1f, (1f - t) * 4f);
                grain.Renderer.color = color;
            }
        }

        private void SpawnGrain(Grain grain)
        {
            grain.Active = true;
            grain.Transform.gameObject.SetActive(true);
            grain.Origin = new Vector2(Random.Range(artMin.x - cellSize, artMax.x), Random.Range(artMin.y, artMax.y));
            grain.Speed = Random.Range(1.5f, 3f) * cellSize;
            grain.HopHeight = Random.Range(2f, 5f) * pixelSize;
            grain.HopRate = Random.Range(7f, 12f);
            grain.Age = 0f;
            grain.Life = Random.Range(0.6f, 1.4f);
        }

        // ---------- Generated sprites ----------

        private static Sprite GetWhiteSprite()
        {
            if (whiteSprite == null)
            {
                whiteSprite = Sprite.Create(
                    Texture2D.whiteTexture,
                    new Rect(0f, 0f, 1f, 1f),
                    new Vector2(0.5f, 0.5f),
                    1f);
            }

            return whiteSprite;
        }

        private static Sprite[] GetCloudSprites()
        {
            // Domain reload is off (Enter Play Mode options), so this array outlives the sprites in it:
            // runtime sprites are destroyed when play mode ends. Check the elements, not just the array.
            if (cloudSprites != null && System.Array.TrueForAll(cloudSprites, sprite => sprite != null)) return cloudSprites;

            cloudSprites = new Sprite[CloudSizes.Length];
            for (int i = 0; i < CloudSizes.Length; i++)
            {
                cloudSprites[i] = CreateCloudSprite(CloudSizes[i].x, CloudSizes[i].y, 4321 + i * 613);
            }

            return cloudSprites;
        }

        /// <summary>
        /// A wind-stretched pixel-art dust cloud. A lumpy body is eroded by stretched noise into streaky
        /// wisps, then quantised into bands: a solid core, a lighter body, a checker dither and a sparse dither
        /// at the edge. Lit from above: top surfaces take the light tone, undersides the shadow tone.
        /// </summary>
        private static Sprite CreateCloudSprite(int width, int height, int seed)
        {
            var random = new System.Random(seed);
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

            var lumps = new Vector4[5];
            for (int i = 0; i < lumps.Length; i++)
            {
                lumps[i] = new Vector4(Range(0.22f, 0.78f), Range(0.38f, 0.6f), Range(0.14f, 0.26f), Range(0.2f, 0.32f));
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
                    float body = 1f - Mathf.Exp(-shape * 1.2f);

                    // Noise stretched along the wind, so the cloud breaks into horizontal wisps.
                    float streaks = Mathf.PerlinNoise(ox + u * 3f, oy + v * 9f) * 0.65f +
                                    Mathf.PerlinNoise(ox + u * 7f + 13f, oy + v * 18f + 5f) * 0.35f;
                    float d = body * Mathf.Lerp(0.55f, 1.15f, streaks);

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
                    if (d > 0.62f) alpha = 0.9f;
                    else if (d > 0.44f) alpha = 0.72f;
                    else if (d > 0.28f) alpha = (x + y) % 2 == 0 ? 0.6f : 0f;
                    else if (d > 0.18f) alpha = x % 2 == 0 && y % 2 == 0 ? 0.45f : 0f;
                    else alpha = 0f;

                    if (alpha <= 0f)
                    {
                        pixels[y * width + x] = Color.clear;
                        continue;
                    }

                    float above = density[x, Mathf.Min(height - 1, y + 2)];
                    float below = density[x, Mathf.Max(0, y - 2)];
                    Color tone = d - above > 0.06f ? DustLight : below < d - 0.06f ? DustShadow : DustMid;
                    tone.a = alpha;
                    pixels[y * width + x] = tone;
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = $"Stage3 Dust Cloud {width}x{height}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), ArtPixelsPerTile);
        }
    }
}
