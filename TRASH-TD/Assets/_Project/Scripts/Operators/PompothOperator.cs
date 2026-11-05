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

        private GridManager gridManager;
        private GridCell[] cachedRangeCells;
        private SpriteRenderer attackOriginIndicator;
        private SpriteRenderer attackOriginGlow;
        private int nextAttackOriginIndex;
        private EnemyBase defenseRampTarget;
        private int landedHitsOnDefenseRampTarget;

        public bool HasDefenseIgnoreUpgrade => currentRarity >= OperatorRarity.Star2;
        public bool HasIncreasedAttackSpeedUpgrade => currentRarity >= OperatorRarity.Star3;
        public int LandedHitsOnDefenseRampTarget => landedHitsOnDefenseRampTarget;

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
            SelectNextAttackOrigin();
            EnsureAttackOriginIndicator();
        }

        protected override void OnRetreated()
        {
            cachedRangeCells = null;
            defenseRampTarget = null;
            landedHitsOnDefenseRampTarget = 0;
            if (attackOriginIndicator != null)
                attackOriginIndicator.enabled = false;
            if (attackOriginGlow != null)
                attackOriginGlow.enabled = false;
            base.OnRetreated();
        }

        protected override void Update()
        {
            base.Update();
            UpdateAttackOriginIndicator();
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
            return interval / GetAttackSpeedMultiplier(enemyCount, currentRarity);
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            SelectDefenseRampTarget(target);
            int hitCountForShot = landedHitsOnDefenseRampTarget;
            PlayAttackSound();
            Vector3 attackOrigin = GetNextAttackOrigin();
            Color projectileColor = IsCopiedFromFramiliar
                ? new Color(0.85f, 0.85f, 0.85f)
                : new Color(0.9f, 0.2f, 0.9f); // distinctive purple color for Pompoth, monochrome for Framiliar copy
            CombatProjectileVisual.Fire(
                attackOrigin,
                target,
                projectileColor,
                14f,
                0.12f,
                0.07f,
                false,
                () => ApplyPompothDamage(target, hitCountForShot),
                ProjectileStyle.Bullet);
            SelectNextAttackOrigin();
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
                return;
            }

            Color diamondColor = IsCopiedFromFramiliar
                ? new Color(0.75f, 0.75f, 0.75f)
                : new Color(0.72f, 0.2f, 1f);
            attackOriginIndicator.sprite = CombatFxArt.Diamond(diamondColor);

            if (attackOriginGlow != null)
            {
                attackOriginGlow.sprite = CombatFxArt.GlowDisc(6);
                Color glowColor = IsCopiedFromFramiliar
                    ? new Color(0.75f, 0.75f, 0.75f, 0.24f)
                    : new Color(0.72f, 0.2f, 1f, 0.24f);
                attackOriginGlow.color = glowColor;
            }
        }

        private void EnsureAttackOriginIndicator()
        {
            if (attackOriginIndicator != null) return;

            var indicatorObject = new GameObject("PompothAttackOriginIndicator");
            indicatorObject.transform.SetParent(transform, false);
            attackOriginIndicator = indicatorObject.AddComponent<SpriteRenderer>();
            Color diamondColor = IsCopiedFromFramiliar
                ? new Color(0.75f, 0.75f, 0.75f)
                : new Color(0.72f, 0.2f, 1f);
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
            Color glowColor = IsCopiedFromFramiliar
                ? new Color(0.75f, 0.75f, 0.75f, 0.24f)
                : new Color(0.72f, 0.2f, 1f, 0.24f);
            attackOriginGlow.color = glowColor;
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
                Color glowBaseColor = IsCopiedFromFramiliar
                    ? new Color(0.75f, 0.75f, 0.75f)
                    : new Color(0.72f, 0.2f, 1f);
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
            float perEnemyBonus = rarity >= OperatorRarity.Star3 ? 0.30f : 0.10f;
            float maxBonus = rarity >= OperatorRarity.Star3 ? 3f : 1f;
            return 1f + Mathf.Min(Mathf.Max(0, enemyCount) * perEnemyBonus, maxBonus);
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
