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
    /// Taunt mechanic: Ranged enemies prioritize attacking Mossmo when the attacker is in a surrounding tile.
    /// Every time he takes damage, he heals up to 2 operators in range (including himself).
    /// Heal cooldown: 1s when attacked by enemy, 3s when attacked by environment (acid rain/acid pool).
    /// </summary>
    public class MossmoOperator : DefenderOperator, IHealer
    {
        public const float EnemyHealCooldownDuration = 1.0f;
        public const float EnvironmentHealCooldownDuration = 3.0f;
        public const float StallEnrageDuration = 20.0f;
        private float healCooldownTimer;
        public float HealCooldownTimer => healCooldownTimer;

        private readonly Dictionary<EnemyBase, float> blockedStallTimers = new Dictionary<EnemyBase, float>();
        private readonly Dictionary<EnemyBase, int> blockedLastHPs = new Dictionary<EnemyBase, int>();
        private readonly List<EnemyBase> stallCleanupList = new List<EnemyBase>();

        public override bool HasTaunt => true;

        public float GetStallTimer(EnemyBase enemy)
        {
            if (enemy == null) return 0f;
            return blockedStallTimers.TryGetValue(enemy, out float t) ? t : 0f;
        }

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

            UpdateBlockedEnemiesStall(Time.deltaTime);
        }

        public void UpdateBlockedEnemiesStall(float deltaTime)
        {
            if (blockedEnemies == null || blockedEnemies.Count == 0)
            {
                if (blockedStallTimers.Count > 0)
                {
                    blockedStallTimers.Clear();
                    blockedLastHPs.Clear();
                }
                return;
            }

            stallCleanupList.Clear();
            foreach (var kvp in blockedStallTimers)
            {
                if (kvp.Key == null || kvp.Key.IsDead || !blockedEnemies.Contains(kvp.Key))
                {
                    stallCleanupList.Add(kvp.Key);
                }
            }
            for (int i = 0; i < stallCleanupList.Count; i++)
            {
                blockedStallTimers.Remove(stallCleanupList[i]);
                blockedLastHPs.Remove(stallCleanupList[i]);
            }

            for (int i = 0; i < blockedEnemies.Count; i++)
            {
                EnemyBase enemy = blockedEnemies[i];
                if (enemy == null || enemy.IsDead || enemy.IsEnraged) continue;

                if (!blockedStallTimers.ContainsKey(enemy))
                {
                    blockedStallTimers[enemy] = 0f;
                    blockedLastHPs[enemy] = enemy.CurrentHP;
                    continue;
                }

                int prevHP = blockedLastHPs[enemy];
                int currentHP = enemy.CurrentHP;

                if (currentHP < prevHP)
                {
                    // Stalled enemy took damage! Reset stall timer
                    blockedStallTimers[enemy] = 0f;
                    blockedLastHPs[enemy] = currentHP;
                }
                else
                {
                    blockedLastHPs[enemy] = currentHP;
                    float elapsed = blockedStallTimers[enemy] + deltaTime;
                    blockedStallTimers[enemy] = elapsed;

                    if (elapsed >= StallEnrageDuration)
                    {
                        MossmoEnragedEffect.Apply(enemy);
                        blockedStallTimers.Remove(enemy);
                        blockedLastHPs.Remove(enemy);
                    }
                }
            }
        }

        public override void TakeDamage(int rawATK, DamageType damageType, EnemyBase attacker = null)
        {
            if (currentHP <= 0 || rawATK <= 0) return;

            GridCell originCell = GetOriginCell();
            int hpBefore = currentHP;
            base.TakeDamage(rawATK, damageType, attacker);
            int hpAfter = currentHP;

            if (hpBefore > hpAfter && healCooldownTimer <= 0f && !isStunned && !isBound)
            {
                healCooldownTimer = attacker != null ? EnemyHealCooldownDuration : EnvironmentHealCooldownDuration;
                TriggerReactiveHeal(originCell);
            }
        }

        public override bool TakeTrueDamage(int damage, bool ignoreShieldNegation = false, bool canDodge = false)
        {
            if (currentHP <= 0 || damage <= 0) return false;

            GridCell originCell = GetOriginCell();
            int hpBefore = currentHP;
            bool dealt = base.TakeTrueDamage(damage, ignoreShieldNegation, canDodge);
            int hpAfter = currentHP;

            if (hpBefore > hpAfter && healCooldownTimer <= 0f && !isStunned && !isBound)
            {
                healCooldownTimer = EnvironmentHealCooldownDuration;
                TriggerReactiveHeal(originCell);
            }

            return dealt;
        }

        private GridCell GetOriginCell()
        {
            if (deployedCell != null) return deployedCell;

            GridManager grid = FindAnyObjectByType<GridManager>();
            if (grid != null)
            {
                return grid.GetCell(grid.WorldToGridPosition(transform.position));
            }
            return null;
        }

        public void Heal(OperatorBase target)
        {
            if (target == null || isStunned || isBound) return;
            target.Heal(GetHealAmount());
        }

        public int GetHealAmount()
        {
            return currentATK > 0 ? currentATK : 10;
        }

        private void TriggerReactiveHeal(GridCell originCell)
        {
            if (isStunned || isBound || data == null) return;

            GridManager gridManager = FindAnyObjectByType<GridManager>();
            List<OperatorBase> candidates = null;
            if (OperatorManager.Instance != null && gridManager != null && originCell != null)
            {
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
                candidates = OperatorManager.Instance.GetOperatorsInCells(rangeCells);
            }

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
            Vector3 visualOrigin = originCell != null ? originCell.WorldPosition : transform.position;
            AoeBlastVisual.PlayCircle(visualOrigin, 1.25f, new Color(0.25f, 0.95f, 0.45f, 0.6f), AoeStyle.Heal);
            PlayAbilitySound();

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

