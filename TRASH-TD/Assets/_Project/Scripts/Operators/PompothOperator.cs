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
    /// </summary>
    public class PompothOperator : SniperOperator
    {
        private const float DamageMultiplier = 0.8f; // 80% of base damage per shot
        private const float IntervalMultiplier = 0.8f; // 80% of base interval (faster)
        private const float AttackOriginRadiusMultiplier = 1.35f;
        public const int AttackOriginPointCount = 8;

        private GridManager gridManager;
        private GridCell[] cachedRangeCells;
        private SpriteRenderer attackOriginIndicator;
        private SpriteRenderer attackOriginGlow;
        private int nextAttackOriginIndex;

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

            // Passive bonus based on enemies in range: +10% attack speed per enemy, capped at +100%
            int enemyCount = GetEnemyCountInRange();
            float bonus = Mathf.Clamp01(enemyCount * 0.10f);
            // Attack speed scales by (1 + bonus), so the interval divides by it: at most half the interval
            interval /= 1f + bonus;
            return interval;
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            PlayAttackSound();
            Vector3 attackOrigin = GetNextAttackOrigin();
            CombatProjectileVisual.Fire(
                attackOrigin,
                target,
                new Color(0.9f, 0.2f, 0.9f), // distinctive purple color for Pompoth
                14f,
                0.12f,
                0.07f,
                false,
                () => ApplyPompothDamage(target),
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

        private void EnsureAttackOriginIndicator()
        {
            if (attackOriginIndicator != null) return;

            var indicatorObject = new GameObject("PompothAttackOriginIndicator");
            indicatorObject.transform.SetParent(transform, false);
            attackOriginIndicator = indicatorObject.AddComponent<SpriteRenderer>();
            attackOriginIndicator.sprite = CombatFxArt.Diamond(new Color(0.72f, 0.2f, 1f));
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
            attackOriginGlow.color = new Color(0.72f, 0.2f, 1f, 0.24f);
            attackOriginGlow.transform.localScale = Vector3.one * 1.8f;
        }

        private void UpdateAttackOriginIndicator()
        {
            if (attackOriginIndicator == null || !isDeployed) return;

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
                attackOriginGlow.color = new Color(0.72f, 0.2f, 1f, Mathf.Lerp(0.16f, 0.3f, charge) * pulse);
            }
        }

        private void ApplyPompothDamage(EnemyBase target)
        {
            if (target == null || target.IsDead) return;

            int atk = Mathf.RoundToInt(CurrentATK * DamageMultiplier);
            int damage = DamageCalculator.CalculateDamage(atk, GetTargetMitigation(target));
            target.TryTakeAttackDamage(damage, data.damageType, this);
        }
    }
}
