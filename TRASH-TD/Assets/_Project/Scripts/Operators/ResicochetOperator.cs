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

        private int charges = 1;
        private float chargeTimer = 0f;
        private bool isFiringBurst = false;
        private Coroutine burstCoroutine;
        private OperatorSpriteAnimation spriteAnim;

        public int CurrentCharges => charges;
        public float ChargeTimer => chargeTimer;
        public int MaxCharges => MaxChargeCount;
        public bool IsFiringBurst => isFiringBurst;

        public Sprite[] Charge1Frames => charge1Frames;
        public Sprite[] Charge2Frames => charge2Frames;
        public Sprite[] Charge3Frames => charge3Frames;

        public void ConfigureAnimationFrames(Sprite[] c1, Sprite[] c2, Sprite[] c3)
        {
            charge1Frames = c1;
            charge2Frames = c2;
            charge3Frames = c3;
            UpdateChargeVisuals();
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
        }

        private void Awake()
        {
            EnsureFramesLoaded();
            UpdateChargeVisuals();
        }

        private void EnsureFramesLoaded()
        {
            if (spriteAnim == null)
            {
                spriteAnim = GetComponent<OperatorSpriteAnimation>();
            }

            if (charge1Frames != null && charge1Frames.Length > 0 &&
                charge2Frames != null && charge2Frames.Length > 0 &&
                charge3Frames != null && charge3Frames.Length > 0)
            {
                return;
            }

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
                    return;
                }
            }
#endif

            // Attempt to load from SpriteRenderer texture or AssetDatabase
            var sr = GetComponent<SpriteRenderer>();
            Texture2D tex = sr != null && sr.sprite != null ? sr.sprite.texture : null;

#if UNITY_EDITOR
            if (tex == null)
            {
                tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/Operators/Resicochet-Sheet.png");
            }
#endif

            if (tex != null)
            {
                SliceFramesFromTexture(tex);
            }
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

            var sr = GetComponent<SpriteRenderer>();
            if (sr != null && activeFrames[0] != null)
            {
                sr.sprite = activeFrames[0];
            }
        }

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
        }

        public override bool CanAttackTarget(EnemyBase target)
        {
            if (isFiringBurst) return false;
            return base.CanAttackTarget(target);
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed || isStunned || isBound || isFiringBurst) return;

            int burstCount = charges;
            float chargeMultiplier = GetChargeDamageMultiplier(charges);

            // Consume extra charges back to default 1
            charges = 1;
            chargeTimer = 0f;
            UpdateChargeVisuals();

            if (burstCoroutine != null)
            {
                StopCoroutine(burstCoroutine);
            }
            burstCoroutine = StartCoroutine(FireBurstRoutine(target, burstCount, chargeMultiplier));
        }

        private IEnumerator FireBurstRoutine(EnemyBase initialTarget, int shotsToFire, float chargeMultiplier)
        {
            isFiringBurst = true;

            for (int i = 0; i < shotsToFire; i++)
            {
                if (!isDeployed || isStunned || isBound || currentHP <= 0) break;

                EnemyBase currentEnemy = initialTarget;
                if (currentEnemy == null || currentEnemy.IsDead || !CanAttackTarget(currentEnemy))
                {
                    currentEnemy = FindTarget();
                }

                if (currentEnemy != null && !currentEnemy.IsDead)
                {
                    PlayAttackSound();
                    FireSingleBouncingProjectile(currentEnemy, chargeMultiplier);
                }

                if (i < shotsToFire - 1)
                {
                    yield return new WaitForSeconds(BurstFireInterval);
                }
            }

            isFiringBurst = false;
            attackTimer = 0f;
            burstCoroutine = null;
        }

        public void FireSingleBouncingProjectile(EnemyBase target, float chargeMultiplier = 1.0f)
        {
            if (target == null) return;

            Vector3 startPos = ProjectileOrigin;
            EnemyBase primaryTarget = target;

            CombatProjectileVisual.Fire(
                startPos,
                primaryTarget,
                new Color(0.85f, 0.45f, 1f),
                13f,
                0.24f,
                0.12f,
                true,
                () => HandleHitAndBounce(primaryTarget, primaryTarget, 0, new HashSet<EnemyBase> { primaryTarget }, chargeMultiplier),
                ProjectileStyle.ArtsOrb);
        }

        /// <summary>
        /// Handles projectile hit and bouncing:
        /// - Deals Arts damage with 25% reduction per bounce.
        /// - Scales with charge multiplier (1 charge: 1.0x, 2 charges: 1.10x, 3 charges: 1.25x).
        /// - Applies Sluggish effect to enemies hit.
        /// - Bounces up to 4 targets total (initial hit + 3 bounces).
        /// </summary>
        public void HandleHitAndBounce(EnemyBase primaryTarget, EnemyBase currentTarget, int bounceIndex, HashSet<EnemyBase> hitEnemies, float chargeMultiplier = 1.0f)
        {
            if (currentTarget != null && !currentTarget.IsDead)
            {
                float bounceMultiplier = Mathf.Max(0.1f, 1f - (bounceIndex * BounceDamageReductionPerHit));
                int rawAtk = Mathf.RoundToInt(CurrentATK * chargeMultiplier * bounceMultiplier);
                int damage = DamageCalculator.CalculateDamage(rawAtk, currentTarget.CurrentRES);
                currentTarget.TryTakeAttackDamage(damage, DamageType.Arts, this);
                currentTarget.ApplySluggish(sluggishDuration, sluggishSpeedMultiplier, sluggishAttackIntervalMultiplier);
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

                    CombatProjectileVisual.Fire(
                        bounceStart,
                        nextTarget,
                        new Color(0.95f, 0.6f, 1f),
                        15f,
                        0.2f,
                        0.1f,
                        true,
                        () => HandleHitAndBounce(primaryTarget, nextTarget, bounceIndex + 1, hitEnemies, chargeMultiplier),
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
            base.OnRetreated();
        }
    }
}

