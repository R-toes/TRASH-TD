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
    /// Taunt mechanic: Ranged enemies prioritize attacking Mossmo when the attacker is in range.
    /// Base (3★): Taunt range covers adjacent tiles (Chebyshev dist <= 1). When taking damage, heals up to 2 operators
    ///   in range (including himself). If an enemy is stalled for 20s, becomes Enraged (+35% ATK, loses 5% HP/sec).
    /// 1st upgrade (4★): Automatically heals operators in range if HP is below 50% (no longer needs to be hit),
    ///   and expands taunt range by 1 additional tile radius (Chebyshev dist <= 2).
    /// 2nd upgrade (5★): Expands healing range (full 3x3 Bubblets range pattern) and reduces Enraged enemy damage by 10%.
    /// Heal cooldown: 1s when attacked by enemy or auto-healing, 3s when attacked by environment (acid rain/acid pool).
    /// </summary>
    public class MossmoOperator : DefenderOperator, IHealer
    {
        public const float EnemyHealCooldownDuration = 1.0f;
        public const float EnvironmentHealCooldownDuration = 3.0f;
        public const float StallEnrageDuration = 20.0f;

        public static readonly Vector2Int[] BaseRangePattern = new[]
        {
            new Vector2Int(0, 0),
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1)
        };

        // 2x Upgrade (5★): Expands healing range like Bubblets (full 3x3 box, 9 tiles)
        public static readonly Vector2Int[] UpgradedRangePattern = new[]
        {
            new Vector2Int(0, 0),
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
            new Vector2Int(1, 1),
            new Vector2Int(-1, 1),
            new Vector2Int(1, -1),
            new Vector2Int(-1, -1)
        };

        public bool HasFirstUpgrade => currentRarity >= OperatorRarity.Star4;
        public bool HasSecondUpgrade => currentRarity >= OperatorRarity.Star5;

        public override Vector2Int[] GetRangePattern()
        {
            return HasSecondUpgrade ? UpgradedRangePattern : BaseRangePattern;
        }

        private float healCooldownTimer;
        public float HealCooldownTimer => healCooldownTimer;

        private readonly Dictionary<EnemyBase, float> blockedStallTimers = new Dictionary<EnemyBase, float>();
        private readonly Dictionary<EnemyBase, int> blockedLastHPs = new Dictionary<EnemyBase, int>();
        private readonly List<EnemyBase> stallCleanupList = new List<EnemyBase>();

        public override bool HasTaunt => true;

        public override bool ShouldTauntAttacker(Vector3 attackerWorldPosition, GridManager grid = null)
        {
            if (!HasTaunt || !isDeployed || currentHP <= 0 || !IsTargetable)
                return false;

            int tauntReach = HasFirstUpgrade ? 2 : 1;

            if (grid == null)
            {
                grid = FindAnyObjectByType<GridManager>();
            }

            if (grid != null)
            {
                Vector2Int opGrid = deployedCell != null
                    ? deployedCell.GridPosition
                    : grid.WorldToGridPosition(transform.position);
                Vector2Int attackerGrid = grid.WorldToGridPosition(attackerWorldPosition);

                int dx = Mathf.Abs(attackerGrid.x - opGrid.x);
                int dy = Mathf.Abs(attackerGrid.y - opGrid.y);
                return dx <= tauntReach && dy <= tauntReach;
            }

            // Fallback for scenes/tests without GridManager (e.g. 1.5f base, 2.5f upgraded like Smog Caster range)
            float maxDist = HasFirstUpgrade ? 2.5f : 1.5f;
            return Mathf.Abs(attackerWorldPosition.x - transform.position.x) <= maxDist &&
                   Mathf.Abs(attackerWorldPosition.y - transform.position.y) <= maxDist;
        }

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

            // 1x Upgrade (4★+): Automatically heals operators in range if HP is below 50%
            if (HasFirstUpgrade && isDeployed && !IsCombatPaused && !isStunned && !isBound && healCooldownTimer <= 0f)
            {
                GridCell originCell = GetOriginCell();
                if (HasOperatorBelowThreshold(originCell, 0.50f))
                {
                    healCooldownTimer = EnemyHealCooldownDuration;
                    TriggerReactiveHeal(originCell);
                }
            }
        }

        public bool HasOperatorBelowThreshold(GridCell originCell, float threshold = 0.50f)
        {
            if (currentHP > 0 && currentHP < maxHP * threshold && CanReceiveHealing)
            {
                return true;
            }

            List<OperatorBase> candidates = GetOperatorsInRange(originCell);
            if (candidates != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    OperatorBase ally = candidates[i];
                    if (ally != null && ally != this && ally.IsDeployed && ally.CanReceiveHealing && ally.CurrentHP > 0)
                    {
                        if (ally.CurrentHP < ally.MaxHP * threshold)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public List<OperatorBase> GetOperatorsInRange(GridCell originCell)
        {
            GridManager gridManager = FindAnyObjectByType<GridManager>();
            if (OperatorManager.Instance != null && gridManager != null && originCell != null)
            {
                Vector2Int[] pattern = GetRangePattern();
                GridCell[] rangeCells = gridManager.GetCellsInRange(originCell.GridPosition, pattern, Facing);
                return OperatorManager.Instance.GetOperatorsInCells(rangeCells);
            }
            return null;
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
                        float multiplier = HasSecondUpgrade
                            ? MossmoEnragedEffect.UpgradedAtkMultiplier
                            : MossmoEnragedEffect.DefaultAtkMultiplier;
                        MossmoEnragedEffect.Apply(enemy, multiplier);
                        blockedStallTimers.Remove(enemy);
                        blockedLastHPs.Remove(enemy);
                    }
                }
            }
        }

        public override void TakeDamage(int rawATK, DamageType damageType, EnemyBase attacker = null)
        {
            if (currentHP <= 0 || rawATK <= 0) return;

            // 2x Upgrade (5★): Reduce damage from Enraged enemies by 10%
            if (HasSecondUpgrade && attacker != null && attacker.IsEnraged)
            {
                rawATK = Mathf.Max(1, Mathf.RoundToInt(rawATK * 0.90f));
            }

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

            List<OperatorBase> candidates = GetOperatorsInRange(originCell);

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

            // Play green restorative pulse visual (larger circle on 2x upgrade)
            Vector3 visualOrigin = originCell != null ? originCell.WorldPosition : transform.position;
            float blastRadius = HasSecondUpgrade ? 1.75f : 1.25f;
            AoeBlastVisual.PlayCircle(visualOrigin, blastRadius, new Color(0.25f, 0.95f, 0.45f, 0.6f), AoeStyle.Heal);
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

