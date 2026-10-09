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
    /// Mossmo — 3-Star Defender.
    /// Does not attack enemies, but blocks up to 3 units.
    /// Every time he takes damage, he heals up to 2 operators (including himself)
    /// within adjacent tiles for ~50% of Coalesce's healing power.
    /// </summary>
    public class MossmoOperator : DefenderOperator, IHealer
    {
        private const float HealCooldownDuration = 3.0f;
        private float healCooldownTimer;

        public override void Attack(EnemyBase target)
        {
            // Mossmo does not attack
        }

        protected override EnemyBase FindTarget()
        {
            // Mossmo does not target or attack enemies
            return null;
        }

        protected override void Update()
        {
            base.Update();

            if (healCooldownTimer > 0f)
            {
                healCooldownTimer -= Time.deltaTime;
            }
        }

        public override void TakeDamage(int rawATK, DamageType damageType, EnemyBase attacker = null)
        {
            if (currentHP <= 0 || rawATK <= 0) return;

            GridCell originCell = deployedCell;
            int hpBefore = currentHP;
            base.TakeDamage(rawATK, damageType, attacker);
            int hpAfter = currentHP;

            if (hpBefore > hpAfter && originCell != null && healCooldownTimer <= 0f)
            {
                healCooldownTimer = HealCooldownDuration;
                TriggerReactiveHeal(originCell);
            }
        }

        public void Heal(OperatorBase target)
        {
            if (target == null) return;
            target.Heal(GetHealAmount());
        }

        public int GetHealAmount()
        {
            return currentATK > 0 ? currentATK : 10;
        }

        private void TriggerReactiveHeal(GridCell originCell)
        {
            if (OperatorManager.Instance == null || data == null || originCell == null)
                return;

            GridManager gridManager = FindFirstObjectByType<GridManager>();
            if (gridManager == null) return;

            // Healing range: adjacent tiles
            Vector2Int[] pattern = data.rangePattern != null && data.rangePattern.Length > 0
                ? data.rangePattern
                : new[]
                {
                    new Vector2Int(0, 0),
                    new Vector2Int(1, 0),
                    new Vector2Int(-1, 0),
                    new Vector2Int(0, 1),
                    new Vector2Int(0, -1)
                };

            GridCell[] rangeCells = gridManager.GetCellsInRange(originCell.GridPosition, pattern, Facing);
            List<OperatorBase> candidates = OperatorManager.Instance.GetOperatorsInCells(rangeCells);

            List<OperatorBase> targetsToHeal = new List<OperatorBase>(2);

            // 1. Mossmo himself if still alive and below max HP
            if (currentHP > 0 && currentHP < maxHP && CanReceiveHealing)
            {
                targetsToHeal.Add(this);
            }

            // 2. Adjacent injured allies, sorted by lowest HP percentage
            List<OperatorBase> injuredAllies = new List<OperatorBase>();
            if (candidates != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    OperatorBase ally = candidates[i];
                    if (ally == null || ally == this || !ally.IsDeployed || !ally.CanReceiveHealing || ally.CurrentHP >= ally.MaxHP)
                        continue;

                    injuredAllies.Add(ally);
                }

                injuredAllies.Sort((a, b) =>
                {
                    float ratioA = (float)a.CurrentHP / Mathf.Max(1, a.MaxHP);
                    float ratioB = (float)b.CurrentHP / Mathf.Max(1, b.MaxHP);
                    return ratioA.CompareTo(ratioB);
                });
            }

            // Fill up to 2 total targets
            for (int i = 0; i < injuredAllies.Count && targetsToHeal.Count < 2; i++)
            {
                targetsToHeal.Add(injuredAllies[i]);
            }

            if (targetsToHeal.Count == 0) return;

            int healAmount = GetHealAmount();

            // Play green restorative pulse visual
            Vector3 visualOrigin = originCell.WorldPosition;
            AoeBlastVisual.PlayCircle(visualOrigin, 1.25f, new Color(0.25f, 0.95f, 0.45f, 0.6f));

            for (int i = 0; i < targetsToHeal.Count; i++)
            {
                OperatorBase target = targetsToHeal[i];
                if (target == null) continue;

                if (target == this)
                {
                    HealSelf(healAmount);
                }
                else
                {
                    target.Heal(healAmount);
                }
            }
        }
    }
}

