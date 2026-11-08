using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Pompoth operator — Sniper variant with rapid fire.
    /// Damage per shot is reduced, but attack interval is faster, keeping DPS equal to Deadeye.
    /// Passive: +10% attack speed per enemy in range, capped at +100% (double speed) with 10 or more enemies.
    /// Upgrades add target-specific DEF ignore and increase her passive to +30% per enemy, capped at +300%.
    /// </summary>
    public class PompothOperator : SniperOperator
    {
        private const float DamageMultiplier = 0.8f; // 80% of base damage per shot
        private const float IntervalMultiplier = 0.8f; // 80% of base interval (faster)
        public const float DefenseIgnorePerHit = 0.01f;
        public const float MaximumDefenseIgnore = 1f;
        private const int DefenseIgnoreHitCap = 100;
        private const float AttackOriginRadiusMultiplier = 1.35f;
        public const int AttackOriginPointCount = 8;
        private static Material powerStateFillMaterial;

        private GridManager gridManager;
        private GridCell[] cachedRangeCells;
        private SpriteRenderer attackOriginIndicator;
        private SpriteRenderer attackOriginGlow;
        private SpriteRenderer powerStateSprite;
        private int nextAttackOriginIndex;
        private EnemyBase defenseRampTarget;
        private int landedHitsOnDefenseRampTarget;
        private int passiveEnemyCount;
        private float attackSpeedPassiveCap;
        private bool hasPassiveSnapshot;
        private PompothPowerVisuals powerVisuals;

        public bool HasDefenseIgnoreUpgrade => currentRarity >= OperatorRarity.Star2;
        public bool HasIncreasedAttackSpeedUpgrade => currentRarity >= OperatorRarity.Star3;
        public int LandedHitsOnDefenseRampTarget => landedHitsOnDefenseRampTarget;
        public float CurrentAttackSpeedPassiveBonus { get; private set; }
        public float AttackSpeedPassiveCap => GetAttackSpeedPassiveCap(currentRarity);
        public event System.Action<int, float, float> OnAttackSpeedPassiveChanged;

        public override void Initialize(OperatorData operatorData, OperatorRarity rarity, OperatorFacing operatorFacing = OperatorFacing.Right)
        {
            base.Initialize(operatorData, rarity, operatorFacing);
            hasPassiveSnapshot = false;
            UpdateAttackSpeedPassive(0);
        }

        private GridManager GetGridManager()
        {
            if (gridManager == null)
            {
                gridManager = FindAnyObjectByType<GridManager>();
            }
            return gridManager;
        }

        protected override void OnDeployed()
        {
            base.OnDeployed();
            RefreshRangeCells();
            powerVisuals = GetComponent<PompothPowerVisuals>();
            if (powerVisuals != null) powerVisuals.OnTierChanged += HandlePowerTierChanged;
            EnsureAttackOriginIndicator();
            EnsurePowerStateSprite();
            UpdateAttackSpeedPassive(GetEnemyCountInRange());
            SelectNextAttackOrigin();
        }

        protected override void OnRetreated()
        {
            UnsubscribeFromPowerVisuals();
            cachedRangeCells = null;
            defenseRampTarget = null;
            landedHitsOnDefenseRampTarget = 0;
            UpdateAttackSpeedPassive(0);
            UpdateIndicatorVisuals();
            if (attackOriginIndicator != null)
                attackOriginIndicator.enabled = false;
            if (attackOriginGlow != null)
                attackOriginGlow.enabled = false;
            if (powerStateSprite != null)
                powerStateSprite.enabled = false;
            base.OnRetreated();
        }

        protected override void Update()
        {
            base.Update();
            UpdateAttackOriginIndicator();
            UpdatePowerStateGlow();
        }

        private void OnEnable()
        {
            hasPassiveSnapshot = false;
            passiveEnemyCount = 0;
            CurrentAttackSpeedPassiveBonus = 0f;
            attackSpeedPassiveCap = 0f;
            powerVisuals = GetComponent<PompothPowerVisuals>();
            if (powerVisuals != null)
            {
                powerVisuals.ResetToTierZero();
                powerVisuals.OnTierChanged += HandlePowerTierChanged;
            }
            UpdateIndicatorVisuals();
            UpdatePowerStateGlow();
        }

        private void OnDisable()
        {
            UnsubscribeFromPowerVisuals();
            hasPassiveSnapshot = false;
            passiveEnemyCount = 0;
            CurrentAttackSpeedPassiveBonus = 0f;
            attackSpeedPassiveCap = 0f;
            if (powerVisuals != null) powerVisuals.ResetToTierZero();
            UpdateIndicatorVisuals();
            UpdatePowerStateGlow();
        }

        private void UnsubscribeFromPowerVisuals()
        {
            if (powerVisuals != null)
                powerVisuals.OnTierChanged -= HandlePowerTierChanged;
        }

        private void HandlePowerTierChanged(int tier)
        {
            UpdateIndicatorVisuals();
            UpdatePowerStateGlow();
        }

        public override void ApplyFacingVisuals()
        {
            base.ApplyFacingVisuals();
            RefreshRangeCells();
        }

        private void RefreshRangeCells()
        {
            var gm = GetGridManager();
            if (gm != null && deployedCell != null && data != null && data.rangePattern != null)
            {
                cachedRangeCells = gm.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
            }
            else
            {
                cachedRangeCells = null;
            }
        }

        private int GetEnemyCountInRange()
        {
            if (!isDeployed || EnemyManager.Instance == null || data == null) return 0;
            if (cachedRangeCells == null || cachedRangeCells.Length == 0) RefreshRangeCells();
            if (cachedRangeCells == null) return 0;

            // Runs every frame (GetAttackInterval), so count without building a list
            return EnemyManager.Instance.CountEnemiesInCells(cachedRangeCells, data.position);
        }

        public override float GetAttackInterval()
        {
            // Base interval from SniperOperator (includes any global modifiers)
            float baseInterval = base.GetAttackInterval();
            float interval = baseInterval * IntervalMultiplier;

            // Passive bonus based on enemies in range; the second upgrade doubles its per-enemy bonus and cap.
            int enemyCount = GetEnemyCountInRange();
            return interval / UpdateAttackSpeedPassive(enemyCount);
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            SelectDefenseRampTarget(target);
            int hitCountForShot = landedHitsOnDefenseRampTarget;
            PlayAttackSound();
            Vector3 attackOrigin = GetNextAttackOrigin();
            int powerTier = powerVisuals != null ? powerVisuals.CurrentTier : 0;
            Color projectileColor = GetAttackProjectileColor(powerTier, IsCopiedFromFramiliar);
            ProjectileStyle projectileStyle = GetAttackProjectileStyle(powerTier, IsCopiedFromFramiliar);
            CombatProjectileVisual.Fire(
                attackOrigin,
                target,
                projectileColor,
                14f,
                0.12f,
                0.07f,
                false,
                () => ApplyPompothDamage(target, hitCountForShot),
                projectileStyle);
            SelectNextAttackOrigin();
        }

        public static Color GetAttackProjectileColor(int powerTier, bool isCopiedFromFramiliar = false)
        {
            if (isCopiedFromFramiliar) return new Color(0.85f, 0.85f, 0.85f);

            return powerTier switch
            {
                1 => new Color(0.55f, 0.08f, 0.28f),
                2 => Color.red,
                _ => new Color(0.9f, 0.2f, 0.9f)
            };
        }

        public static ProjectileStyle GetAttackProjectileStyle(int powerTier, bool isCopiedFromFramiliar = false)
        {
            return powerTier >= 2 && !isCopiedFromFramiliar
                ? ProjectileStyle.PompothFullPower
                : ProjectileStyle.Bullet;
        }

        public Vector3 GetRandomAttackOrigin()
        {
            int pointIndex = Random.Range(0, AttackOriginPointCount);
            return GetAttackOriginAtPoint(pointIndex);
        }

        public Vector3 GetNextAttackOrigin()
        {
            return GetAttackOriginAtPoint(nextAttackOriginIndex);
        }

        private Vector3 GetAttackOriginAtPoint(int pointIndex)
        {
            float angle = pointIndex * (2f * Mathf.PI / AttackOriginPointCount);
            SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
            float radius = spriteRenderer != null
                ? spriteRenderer.bounds.extents.y * AttackOriginRadiusMultiplier
                : Mathf.Abs(transform.lossyScale.y) * 0.5f;
            Vector3 circleOffset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
            return ProjectileOrigin + circleOffset;
        }

        private void SelectNextAttackOrigin()
        {
            nextAttackOriginIndex = Random.Range(0, AttackOriginPointCount);
        }

        public void UpdateIndicatorVisuals()
        {
            if (attackOriginIndicator == null)
            {
                EnsureAttackOriginIndicator();
            }

            Color diamondColor = GetCurrentAttackColor();
            attackOriginIndicator.sprite = CombatFxArt.Diamond(diamondColor);

            if (attackOriginGlow != null)
            {
                attackOriginGlow.sprite = CombatFxArt.GlowDisc(6);
                attackOriginGlow.color = new Color(diamondColor.r, diamondColor.g, diamondColor.b, 0.24f);
            }
            UpdatePowerStateGlow();
        }

        private Color GetCurrentAttackColor()
        {
            int tier = powerVisuals != null ? powerVisuals.CurrentTier : 0;
            return GetAttackProjectileColor(tier, IsCopiedFromFramiliar);
        }

        private void EnsurePowerStateSprite()
        {
            if (powerStateSprite != null) return;

            var afterimageObject = new GameObject("PompothPowerStateSprite");
            afterimageObject.transform.SetParent(transform, false);
            powerStateSprite = afterimageObject.AddComponent<SpriteRenderer>();
            SpriteRenderer operatorRenderer = GetComponent<SpriteRenderer>();
            if (operatorRenderer != null)
            {
                powerStateSprite.sortingLayerID = operatorRenderer.sortingLayerID;
                powerStateSprite.sortingOrder = operatorRenderer.sortingOrder - 1;
                powerStateSprite.maskInteraction = operatorRenderer.maskInteraction;
            }
            powerStateSprite.sharedMaterial = GetPowerStateFillMaterial();
            powerStateSprite.color = new Color(1f, 0f, 0f, 0.8f);
            powerStateSprite.enabled = false;
        }

        private void UpdatePowerStateGlow()
        {
            if (powerStateSprite == null && isDeployed) EnsurePowerStateSprite();
            if (powerStateSprite == null) return;

            bool isAtFullPower = isDeployed && !IsCopiedFromFramiliar &&
                                 powerVisuals != null && powerVisuals.CurrentTier == 2;
            powerStateSprite.enabled = isAtFullPower;
            if (!isAtFullPower) return;

            SpriteRenderer operatorRenderer = GetComponent<SpriteRenderer>();
            if (operatorRenderer == null || operatorRenderer.sprite == null) return;

            powerStateSprite.sprite = operatorRenderer.sprite;
            powerStateSprite.flipX = operatorRenderer.flipX;
            powerStateSprite.flipY = operatorRenderer.flipY;
            float pulse = (Mathf.Sin(Time.time * 6f) + 1f) * 0.5f;
            float scale = Mathf.Lerp(1.08f, 1.14f, pulse);
            powerStateSprite.transform.localScale = Vector3.one * scale;
            Color redPulse = new Color(1f, 0f, 0f, Mathf.Lerp(0.15f, 0.9f, pulse));
            powerStateSprite.color = redPulse;
        }

        private static Material GetPowerStateFillMaterial()
        {
            if (powerStateFillMaterial != null) return powerStateFillMaterial;

            Shader fillShader = Resources.Load<Shader>("Shaders/SpriteColorFill");
            if (fillShader == null)
            {
                Debug.LogError("Pompoth sprite fill shader is missing from Resources/Shaders.");
                return null;
            }

            powerStateFillMaterial = new Material(fillShader)
            {
                name = "PompothPowerStateFill"
            };
            return powerStateFillMaterial;
        }

        private void EnsureAttackOriginIndicator()
        {
            if (attackOriginIndicator != null) return;

            var indicatorObject = new GameObject("PompothAttackOriginIndicator");
            indicatorObject.transform.SetParent(transform, false);
            attackOriginIndicator = indicatorObject.AddComponent<SpriteRenderer>();
            Color diamondColor = GetCurrentAttackColor();
            attackOriginIndicator.sprite = CombatFxArt.Diamond(diamondColor);
            attackOriginIndicator.sortingLayerID = GetComponent<SpriteRenderer>().sortingLayerID;
            attackOriginIndicator.sortingOrder = GetComponent<SpriteRenderer>().sortingOrder + 2;
            attackOriginIndicator.color = new Color(1f, 1f, 1f, 0.5f);
            attackOriginIndicator.transform.position = GetNextAttackOrigin();
            attackOriginIndicator.transform.localScale = Vector3.one * 0.3f;

            var glowObject = new GameObject("PompothAttackOriginGlow");
            glowObject.transform.SetParent(indicatorObject.transform, false);
            attackOriginGlow = glowObject.AddComponent<SpriteRenderer>();
            attackOriginGlow.sprite = CombatFxArt.GlowDisc(6);
            attackOriginGlow.sortingLayerID = attackOriginIndicator.sortingLayerID;
            attackOriginGlow.sortingOrder = attackOriginIndicator.sortingOrder - 1;
            attackOriginGlow.color = new Color(diamondColor.r, diamondColor.g, diamondColor.b, 0.24f);
            attackOriginGlow.transform.localScale = Vector3.one * 1.8f;
        }

        private void UpdateAttackOriginIndicator()
        {
            if (attackOriginIndicator == null || !isDeployed) return;

            // If Framiliar transform transition is still playing, keep indicator hidden
            if (TryGetComponent<FramiliarTransformTransition>(out var transition) && !transition.IsFinished)
            {
                attackOriginIndicator.enabled = false;
                if (attackOriginGlow != null)
                    attackOriginGlow.enabled = false;
                return;
            }

            attackOriginIndicator.enabled = true;
            if (attackOriginGlow != null)
                attackOriginGlow.enabled = true;
            attackOriginIndicator.transform.position = GetNextAttackOrigin();
            float interval = GetAttackInterval();
            float charge = interval > 0f ? Mathf.Clamp01(attackTimer / interval) : 0f;
            float pulse = 0.92f + Mathf.Sin(Time.time * 8f) * 0.08f;
            float scale = Mathf.Lerp(0.3f, 1.1f, charge) * pulse;
            attackOriginIndicator.transform.localScale = Vector3.one * scale;
            attackOriginIndicator.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.4f, 1f, charge));

            if (attackOriginGlow != null)
            {
                attackOriginGlow.transform.localScale = Vector3.one * (1.8f + charge * 0.3f);
                Color glowBaseColor = GetCurrentAttackColor();
                attackOriginGlow.color = new Color(
                    glowBaseColor.r,
                    glowBaseColor.g,
                    glowBaseColor.b,
                    Mathf.Lerp(0.16f, 0.3f, charge) * pulse);
            }
        }

        private void SelectDefenseRampTarget(EnemyBase target)
        {
            if (defenseRampTarget == target) return;

            defenseRampTarget = target;
            landedHitsOnDefenseRampTarget = 0;
        }

        private void ApplyPompothDamage(EnemyBase target, int hitCountForShot)
        {
            if (target == null || target.IsDead) return;

            int atk = Mathf.RoundToInt(CurrentATK * DamageMultiplier);
            bool isCurrentRampTarget = defenseRampTarget == target;
            int currentHitCount = isCurrentRampTarget
                ? landedHitsOnDefenseRampTarget
                : hitCountForShot;
            int mitigation = data.damageType == DamageType.Physical
                ? CalculateDefenseAfterIgnore(target.CurrentDEF, currentHitCount, HasDefenseIgnoreUpgrade)
                : target.CurrentRES;
            int damage = DamageCalculator.CalculateDamage(atk, mitigation);
            int previousHP = target.CurrentHP;
            if (!target.TryTakeAttackDamage(damage, data.damageType, this) || target.CurrentHP >= previousHP)
                return;

            if (HasDefenseIgnoreUpgrade && isCurrentRampTarget)
            {
                landedHitsOnDefenseRampTarget = Mathf.Max(
                    landedHitsOnDefenseRampTarget,
                    Mathf.Min(currentHitCount + 1, DefenseIgnoreHitCap));
            }
        }

        public static float GetAttackSpeedMultiplier(int enemyCount, OperatorRarity rarity)
        {
            return 1f + GetAttackSpeedPassiveBonus(enemyCount, rarity);
        }

        public static float GetAttackSpeedPassiveBonus(int enemyCount, OperatorRarity rarity)
        {
            float perEnemyBonus = rarity >= OperatorRarity.Star3 ? 0.30f : 0.10f;
            return Mathf.Min(Mathf.Max(0, enemyCount) * perEnemyBonus, GetAttackSpeedPassiveCap(rarity));
        }

        public static float GetAttackSpeedPassiveCap(OperatorRarity rarity)
        {
            return rarity >= OperatorRarity.Star3 ? 3f : 1f;
        }

        private float UpdateAttackSpeedPassive(int enemyCount)
        {
            float cap = AttackSpeedPassiveCap;
            if (hasPassiveSnapshot && enemyCount == passiveEnemyCount &&
                Mathf.Approximately(cap, attackSpeedPassiveCap))
                return 1f + CurrentAttackSpeedPassiveBonus;

            float bonus = GetAttackSpeedPassiveBonus(enemyCount, currentRarity);
            passiveEnemyCount = enemyCount;
            CurrentAttackSpeedPassiveBonus = bonus;
            attackSpeedPassiveCap = cap;
            hasPassiveSnapshot = true;

            if (powerVisuals == null) powerVisuals = GetComponent<PompothPowerVisuals>();
            powerVisuals?.SetPassiveState(enemyCount, bonus, cap);
            OnAttackSpeedPassiveChanged?.Invoke(enemyCount, bonus, cap);

            return 1f + bonus;
        }

        public static int CalculateDefenseAfterIgnore(int defense, int landedHitsOnTarget, bool hasDefenseIgnoreUpgrade)
        {
            if (!hasDefenseIgnoreUpgrade) return Mathf.Max(0, defense);

            float ignore = Mathf.Min(
                Mathf.Clamp(landedHitsOnTarget + 1, 1, DefenseIgnoreHitCap) * DefenseIgnorePerHit,
                MaximumDefenseIgnore);
            return Mathf.Max(0, Mathf.RoundToInt(defense * (1f - ignore)));
        }
    }
}
