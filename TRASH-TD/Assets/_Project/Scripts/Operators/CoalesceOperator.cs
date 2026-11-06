using System;
using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Coalesce operator — 1-Star Medic.
    /// Base (1★): Healing an operator with HP below 30% has a 20% chance to trigger an extra healing.
    /// 1x Upgraded (2★): Extra healing chance updated to 40%.
    /// 2x Upgraded (3★): HP requirement updated to 60%, Increases Max HP of operators in range by 10%.
    /// </summary>
    public class CoalesceOperator : MedicOperator
    {
        public const float BaseHpThreshold = 0.30f;
        public const float UpgradedHpThreshold = 0.60f;

        public const float BaseExtraHealChance = 0.20f;
        public const float UpgradedExtraHealChance = 0.40f;

        public const float MaxHpBuffPercent = 0.10f;

        public bool HasFirstUpgrade => currentRarity >= OperatorRarity.Star2;
        public bool HasSecondUpgrade => currentRarity >= OperatorRarity.Star3;

        public float CurrentHpThreshold => HasSecondUpgrade ? UpgradedHpThreshold : BaseHpThreshold;
        public float CurrentExtraHealChance => HasFirstUpgrade ? UpgradedExtraHealChance : BaseExtraHealChance;

        /// <summary>
        /// Optional random value generator for testing extra heal chance in [0, 1).
        /// </summary>
        public Func<float> RandomValueProvider { get; set; }

        private readonly Dictionary<OperatorBase, int> appliedMaxHpBuffs = new Dictionary<OperatorBase, int>();
        private readonly List<OperatorBase> tempCleanupList = new List<OperatorBase>();

        public IReadOnlyDictionary<OperatorBase, int> AppliedMaxHpBuffs => appliedMaxHpBuffs;

        public bool TryRollExtraHeal(float roll = -1f)
        {
            if (roll < 0f)
            {
                roll = RandomValueProvider != null ? RandomValueProvider() : UnityEngine.Random.value;
            }
            return roll < CurrentExtraHealChance;
        }

        public override void Heal(OperatorBase target)
        {
            if (target == null) return;

            bool wasBelowThreshold = target.MaxHP > 0 && ((float)target.CurrentHP / target.MaxHP) < CurrentHpThreshold;

            base.Heal(target);

            if (wasBelowThreshold && TryRollExtraHeal())
            {
                int extraAmount = GetHealAmount();
                target.Heal(extraAmount);
                PlayAbilitySound();
                FloatingCombatNumber.ShowText(target.transform.position + Vector3.up * 0.6f, "EXTRA HEAL!", new Color(0.25f, 1f, 0.38f));
            }
        }

        protected override void Update()
        {
            base.Update();

            if (HasSecondUpgrade && isDeployed && currentHP > 0)
            {
                UpdateMaxHpAura();
            }
        }

        protected override void OnDeployed()
        {
            base.OnDeployed();
            if (HasSecondUpgrade)
            {
                UpdateMaxHpAura();
            }
        }

        /// <summary>
        /// Updates the 2x Upgrade Max HP aura (+10% Max HP to operators in range).
        /// </summary>
        public void UpdateMaxHpAura()
        {
            if (!HasSecondUpgrade || !isDeployed || currentHP <= 0)
            {
                if (appliedMaxHpBuffs.Count > 0)
                {
                    ClearMaxHpAura();
                }
                return;
            }

            List<OperatorBase> currentInRange = GetOperatorsInRange();

            // 1. Remove buff from operators no longer in range or no longer deployed
            tempCleanupList.Clear();
            foreach (var kvp in appliedMaxHpBuffs)
            {
                OperatorBase op = kvp.Key;
                if (op == null || !op.IsDeployed || op.CurrentHP <= 0 || currentInRange == null || !currentInRange.Contains(op))
                {
                    tempCleanupList.Add(op);
                }
            }

            for (int i = 0; i < tempCleanupList.Count; i++)
            {
                OperatorBase op = tempCleanupList[i];
                if (appliedMaxHpBuffs.TryGetValue(op, out int bonus))
                {
                    if (op != null && op.IsDeployed && op.CurrentHP > 0)
                    {
                        op.ModifyMaxHP(-bonus);
                    }
                    appliedMaxHpBuffs.Remove(op);
                }
            }

            // 2. Add buff to newly detected operators in range
            if (currentInRange != null)
            {
                for (int i = 0; i < currentInRange.Count; i++)
                {
                    OperatorBase op = currentInRange[i];
                    if (op != null && op.IsDeployed && op.CurrentHP > 0 && !appliedMaxHpBuffs.ContainsKey(op))
                    {
                        int bonus = Mathf.Max(1, Mathf.RoundToInt(op.MaxHP * MaxHpBuffPercent));
                        op.ModifyMaxHP(bonus);
                        appliedMaxHpBuffs[op] = bonus;
                    }
                }
            }
        }

        public void ClearMaxHpAura()
        {
            foreach (var kvp in appliedMaxHpBuffs)
            {
                OperatorBase op = kvp.Key;
                int bonus = kvp.Value;
                if (op != null && op.IsDeployed && op.CurrentHP > 0)
                {
                    op.ModifyMaxHP(-bonus);
                }
            }
            appliedMaxHpBuffs.Clear();
        }

        public List<OperatorBase> GetOperatorsInRange()
        {
            GridCell originCell = deployedCell;
            GridManager gridManager = FindAnyObjectByType<GridManager>();
            if (gridManager == null && OperatorManager.Instance != null)
            {
                gridManager = OperatorManager.Instance.GridManager;
            }

            if (originCell == null && gridManager != null)
            {
                originCell = gridManager.GetCell(gridManager.WorldToGridPosition(transform.position));
            }

            if (OperatorManager.Instance != null && gridManager != null && originCell != null)
            {
                Vector2Int[] pattern = GetRangePattern();
                if (pattern != null)
                {
                    GridCell[] rangeCells = gridManager.GetCellsInRange(originCell.GridPosition, pattern, Facing);
                    return OperatorManager.Instance.GetOperatorsInCells(rangeCells);
                }
            }

            return GetNearbyOperatorsFallback();
        }

        private List<OperatorBase> GetNearbyOperatorsFallback()
        {
            var list = new List<OperatorBase>();
            var allOps = FindObjectsByType<OperatorBase>();
            float maxDist = (data != null && data.attackRange > 0) ? data.attackRange + 0.5f : 2.5f;

            for (int i = 0; i < allOps.Length; i++)
            {
                OperatorBase op = allOps[i];
                if (op != null && op.IsDeployed && op.CurrentHP > 0)
                {
                    float dx = Mathf.Abs(op.transform.position.x - transform.position.x);
                    float dy = Mathf.Abs(op.transform.position.y - transform.position.y);
                    if (dx <= maxDist && dy <= maxDist)
                    {
                        list.Add(op);
                    }
                }
            }
            return list;
        }

        protected override void OnRetreated()
        {
            ClearMaxHpAura();
            base.OnRetreated();
        }

        protected override void Die()
        {
            ClearMaxHpAura();
            base.Die();
        }

        protected override void OnDeath()
        {
            ClearMaxHpAura();
            base.OnDeath();
        }

        private void OnDestroy()
        {
            ClearMaxHpAura();
        }

        public override string GetSkillDescription() => GetSkillDescription(data, currentRarity);
    }
}
