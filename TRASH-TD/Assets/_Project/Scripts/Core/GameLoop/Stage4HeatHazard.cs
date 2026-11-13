using System.Collections.Generic;
using UnityEngine;
using TrashTD.Audio;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Operators;
using TrashTD.Systems;
using TrashTD.UI;

namespace TrashTD.Core.GameLoop
{
    /// <summary>
    /// Implements Stage 4's Factory Heat Hazard mechanic:
    /// - Every round, deployed operators build up 1 heat stack (max 5 stacks).
    /// - At 5 stacks, the heat bar fills and the operator ignites, suffering burning damage over time.
    /// - Burning can ONLY be extinguished by retreating the operator.
    /// - While benched in the deck, heat stacks fade away by 1 each round until completely cooled down.
    /// - Adds warm factory atmosphere lighting and organic drifting embers.
    /// </summary>
    public sealed class Stage4HeatHazard : MonoBehaviour
    {
        public const int MaxHeatStacks = 5;
        public const float BurnIntervalSeconds = 1.0f;
        public const int BurnDamagePerTick = 5;
        // Round-start furnace blast: glow, blur, embers, sparks, steam jets and smoke (see EmitHeatFlareBurst)
        public const float HeatFlareDurationSeconds = 2.2f;
        public const float FlareEmberMaxLifetime = 2.0f;
        private const float NormalEmberEmissionRate = 24f;
        // Screen-space heat shimmer (CrtDisplay.SetHeatWarp): picture-UV strength, and how far up the screen it reaches
        private const float AmbientHeatWarp = 0.0021f;
        private const float FlareHeatWarp = 0.0032f;
        private const float HeatWarpBand = 0.40f;
        private const int FlareEmberBurstCount = 96;
        private const float FlareEmberStreamRate = 70f;   // embers per second at full flare, fading with it
        private const int FlareSparkCount = 40;
        private const int FlareSmokeCount = 26;
        private const int FlareSteamPuffsPerVent = 8;
        private const int FlareFloorSteamCount = 14;

        private GridManager gridManager;
        private WaveManager waveManager;
        private OperatorManager operatorManager;
        private PlayerDeck playerDeck;
        private StageData stageData;

        private float burnTickTimer;
        private float heatFlareTimer;
        private SpriteRenderer furnaceUnderglowRenderer;
        private SpriteRenderer heatFlareRenderer;
        private Sprite heatFlareSprite;
        private Camera heatFlareCamera;
        private ParticleSystem emberParticleSystem;
        private float emberFlareMinX;
        private float emberFlareMaxX;
        private float emberFlareY;
        private float emberFlareZ;
        private float emberStreamAccumulator;
        private SpriteRenderer heatFlareGlowRenderer;   // bottom-heavy furnace glow over the view
        private ParticleSystem flareSparkSystem;
        private ParticleSystem flareSteamSystem;
        private ParticleSystem flareSmokeSystem;
        private readonly List<Vector3> steamVentPositions = new List<Vector3>();

        public void Initialize(
            GridManager grid,
            WaveManager waves,
            OperatorManager operators,
            PlayerDeck deck,
            StageData stage)
        {
            gridManager = grid;
            waveManager = waves;
            operatorManager = operators;
            playerDeck = deck;
            stageData = stage;

            if (waveManager != null)
            {
                waveManager.OnWaveStarted += HandleWaveStarted;
            }

            if (operatorManager != null)
            {
                operatorManager.OnOperatorDeployed += HandleOperatorDeployed;
                operatorManager.OnOperatorRetreated += HandleOperatorRetreated;
            }

            ApplyWarmMapAtmosphere();
            CreateHeatFlareOverlay();
            CreateDriftingEmbers();
            CreateIndustrialSteam();
            CreateVentEmbers();
            CreateFloorMist();
            CreateHeatFlareParticles();
        }

        private void OnDestroy()
        {
            if (waveManager != null)
            {
                waveManager.OnWaveStarted -= HandleWaveStarted;
            }

            if (operatorManager != null)
            {
                operatorManager.OnOperatorDeployed -= HandleOperatorDeployed;
                operatorManager.OnOperatorRetreated -= HandleOperatorRetreated;
            }

            // The overlays hang off the camera, not this object
            if (heatFlareRenderer != null)
                Destroy(heatFlareRenderer.gameObject);
            if (heatFlareGlowRenderer != null)
                Destroy(heatFlareGlowRenderer.gameObject);
            if (heatFlareSprite != null)
                Destroy(heatFlareSprite);
            CrtDisplay.SetHeatBlur(0f);
            CrtDisplay.SetHeatWarp(0f, HeatWarpBand, new Vector4(-1f, -1f, -1f, -1f), Vector4.zero);
        }

        private void CreateHeatFlareOverlay()
        {
            heatFlareCamera = Camera.main;
            if (heatFlareCamera == null) return;

            var overlayObject = new GameObject("Stage4HeatFlareOverlay");
            overlayObject.transform.SetParent(heatFlareCamera.transform, false);
            overlayObject.transform.localPosition = Vector3.forward * (heatFlareCamera.nearClipPlane + 0.01f);

            heatFlareRenderer = overlayObject.AddComponent<SpriteRenderer>();
            heatFlareSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
            heatFlareRenderer.sprite = heatFlareSprite;
            heatFlareRenderer.sortingLayerName = GridSorting.DefaultLayerName;
            heatFlareRenderer.sortingOrder = short.MaxValue;
            heatFlareRenderer.color = new Color(1f, 0.34f, 0.12f, 0f);

            // Furnace glow rising from the bottom of the view (the map's thermal gradient, stretched over it)
            var glowObject = new GameObject("Stage4HeatFlareGlow");
            glowObject.transform.SetParent(heatFlareCamera.transform, false);
            heatFlareGlowRenderer = glowObject.AddComponent<SpriteRenderer>();
            heatFlareGlowRenderer.sprite = GetVerticalThermalGradientSprite();
            heatFlareGlowRenderer.sortingLayerName = GridSorting.DefaultLayerName;
            heatFlareGlowRenderer.sortingOrder = short.MaxValue - 1;
            heatFlareGlowRenderer.color = new Color(1f, 0.38f, 0.08f, 0f);
        }

        /// <summary>
        /// Drives the heat shimmer: a constant ripple over the hot floor with a slow breathing, a column of it above
        /// each steam vent, and a stronger, taller surge while the round-start blast is running.
        /// </summary>
        private void UpdateHeatWarp(float flare)
        {
            if (heatFlareCamera == null) heatFlareCamera = Camera.main;

            var ventX = new Vector4(-1f, -1f, -1f, -1f);
            var ventY = Vector4.zero;
            if (heatFlareCamera != null)
            {
                for (int i = 0; i < steamVentPositions.Count && i < 4; i++)
                {
                    Vector3 viewport = heatFlareCamera.WorldToViewportPoint(steamVentPositions[i]);
                    ventX[i] = viewport.x;
                    ventY[i] = viewport.y;
                }
            }

            float breathing = 1f + 0.18f * Mathf.Sin(Time.unscaledTime * 0.7f);
            CrtDisplay.SetHeatWarp(
                AmbientHeatWarp * breathing + FlareHeatWarp * flare,
                HeatWarpBand + 0.22f * flare,
                ventX,
                ventY);
        }

        private float UpdateHeatFlare()
        {
            float flare = GetHeatFlareIntensity();
            UpdateHeatWarp(flare);
            // The blur eases off sooner than the glow, so the picture is readable for most of the blast
            CrtDisplay.SetHeatBlur(0.8f * flare * flare);
            if (heatFlareTimer > 0f)
            {
                heatFlareTimer = Mathf.Max(0f, heatFlareTimer - Time.deltaTime);
                StreamHeatFlareEmbers(flare);
            }

            if (heatFlareCamera == null)
                heatFlareCamera = Camera.main;
            if ((heatFlareRenderer == null || heatFlareGlowRenderer == null) && heatFlareCamera != null)
            {
                if (heatFlareRenderer != null) Destroy(heatFlareRenderer.gameObject);
                if (heatFlareGlowRenderer != null) Destroy(heatFlareGlowRenderer.gameObject);
                CreateHeatFlareOverlay();
            }
            if (heatFlareRenderer == null || heatFlareCamera == null) return flare;

            float viewHeight = heatFlareCamera.orthographicSize * 2f;
            float viewWidth = viewHeight * heatFlareCamera.aspect;
            Vector3 overlayPosition = Vector3.forward * (heatFlareCamera.nearClipPlane + 0.01f);
            // Fire light isn't steady: a quick flicker on top of the envelope
            float flicker = 0.88f + 0.12f * Mathf.PerlinNoise(Time.time * 11f, 0.37f);

            heatFlareRenderer.transform.localPosition = overlayPosition;
            heatFlareRenderer.transform.localScale = new Vector3(viewWidth, viewHeight, 1f);
            heatFlareRenderer.color = new Color(1f, 0.30f, 0.08f, 0.13f * flare * flicker);

            if (heatFlareGlowRenderer != null && heatFlareGlowRenderer.sprite != null)
            {
                Vector2 spriteSize = heatFlareGlowRenderer.sprite.bounds.size;
                heatFlareGlowRenderer.transform.localPosition = overlayPosition;
                heatFlareGlowRenderer.transform.localScale = new Vector3(
                    viewWidth / Mathf.Max(0.001f, spriteSize.x),
                    viewHeight / Mathf.Max(0.001f, spriteSize.y),
                    1f);
                heatFlareGlowRenderer.color = new Color(1f, 0.40f, 0.08f, 0.55f * flare * flicker);
            }
            return flare;
        }

        /// <summary>
        /// 1 at the blast, then a long fall-off with a second, smaller throb a third of the way in
        /// (the furnace breathing out again) before it settles.
        /// </summary>
        private float GetHeatFlareIntensity()
        {
            if (heatFlareTimer <= 0f) return 0f;

            float progress = Mathf.Clamp01(1f - heatFlareTimer / HeatFlareDurationSeconds);
            float decay = Mathf.Pow(1f - progress, 1.6f);
            float throbT = Mathf.InverseLerp(0.22f, 0.62f, progress);
            float throb = 0.28f * Mathf.Sin(throbT * Mathf.PI) * (1f - progress);
            return Mathf.Clamp01(decay + throb);
        }

        /// <summary>
        /// When a wave round begins, heat builds up on all active deployed operators.
        /// </summary>
        private void HandleWaveStarted(int currentWave, int totalWaves)
        {
            if (operatorManager == null || !StageCombatModifiers.IsHeatHazardActive) return;

            var deployed = operatorManager.DeployedOperators;
            bool heatStackAdded = false;
            for (int i = 0; i < deployed.Count; i++)
            {
                OperatorBase op = deployed[i];
                if (op == null || !op.IsDeployed || op.CurrentHP <= 0) continue;

                int previousStacks = op.HeatStacks;
                AddHeatStack(op);
                heatStackAdded |= op.HeatStacks > previousStacks;
                OperatorHeatBar.ShowFor(op);
            }

            if (heatStackAdded)
            {
                heatFlareTimer = HeatFlareDurationSeconds;
                emberStreamAccumulator = 0f;
                EmitHeatFlareEmbers();
                EmitHeatFlareSparks();
                EmitHeatFlareSteamAndSmoke();
                GameplayCameraController.Instance?.Shake(0.07f, 0.5f);
            }
        }

        private void HandleOperatorDeployed(OperatorBase op)
        {
            if (op == null || !StageCombatModifiers.IsHeatHazardActive) return;

            // If deployed mid-wave, it immediately participates in this wave's heat
            if (waveManager != null && waveManager.IsWaveInProgress)
            {
                AddHeatStack(op);
            }

            OperatorHeatBar.ShowFor(op);
        }

        /// <summary>Adds one heat stack (max 5); at 5 the operator ignites. Also used by enemy fire attacks (Roboflame, Eradicator).</summary>
        public static void AddHeatStack(OperatorBase op)
        {
            if (op == null) return;

            int previousStacks = op.HeatStacks;
            op.HeatStacks = Mathf.Min(MaxHeatStacks, op.HeatStacks + 1);
            if (op.HeatStacks > previousStacks)
                AudioManager.Instance?.PlaySfx(SfxId.Stage4HeatUp, 0.7f);

            if (op.HeatStacks >= MaxHeatStacks && !op.IsBurning)
            {
                op.IsBurning = true;
                AudioManager.Instance?.PlaySfx(SfxId.Stage4Ignite);
            }

            // The gauge only used to refresh at the start of a round, so heat from an enemy attack showed up late
            if (Application.isPlaying && op.HeatStacks != previousStacks)
                OperatorHeatBar.ShowFor(op);
        }

        private void HandleOperatorRetreated(OperatorBase op)
        {
            if (op == null) return;

            // Fire is extinguished when retreated (a death isn't an extinguish)
            if (op.IsBurning && op.CurrentHP > 0)
                AudioManager.Instance?.PlaySfx(SfxId.Stage4Extinguish);
            op.IsBurning = false;
        }

        private void Update()
        {
            float flare = UpdateHeatFlare();

            // Subtle breathing shimmer only on the bottom furnace glow
            if (furnaceUnderglowRenderer != null)
            {
                float glow = 0.18f + 0.03f * Mathf.Sin(Time.time * 1.1f) + 0.50f * flare;
                furnaceUnderglowRenderer.color = new Color(1f, 0.44f, 0.10f, glow);
            }

            // Burning overtime damage
            if (waveManager == null || !waveManager.IsWaveInProgress || !StageCombatModifiers.IsHeatHazardActive)
            {
                burnTickTimer = 0f;
                return;
            }

            burnTickTimer += Time.deltaTime;
            while (burnTickTimer >= BurnIntervalSeconds)
            {
                burnTickTimer -= BurnIntervalSeconds;
                ApplyBurningDamage();
            }
        }

        private void ApplyBurningDamage()
        {
            if (operatorManager == null) return;

            var deployed = operatorManager.DeployedOperators;
            for (int i = deployed.Count - 1; i >= 0; i--)
            {
                if (i >= deployed.Count) continue;
                OperatorBase op = deployed[i];
                if (op == null || !op.IsDeployed || !op.IsBurning || op.IsCombatPaused || op.CurrentHP <= 0)
                {
                    continue;
                }

                BurnDamageFlash.Flash(op.gameObject);
                // Several burning operators tick together; the shared-sound throttle plays this once.
                AudioManager.Instance?.PlaySfx(SfxId.Stage4BurnTick, 0.6f);
                op.TakeTrueDamage(BurnDamagePerTick);
            }
        }

        /// <summary>
        /// Warms up the map palette with ambient furnace lighting and heat overlays.
        /// </summary>
        private void ApplyWarmMapAtmosphere()
        {
            if (gridManager == null || stageData == null) return;

            float cellSize = gridManager.CellSize;
            float mapWidth = stageData.gridWidth * cellSize;
            float mapHeight = stageData.gridHeight * cellSize;
            Vector3 gridBottomLeft = gridManager.GridToWorldPosition(0, 0)
                - new Vector3(cellSize * 0.5f, cellSize * 0.5f, 0f);
            Vector3 mapCenter = gridBottomLeft + new Vector3(mapWidth * 0.5f, mapHeight * 0.5f, 0f);

            var atmosphereRoot = new GameObject("Stage4WarmAtmosphere");
            atmosphereRoot.transform.SetParent(transform, false);

            // Vertical thermal gradient: warm at the bottom floor, fading to completely cool/natural at the top
            var underglowObj = new GameObject("VerticalThermalGradient");
            underglowObj.transform.SetParent(atmosphereRoot.transform, false);
            underglowObj.transform.position = mapCenter + new Vector3(0f, 0f, -0.02f);
            underglowObj.transform.localScale = new Vector3(mapWidth + 2f, mapHeight, 1f);

            furnaceUnderglowRenderer = underglowObj.AddComponent<SpriteRenderer>();
            furnaceUnderglowRenderer.sprite = GetVerticalThermalGradientSprite();
            furnaceUnderglowRenderer.color = new Color(1f, 0.44f, 0.10f, 0.18f);
            GridSorting.PlaceOnGround(furnaceUnderglowRenderer);
            furnaceUnderglowRenderer.sortingOrder = 2; // Above map artwork (1), below units

            // Ensure base map artwork is 100% natural pure color
            var mapArtworkObj = GameObject.Find("MapArtwork");
            if (mapArtworkObj != null)
            {
                var sr = mapArtworkObj.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    sr.color = Color.white;
                }
            }
        }

        /// <summary>
        /// Creates organic, floating glowing embers that drift lazily upwards across the map.
        /// </summary>
        private void CreateDriftingEmbers()
        {
            if (gridManager == null || stageData == null) return;

            float cellSize = gridManager.CellSize;
            float mapWidth = stageData.gridWidth * cellSize;
            float mapHeight = stageData.gridHeight * cellSize;
            Vector3 gridBottomLeft = gridManager.GridToWorldPosition(0, 0)
                - new Vector3(cellSize * 0.5f, cellSize * 0.5f, 0f);
            Vector3 mapCenter = gridBottomLeft + new Vector3(mapWidth * 0.5f, mapHeight * 0.5f, 0f);
            emberFlareMinX = mapCenter.x - mapWidth * 0.5f;
            emberFlareMaxX = mapCenter.x + mapWidth * 0.5f;
            emberFlareY = mapCenter.y - mapHeight * 0.42f;

            var embersRoot = new GameObject("Stage4DriftingEmbers");
            embersRoot.transform.SetParent(transform, false);

            var emberObj = new GameObject("EmberParticleSystem");
            emberObj.transform.SetParent(embersRoot.transform, false);
            // Spawn origin near the bottom of the map
            emberObj.transform.position = mapCenter + new Vector3(0f, -mapHeight * 0.42f, -0.1f);
            emberFlareZ = emberObj.transform.position.z;

            ParticleSystem ps = emberObj.AddComponent<ParticleSystem>();
            emberParticleSystem = ps;

            // Main module
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.prewarm = true; // Embers already active and in flight when map loads!
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 10f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.13f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.86f, 0.42f, 0.95f), new Color(1f, 0.55f, 0.16f, 0.9f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;

            // Emission
            var emission = ps.emission;
            emission.rateOverTime = NormalEmberEmissionRate;

            // Shape: horizontal emitter bar along the bottom of the map
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(mapWidth + 2f, 0.6f, 0.1f);

            // Velocity over lifetime: gentle upward drift with slight horizontal sway
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.y = new ParticleSystem.MinMaxCurve(0.5f, 0.95f);
            velocity.x = new ParticleSystem.MinMaxCurve(-0.14f, 0.14f);
            // All three axes must use the same curve mode (two constants), or Unity errors.
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            // Hot air buffets the embers: lively swirling, plus a size/spin noise that makes each one flicker
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 0.7f;
            noise.scrollSpeed = 0.45f;
            noise.damping = true;
            noise.sizeAmount = new ParticleSystem.MinMaxCurve(0.55f);
            noise.octaveCount = 2;

            // Size over lifetime: starts glowing, gently shrinks as it burns out
            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.6f),
                new Keyframe(0.2f, 1f),
                new Keyframe(0.7f, 0.9f),
                new Keyframe(1f, 0.35f)
            );
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // Color & Alpha over lifetime: smooth fade-in and gradual cooling decay (NO BLINKING)
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(1f, 0.92f, 0.45f), 0.0f),  // Bright yellow-white incandescent
                    new GradientColorKey(new Color(1f, 0.65f, 0.15f), 0.35f), // Warm golden ember
                    new GradientColorKey(new Color(1f, 0.35f, 0.08f), 0.70f), // Deep fiery orange
                    new GradientColorKey(new Color(0.85f, 0.15f, 0.04f), 0.90f), // Cooling crimson
                    new GradientColorKey(new Color(0.35f, 0.04f, 0.02f), 1.0f)   // Fading ash
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0.0f, 0.0f),   // Soft spawn (no pop)
                    new GradientAlphaKey(0.85f, 0.15f), // Full radiant glow
                    new GradientAlphaKey(0.85f, 0.65f), // Sustained flight
                    new GradientAlphaKey(0.40f, 0.85f), // Dimming
                    new GradientAlphaKey(0.0f, 1.0f)    // Gentle fade out
                }
            );
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(grad);

            // Renderer with soft radial glow material
            var renderer = emberObj.GetComponent<ParticleSystemRenderer>();
            renderer.sortingOrder = 25; // Above map and tiles
            renderer.sharedMaterial = GetEmberMaterial();

            ps.Play();
        }

        // Flare embers run from white-hot to deep orange, so the burst doesn't read as one flat colour
        private static readonly Color FlareEmberHot = new Color(1f, 0.97f, 0.75f, 1f);
        private static readonly Color FlareEmberWarm = new Color(1f, 0.62f, 0.18f, 1f);

        /// <summary>The blast's big ember burst along the bottom of the map, thrown higher and wider than the idle drift.</summary>
        private void EmitHeatFlareEmbers()
        {
            if (emberParticleSystem == null) return;

            for (int i = 0; i < FlareEmberBurstCount; i++)
            {
                var ember = new ParticleSystem.EmitParams
                {
                    position = new Vector3(
                        Random.Range(emberFlareMinX, emberFlareMaxX),
                        emberFlareY + Random.Range(-0.2f, 0.35f),
                        emberFlareZ),
                    velocity = new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(0.8f, 1.9f), 0f),
                    startLifetime = Random.Range(1.1f, FlareEmberMaxLifetime),
                    startSize = Random.Range(0.10f, 0.22f),
                    startColor = Color.Lerp(FlareEmberHot, FlareEmberWarm, Random.value)
                };
                emberParticleSystem.Emit(ember, 1);
            }
        }

        /// <summary>Keeps embers rising for the length of the blast, thinning out as it fades.</summary>
        private void StreamHeatFlareEmbers(float flare)
        {
            if (emberParticleSystem == null) return;

            emberStreamAccumulator += FlareEmberStreamRate * flare * Time.deltaTime;
            while (emberStreamAccumulator >= 1f)
            {
                emberStreamAccumulator -= 1f;
                var ember = new ParticleSystem.EmitParams
                {
                    position = new Vector3(
                        Random.Range(emberFlareMinX, emberFlareMaxX),
                        emberFlareY + Random.Range(-0.2f, 0.2f),
                        emberFlareZ),
                    velocity = new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(0.7f, 1.5f), 0f),
                    startLifetime = Random.Range(1.0f, 1.8f),
                    startSize = Random.Range(0.08f, 0.17f),
                    startColor = Color.Lerp(FlareEmberHot, FlareEmberWarm, Random.value)
                };
                emberParticleSystem.Emit(ember, 1);
            }
        }

        /// <summary>Fast spark streaks thrown up from the floor; they arc and fall back under gravity.</summary>
        private void EmitHeatFlareSparks()
        {
            if (flareSparkSystem == null) return;

            for (int i = 0; i < FlareSparkCount; i++)
            {
                var spark = new ParticleSystem.EmitParams
                {
                    position = new Vector3(
                        Random.Range(emberFlareMinX, emberFlareMaxX),
                        emberFlareY + Random.Range(-0.1f, 0.25f),
                        emberFlareZ),
                    velocity = new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(2.4f, 4.6f), 0f),
                    startLifetime = Random.Range(0.6f, 1.2f),
                    startSize = Random.Range(0.035f, 0.07f),
                    startColor = Color.Lerp(FlareEmberHot, new Color(1f, 0.82f, 0.4f, 1f), Random.value)
                };
                flareSparkSystem.Emit(spark, 1);
            }
        }

        /// <summary>
        /// Steam jets out of every vent and hisses up from the floor, while ember-lit smoke rolls up along the
        /// bottom of the map behind it.
        /// </summary>
        private void EmitHeatFlareSteamAndSmoke()
        {
            if (flareSmokeSystem != null)
            {
                for (int i = 0; i < FlareSmokeCount; i++)
                {
                    var smoke = new ParticleSystem.EmitParams
                    {
                        position = new Vector3(
                            Random.Range(emberFlareMinX - 0.5f, emberFlareMaxX + 0.5f),
                            emberFlareY + Random.Range(-0.45f, 0.15f),
                            emberFlareZ),
                        velocity = new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(0.45f, 0.9f), 0f),
                        startLifetime = Random.Range(1.8f, 2.6f),
                        startSize = Random.Range(0.9f, 1.6f),
                        rotation = Random.Range(0f, 360f),
                        startColor = new Color(0.52f, 0.44f, 0.40f, 0.45f) // light enough to see units through
                    };
                    flareSmokeSystem.Emit(smoke, 1);
                }
            }

            if (flareSteamSystem == null) return;

            for (int v = 0; v < steamVentPositions.Count; v++)
            {
                for (int i = 0; i < FlareSteamPuffsPerVent; i++)
                {
                    var puff = new ParticleSystem.EmitParams
                    {
                        position = steamVentPositions[v] + new Vector3(Random.Range(-0.08f, 0.08f), 0f, 0f),
                        velocity = new Vector3(Random.Range(-0.35f, 0.35f), Random.Range(1.3f, 2.2f), 0f),
                        startLifetime = Random.Range(1.6f, 2.4f),
                        startSize = Random.Range(0.45f, 0.85f),
                        rotation = Random.Range(0f, 360f),
                        startColor = new Color(1f, 1f, 1f, 0.5f)
                    };
                    flareSteamSystem.Emit(puff, 1);
                }
            }

            for (int i = 0; i < FlareFloorSteamCount; i++)
            {
                var wisp = new ParticleSystem.EmitParams
                {
                    position = new Vector3(
                        Random.Range(emberFlareMinX, emberFlareMaxX),
                        emberFlareY + Random.Range(-0.1f, 0.3f),
                        emberFlareZ),
                    velocity = new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0.6f, 1.1f), 0f),
                    startLifetime = Random.Range(1.4f, 2.1f),
                    startSize = Random.Range(0.35f, 0.65f),
                    rotation = Random.Range(0f, 360f),
                    startColor = new Color(1f, 0.96f, 0.92f, 0.35f)
                };
                flareSteamSystem.Emit(wisp, 1);
            }
        }

        /// <summary>
        /// The blast's own particle systems. They emit nothing on their own: EmitHeatFlare* fills them when a round
        /// heats the squad. Smoke draws behind the steam, steam behind the embers (25), sparks on top.
        /// </summary>
        private void CreateHeatFlareParticles()
        {
            var flareRoot = new GameObject("Stage4HeatFlare");
            flareRoot.transform.SetParent(transform, false);

            // Smoke: warm-lit at first (the fire under it), cooling to grey as it rises and spreads
            flareSmokeSystem = CreateBurstParticleSystem(flareRoot.transform, "FlareSmoke", GetSteamMaterial(), 23, 80);
            SetBurstSizeCurve(flareSmokeSystem, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.4f, 1.3f), new Keyframe(1f, 2.0f)));
            SetBurstColorGradient(flareSmokeSystem,
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.60f, 0.35f), 0f),
                    new GradientColorKey(new Color(0.60f, 0.58f, 0.60f), 0.45f),
                    new GradientColorKey(new Color(0.42f, 0.42f, 0.45f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(0.7f, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                });
            AddBurstDrift(flareSmokeSystem, 0.3f, 0.4f, 20f);

            // Steam: bright jets that burst out fast, slow down and billow
            flareSteamSystem = CreateBurstParticleSystem(flareRoot.transform, "FlareSteam", GetSteamMaterial(), 24, 120);
            SetBurstSizeCurve(flareSteamSystem, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.25f, 1.4f), new Keyframe(1f, 3.0f)));
            SetBurstColorGradient(flareSteamSystem,
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(0.90f, 0.92f, 0.96f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.1f),
                    new GradientAlphaKey(0.6f, 0.5f),
                    new GradientAlphaKey(0f, 1f)
                });
            AddBurstDrift(flareSteamSystem, 0.35f, 0.5f, 30f);
            var steamDrag = flareSteamSystem.limitVelocityOverLifetime;
            steamDrag.enabled = true;
            steamDrag.limit = 0.45f;
            steamDrag.dampen = 0.06f;

            // Sparks: stretched along their velocity so they read as streaks, pulled back down by gravity
            flareSparkSystem = CreateBurstParticleSystem(flareRoot.transform, "FlareSparks", GetEmberMaterial(), 27, 120);
            var sparkMain = flareSparkSystem.main;
            sparkMain.gravityModifier = 0.35f;
            SetBurstColorGradient(flareSparkSystem,
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(1f, 0.7f, 0.25f), 0.5f),
                    new GradientColorKey(new Color(1f, 0.32f, 0.08f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.9f, 0.6f),
                    new GradientAlphaKey(0f, 1f)
                });
            var sparkRenderer = flareSparkSystem.GetComponent<ParticleSystemRenderer>();
            sparkRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            sparkRenderer.velocityScale = 0.07f;
            sparkRenderer.lengthScale = 1.5f;
        }

        private ParticleSystem CreateBurstParticleSystem(Transform parent, string objectName, Material material, int sortingOrder, int maxParticles)
        {
            var systemObject = new GameObject(objectName);
            systemObject.transform.SetParent(parent, false);
            systemObject.transform.position = new Vector3(0f, 0f, emberFlareZ);

            ParticleSystem ps = systemObject.AddComponent<ParticleSystem>();
            // AddComponent starts it playing; stop before changing duration-type settings
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            var emission = ps.emission;
            emission.enabled = false; // Emit() still works; nothing spawns by itself
            var shape = ps.shape;
            shape.enabled = false;

            var renderer = systemObject.GetComponent<ParticleSystemRenderer>();
            renderer.sortingOrder = sortingOrder;
            renderer.sharedMaterial = material;
            ps.Play();
            return ps;
        }

        private static void SetBurstSizeCurve(ParticleSystem ps, AnimationCurve curve)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        private static void SetBurstColorGradient(ParticleSystem ps, GradientColorKey[] colors, GradientAlphaKey[] alphas)
        {
            var gradient = new Gradient();
            gradient.SetKeys(colors, alphas);
            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        /// <summary>Wandering noise plus a slow spin, for soft puffs.</summary>
        private static void AddBurstDrift(ParticleSystem ps, float noiseStrength, float noiseFrequency, float spinDegrees)
        {
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = noiseStrength;
            noise.frequency = noiseFrequency;
            noise.scrollSpeed = 0.3f;
            noise.damping = true;

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-spinDegrees * Mathf.Deg2Rad, spinDegrees * Mathf.Deg2Rad);
        }

        private static Material emberMaterial;
        private static Material GetEmberMaterial()
        {
            if (emberMaterial == null)
            {
                Shader shader = Shader.Find("Particles/Standard Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                emberMaterial = new Material(shader);
                emberMaterial.mainTexture = CreateEmberRadialTexture();
                // Additive blending so embers glow without black backgrounds
                emberMaterial.SetFloat("_Mode", 4); // Additive
                emberMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                emberMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
                emberMaterial.SetInt("_ZWrite", 0);
                emberMaterial.DisableKeyword("_ALPHATEST_ON");
                emberMaterial.EnableKeyword("_ALPHABLEND_ON");
                emberMaterial.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                emberMaterial.renderQueue = 3000;
            }
            return emberMaterial;
        }

        private static Texture2D CreateEmberRadialTexture()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            float center = (size - 1) * 0.5f;
            float maxDist = center;

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) / maxDist;
                    if (dist >= 1f)
                    {
                        pixels[y * size + x] = Color.clear;
                    }
                    else
                    {
                        // Gaussian-like radial falloff with bright hot core
                        float alpha = Mathf.Pow(1f - dist, 2.0f);
                        pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Sprite verticalThermalGradientSprite;
        private static Sprite GetVerticalThermalGradientSprite()
        {
            if (verticalThermalGradientSprite == null)
            {
                const int h = 64;
                var tex = new Texture2D(2, h, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                Color[] colors = new Color[2 * h];
                for (int y = 0; y < h; y++)
                {
                    float t = (float)y / (h - 1); // 0 at bottom, 1 at top
                    // Strong warm glow at bottom, gradually fading — top ~40% is completely cool
                    float alpha = t < 0.60f ? Mathf.Pow(1f - (t / 0.60f), 1.6f) : 0f;
                    Color c = new Color(1f, 1f, 1f, alpha);
                    colors[y * 2] = c;
                    colors[y * 2 + 1] = c;
                }
                tex.SetPixels(colors);
                tex.Apply();
                verticalThermalGradientSprite = Sprite.Create(tex, new Rect(0, 0, 2, h), new Vector2(0.5f, 0.5f), 1f);
            }
            return verticalThermalGradientSprite;
        }

        /// <summary>
        /// Creates industrial steam vents across the factory machinery that billow upward.
        /// </summary>
        private void CreateIndustrialSteam()
        {
            if (gridManager == null || stageData == null) return;

            float cellSize = gridManager.CellSize;
            Vector3 gridBottomLeft = gridManager.GridToWorldPosition(0, 0)
                - new Vector3(cellSize * 0.5f, cellSize * 0.5f, 0f);

            var steamRoot = new GameObject("Stage4IndustrialSteam");
            steamRoot.transform.SetParent(transform, false);

            // Three steam vents on the map's machinery, in tile units from the bottom-left corner: the left-hand
            // tanks at the middle of the left edge, the big cogwheel, and the bottom-right corner machinery.
            Vector2[] ventGridCoords = new Vector2[]
            {
                new Vector2(1.0f, 3.5f),
                new Vector2(5.0f, 3.0f),
                new Vector2(12.8f, 1.4f)
            };
            for (int i = 0; i < ventGridCoords.Length; i++)
            {
                Vector3 ventPos = gridBottomLeft + new Vector3(
                    ventGridCoords[i].x * cellSize,
                    ventGridCoords[i].y * cellSize,
                    -0.08f);
                steamVentPositions.Add(ventPos); // the round-start blast jets steam out of these too

                var ventObj = new GameObject($"SteamVent_{i + 1}");
                ventObj.transform.SetParent(steamRoot.transform, false);
                ventObj.transform.position = ventPos;

                ParticleSystem ps = ventObj.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.loop = true;
                main.playOnAwake = true;
                main.prewarm = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(3.6f, 5.6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.95f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 80;

                var emission = ps.emission;
                emission.rateOverTime = 7f;
                // A pressure hiss every few seconds, staggered between the vents
                emission.SetBursts(new[] { new ParticleSystem.Burst(i * 1.9f, (short)7, (short)11, 0, 5.5f) });

                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 14f;
                shape.radius = 0.08f;
                shape.rotation = new Vector3(-90f, 0f, 0f); // Pointing upward

                var velocity = ps.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.World;
                velocity.y = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
                velocity.x = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
                // All three axes must use the same curve mode (two constants), or Unity errors.
                velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-25f * Mathf.Deg2Rad, 25f * Mathf.Deg2Rad);

                var sizeOverLifetime = ps.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                AnimationCurve steamSizeCurve = new AnimationCurve(
                    new Keyframe(0f, 0.45f),
                    new Keyframe(0.25f, 1.25f),
                    new Keyframe(1f, 3.1f)
                );
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, steamSizeCurve);

                var colorOverLifetime = ps.colorOverLifetime;
                colorOverLifetime.enabled = true;
                Gradient steamGrad = new Gradient();
                steamGrad.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(new Color(1f, 0.80f, 0.60f), 0f),    // lit warm by the furnace at the vent
                        new GradientColorKey(new Color(0.96f, 0.92f, 0.90f), 0.3f),
                        new GradientColorKey(new Color(0.74f, 0.77f, 0.83f), 1f)  // cooling to grey-blue as it spreads
                    },
                    new GradientAlphaKey[]
                    {
                        new GradientAlphaKey(0f, 0f),
                        new GradientAlphaKey(0.40f, 0.12f),
                        new GradientAlphaKey(0.30f, 0.5f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );
                colorOverLifetime.color = new ParticleSystem.MinMaxGradient(steamGrad);

                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 0.55f;
                noise.frequency = 0.65f;
                noise.scrollSpeed = 0.4f;
                noise.damping = true;

                var renderer = ventObj.GetComponent<ParticleSystemRenderer>();
                renderer.sortingOrder = 12; // Above map and tiles, behind floating combat text
                renderer.sharedMaterial = GetSteamMaterial();

                ps.Play();
            }
        }

        /// <summary>Small, fast embers spat out of each steam vent: they shoot up, wobble in the hot air and burn out.</summary>
        private void CreateVentEmbers()
        {
            if (steamVentPositions.Count == 0) return;

            var root = new GameObject("Stage4VentEmbers");
            root.transform.SetParent(transform, false);

            for (int i = 0; i < steamVentPositions.Count; i++)
            {
                var ventObj = new GameObject($"VentEmbers_{i + 1}");
                ventObj.transform.SetParent(root.transform, false);
                ventObj.transform.position = steamVentPositions[i] + new Vector3(0f, 0.05f, -0.02f);

                ParticleSystem ps = ventObj.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.loop = true;
                main.playOnAwake = true;
                main.prewarm = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.0f, 2.6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.085f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.7f, 1f), new Color(1f, 0.58f, 0.18f, 1f));
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.gravityModifier = -0.06f; // hot air lifts them
                main.maxParticles = 90;

                var emission = ps.emission;
                emission.rateOverTime = 4.5f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(i * 1.9f + 0.1f, (short)5, (short)9, 0, 5.5f) }); // with the steam hiss

                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 24f;
                shape.radius = 0.07f;
                shape.rotation = new Vector3(-90f, 0f, 0f);

                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 0.8f;
                noise.frequency = 0.9f;
                noise.scrollSpeed = 0.6f;
                noise.damping = true;
                noise.sizeAmount = new ParticleSystem.MinMaxCurve(0.5f);

                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 1f), new Keyframe(0.6f, 0.8f), new Keyframe(1f, 0.1f)));

                var color = ps.colorOverLifetime;
                color.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(1f, 0.98f, 0.8f), 0f),
                        new GradientColorKey(new Color(1f, 0.6f, 0.18f), 0.4f),
                        new GradientColorKey(new Color(0.9f, 0.2f, 0.05f), 1f)
                    },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0f, 1f) });
                color.color = new ParticleSystem.MinMaxGradient(gradient);

                var renderer = ventObj.GetComponent<ParticleSystemRenderer>();
                renderer.sortingOrder = 26;
                renderer.sharedMaterial = GetEmberMaterial();
                ps.Play();
            }
        }

        /// <summary>A low, warm bank of mist rolling along the floor, as if the whole factory floor is sweating heat.</summary>
        private void CreateFloorMist()
        {
            if (gridManager == null || stageData == null) return;

            float cellSize = gridManager.CellSize;
            float mapWidth = stageData.gridWidth * cellSize;
            float mapHeight = stageData.gridHeight * cellSize;
            Vector3 gridBottomLeft = gridManager.GridToWorldPosition(0, 0)
                - new Vector3(cellSize * 0.5f, cellSize * 0.5f, 0f);
            Vector3 mapCenter = gridBottomLeft + new Vector3(mapWidth * 0.5f, mapHeight * 0.5f, 0f);

            var mistObj = new GameObject("Stage4FloorMist");
            mistObj.transform.SetParent(transform, false);
            mistObj.transform.position = mapCenter + new Vector3(0f, -mapHeight * 0.40f, -0.05f);

            ParticleSystem ps = mistObj.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.18f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;

            var emission = ps.emission;
            emission.rateOverTime = 3.5f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(mapWidth + 2f, 0.7f, 0.1f);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.04f, 0.14f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-8f * Mathf.Deg2Rad, 8f * Mathf.Deg2Rad);

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.62f, 0.34f), 0f),   // lit by the furnace glow
                    new GradientColorKey(new Color(0.85f, 0.55f, 0.40f), 0.6f),
                    new GradientColorKey(new Color(0.55f, 0.45f, 0.42f), 1f)
                },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.16f, 0.25f), new GradientAlphaKey(0.12f, 0.7f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = mistObj.GetComponent<ParticleSystemRenderer>();
            renderer.sortingOrder = 11; // Under the vent steam (12), over the map and tiles
            renderer.sharedMaterial = GetSteamMaterial();
            ps.Play();
        }

        private static Material steamMaterial;
        private static Material GetSteamMaterial()
        {
            if (steamMaterial == null)
            {
                Shader shader = Shader.Find("Particles/Standard Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                steamMaterial = new Material(shader);
                steamMaterial.mainTexture = CreateSteamPuffTexture();
                // Fade blending so steam is translucent without black background
                steamMaterial.SetFloat("_Mode", 2); // Fade
                steamMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                steamMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                steamMaterial.SetInt("_ZWrite", 0);
                steamMaterial.DisableKeyword("_ALPHATEST_ON");
                steamMaterial.EnableKeyword("_ALPHABLEND_ON");
                steamMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                steamMaterial.renderQueue = 3000;
            }
            return steamMaterial;
        }

        /// <summary>
        /// A soft, billowy cloud puff: a ragged round edge and uneven density from layered noise, so rotated
        /// copies of it overlap into convincing steam and smoke instead of identical round blobs.
        /// </summary>
        private static Texture2D CreateSteamPuffTexture()
        {
            const int size = 96;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    // Three octaves of noise
                    float n = 0.55f * Mathf.PerlinNoise(dx * 1.7f + 4.3f, dy * 1.7f + 9.1f)
                            + 0.30f * Mathf.PerlinNoise(dx * 3.6f + 1.7f, dy * 3.6f + 5.3f)
                            + 0.15f * Mathf.PerlinNoise(dx * 7.4f + 8.2f, dy * 7.4f + 2.6f);

                    float edge = 0.80f + (n - 0.5f) * 0.55f;
                    if (r >= edge)
                    {
                        pixels[y * size + x] = Color.clear;
                        continue;
                    }

                    float falloff = Mathf.Pow(1f - r / edge, 1.35f);
                    float density = Mathf.Lerp(0.45f, 1.15f, n);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(falloff * density));
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
