using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TrashTD.Audio;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Resicochet — 3-Star Caster.
    /// High physical DEF, low magic RES.
    /// Ability:
    /// - Stores 1 charge every 5 seconds (up to 3 charges, default 1).
    /// - When attacking, fires 1 projectile per stored charge at 0.5s intervals.
    /// - Each projectile bounces to surrounding tiles of the hit target (up to 4 targets total),
    ///   reducing damage by 25% on each bounce.
    /// - Sprite row changes dynamically with charge count (Row 1: 1 charge, Row 2: 2 charges, Row 3: 3 charges).
    /// - Floating crystals have an outline aura effect matching Shadeslice's isolation talent,
    ///   scaling, pulsing, and bobbing with the crystals frame-by-frame and matching charge color (Orange -> Green -> Blue).
    /// </summary>
    public class ResicochetOperator : CasterOperator
    {
        public const float ChargeIntervalSeconds = 5.0f;
        public const int MaxChargeCount = 3;
        public const float BurstFireInterval = 0.5f;
        public const int MaxBounceTargets = 4;
        public const float BounceDamageReductionPerHit = 0.25f;

        [Header("Sluggish Effect")]
        [SerializeField] private float sluggishDuration = 3.0f;
        [SerializeField] private float sluggishSpeedMultiplier = 0.6f;
        [SerializeField] private float sluggishAttackIntervalMultiplier = 1.5f;

        public float SluggishDuration => sluggishDuration;
        public float SluggishSpeedMultiplier => sluggishSpeedMultiplier;
        public float SluggishAttackIntervalMultiplier => sluggishAttackIntervalMultiplier;

        public static readonly Color Charge1ProjectileColor = new Color(1f, 0.55f, 0.12f); // Orange (1 charge)
        public static readonly Color Charge2ProjectileColor = new Color(0.2f, 0.95f, 0.35f); // Green (2 charges)
        public static readonly Color Charge3ProjectileColor = new Color(0.2f, 0.65f, 1f);   // Blue (3 charges)

        public static Color GetProjectileColorForCharges(int chargeCount)
        {
            return chargeCount switch
            {
                1 => Charge1ProjectileColor,
                2 => Charge2ProjectileColor,
                3 => Charge3ProjectileColor,
                _ => Charge1ProjectileColor
            };
        }

        public static float GetChargeDamageMultiplier(int chargeCount)
        {
            return chargeCount switch
            {
                2 => 1.10f,
                3 => 1.25f,
                _ => 1.0f
            };
        }

        [Header("Resicochet Charge Animation Frames (4 frames each)")]
        [SerializeField] private Sprite[] charge1Frames;
        [SerializeField] private Sprite[] charge2Frames;
        [SerializeField] private Sprite[] charge3Frames;

        [Header("Resicochet Crystal Outline Frames (4 frames each)")]
        [SerializeField] private Sprite[] crystalCharge1Frames;
        [SerializeField] private Sprite[] crystalCharge2Frames;
        [SerializeField] private Sprite[] crystalCharge3Frames;

        private int charges = 1;
        private float chargeTimer = 0f;
        private bool isFiringBurst = false;
        private Coroutine burstCoroutine;
        private OperatorSpriteAnimation spriteAnim;

        // Shadeslice-style crystal aura & outline renderers
        private SpriteRenderer opSpriteRenderer;
        private SpriteRenderer crystalAuraOuter;
        private SpriteRenderer crystalAuraInner;
        private SpriteRenderer crystalOutlineLeft;
        private SpriteRenderer crystalOutlineRight;
        private SpriteRenderer crystalOutlineUp;
        private SpriteRenderer crystalOutlineDown;

        public int CurrentCharges => charges;
        public float ChargeTimer => chargeTimer;
        public int MaxCharges => MaxChargeCount;
        public bool IsFiringBurst => isFiringBurst;

        public Sprite[] Charge1Frames => charge1Frames;
        public Sprite[] Charge2Frames => charge2Frames;
        public Sprite[] Charge3Frames => charge3Frames;

        public Sprite[] CrystalCharge1Frames => crystalCharge1Frames;
        public Sprite[] CrystalCharge2Frames => crystalCharge2Frames;
        public Sprite[] CrystalCharge3Frames => crystalCharge3Frames;

        public SpriteRenderer CrystalAuraOuter => crystalAuraOuter;
        public SpriteRenderer CrystalAuraInner => crystalAuraInner;
        public SpriteRenderer CrystalOutlineLeft => crystalOutlineLeft;
        public SpriteRenderer CrystalOutlineRight => crystalOutlineRight;
        public SpriteRenderer CrystalOutlineUp => crystalOutlineUp;
        public SpriteRenderer CrystalOutlineDown => crystalOutlineDown;

        public void ConfigureAnimationFrames(Sprite[] c1, Sprite[] c2, Sprite[] c3)
        {
            charge1Frames = c1;
            charge2Frames = c2;
            charge3Frames = c3;
            UpdateChargeVisuals();
        }

        public void ConfigureCrystalAnimationFrames(Sprite[] c1, Sprite[] c2, Sprite[] c3)
        {
            crystalCharge1Frames = c1;
            crystalCharge2Frames = c2;
            crystalCharge3Frames = c3;
            if (crystalAuraOuter != null)
            {
                UpdateCrystalAuras();
            }
        }

        public override void Initialize(OperatorData operatorData, OperatorRarity rarity, OperatorFacing operatorFacing = OperatorFacing.Right)
        {
            base.Initialize(operatorData, rarity, operatorFacing);
            charges = 1;
            chargeTimer = 0f;
            isFiringBurst = false;
            EnsureFramesLoaded();
            UpdateChargeVisuals();
        }

        protected override void OnDeployed()
        {
            base.OnDeployed();
            charges = 1;
            chargeTimer = 0f;
            isFiringBurst = false;
            EnsureFramesLoaded();
            UpdateChargeVisuals();
            CreateCrystalAuras();
            SetCrystalAuraEnabled(true);
            UpdateCrystalAuras();
        }

        private void Awake()
        {
            EnsureFramesLoaded();
            UpdateChargeVisuals();
        }

        private static Texture2D cachedCrystalTexture;
        private static Sprite[] cachedCrystal1Frames;
        private static Sprite[] cachedCrystal2Frames;
        private static Sprite[] cachedCrystal3Frames;

        /// <summary>
        /// Identifies whether a 32x32 frame pixel belongs to the floating crystals.
        /// Evaluated in Unity texture coordinates where localY=0 is bottom and localY=31 is top.
        /// Isolates Crystal 1 (Mid), Crystal 2 (Top-Left), and Crystal 3 (Top) without including body or flame pixels.
        /// </summary>
        public static bool IsCrystalPixelInUnityCoords(int chargeLevel, int localX, int localY)
        {
            int yTop = 31 - localY;

            // Crystal 1 (Mid): present at all charges
            if (localX >= 0 && localX <= 5 && yTop >= 9 && yTop <= 15) return true;

            // Crystal 2 (Top-Left): present at charge 2 and 3
            if (chargeLevel >= 2 && localX >= 2 && localX <= 8 && yTop >= 1 && yTop <= 8) return true;

            // Crystal 3 (Top): present at charge 3 only
            if (chargeLevel >= 3 && localX >= 9 && localX <= 14 && yTop >= 1 && yTop <= 6)
            {
                if (localX == 14 && yTop == 6) return false; // Head flame edge
                return true;
            }

            return false;
        }

        private void EnsureFramesLoaded()
        {
            if (spriteAnim == null)
            {
                spriteAnim = GetComponent<OperatorSpriteAnimation>();
            }

            var sr = GetComponent<SpriteRenderer>();
            Texture2D tex = sr != null && sr.sprite != null ? sr.sprite.texture : null;

#if UNITY_EDITOR
            if (tex == null)
            {
                tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/Operators/Resicochet-Sheet.png");
            }
#endif

            if (charge1Frames == null || charge1Frames.Length == 0 ||
                charge2Frames == null || charge2Frames.Length == 0 ||
                charge3Frames == null || charge3Frames.Length == 0)
            {
#if UNITY_EDITOR
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Operators/Resicochet-Sheet.png");
                if (assets != null && assets.Length > 0)
                {
                    var subSprites = new Dictionary<string, Sprite>();
                    foreach (var a in assets)
                    {
                        if (a is Sprite s) subSprites[s.name] = s;
                    }
                    if (subSprites.ContainsKey("Resicochet_0") && subSprites.ContainsKey("Resicochet_4") && subSprites.ContainsKey("Resicochet_8"))
                    {
                        charge1Frames = new[] { subSprites["Resicochet_0"], subSprites["Resicochet_1"], subSprites["Resicochet_2"], subSprites["Resicochet_3"] };
                        charge2Frames = new[] { subSprites["Resicochet_4"], subSprites["Resicochet_5"], subSprites["Resicochet_6"], subSprites["Resicochet_7"] };
                        charge3Frames = new[] { subSprites["Resicochet_8"], subSprites["Resicochet_9"], subSprites["Resicochet_10"], subSprites["Resicochet_11"] };
                    }
                }
#endif
                if ((charge1Frames == null || charge1Frames.Length == 0) && tex != null)
                {
                    SliceFramesFromTexture(tex);
                }
            }

            EnsureCrystalFramesLoaded(tex);
        }

        private void EnsureCrystalFramesLoaded(Texture2D sourceTex)
        {
            if (crystalCharge1Frames != null && crystalCharge1Frames.Length > 0 &&
                crystalCharge2Frames != null && crystalCharge2Frames.Length > 0 &&
                crystalCharge3Frames != null && crystalCharge3Frames.Length > 0)
            {
                return;
            }

            if (cachedCrystal1Frames != null && cachedCrystal1Frames.Length > 0)
            {
                crystalCharge1Frames = cachedCrystal1Frames;
                crystalCharge2Frames = cachedCrystal2Frames;
                crystalCharge3Frames = cachedCrystal3Frames;
                return;
            }

            Texture2D readable = null;
            if (sourceTex != null && sourceTex.isReadable)
            {
                readable = sourceTex;
            }
            else
            {
                string path = System.IO.Path.Combine(Application.dataPath, "_Project/Art/Operators/Resicochet-Sheet.png");
                if (System.IO.File.Exists(path))
                {
                    try
                    {
                        byte[] raw = System.IO.File.ReadAllBytes(path);
                        readable = new Texture2D(128, 96, TextureFormat.RGBA32, false);
                        readable.filterMode = FilterMode.Point;
                        readable.LoadImage(raw);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[ResicochetOperator] Could not load raw texture bytes: {ex.Message}");
                    }
                }
            }

            if (readable == null) return;

            cachedCrystalTexture = new Texture2D(128, 96, TextureFormat.RGBA32, false);
            cachedCrystalTexture.filterMode = FilterMode.Point;

            Color32[] clearColors = new Color32[128 * 96];
            cachedCrystalTexture.SetPixels32(clearColors);

            for (int col = 0; col < 4; col++)
            {
                int xOffset = col * 32;

                for (int y = 0; y < 96; y++)
                {
                    int row = y / 32;
                    int localY = y % 32;
                    int chargeLevel = row switch
                    {
                        2 => 1,
                        1 => 2,
                        0 => 3,
                        _ => 1
                    };

                    for (int x = 0; x < 32; x++)
                    {
                        int texX = xOffset + x;
                        int texY = y;

                        if (IsCrystalPixelInUnityCoords(chargeLevel, x, localY))
                        {
                            Color srcPixel = readable.GetPixel(texX, texY);
                            if (srcPixel.a > 0.05f)
                            {
                                cachedCrystalTexture.SetPixel(texX, texY, Color.white);
                            }
                        }
                    }
                }
            }

            cachedCrystalTexture.Apply(false);

            float ppu = 32f;
            Vector2 pivot = new Vector2(0.5f, 0.5f);

            cachedCrystal1Frames = new Sprite[4];
            cachedCrystal2Frames = new Sprite[4];
            cachedCrystal3Frames = new Sprite[4];

            for (int col = 0; col < 4; col++)
            {
                cachedCrystal1Frames[col] = Sprite.Create(cachedCrystalTexture, new Rect(col * 32, 64, 32, 32), pivot, ppu);
                cachedCrystal2Frames[col] = Sprite.Create(cachedCrystalTexture, new Rect(col * 32, 32, 32, 32), pivot, ppu);
                cachedCrystal3Frames[col] = Sprite.Create(cachedCrystalTexture, new Rect(col * 32, 0, 32, 32), pivot, ppu);
            }

            crystalCharge1Frames = cachedCrystal1Frames;
            crystalCharge2Frames = cachedCrystal2Frames;
            crystalCharge3Frames = cachedCrystal3Frames;
        }

        private void SliceFramesFromTexture(Texture2D tex)
        {
            float ppu = 32f;
            Vector2 pivot = new Vector2(0.5f, 0.5f);

            // Sprite sheet is 128x96 (4 columns, 3 rows of 32x32)
            // Row 1 (y = 64..96): 1 charge
            // Row 2 (y = 32..64): 2 charges
            // Row 3 (y = 0..32): 3 charges
            charge1Frames = new Sprite[4];
            charge2Frames = new Sprite[4];
            charge3Frames = new Sprite[4];

            for (int col = 0; col < 4; col++)
            {
                charge1Frames[col] = Sprite.Create(tex, new Rect(col * 32, 64, 32, 32), pivot, ppu);
                charge2Frames[col] = Sprite.Create(tex, new Rect(col * 32, 32, 32, 32), pivot, ppu);
                charge3Frames[col] = Sprite.Create(tex, new Rect(col * 32, 0, 32, 32), pivot, ppu);
            }
        }

        public void SetCharges(int amount)
        {
            charges = Mathf.Clamp(amount, 1, MaxChargeCount);
            chargeTimer = 0f;
            UpdateChargeVisuals();
        }

        public void AddCharge()
        {
            if (charges < MaxChargeCount)
            {
                charges++;
                chargeTimer = 0f;
                UpdateChargeVisuals();
                FloatingCombatNumber.ShowText(transform.position, $"CHARGE {charges}!", new Color(0.4f, 0.9f, 1f));
                PlayAbilitySound();
            }
        }

        public void UpdateChargeVisuals()
        {
            Sprite[] activeFrames = charges switch
            {
                1 => charge1Frames,
                2 => charge2Frames,
                3 => charge3Frames,
                _ => charge1Frames
            };

            if (activeFrames == null || activeFrames.Length == 0) return;

            if (spriteAnim == null)
            {
                spriteAnim = GetComponent<OperatorSpriteAnimation>();
            }

            if (spriteAnim != null)
            {
                spriteAnim.Configure(activeFrames);
            }

            if (opSpriteRenderer == null)
            {
                opSpriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (opSpriteRenderer != null && activeFrames[0] != null)
            {
                opSpriteRenderer.sprite = activeFrames[0];
            }

            if (crystalAuraOuter != null && isDeployed)
            {
                UpdateCrystalAuras();
            }
        }

        #region Shadeslice-Style Crystal Aura & Outline Effect

        private void CreateCrystalAuras()
        {
            if (crystalAuraOuter != null) return;

            if (opSpriteRenderer == null)
            {
                opSpriteRenderer = GetComponent<SpriteRenderer>();
            }
            if (opSpriteRenderer == null) return;

            int baseOrder = opSpriteRenderer.sortingOrder;

            // Shadeslice outer & inner aura layers
            crystalAuraOuter = CreateAuraLayer("CrystalAuraOuter", baseOrder - 2, Charge1ProjectileColor);
            crystalAuraInner = CreateAuraLayer("CrystalAuraInner", baseOrder - 1, Charge1ProjectileColor);

            // 1-pixel cardinal outline layers
            crystalOutlineLeft = CreateAuraLayer("CrystalOutlineLeft", baseOrder - 1, Charge1ProjectileColor);
            crystalOutlineRight = CreateAuraLayer("CrystalOutlineRight", baseOrder - 1, Charge1ProjectileColor);
            crystalOutlineUp = CreateAuraLayer("CrystalOutlineUp", baseOrder - 1, Charge1ProjectileColor);
            crystalOutlineDown = CreateAuraLayer("CrystalOutlineDown", baseOrder - 1, Charge1ProjectileColor);
        }

        private SpriteRenderer CreateAuraLayer(string objectName, int sortingOrder, Color color)
        {
            var auraObj = new GameObject(objectName);
            auraObj.transform.SetParent(transform, false);
            auraObj.transform.localPosition = Vector3.zero;

            var sr = auraObj.AddComponent<SpriteRenderer>();
            sr.color = color;
            if (opSpriteRenderer != null)
            {
                sr.sortingLayerID = opSpriteRenderer.sortingLayerID;
                sr.sharedMaterial = opSpriteRenderer.sharedMaterial;
                sr.flipX = opSpriteRenderer.flipX;
                sr.flipY = opSpriteRenderer.flipY;
            }
            sr.enabled = false;
            return sr;
        }

        public void SetCrystalAuraEnabled(bool enabled)
        {
            if (crystalAuraOuter != null) crystalAuraOuter.enabled = enabled;
            if (crystalAuraInner != null) crystalAuraInner.enabled = enabled;
            if (crystalOutlineLeft != null) crystalOutlineLeft.enabled = enabled;
            if (crystalOutlineRight != null) crystalOutlineRight.enabled = enabled;
            if (crystalOutlineUp != null) crystalOutlineUp.enabled = enabled;
            if (crystalOutlineDown != null) crystalOutlineDown.enabled = enabled;
        }

        public void UpdateCrystalAuras()
        {
            if (!isDeployed) return;
            if (crystalAuraOuter == null) CreateCrystalAuras();
            if (crystalAuraOuter == null) return;

            if (opSpriteRenderer == null)
            {
                opSpriteRenderer = GetComponent<SpriteRenderer>();
            }
            if (opSpriteRenderer == null) return;

            float time = Time.time;
            float pulse = 0.5f + 0.5f * Mathf.Sin(time * 5f);
            float flicker = Mathf.Sin(time * 13f) * 0.025f;

            // Determine current animation frame index (0..3) from the operator sprite
            int frameIndex = 0;
            Sprite currentOpSprite = opSpriteRenderer.sprite;
            Sprite[] activeOpFrames = charges switch
            {
                1 => charge1Frames,
                2 => charge2Frames,
                3 => charge3Frames,
                _ => charge1Frames
            };
            if (activeOpFrames != null && currentOpSprite != null)
            {
                for (int i = 0; i < activeOpFrames.Length; i++)
                {
                    if (activeOpFrames[i] == currentOpSprite)
                    {
                        frameIndex = i;
                        break;
                    }
                }
            }

            // Pick the crystal-only sprite for current charges and animation frame
            Sprite[] activeCrystalFrames = charges switch
            {
                1 => crystalCharge1Frames,
                2 => crystalCharge2Frames,
                3 => crystalCharge3Frames,
                _ => crystalCharge1Frames
            };

            Sprite crystalSprite = (activeCrystalFrames != null && frameIndex < activeCrystalFrames.Length)
                ? activeCrystalFrames[frameIndex]
                : null;

            bool isFlipped = opSpriteRenderer.flipX;

            Color outerColor;
            Color innerColor;

            if (charges == 1)
            {
                // Vibrant Pulsing Amber/Orange (Charge 1)
                outerColor = new Color(
                    1.0f,
                    Mathf.Lerp(0.35f, 0.65f, pulse),
                    0.05f,
                    0.38f + pulse * 0.16f + flicker);
                innerColor = new Color(
                    1.0f,
                    Mathf.Lerp(0.55f, 0.85f, pulse),
                    0.12f,
                    0.70f + pulse * 0.20f);
            }
            else if (charges == 2)
            {
                // Vibrant Emerald Green (Charge 2)
                outerColor = new Color(
                    0.08f,
                    Mathf.Lerp(0.75f, 1.0f, pulse),
                    0.20f,
                    0.38f + pulse * 0.16f + flicker);
                innerColor = new Color(
                    0.30f,
                    1.0f,
                    Mathf.Lerp(0.45f, 0.75f, pulse),
                    0.70f + pulse * 0.20f);
            }
            else
            {
                // Radiant Azure/Blue (Charge 3)
                outerColor = new Color(
                    0.12f,
                    Mathf.Lerp(0.50f, 0.80f, pulse),
                    1.0f,
                    0.38f + pulse * 0.16f + flicker);
                innerColor = new Color(
                    0.35f,
                    Mathf.Lerp(0.75f, 0.95f, pulse),
                    1.0f,
                    0.70f + pulse * 0.20f);
            }

            // Sync sprite, flip and colors
            SyncAuraRenderer(crystalAuraOuter, crystalSprite, isFlipped, outerColor);
            SyncAuraRenderer(crystalAuraInner, crystalSprite, isFlipped, innerColor);
            SyncAuraRenderer(crystalOutlineLeft, crystalSprite, isFlipped, innerColor);
            SyncAuraRenderer(crystalOutlineRight, crystalSprite, isFlipped, innerColor);
            SyncAuraRenderer(crystalOutlineUp, crystalSprite, isFlipped, innerColor);
            SyncAuraRenderer(crystalOutlineDown, crystalSprite, isFlipped, innerColor);

            // Shadeslice-style pulsing outer & inner scale and float
            crystalAuraOuter.transform.localScale = Vector3.one * (1.22f + pulse * 0.035f + flicker);
            crystalAuraInner.transform.localScale = Vector3.one * (1.11f + pulse * 0.025f - flicker * 0.5f);
            crystalAuraOuter.transform.localPosition = new Vector3(0f, Mathf.Sin(time * 7f) * 0.012f, 0.01f);
            crystalAuraInner.transform.localPosition = new Vector3(0f, Mathf.Sin(time * 9f + 1f) * 0.008f, 0.005f);

            // Outline 1-pixel cardinal offsets (1 / 32 = 0.03125 world units)
            float pixelOffset = (1.0f + pulse * 0.3f) / 32f;
            float flipDir = isFlipped ? -1f : 1f;

            crystalOutlineLeft.transform.localPosition = new Vector3(-pixelOffset * flipDir, 0f, 0.005f);
            crystalOutlineRight.transform.localPosition = new Vector3(pixelOffset * flipDir, 0f, 0.005f);
            crystalOutlineUp.transform.localPosition = new Vector3(0f, pixelOffset, 0.005f);
            crystalOutlineDown.transform.localPosition = new Vector3(0f, -pixelOffset, 0.005f);
        }

        private void SyncAuraRenderer(SpriteRenderer r, Sprite s, bool flipX, Color c)
        {
            if (r == null) return;
            r.sprite = s;
            r.flipX = flipX;
            r.color = c;
            if (opSpriteRenderer != null)
            {
                r.sortingLayerID = opSpriteRenderer.sortingLayerID;
            }
        }

        #endregion

        private TrashTD.Core.GameLoop.WaveManager cachedWaveManager;

        public bool IsWaveActive()
        {
            if (cachedWaveManager == null)
            {
                cachedWaveManager = FindAnyObjectByType<TrashTD.Core.GameLoop.WaveManager>();
            }

            if (cachedWaveManager != null)
            {
                return cachedWaveManager.IsWaveInProgress;
            }

            if (TrashTD.Core.GameLoop.GameManager.Instance != null)
            {
                return TrashTD.Core.GameLoop.GameManager.Instance.CurrentPhase == TrashTD.Core.GameLoop.StagePhase.WaveActive;
            }

            return true;
        }

        protected override void Update()
        {
            if (!isDeployed) return;

            // Charge accumulation: 1 charge every 5 seconds (up to 3 charges).
            // Never charges during preparation or draft phase; only during active wave combat.
            bool isWaveActive = IsWaveActive();
            if (isWaveActive && !isStunned && !isBound && charges < MaxChargeCount)
            {
                chargeTimer += Time.deltaTime;
                if (chargeTimer >= ChargeIntervalSeconds)
                {
                    AddCharge();
                }
            }
            else if (!isWaveActive)
            {
                chargeTimer = 0f;
            }

            // If firing a burst, hold the attack timer at 0
            if (isFiringBurst)
            {
                attackTimer = 0f;
            }

            base.Update();

            UpdateCrystalAuras();
        }

        public override bool CanAttackTarget(EnemyBase target)
        {
            return base.CanAttackTarget(target);
        }

        /// <summary>
        /// Returns world positions of the floating crystals based on charge count and operator facing:
        /// - 1 charge: 1 crystal (mid)
        /// - 2 charges: 2 crystals (mid, top)
        /// - 3 charges: 3 crystals (mid, top, bot)
        /// </summary>
        public Vector3[] GetCrystalPositions(int chargeCount)
        {
            int count = Mathf.Clamp(chargeCount, 1, 3);
            Vector3[] positions = new Vector3[count];
            float flipFactor = (Facing == OperatorFacing.Right) ? -1f : 1f;

            // Pixel-derived crystal offsets relative to sprite center (32 PPU)
            Vector3 c1 = transform.position + new Vector3(-0.39f * flipFactor, 0.09f, 0f);
            Vector3 c2 = transform.position + new Vector3(-0.33f * flipFactor, 0.31f, 0f);
            Vector3 c3 = transform.position + new Vector3(-0.11f * flipFactor, 0.38f, 0f);

            if (count == 1)
            {
                positions[0] = c1;
            }
            else if (count == 2)
            {
                positions[0] = c1;
                positions[1] = c2;
            }
            else
            {
                positions[0] = c1;
                positions[1] = c2;
                positions[2] = c3;
            }
            return positions;
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed || isStunned || isBound || isFiringBurst) return;

            HasFoughtInWave = true;
            DeployedDuringCurrentPreparation = false;

            int chargeLevel = charges;
            float chargeMultiplier = GetChargeDamageMultiplier(chargeLevel);
            Color projectileColor = GetProjectileColorForCharges(chargeLevel);

            // Consume extra charges back to default 1
            charges = 1;
            chargeTimer = 0f;
            UpdateChargeVisuals();

            PlayAttackSound();
            FireChargeAttack(target, chargeLevel, chargeMultiplier, projectileColor);
        }

        protected override void FireAttackProjectile(EnemyBase target, Vector3 startOffset = default)
        {
            FireChargeAttack(target, charges, GetChargeDamageMultiplier(charges), GetProjectileColorForCharges(charges));
        }

        public void FireChargeAttack(EnemyBase target, int chargeLevel, float chargeMultiplier, Color projectileColor)
        {
            if (target == null) return;

            Vector3[] crystals = GetCrystalPositions(chargeLevel);
            Vector3 targetPos = target.VisualPosition;

            // Flashy laser/beam lines from crystals to target
            ResicochetAttackBeamVisual.PlayFromCrystals(crystals, targetPos, projectileColor);

            // Fire projectiles along each crystal beam line
            for (int i = 0; i < crystals.Length; i++)
            {
                Vector3 crystalPos = crystals[i];
                bool isPrimary = (i == 0);

                CombatProjectileVisual.Fire(
                    crystalPos,
                    target,
                    projectileColor,
                    14f,
                    0.24f,
                    0.12f,
                    true,
                    isPrimary ? () => HandleHitAndBounce(target, target, 0, new HashSet<EnemyBase> { target }, chargeLevel, chargeMultiplier, projectileColor) : null,
                    ProjectileStyle.ArtsOrb);
            }
        }

        public void FireSingleBouncingProjectile(EnemyBase target, float chargeMultiplier = 1.0f, Color? projectileColor = null)
        {
            Color color = projectileColor ?? GetProjectileColorForCharges(1);
            FireChargeAttack(target, 1, chargeMultiplier, color);
        }

        public void HandleHitAndBounce(EnemyBase primaryTarget, EnemyBase currentTarget, int bounceIndex, HashSet<EnemyBase> hitEnemies, float chargeMultiplier = 1.0f, Color? projectileColor = null)
        {
            HandleHitAndBounce(primaryTarget, currentTarget, bounceIndex, hitEnemies, 1, chargeMultiplier, projectileColor);
        }

        /// <summary>
        /// Handles projectile hit and bouncing:
        /// - Deals Arts damage with 25% reduction per bounce.
        /// - Scales with charge multiplier (1 charge: 1.0x, 2 charges: 1.10x, 3 charges: 1.25x).
        /// - Applies Sluggish effect to enemies hit.
        /// - Tints damaged enemies to the highest charge color dealt to them.
        /// - Shows 1, 2, or 3 flashy parallel bounce lines to each bounce target.
        /// - Bounces up to 4 targets total (initial hit + 3 bounces).
        /// </summary>
        public void HandleHitAndBounce(
            EnemyBase primaryTarget,
            EnemyBase currentTarget,
            int bounceIndex,
            HashSet<EnemyBase> hitEnemies,
            int chargeLevel,
            float chargeMultiplier = 1.0f,
            Color? projectileColor = null)
        {
            Color color = projectileColor ?? GetProjectileColorForCharges(chargeLevel);

            if (currentTarget != null && !currentTarget.IsDead)
            {
                float bounceMultiplier = Mathf.Max(0.1f, 1f - (bounceIndex * BounceDamageReductionPerHit));
                int rawAtk = Mathf.RoundToInt(CurrentATK * chargeMultiplier * bounceMultiplier);
                int damage = DamageCalculator.CalculateDamage(rawAtk, currentTarget.CurrentRES);
                currentTarget.TryTakeAttackDamage(damage, DamageType.Arts, this);
                currentTarget.ApplySluggish(sluggishDuration, sluggishSpeedMultiplier, sluggishAttackIntervalMultiplier);
                currentTarget.ApplyResicochetHit(chargeLevel);
                PlayImpactSound();
            }

            // If we have not reached max targets (4), search for next bounce target
            if (bounceIndex + 1 < MaxBounceTargets)
            {
                EnemyBase nextTarget = FindNextBounceTarget(primaryTarget, currentTarget, hitEnemies);
                if (nextTarget != null)
                {
                    hitEnemies.Add(nextTarget);
                    Vector3 bounceStart = currentTarget != null ? currentTarget.VisualPosition : transform.position;
                    Vector3 bounceEnd = nextTarget.VisualPosition;

                    // Play flashy parallel bounce lines matching charge
                    ResicochetAttackBeamVisual.PlayParallel(bounceStart, bounceEnd, chargeLevel, color);

                    CombatProjectileVisual.Fire(
                        bounceStart,
                        nextTarget,
                        color,
                        15f,
                        0.2f,
                        0.1f,
                        true,
                        () => HandleHitAndBounce(primaryTarget, nextTarget, bounceIndex + 1, hitEnemies, chargeLevel, chargeMultiplier, color),
                        ProjectileStyle.ArtsOrb);
                }
            }
        }

        /// <summary>
        /// Finds an enemy on the surrounding tiles of the primary target or current target.
        /// Excludes enemies already hit in this bounce chain.
        /// </summary>
        public EnemyBase FindNextBounceTarget(EnemyBase primaryTarget, EnemyBase currentTarget, HashSet<EnemyBase> hitEnemies)
        {
            // First check surrounding tiles of primary target
            var candidates = GetEnemiesInSurroundingTiles(primaryTarget, hitEnemies);
            if (candidates.Count == 0 && currentTarget != null && currentTarget != primaryTarget)
            {
                // Fallback to surrounding tiles of current hit target
                candidates = GetEnemiesInSurroundingTiles(currentTarget, hitEnemies);
            }

            if (candidates.Count == 0) return null;

            Vector3 fromPos = currentTarget != null ? currentTarget.transform.position : transform.position;
            EnemyBase best = null;
            float closestDist = float.PositiveInfinity;

            for (int i = 0; i < candidates.Count; i++)
            {
                var enemy = candidates[i];
                if (enemy == null || enemy.IsDead) continue;

                float dist = Vector3.SqrMagnitude(enemy.transform.position - fromPos);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    best = enemy;
                }
            }

            return best;
        }

        private List<EnemyBase> GetEnemiesInSurroundingTiles(EnemyBase center, HashSet<EnemyBase> exclude)
        {
            var result = new List<EnemyBase>();
            if (center == null || EnemyManager.Instance == null) return result;

            var grid = FindAnyObjectByType<GridManager>();
            if (grid != null)
            {
                Vector2Int centerGrid = grid.WorldToGridPosition(center.transform.position);

                var cells = new List<GridCell>(9);
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        GridCell cell = grid.GetCell(centerGrid.x + dx, centerGrid.y + dy);
                        if (cell != null) cells.Add(cell);
                    }
                }

                var inCells = EnemyManager.Instance.GetEnemiesInCells(cells, data != null ? data.position : OperatorPosition.Ranged);
                if (inCells != null)
                {
                    for (int i = 0; i < inCells.Count; i++)
                    {
                        var e = inCells[i];
                        if (e != null && !e.IsDead && (exclude == null || !exclude.Contains(e)) && CanAttackTarget(e))
                        {
                            result.Add(e);
                        }
                    }
                }
            }
            else
            {
                // Fallback for tests or scenes without GridManager (1.6 world units radius)
                var inRadius = EnemyManager.Instance.GetEnemiesInRadius(center.transform.position, 1.6f, data != null ? data.position : OperatorPosition.Ranged);
                if (inRadius != null)
                {
                    for (int i = 0; i < inRadius.Count; i++)
                    {
                        var e = inRadius[i];
                        if (e != null && !e.IsDead && (exclude == null || !exclude.Contains(e)) && CanAttackTarget(e))
                        {
                            result.Add(e);
                        }
                    }
                }
            }

            return result;
        }

        protected override void OnRetreated()
        {
            if (burstCoroutine != null)
            {
                StopCoroutine(burstCoroutine);
                burstCoroutine = null;
            }
            isFiringBurst = false;
            charges = 1;
            chargeTimer = 0f;
            SetCrystalAuraEnabled(false);
            base.OnRetreated();
        }
    }
}
