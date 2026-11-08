using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Proxyshot — Short-range physical sniper that fires a fan of pellets in a spread.
    /// Talent: Ignores the miss mechanic (attacks never miss).
    /// When attacked by an enemy, gains Camouflage for 5 seconds (cannot be targeted by enemies, lower sprite opacity).
    /// </summary>
    public class ProxishotOperator : SniperOperator
    {
        public const float CamouflageDuration = 5.0f;
        public const float CamouflageAlpha = 0.45f;
        public const float CamouflageDefenseBonus = 0.5f;
        public const float CamouflageDefenseDuration = 3f;
        public const float CamouflageHealMaxHpRatio = 0.25f;
        private const int PelletCount = 9;
        private const OperatorRarity FirstUpgradeRarity = OperatorRarity.Star3;
        private const OperatorRarity SecondUpgradeRarity = OperatorRarity.Star4;
        private const float CamouflageCounterattackDamageMultiplier = 0.5f;

        private float camouflageTimer;
        private float camouflageDefenseTimer;
        private float camouflageHealProgress;
        private int camouflageHealApplied;
        private int camouflageHealTotal;
        private OperatorData cachedRangeData;
        private Vector2Int[] cachedUpgradedRangePattern;
        [SerializeField] private Sprite projectileSprite;
        private ProxishotSpriteAnimation spriteAnimation;
        private static Sprite cachedProjectileSprite;
        private static readonly Color ProjectileGlowColor = new Color(0.55f, 0.9f, 0.28f, 0.35f);

        public override bool IgnoresMissMechanic => true;

        public override bool IsCamouflaged => camouflageTimer > 0f;

        public override int CurrentDEF => camouflageDefenseTimer > 0f
            ? Mathf.RoundToInt(base.CurrentDEF * (1f + CamouflageDefenseBonus))
            : base.CurrentDEF;

        public float CamouflageTimeRemaining => Mathf.Max(0f, camouflageTimer);
        public float CamouflageDefenseTimeRemaining => Mathf.Max(0f, camouflageDefenseTimer);
        private Sprite ProjectileSprite => projectileSprite != null ? projectileSprite : FindProjectileSprite();

        private ProxishotSpriteAnimation SpriteAnimation
        {
            get
            {
                if (spriteAnimation == null) spriteAnimation = GetComponent<ProxishotSpriteAnimation>();
                return spriteAnimation;
            }
        }

        public override Vector2Int[] GetRangePattern()
        {
            if (data == null || data.rangePattern == null || currentRarity < SecondUpgradeRarity)
                return data != null ? data.rangePattern : null;

            if (cachedRangeData != data || cachedUpgradedRangePattern == null)
            {
                cachedRangeData = data;
                cachedUpgradedRangePattern = BuildUpgradedRangePattern(data.rangePattern);
            }

            return cachedUpgradedRangePattern;
        }

        public static Vector2Int[] GetRangePatternForRarity(OperatorData operatorData, OperatorRarity rarity)
        {
            if (operatorData == null || operatorData.rangePattern == null || rarity < SecondUpgradeRarity)
                return operatorData != null ? operatorData.rangePattern : null;

            return BuildUpgradedRangePattern(operatorData.rangePattern);
        }

        private static Vector2Int[] BuildUpgradedRangePattern(Vector2Int[] basePattern)
        {
            var extendedPattern = new List<Vector2Int>(basePattern);
            for (int y = -2; y <= 2; y++)
            {
                var tile = new Vector2Int(2, y);
                if (!extendedPattern.Contains(tile))
                    extendedPattern.Add(tile);
            }

            return extendedPattern.ToArray();
        }

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        protected override void Update()
        {
            UpdateAttackWindup();
            base.Update();

            if (camouflageTimer > 0f)
            {
                UpdateCamouflageHealing(Mathf.Min(Time.deltaTime, camouflageTimer));
                camouflageTimer -= Time.deltaTime;
                if (camouflageTimer <= 0f)
                {
                    camouflageTimer = 0f;
                    SetSpriteAlpha(1.0f);
                    if (currentRarity >= FirstUpgradeRarity)
                        camouflageDefenseTimer = CamouflageDefenseDuration;
                }
                else
                {
                    SetSpriteAlpha(CamouflageAlpha);
                }
            }
            else if (camouflageDefenseTimer > 0f)
            {
                camouflageDefenseTimer = Mathf.Max(0f, camouflageDefenseTimer - Time.deltaTime);
            }
        }

        private void UpdateCamouflageHealing(float deltaTime)
        {
            if (currentRarity < FirstUpgradeRarity || IsCombatPaused || deltaTime <= 0f)
                return;

            camouflageHealProgress += camouflageHealTotal * deltaTime / CamouflageDuration;
            int healTarget = Mathf.Min(camouflageHealTotal, Mathf.FloorToInt(camouflageHealProgress));
            int healing = healTarget - camouflageHealApplied;
            if (healing <= 0) return;

            camouflageHealApplied = healTarget;
            HealSelf(healing);
        }

        public override void TakeDamage(int rawATK, DamageType damageType, EnemyBase attacker = null)
        {
            if (IsCombatPaused) return;

            base.TakeDamage(rawATK, damageType, attacker);

            // When attacked by an enemy, gain camouflage for 5 seconds
            if (attacker != null && currentHP > 0)
            {
                ActivateCamouflage();
            }
        }

        public override bool TakeTrueDamage(int damage, bool ignoreShieldNegation = false, bool canDodge = false)
        {
            if (IsCombatPaused || currentHP <= 0 || damage <= 0) return false;

            bool dealt = base.TakeTrueDamage(damage, ignoreShieldNegation, canDodge);
            if (currentHP > 0)
            {
                ActivateCamouflage();
            }
            return dealt;
        }

        public void ActivateCamouflage()
        {
            bool wasCamouflaged = IsCamouflaged;
            camouflageTimer = CamouflageDuration;
            SetSpriteAlpha(CamouflageAlpha);

            if (!wasCamouflaged)
            {
                camouflageDefenseTimer = 0f;
                camouflageHealProgress = 0f;
                camouflageHealApplied = 0;
                camouflageHealTotal = currentRarity >= FirstUpgradeRarity
                    ? Mathf.RoundToInt(maxHP * CamouflageHealMaxHpRatio)
                    : 0;
                FloatingCombatNumber.ShowText(transform.position + Vector3.up * 0.6f, "CAMOUFLAGE!", new Color(0.6f, 0.9f, 1f));
                PlayAbilitySound();
                if (currentRarity >= FirstUpgradeRarity)
                    FireCamouflageCounterattack();
            }
        }

        private void FireCamouflageCounterattack()
        {
            if (!isDeployed) return;

            EnemyBase target = FindTarget();
            if (target != null)
                FireShotgunAttack(target, CamouflageCounterattackDamageMultiplier);
        }

        private void SetSpriteAlpha(float alpha)
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (spriteRenderer != null)
            {
                Color color = spriteRenderer.color;
                if (!Mathf.Approximately(color.a, alpha))
                {
                    color.a = alpha;
                    spriteRenderer.color = color;
                }
            }
        }

        protected override void OnDeployed()
        {
            base.OnDeployed();
            SpriteAnimation?.CancelAttack();
            camouflageTimer = 0f;
            camouflageDefenseTimer = 0f;
            ResetCamouflageHealing();
            SetSpriteAlpha(1.0f);
        }

        protected override void OnRetreated()
        {
            SpriteAnimation?.CancelAttack();
            camouflageTimer = 0f;
            camouflageDefenseTimer = 0f;
            ResetCamouflageHealing();
            SetSpriteAlpha(1.0f);
            base.OnRetreated();
        }

        private void ResetCamouflageHealing()
        {
            camouflageHealProgress = 0f;
            camouflageHealApplied = 0;
            camouflageHealTotal = 0;
        }

        public override void Attack(EnemyBase target)
        {
            FireShotgunAttack(target, 1f);
        }

        private void FireShotgunAttack(EnemyBase target, float damageMultiplier)
        {
            if (target == null || !isDeployed || EnemyManager.Instance == null ||
                deployedCell == null || data == null || GetRangePattern() == null)
                return;

            var gridManager = FindAnyObjectByType<GridManager>();
            if (gridManager == null) return;

            var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, GetRangePattern(), Facing);
            var targets = EnemyManager.Instance.GetEnemiesInCells(rangeCells, data.position);
            if (targets.Count == 0) return;

            Vector3 facingDirection = GetFacingDirection();
            Vector3 perpendicular = Vector3.Cross(facingDirection, Vector3.forward).normalized;
            float rangeDistance = 0f;
            float minLateralOffset = float.PositiveInfinity;
            float maxLateralOffset = float.NegativeInfinity;
            for (int i = 0; i < rangeCells.Length; i++)
            {
                var rangeCell = rangeCells[i];
                if (rangeCell == null) continue;
                Vector3 toCell = rangeCell.WorldPosition - transform.position;
                rangeDistance = Mathf.Max(rangeDistance, Vector3.Dot(toCell, facingDirection));
                float lateralOffset = Vector3.Dot(toCell, perpendicular);
                minLateralOffset = Mathf.Min(minLateralOffset, lateralOffset);
                maxLateralOffset = Mathf.Max(maxLateralOffset, lateralOffset);
            }
            if (rangeDistance <= 0f) return;

            SpriteAnimation?.ShotFired();
            PlayAttackSound();

            minLateralOffset -= gridManager.CellSize * 0.5f;
            maxLateralOffset += gridManager.CellSize * 0.5f;

            CombatProjectileVisual.FireShotgunSpread(
                ProjectileOrigin,
                facingDirection,
                rangeDistance,
                minLateralOffset,
                maxLateralOffset,
                new Color(1f, 0.58f, 0.2f),
                8f,
                0.12f,
                0.06f,
                PelletCount,
                () => ApplyShotgunDamage(targets, damageMultiplier),
                target,
                ProjectileSprite,
                ProjectileGlowColor,
                1f);
        }

        private Sprite FindProjectileSprite()
        {
            if (cachedProjectileSprite != null) return cachedProjectileSprite;

            Sprite[] loadedSprites = Resources.FindObjectsOfTypeAll<Sprite>();
            for (int i = 0; i < loadedSprites.Length; i++)
            {
                if (loadedSprites[i] != null && loadedSprites[i].name == "Proxishot Projectile_0")
                {
                    cachedProjectileSprite = loadedSprites[i];
                    return cachedProjectileSprite;
                }
            }

            Debug.LogError("Proxishot projectile sprite is not assigned or loaded.", this);
            return null;
        }

        private void UpdateAttackWindup()
        {
            ProxishotSpriteAnimation anim = SpriteAnimation;
            if (anim == null || !anim.HasAttackAnimation) return;

            if (!isDeployed)
            {
                anim.CancelAttack();
                return;
            }

            if (IsCombatPaused || isStunned || isBound)
            {
                anim.CancelAttack();
                return;
            }

            if (anim.IsWindingUp)
            {
                EnemyBase windupTarget = FindTarget();
                if (windupTarget == null || !CanAttackTarget(windupTarget))
                    anim.CancelAttack();
                return;
            }

            if (anim.IsRecovering) return;

            float timeUntilShot = GetAttackInterval() - attackTimer;
            if (timeUntilShot <= 0f || timeUntilShot > ProxishotSpriteAnimation.WindupDuration)
                return;

            EnemyBase target = FindTarget();
            if (target != null && CanAttackTarget(target))
                anim.BeginAttack(timeUntilShot);
        }

        private void ApplyShotgunDamage(List<EnemyBase> targets, float damageMultiplier)
        {
            if (targets == null) return;

            for (int i = 0; i < targets.Count; i++)
            {
                var inRangeTarget = targets[i];
                if (inRangeTarget == null || inRangeTarget.IsDead) continue;

                int damage = DamageCalculator.CalculateDamage(CurrentATK, inRangeTarget.CurrentDEF);
                if (!Mathf.Approximately(damageMultiplier, 1f))
                    damage = Mathf.Max(1, Mathf.RoundToInt(damage * damageMultiplier));
                inRangeTarget.TryTakeAttackDamage(damage, DamageType.Physical, this);
            }
        }

        private Vector3 GetFacingDirection()
        {
            return Facing switch
            {
                OperatorFacing.Right => Vector3.right,
                OperatorFacing.Up => Vector3.up,
                OperatorFacing.Left => Vector3.left,
                OperatorFacing.Down => Vector3.down,
                _ => Vector3.right
            };
        }
    }
}
