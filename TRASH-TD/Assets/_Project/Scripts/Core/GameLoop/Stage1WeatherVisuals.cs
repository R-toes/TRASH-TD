using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;

namespace TrashTD.Core.GameLoop
{
    /// <summary>
    /// Stage 1 (Blackwater Facilities) atmosphere. Visual only: no gameplay effect.
    /// - Volumetric mist in two layers of shaded, billowing puffs that drift left with the wind:
    ///   a back layer on the ground beneath the units, and a front layer above them that can
    ///   roll over operators and enemies. Both follow a per-tile density field: thickest over the
    ///   off-grid backdrop and the map edges, thin over the enemy lane. The front layer thins out
    ///   much faster, so the path stays readable. A fog bank sits over the backdrop row.
    /// - It rains almost all the time: long light spells and medium downpours, with the odd
    ///   short dry break. Slanted pixel streaks above the units, splashes on the ground,
    ///   a slight darkening of the map and thicker mist. Intensity eases between states.
    /// Runs on scaled time, so it freezes while paused like the Stage 6 acid rain.
    /// </summary>
    public sealed class Stage1WeatherVisuals : MonoBehaviour
    {
        private const int RainTintSortingOrder = 2; // Map artwork is 1
        private const int GroundSortingOrder = 3;   // Ground units are 4-5, air shadows 6
        private const int FrontMistSortingOrder = 11; // Above ground units and foreground art (10), below air units (12)
        private const int RainSortingOrder = 24;    // Above units, below projectiles (40)

        private const int MaxRainDrops = 220;

        private const float LightRainIntensity = 0.35f;
        private const float MediumRainIntensity = 1f;
        private const float IntensityChangePerSecond = 0.12f; // Light <-> medium takes ~5 s
        private const float DryBreakChance = 0.3f;
        private const float RainMistBoost = 0.35f;            // Mist thickens by up to 35% in heavy rain
        private const float RainTintMaxAlpha = 0.16f;

        private const float SplashDuration = 0.16f;
        private const int MistSpriteVariants = 3;

        private static readonly Color MistColor = new Color(0.82f, 0.87f, 0.90f, 1f);
        private static readonly Color RainColor = new Color(0.72f, 0.82f, 0.93f, 0.42f);
        private static readonly Color SplashColor = new Color(0.80f, 0.88f, 0.96f, 0.5f);
        private static readonly Color RainTintColor = new Color(0.04f, 0.07f, 0.11f, 1f);

        private enum Weather { Clear, LightRain, MediumRain }

        /// <summary>One depth layer of mist puffs. Sizes are in cells, speeds in cells per second.</summary>
        private sealed class MistLayer
        {
            public string Name;
            public int Count;
            public int SortingOrder;
            public Vector2 WidthRange;
            public Vector2 HeightRange;
            public Vector2 AlphaRange;
            public Vector2 SpeedRange;
            public float DensityExponent; // > 1 thins the layer out faster away from the backdrop
            public float BackdropChance;  // Share of puffs that start in the backdrop band
        }

        private static readonly MistLayer[] MistLayers =
        {
            new MistLayer
            {
                Name = "Back", Count = 26, SortingOrder = GroundSortingOrder,
                WidthRange = new Vector2(3f, 6.5f), HeightRange = new Vector2(1.3f, 2.4f),
                AlphaRange = new Vector2(0.4f, 0.6f), SpeedRange = new Vector2(0.06f, 0.14f),
                DensityExponent = 1f, BackdropChance = 0.4f
            },
            new MistLayer
            {
                Name = "Front", Count = 18, SortingOrder = FrontMistSortingOrder,
                WidthRange = new Vector2(2.5f, 5.5f), HeightRange = new Vector2(1.1f, 2.2f),
                AlphaRange = new Vector2(0.35f, 0.55f), SpeedRange = new Vector2(0.14f, 0.28f),
                DensityExponent = 2.2f, BackdropChance = 0.55f
            }
        };

        private sealed class MistPuff
        {
            public MistLayer Layer;
            public Transform Transform;
            public SpriteRenderer Renderer;
            public Vector3 BaseScale;
            public float BaseY;
            public float BaseAlpha;
            public float Speed;
            public float HalfWidth;
            public float Phase;
            public float BillowSpeed;
            public float FadeIn;
        }

        private sealed class RainDrop
        {
            public Transform Transform;
            public SpriteRenderer Renderer;
            public bool Active;
            public float LandY;
            public float SplashTimer; // > 0 while showing its splash
        }

        private GridManager gridManager;
        private StageData stageData;

        private float cellSize;
        private float pixelSize;
        private Vector2 artMin;
        private Vector2 artMax;
        private Vector2 gridMin;
        private float gridTopY;
        private float[,] mistDensityField; // Per cell, with a 1-tile border ring of full density

        private readonly List<MistPuff> mistPuffs = new List<MistPuff>();
        private readonly List<RainDrop> rainDrops = new List<RainDrop>();
        private SpriteRenderer rainTint;
        private SpriteRenderer backdropFogBank;

        private readonly Queue<(Weather weather, float duration)> forecast = new Queue<(Weather, float)>();
        private Weather weather = Weather.LightRain;
        private float weatherTimeLeft;
        private float rainIntensity;
        private Vector2 rainVelocity;

        private static Sprite whiteSprite;
        private static Sprite[] mistSprites;
        private static Sprite fogBankSprite;

        public void Initialize(GridManager grid, StageData stage)
        {
            gridManager = grid;
            stageData = stage;
            if (gridManager == null || stageData == null) return;

            cellSize = gridManager.CellSize;
            pixelSize = cellSize / 32f;
            CalculateBounds();
            BuildMistDensityField();

            CreateArtMask();
            CreateRainTint();
            CreateBackdropFogBank();
            CreateMist();
            CreateRainDrops();

            // The stage opens already raining.
            rainIntensity = LightRainIntensity;
            UpdateRainVelocity();
            AdvanceForecast();
            PrefillRain();
        }

        private void Update()
        {
            if (gridManager == null) return;

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            UpdateWeather(deltaTime);
            UpdateMist(deltaTime);
            UpdateRain(deltaTime);

            if (rainTint != null)
            {
                Color tint = RainTintColor;
                tint.a = RainTintMaxAlpha * rainIntensity;
                rainTint.color = tint;
            }
        }

        // ---------- Weather cycle ----------

        private void UpdateWeather(float deltaTime)
        {
            weatherTimeLeft -= deltaTime;
            if (weatherTimeLeft <= 0f) AdvanceForecast();

            float target = weather switch
            {
                Weather.LightRain => LightRainIntensity,
                Weather.MediumRain => MediumRainIntensity,
                _ => 0f
            };
            rainIntensity = Mathf.MoveTowards(rainIntensity, target, IntensityChangePerSecond * deltaTime);
            UpdateRainVelocity();
        }

        /// <summary>Heavier rain falls faster and slants more, blown the same way as the mist.</summary>
        private void UpdateRainVelocity()
        {
            rainVelocity = new Vector2(
                Mathf.Lerp(-0.8f, -1.8f, rainIntensity),
                Mathf.Lerp(-9f, -12.5f, rainIntensity)) * cellSize;
        }

        private void AdvanceForecast()
        {
            if (forecast.Count == 0) PlanNextWeather();

            (weather, weatherTimeLeft) = forecast.Dequeue();
        }

        /// <summary>
        /// Mostly rain: a long light spell, then a medium downpour. Now and then the rain tapers off
        /// for a short dry break (the fade out and back in takes ~3 s each, so it's only dry for a few seconds).
        /// </summary>
        private void PlanNextWeather()
        {
            forecast.Enqueue((Weather.LightRain, Random.Range(30f, 60f)));
            forecast.Enqueue((Weather.MediumRain, Random.Range(25f, 50f)));

            if (Random.value < DryBreakChance)
            {
                forecast.Enqueue((Weather.LightRain, Random.Range(10f, 20f)));
                forecast.Enqueue((Weather.Clear, Random.Range(6f, 12f)));
            }
        }

        // ---------- Bounds and mist density ----------

        private void CalculateBounds()
        {
            gridMin = gridManager.GridToWorldPosition(0, 0) - new Vector3(cellSize * 0.5f, cellSize * 0.5f, 0f);
            Vector2 gridMax = gridMin + new Vector2(stageData.gridWidth, stageData.gridHeight) * cellSize;
            gridTopY = gridMax.y;
            artMin = gridMin;
            artMax = gridMax;

            // The artwork can be larger than the grid (Stage 1 has a backdrop row above it).
            if (stageData.mapVisualSprite != null)
            {
                float pixelPerTile = stageData.visualTilePixelSize > 0 ? stageData.visualTilePixelSize : 32f;
                Vector2 spriteMin = gridMin + stageData.visualTileOffset * cellSize;
                Vector2 spriteMax = spriteMin + stageData.mapVisualSprite.rect.size / pixelPerTile * cellSize;
                artMin = Vector2.Min(artMin, spriteMin);
                artMax = Vector2.Max(artMax, spriteMax);
            }
        }

        private void BuildMistDensityField()
        {
            int width = stageData.gridWidth;
            int height = stageData.gridHeight;
            mistDensityField = new float[width + 2, height + 2];

            for (int x = 0; x < width + 2; x++)
            {
                for (int y = 0; y < height + 2; y++)
                {
                    bool insideGrid = x >= 1 && x <= width && y >= 1 && y <= height;
                    mistDensityField[x, y] = insideGrid ? GetTileMistDensity(stageData.GetTile(x - 1, y - 1)) : 1f;
                }
            }
        }

        private static float GetTileMistDensity(TileType tile)
        {
            return tile switch
            {
                TileType.HighGround => 0.55f,
                TileType.Blocked => 0.85f,
                _ => 0.3f // Enemy lane, melee ground, spawns, exits, traps, holes
            };
        }

        /// <summary>0-1 mist thickness at a world position: 1 off the grid, lowest over the enemy lane.</summary>
        private float GetMistDensity(Vector2 worldPosition)
        {
            int width = stageData.gridWidth;
            int height = stageData.gridHeight;

            // Field coordinates where integer values are cell centres (border ring at -1 and width/height).
            float fx = Mathf.Clamp((worldPosition.x - gridMin.x) / cellSize - 0.5f, -1f, width) + 1f;
            float fy = Mathf.Clamp((worldPosition.y - gridMin.y) / cellSize - 0.5f, -1f, height) + 1f;

            int x0 = Mathf.Min(Mathf.FloorToInt(fx), width);
            int y0 = Mathf.Min(Mathf.FloorToInt(fy), height);
            float tx = fx - x0;
            float ty = fy - y0;

            float bottom = Mathf.Lerp(mistDensityField[x0, y0], mistDensityField[x0 + 1, y0], tx);
            float top = Mathf.Lerp(mistDensityField[x0, y0 + 1], mistDensityField[x0 + 1, y0 + 1], tx);
            return Mathf.Lerp(bottom, top, ty);
        }

        // ---------- Scene objects ----------

        /// <summary>Clips the mist and rain to the map artwork so nothing draws over the void around it.</summary>
        private void CreateArtMask()
        {
            var maskObject = new GameObject("Stage1WeatherMask");
            maskObject.transform.SetParent(transform, false);
            maskObject.transform.position = (Vector3)((artMin + artMax) * 0.5f);
            maskObject.transform.localScale = new Vector3(artMax.x - artMin.x, artMax.y - artMin.y, 1f);

            var mask = maskObject.AddComponent<SpriteMask>();
            mask.sprite = GetWhiteSprite();
        }

        private void CreateRainTint()
        {
            var tintObject = new GameObject("Stage1RainTint");
            tintObject.transform.SetParent(transform, false);
            tintObject.transform.position = (Vector3)((artMin + artMax) * 0.5f);
            tintObject.transform.localScale = new Vector3(artMax.x - artMin.x, artMax.y - artMin.y, 1f);

            rainTint = tintObject.AddComponent<SpriteRenderer>();
            rainTint.sprite = GetWhiteSprite();
            rainTint.sortingOrder = RainTintSortingOrder;
            rainTint.color = Color.clear;
        }

        /// <summary>A soft fog band over the backdrop above the grid, thickest at the top of the map.</summary>
        private void CreateBackdropFogBank()
        {
            float bottom = Mathf.Min(gridTopY, artMax.y - cellSize) - cellSize * 0.5f;
            float height = artMax.y - bottom;

            var bankObject = new GameObject("Stage1BackdropFogBank");
            bankObject.transform.SetParent(transform, false);
            bankObject.transform.position = new Vector3((artMin.x + artMax.x) * 0.5f, bottom + height * 0.5f, 0f);
            Sprite sprite = GetFogBankSprite();
            bankObject.transform.localScale = new Vector3(
                (artMax.x - artMin.x) / sprite.bounds.size.x,
                height / sprite.bounds.size.y,
                1f);

            backdropFogBank = bankObject.AddComponent<SpriteRenderer>();
            backdropFogBank.sprite = sprite;
            backdropFogBank.sortingOrder = FrontMistSortingOrder;
            backdropFogBank.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
        }

        private void CreateMist()
        {
            Sprite[] sprites = GetMistSprites();

            for (int l = 0; l < MistLayers.Length; l++)
            {
                MistLayer layer = MistLayers[l];
                var layerRoot = new GameObject($"Stage1Mist_{layer.Name}");
                layerRoot.transform.SetParent(transform, false);

                for (int i = 0; i < layer.Count; i++)
                {
                    var puffObject = new GameObject($"MistPuff_{i}");
                    puffObject.transform.SetParent(layerRoot.transform, false);

                    var renderer = puffObject.AddComponent<SpriteRenderer>();
                    renderer.sprite = sprites[i % sprites.Length];
                    renderer.sortingOrder = layer.SortingOrder;
                    renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

                    var puff = new MistPuff { Layer = layer, Transform = puffObject.transform, Renderer = renderer };
                    ResetMistPuff(puff, true);
                    mistPuffs.Add(puff);
                }
            }
        }

        private void ResetMistPuff(MistPuff puff, bool anywhere)
        {
            MistLayer layer = puff.Layer;
            float width = Random.Range(layer.WidthRange.x, layer.WidthRange.y) * cellSize;
            float height = Random.Range(layer.HeightRange.x, layer.HeightRange.y) * cellSize;
            puff.HalfWidth = width * 0.5f;

            // The mist sprites are 1 x 0.5 units.
            puff.BaseScale = new Vector3(width, height * 2f, 1f);
            puff.Transform.localScale = puff.BaseScale;
            puff.Renderer.flipX = Random.value < 0.5f;

            // Drift left with the wind; new puffs roll in from the right edge.
            float x = anywhere
                ? Random.Range(artMin.x - puff.HalfWidth, artMax.x + puff.HalfWidth)
                : artMax.x + puff.HalfWidth;
            puff.BaseY = PickMistY(layer.BackdropChance);
            puff.Transform.position = new Vector3(x, puff.BaseY, 0f);

            puff.BaseAlpha = Random.Range(layer.AlphaRange.x, layer.AlphaRange.y);
            puff.Speed = Random.Range(layer.SpeedRange.x, layer.SpeedRange.y) * cellSize;
            puff.Phase = Random.Range(0f, Mathf.PI * 2f);
            puff.BillowSpeed = Random.Range(0.25f, 0.6f);
            puff.FadeIn = anywhere ? 1f : 0f;
        }

        /// <summary>Favours the backdrop above the grid and the bottom edge, but can land anywhere.</summary>
        private float PickMistY(float backdropChance)
        {
            float roll = Random.value;
            float backdropBottom = Mathf.Min(gridTopY, artMax.y - cellSize) - cellSize * 0.3f;

            if (roll < backdropChance) return Random.Range(backdropBottom, artMax.y);
            if (roll < backdropChance + 0.15f) return Random.Range(artMin.y, artMin.y + cellSize * 0.8f);
            return Random.Range(artMin.y, artMax.y);
        }

        private void CreateRainDrops()
        {
            var rainRoot = new GameObject("Stage1Rain");
            rainRoot.transform.SetParent(transform, false);
            Sprite sprite = GetWhiteSprite();

            for (int i = 0; i < MaxRainDrops; i++)
            {
                var dropObject = new GameObject($"RainDrop_{i}");
                dropObject.transform.SetParent(rainRoot.transform, false);

                var renderer = dropObject.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                dropObject.SetActive(false);

                rainDrops.Add(new RainDrop { Transform = dropObject.transform, Renderer = renderer });
            }
        }

        /// <summary>Scatters the opening shower's drops through the air so the stage doesn't start with an empty sky.</summary>
        private void PrefillRain()
        {
            int wantedDrops = Mathf.RoundToInt(rainIntensity * MaxRainDrops);
            for (int i = 0; i < wantedDrops && i < rainDrops.Count; i++)
            {
                SpawnRainDrop(rainDrops[i], true);
            }
        }

        // ---------- Per-frame updates ----------

        private void UpdateMist(float deltaTime)
        {
            float mistBoost = 1f + RainMistBoost * rainIntensity;
            float time = Time.time;

            if (backdropFogBank != null)
            {
                Color bank = MistColor;
                bank.a = (0.3f + 0.06f * Mathf.Sin(time * 0.35f)) * mistBoost;
                backdropFogBank.color = bank;
            }

            for (int i = 0; i < mistPuffs.Count; i++)
            {
                MistPuff puff = mistPuffs[i];
                Vector3 position = puff.Transform.position;
                position.x -= puff.Speed * deltaTime;
                if (position.x < artMin.x - puff.HalfWidth)
                {
                    ResetMistPuff(puff, false);
                    position = puff.Transform.position;
                }

                // Billowing: the puff slowly swells, flattens and bobs, out of step with its neighbours.
                float wave = time * puff.BillowSpeed + puff.Phase;
                position.y = puff.BaseY + Mathf.Sin(wave * 0.7f) * cellSize * 0.08f;
                puff.Transform.position = position;
                puff.Transform.localScale = new Vector3(
                    puff.BaseScale.x * (1f + 0.06f * Mathf.Sin(wave)),
                    puff.BaseScale.y * (1f + 0.09f * Mathf.Sin(wave * 1.3f + 1f)),
                    1f);
                // A slow sway so the wisps seem to curl rather than slide
                puff.Transform.rotation = Quaternion.Euler(0f, 0f, 4f * Mathf.Sin(wave * 0.6f + puff.Phase));

                puff.FadeIn = Mathf.Min(1f, puff.FadeIn + deltaTime * 0.5f);

                float density = Mathf.Pow(GetMistDensity(position), puff.Layer.DensityExponent);
                float breathe = 0.8f + 0.2f * Mathf.Sin(wave * 0.9f + 2f);
                Color color = MistColor;
                color.a = puff.BaseAlpha * density * mistBoost * breathe * puff.FadeIn;
                puff.Renderer.color = color;
            }
        }

        private void UpdateRain(float deltaTime)
        {
            int wantedDrops = Mathf.RoundToInt(rainIntensity * MaxRainDrops);

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
                    Color splash = SplashColor;
                    splash.a *= Mathf.Clamp01(drop.SplashTimer / SplashDuration);
                    drop.Renderer.color = splash;

                    if (drop.SplashTimer <= 0f)
                    {
                        // Drops past the current count stop here, so the rain thins out gradually.
                        if (i < wantedDrops) SpawnRainDrop(drop, false);
                        else DeactivateRainDrop(drop);
                    }
                    continue;
                }

                Vector3 position = drop.Transform.position;
                position.x += rainVelocity.x * deltaTime;
                position.y += rainVelocity.y * deltaTime;
                drop.Transform.position = position;

                if (position.y <= drop.LandY) StartSplash(drop);
            }
        }

        private void SpawnRainDrop(RainDrop drop, bool midAir)
        {
            drop.Active = true;
            drop.SplashTimer = 0f;
            drop.Transform.gameObject.SetActive(true);

            // Drops fall diagonally, so spawn them further right to cover the whole map.
            float fallTime = (artMax.y - artMin.y) / Mathf.Max(0.01f, -rainVelocity.y);
            float slant = -rainVelocity.x * fallTime;
            float startY = midAir
                ? Random.Range(artMin.y, artMax.y + cellSize)
                : artMax.y + Random.Range(0f, cellSize);
            drop.Transform.position = new Vector3(Random.Range(artMin.x, artMax.x + slant), startY, 0f);
            drop.LandY = Random.Range(artMin.y, Mathf.Min(artMax.y, startY));

            float lengthPixels = Mathf.Round(Mathf.Lerp(3f, 5f, rainIntensity));
            drop.Transform.localScale = new Vector3(pixelSize, pixelSize * lengthPixels, 1f);
            drop.Transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(rainVelocity.x, -rainVelocity.y) * Mathf.Rad2Deg);
            drop.Renderer.sortingOrder = RainSortingOrder;
            drop.Renderer.color = RainColor;
        }

        /// <summary>The drop becomes a short flat dash on the ground, beneath the units.</summary>
        private void StartSplash(RainDrop drop)
        {
            drop.SplashTimer = SplashDuration;
            Vector3 position = drop.Transform.position;
            position.y = drop.LandY;
            drop.Transform.position = position;
            drop.Transform.rotation = Quaternion.identity;
            drop.Transform.localScale = new Vector3(pixelSize * 3f, pixelSize, 1f);
            drop.Renderer.sortingOrder = GroundSortingOrder;
            drop.Renderer.color = SplashColor;
        }

        private static void DeactivateRainDrop(RainDrop drop)
        {
            drop.Active = false;
            drop.SplashTimer = 0f;
            drop.Transform.gameObject.SetActive(false);
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

        private static Sprite[] GetMistSprites()
        {
            // Domain reload is off (Enter Play Mode options), so this array outlives the sprites in it:
            // runtime sprites are destroyed when play mode ends. Check the elements, not just the array.
            if (mistSprites != null && System.Array.TrueForAll(mistSprites, sprite => sprite != null)) return mistSprites;

            mistSprites = new Sprite[MistSpriteVariants];
            for (int i = 0; i < MistSpriteVariants; i++)
            {
                mistSprites[i] = CreateMistSprite(1234 + i * 977);
            }

            return mistSprites;
        }

        /// <summary>
        /// A 128x64 wisp of smoke. A soft, lumpy falloff (never fully opaque, so overlapping puffs build up
        /// depth instead of reading as solid shapes) is eroded by domain-warped noise into curling tendrils
        /// and holes. It is lit from above: billow tops are bright, undersides a cool shadow.
        /// </summary>
        private static Sprite CreateMistSprite(int seed)
        {
            const int width = 128;
            const int height = 64;
            const float MaxAlpha = 0.7f;
            var random = new System.Random(seed);
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

            // Lump centres and radii in 0-1 texture space (x, y, radius x, radius y).
            var lumps = new Vector4[5];
            for (int i = 0; i < lumps.Length; i++)
            {
                lumps[i] = new Vector4(Range(0.25f, 0.75f), Range(0.38f, 0.58f), Range(0.12f, 0.22f), Range(0.18f, 0.3f));
            }

            float ox = Range(0f, 100f);
            float oy = Range(0f, 100f);

            float Fbm(float x, float y)
            {
                return Mathf.PerlinNoise(x, y) * 0.5f +
                       Mathf.PerlinNoise(x * 2.03f + 17f, y * 2.03f + 5f) * 0.3f +
                       Mathf.PerlinNoise(x * 4.1f + 41f, y * 4.1f + 23f) * 0.2f;
            }

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

                    // Soft saturation: thick in the middle but with a gradient everywhere, no flat plateau.
                    float body = 1f - Mathf.Exp(-shape * 1.1f);

                    // Domain warp bends the noise into curls, like smoke being stirred.
                    float warpX = Fbm(ox + u * 2.5f, oy + v * 1.4f) - 0.5f;
                    float warpY = Fbm(ox + u * 2.5f + 31f, oy + v * 1.4f + 7f) - 0.5f;
                    float smoke = Fbm(ox + u * 5f + warpX * 1.6f, oy + v * 2.6f + warpY * 1.6f);

                    // Erode: thin areas break into tendrils and holes, thick areas stay hazy.
                    float erosion = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.32f, 0.72f, smoke + body * 0.25f));
                    float d = body * body * erosion;

                    // Fade out towards the texture edges so the puff never shows a hard border.
                    float edgeX = Mathf.Min(u, 1f - u) * 2f;
                    float edgeY = Mathf.Min(v, 1f - v) * 2f;
                    d *= Mathf.SmoothStep(0f, 1f, Mathf.Min(edgeX * 2.5f, 1f)) * Mathf.SmoothStep(0f, 1f, Mathf.Min(edgeY * 2.5f, 1f));
                    density[x, y] = d;
                }
            }

            Color shadow = new Color(0.62f, 0.68f, 0.74f);
            Color light = Color.white;
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float d = density[x, y];
                    float above = density[x, Mathf.Min(height - 1, y + 4)];
                    float v = (y + 0.5f) / height;

                    // Thinner mist above this pixel means it is on a top surface facing the light.
                    float shade = Mathf.Clamp01(0.68f + (d - above) * 1.8f + (v - 0.5f) * 0.35f);
                    Color color = Color.Lerp(shadow, light, shade);
                    color.a = d * MaxAlpha;
                    pixels[y * width + x] = color;
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = $"Stage1 Mist Puff {seed}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), width);
        }

        /// <summary>A vertical gradient: clear at the bottom, solid towards the top.</summary>
        private static Sprite GetFogBankSprite()
        {
            if (fogBankSprite != null) return fogBankSprite;

            const int height = 32;
            var texture = new Texture2D(1, height, TextureFormat.RGBA32, false)
            {
                name = "Stage1 Backdrop Fog Bank",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                texture.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, v * 1.4f)));
            }

            texture.Apply();
            fogBankSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, height), new Vector2(0.5f, 0.5f), height);
            return fogBankSprite;
        }
    }
}
