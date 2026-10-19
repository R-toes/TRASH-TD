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
        private const int PelletCount = 9;

        private float camouflageTimer;
        private SpriteRenderer spriteRenderer;

        public override bool IgnoresMissMechanic => true;

        public override bool IsCamouflaged => camouflageTimer > 0f;

        public float CamouflageTimeRemaining => Mathf.Max(0f, camouflageTimer);

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        protected override void Update()
        {
            base.Update();

            if (camouflageTimer > 0f)
            {
                camouflageTimer -= Time.deltaTime;
                if (camouflageTimer <= 0f)
                {
                    camouflageTimer = 0f;
                    SetSpriteAlpha(1.0f);
                }
                else
                {
                    SetSpriteAlpha(CamouflageAlpha);
                }
            }
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
                FloatingCombatNumber.ShowText(transform.position + Vector3.up * 0.6f, "CAMOUFLAGE!", new Color(0.6f, 0.9f, 1f));
                PlayAbilitySound();
            }
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
            camouflageTimer = 0f;
            SetSpriteAlpha(1.0f);
        }

        protected override void OnRetreated()
        {
            camouflageTimer = 0f;
            SetSpriteAlpha(1.0f);
            base.OnRetreated();
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed || EnemyManager.Instance == null ||
                deployedCell == null || data == null || data.rangePattern == null)
                return;

            var gridManager = FindFirstObjectByType<GridManager>();
            if (gridManager == null) return;

            var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
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

            PlayAttackSound();

            minLateralOffset -= gridManager.CellSize * 0.5f;
            maxLateralOffset += gridManager.CellSize * 0.5f;

            CombatProjectileVisual.FireShotgunSpread(
                transform.position,
                facingDirection,
                rangeDistance,
                minLateralOffset,
                maxLateralOffset,
                new Color(1f, 0.58f, 0.2f),
                8f,
                0.12f,
                0.06f,
                PelletCount,
                () => ApplyShotgunDamage(targets),
                target);
        }

        private void ApplyShotgunDamage(List<EnemyBase> targets)
        {
            if (targets == null) return;

            for (int i = 0; i < targets.Count; i++)
            {
                var inRangeTarget = targets[i];
                if (inRangeTarget == null || inRangeTarget.IsDead) continue;

                int damage = DamageCalculator.CalculateDamage(CurrentATK, inRangeTarget.CurrentDEF);
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
