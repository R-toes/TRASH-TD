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
        public const int BurnDamagePerTick = 15;

        private GridManager gridManager;
        private WaveManager waveManager;
        private OperatorManager operatorManager;
        private PlayerDeck playerDeck;
        private StageData stageData;

        private float burnTickTimer;
        private SpriteRenderer furnaceUnderglowRenderer;

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
            CreateDriftingEmbers();
            CreateIndustrialSteam();
            CreateHeatDistortionEffect();
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
        }

        /// <summary>
        /// When a wave round begins, heat builds up on all active deployed operators.
        /// </summary>
        private void HandleWaveStarted(int currentWave, int totalWaves)
        {
            if (operatorManager == null) return;

            var deployed = operatorManager.DeployedOperators;
            for (int i = 0; i < deployed.Count; i++)
            {
                OperatorBase op = deployed[i];
                if (op == null || !op.IsDeployed || op.CurrentHP <= 0) continue;

                AddHeatStack(op);
                OperatorHeatBar.ShowFor(op);
            }
        }

        private void HandleOperatorDeployed(OperatorBase op)
        {
            if (op == null) return;

            // If deployed mid-wave, it immediately participates in this wave's heat
            if (waveManager != null && waveManager.IsWaveInProgress)
            {
                AddHeatStack(op);
            }

            OperatorHeatBar.ShowFor(op);
        }

        private static void AddHeatStack(OperatorBase op)
        {
            int previousStacks = op.HeatStacks;
            op.HeatStacks = Mathf.Min(MaxHeatStacks, op.HeatStacks + 1);
            if (op.HeatStacks > previousStacks)
                AudioManager.Instance?.PlaySfx(SfxId.Stage4HeatUp, 0.7f);

            if (op.HeatStacks >= MaxHeatStacks && !op.IsBurning)
            {
                op.IsBurning = true;
                AudioManager.Instance?.PlaySfx(SfxId.Stage4Ignite);
            }
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
            // Subtle breathing shimmer only on the bottom furnace glow
            if (furnaceUnderglowRenderer != null)
            {
                float glow = 0.18f + 0.03f * Mathf.Sin(Time.time * 1.1f);
                furnaceUnderglowRenderer.color = new Color(1f, 0.44f, 0.10f, glow);
            }

            // Burning overtime damage
            if (waveManager == null || !waveManager.IsWaveInProgress)
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
                op.TakeDamage(BurnDamagePerTick, DamageType.Arts);
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
            furnaceUnderglowRenderer.sortingOrder = 2; // Above map artwork (1), below units (10+)

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

            var embersRoot = new GameObject("Stage4DriftingEmbers");
            embersRoot.transform.SetParent(transform, false);

            var emberObj = new GameObject("EmberParticleSystem");
            emberObj.transform.SetParent(embersRoot.transform, false);
            // Spawn origin near the bottom of the map
            emberObj.transform.position = mapCenter + new Vector3(0f, -mapHeight * 0.42f, -0.1f);

            ParticleSystem ps = emberObj.AddComponent<ParticleSystem>();

            // Main module
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.prewarm = true; // Embers already active and in flight when map loads!
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 13f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.095f);
            main.startColor = new Color(1f, 0.80f, 0.30f, 0.9f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 85;

            // Emission
            var emission = ps.emission;
            emission.rateOverTime = 10f;

            // Shape: horizontal emitter bar along the bottom of the map
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(mapWidth + 2f, 0.6f, 0.1f);

            // Velocity over lifetime: gentle upward drift with slight horizontal sway
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.y = new ParticleSystem.MinMaxCurve(0.45f, 0.80f);
            velocity.x = new ParticleSystem.MinMaxCurve(-0.10f, 0.10f);

            // Organic turbulence noise: creates natural lazy drifting and wandering
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.45f;
            noise.scrollSpeed = 0.20f;
            noise.damping = true;

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

            // Three atmospheric steam vent positions in the factory machinery
            Vector2[] ventGridCoords = new Vector2[]
            {
                new Vector2(1.5f, 1.8f),
                new Vector2(5.5f, 2.5f),
                new Vector2(11.5f, 1.8f)
            };

            for (int i = 0; i < ventGridCoords.Length; i++)
            {
                Vector3 ventPos = gridBottomLeft + new Vector3(
                    ventGridCoords[i].x * cellSize,
                    ventGridCoords[i].y * cellSize,
                    -0.08f);

                var ventObj = new GameObject($"SteamVent_{i + 1}");
                ventObj.transform.SetParent(steamRoot.transform, false);
                ventObj.transform.position = ventPos;

                ParticleSystem ps = ventObj.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.loop = true;
                main.playOnAwake = true;
                main.prewarm = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(3.2f, 4.8f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 35;

                var emission = ps.emission;
                emission.rateOverTime = 4.5f;

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

                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-25f * Mathf.Deg2Rad, 25f * Mathf.Deg2Rad);

                var sizeOverLifetime = ps.sizeOverLifetime;
                sizeOverLifetime.enabled = true;
                AnimationCurve steamSizeCurve = new AnimationCurve(
                    new Keyframe(0f, 0.5f),
                    new Keyframe(0.3f, 1.2f),
                    new Keyframe(1f, 2.4f)
                );
                sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, steamSizeCurve);

                var colorOverLifetime = ps.colorOverLifetime;
                colorOverLifetime.enabled = true;
                Gradient steamGrad = new Gradient();
                steamGrad.SetKeys(
                    new GradientColorKey[]
                    {
                        new GradientColorKey(new Color(0.95f, 0.96f, 0.98f), 0f),
                        new GradientColorKey(new Color(0.88f, 0.90f, 0.94f), 1f)
                    },
                    new GradientAlphaKey[]
                    {
                        new GradientAlphaKey(0f, 0f),
                        new GradientAlphaKey(0.24f, 0.2f),
                        new GradientAlphaKey(0.18f, 0.55f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );
                colorOverLifetime.color = new ParticleSystem.MinMaxGradient(steamGrad);

                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 0.25f;
                noise.frequency = 0.5f;
                noise.scrollSpeed = 0.3f;
                noise.damping = true;

                var renderer = ventObj.GetComponent<ParticleSystemRenderer>();
                renderer.sortingOrder = 12; // Above map and tiles, behind floating combat text
                renderer.sharedMaterial = GetSteamMaterial();

                ps.Play();
            }
        }

        /// <summary>
        /// Creates heat distortion shimmer waves that undulate upward from the hot industrial floor.
        /// </summary>
        private void CreateHeatDistortionEffect()
        {
            if (gridManager == null || stageData == null) return;

            float cellSize = gridManager.CellSize;
            float mapWidth = stageData.gridWidth * cellSize;
            float mapHeight = stageData.gridHeight * cellSize;
            Vector3 gridBottomLeft = gridManager.GridToWorldPosition(0, 0)
                - new Vector3(cellSize * 0.5f, cellSize * 0.5f, 0f);
            Vector3 mapCenter = gridBottomLeft + new Vector3(mapWidth * 0.5f, mapHeight * 0.5f, 0f);

            var distortionRoot = new GameObject("Stage4HeatDistortion");
            distortionRoot.transform.SetParent(transform, false);

            var waveObj = new GameObject("HeatHazeShimmer");
            waveObj.transform.SetParent(distortionRoot.transform, false);
            // Anchor emitter near lower third of the map
            waveObj.transform.position = mapCenter + new Vector3(0f, -mapHeight * 0.25f, -0.06f);

            ParticleSystem ps = waveObj.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.6f, 1.2f); // Narrow subtle shimmer wisps
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.08f, 0.15f);
            main.startSizeZ = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 15;

            var emission = ps.emission;
            emission.rateOverTime = 1.5f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(mapWidth + 1f, mapHeight * 0.4f, 0.1f);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.y = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            velocity.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);

            // Horizontal optical waver / turbulence
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.45f;
            noise.frequency = 0.65f;
            noise.scrollSpeed = 0.35f;
            noise.damping = true;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 1.4f));

            // Optical refraction shimmer gradient (translucent with subtle warm highlight)
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient hazeGrad = new Gradient();
            hazeGrad.SetKeys(
                new GradientColorKey[]
                {
                    new GradientColorKey(new Color(1f, 0.97f, 0.93f), 0f),  // Nearly white, barely warm
                    new GradientColorKey(new Color(1f, 0.95f, 0.90f), 0.5f),
                    new GradientColorKey(new Color(1f, 0.97f, 0.95f), 1f)
                },
                new GradientAlphaKey[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.02f, 0.25f),  // Extremely subtle - barely perceptible
                    new GradientAlphaKey(0.015f, 0.65f),
                    new GradientAlphaKey(0f, 1f)
                }
            );
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(hazeGrad);

            var renderer = waveObj.GetComponent<ParticleSystemRenderer>();
            renderer.sortingOrder = 9; // In front of map artwork, behind characters
            renderer.sharedMaterial = GetHeatHazeMaterial();

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

        private static Texture2D CreateSteamPuffTexture()
        {
            const int size = 64;
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
                    float wobble = Mathf.Sin(dx * 6f + dy * 4f) * 0.08f + Mathf.Sin(dx * 11f - dy * 9f) * 0.04f;
                    float edge = 0.85f + wobble;
                    if (r < edge)
                    {
                        float alpha = Mathf.Pow(1f - (r / edge), 1.8f);
                        pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                    }
                    else
                    {
                        pixels[y * size + x] = Color.clear;
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Material heatHazeMaterial;
        private static Material GetHeatHazeMaterial()
        {
            if (heatHazeMaterial == null)
            {
                Shader shader = Shader.Find("Particles/Standard Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                heatHazeMaterial = new Material(shader);
                heatHazeMaterial.mainTexture = CreateHeatHazeTexture();
                // Fade blending for transparent heat shimmer
                heatHazeMaterial.SetFloat("_Mode", 2); // Fade
                heatHazeMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                heatHazeMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                heatHazeMaterial.SetInt("_ZWrite", 0);
                heatHazeMaterial.DisableKeyword("_ALPHATEST_ON");
                heatHazeMaterial.EnableKeyword("_ALPHABLEND_ON");
                heatHazeMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                heatHazeMaterial.renderQueue = 3000;
            }
            return heatHazeMaterial;
        }

        private static Texture2D CreateHeatHazeTexture()
        {
            const int w = 64;
            const int h = 16;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;

            Color[] pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float nx = (x + 0.5f) / w * 2f - 1f;
                    float ny = (y + 0.5f) / h * 2f - 1f;
                    float xFalloff = Mathf.Max(0f, 1f - nx * nx);
                    float yFalloff = Mathf.Max(0f, 1f - ny * ny);
                    float alpha = xFalloff * yFalloff;
                    pixels[y * w + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
