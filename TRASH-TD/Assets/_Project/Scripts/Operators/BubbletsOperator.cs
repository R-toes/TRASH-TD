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
    /// Bubblets — 3-Star Medic.
    /// Range covers self and 8 surrounding tiles (3x3 area).
    /// Does not heal directly; every 2.0 seconds, grants allied operator(s) in range
    /// a protective bubble shield that negates one instance of damage (even at full HP).
    /// When the bubble pops, it heals the recipient for 100% of Bubblets' ATK and deals Arts damage
    /// to enemies on that tile and surrounding tiles.
    /// Upgrades:
    /// - Base (3★): Deals Arts damage equal to 30% of Bubblets' ATK when popped.
    /// - 1x Upgraded (4★): Arts damage increases to 50% of Bubblets' ATK.
    /// - 2x Upgraded (5★): Gives bubble shield to an additional operator (up to 2), and allies in range gain +5 RES and +20 DEF.
    /// </summary>
    public class BubbletsOperator : MedicOperator
    {
        public const int AuraDefBonus = 20;
        public const int AuraResBonus = 5;

        public bool HasFirstUpgrade => currentRarity >= OperatorRarity.Star4;
        public bool HasSecondUpgrade => currentRarity >= OperatorRarity.Star5;

        public float ArtsDamageMultiplier => HasFirstUpgrade ? 0.50f : 0.30f;
        public int TargetCount => HasSecondUpgrade ? 2 : 1;

        private readonly HashSet<OperatorBase> appliedAuraAllies = new HashSet<OperatorBase>();
        private readonly List<OperatorBase> tempAuraCleanupList = new List<OperatorBase>();

        public IReadOnlyCollection<OperatorBase> AppliedAuraAllies => appliedAuraAllies;

        public void SetRarityForTesting(OperatorRarity rarity)
        {
            currentRarity = rarity;
        }

        protected override EnemyBase FindTarget()
        {
            // Bubblets does not target enemies
            return null;
        }

        public override void Attack(EnemyBase target)
        {
            // Bubblets does not deal basic attack damage directly to enemies
        }

        public override void Heal(OperatorBase target)
        {
            // Forward healing requests to shielding
            if (target != null && isDeployed)
            {
                Shield(target);
                if (HasSecondUpgrade)
                {
                    var targets = FindShieldTargets(2);
                    for (int i = 0; i < targets.Count; i++)
                    {
                        if (targets[i] != target)
                        {
                            Shield(targets[i]);
                            break;
                        }
                    }
                }
            }
        }

        protected override void Update()
        {
            if (!isDeployed) return;

            // Shield cast interval (uses attackInterval as cycle interval, defaulting to 2.0s).
            // Stays ready until a shield target exists, so the first shield is cast immediately.
            float interval = Combat.StageCombatModifiers.GetAttackInterval(data != null ? data.attackInterval : 2.0f);
            attackTimer = Mathf.Min(attackTimer + Time.deltaTime, interval);

            // No bubbles between waves.
            if (IsCombatPaused) return;

            if (isStunned || isBound)
            {
                if (isStunned) UpdateStunStatus(Time.deltaTime);
                if (isBound) UpdateBindStatus(Time.deltaTime);
                return;
            }

            equippedSkill?.UpdateSkill(Time.deltaTime);

            if (HasSecondUpgrade && currentHP > 0)
            {
                UpdateDefResAura();
            }

            if (attackTimer >= interval)
            {
                int count = TargetCount;
                var targets = FindShieldTargets(count);
                if (targets != null && targets.Count > 0)
                {
                    attackTimer = 0f;
                    for (int i = 0; i < targets.Count; i++)
                    {
                        Shield(targets[i]);
                    }
                }
            }
        }

        protected override void OnDeployed()
        {
            base.OnDeployed();
            if (HasSecondUpgrade)
            {
                UpdateDefResAura();
            }
        }

        protected override void OnRetreated()
        {
            ClearDefResAura();
            base.OnRetreated();
        }

        private void OnDestroy()
        {
            ClearDefResAura();
        }

        /// <summary>Played by a bubble this Bubblets cast when it pops.</summary>
        public void PlayShieldPopSound() => PlayAbilitySound(Audio.SfxId.GameplayMagicAttack);

        public void Shield(OperatorBase target)
        {
            if (target == null || !isDeployed || IsCombatPaused || isStunned || isBound) return;

            PlayAttackSound();

            if (!Application.isPlaying)
            {
                BubbleShield.Apply(target, currentATK, this);
                return;
            }

            CombatProjectileVisual.Fire(
                ProjectileOrigin,
                target.transform.position,
                new Color(0.4f, 0.85f, 1f, 0.9f),
                11f,
                0.15f,
                0.08f,
                true,
                () =>
                {
                    // A bubble still in flight when the wave ends is dropped.
                    if (target != null && target.IsDeployed && !IsCombatPaused)
                    {
                        BubbleShield.Apply(target, currentATK, this);
                    }
                }, style: ProjectileStyle.Bubble);
        }

        /// <summary>
        /// Selects the best unshielded ally in range to grant a bubble shield.
        /// Prioritizes actively blocking allies, then injured allies, then unshielded full HP allies.
        /// </summary>
        public OperatorBase FindShieldTarget()
        {
            var targets = FindShieldTargets(1);
            return targets.Count > 0 ? targets[0] : null;
        }

        /// <summary>
        /// Finds up to maxCount unshielded allies in range, sorted by priority.
        /// </summary>
        public List<OperatorBase> FindShieldTargets(int maxCount = 1)
        {
            var targets = new List<OperatorBase>();
            if (OperatorManager.Instance == null || deployedCell == null || data == null || data.rangePattern == null)
                return targets;

            GridManager gridManager = FindAnyObjectByType<GridManager>();
            if (gridManager == null && OperatorManager.Instance != null)
            {
                gridManager = OperatorManager.Instance.GridManager;
            }
            if (gridManager == null) return targets;

            GridCell[] rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
            List<OperatorBase> candidates = OperatorManager.Instance.GetOperatorsInCells(rangeCells);
            if (candidates == null || candidates.Count == 0) return targets;

            var scoredCandidates = new List<KeyValuePair<OperatorBase, float>>();

            for (int i = 0; i < candidates.Count; i++)
            {
                OperatorBase ally = candidates[i];
                if (ally == null || !ally.IsDeployed || ally.CurrentHP <= 0 || BubbleShield.HasActiveShield(ally))
                    continue;

                float score = 10f;

                // Priority 1: Actively blocking enemies (taking front-line pressure)
                if (ally.GetCurrentBlockCount() > 0)
                {
                    score += 1000f;
                }

                // Priority 2: Injured allies (lower HP percentage gets higher score)
                if (ally.MaxHP > 0)
                {
                    float missingRatio = 1f - Mathf.Clamp01((float)ally.CurrentHP / ally.MaxHP);
                    score += missingRatio * 100f;
                }

                scoredCandidates.Add(new KeyValuePair<OperatorBase, float>(ally, score));
            }

            scoredCandidates.Sort((a, b) => b.Value.CompareTo(a.Value));

            for (int i = 0; i < scoredCandidates.Count && i < maxCount; i++)
            {
                targets.Add(scoredCandidates[i].Key);
            }

            return targets;
        }

        /// <summary>
        /// Updates the 2x Upgrade DEF & RES aura (+20 DEF, +5 RES to allies in range).
        /// </summary>
        public void UpdateDefResAura()
        {
            if (!HasSecondUpgrade || !isDeployed || currentHP <= 0)
            {
                if (appliedAuraAllies.Count > 0)
                {
                    ClearDefResAura();
                }
                return;
            }

            List<OperatorBase> currentInRange = GetOperatorsInRange();

            // 1. Remove buff from operators no longer in range or no longer deployed/alive
            tempAuraCleanupList.Clear();
            foreach (var op in appliedAuraAllies)
            {
                if (op == null || !op.IsDeployed || op.CurrentHP <= 0 || currentInRange == null || !currentInRange.Contains(op))
                {
                    tempAuraCleanupList.Add(op);
                }
            }

            for (int i = 0; i < tempAuraCleanupList.Count; i++)
            {
                OperatorBase op = tempAuraCleanupList[i];
                if (op != null)
                {
                    if (op.IsDeployed && op.CurrentHP > 0)
                    {
                        op.ModifyDEF(-AuraDefBonus);
                        op.ModifyRES(-AuraResBonus);
                    }
                    appliedAuraAllies.Remove(op);
                }
            }

            // 2. Add buff to newly detected operators in range
            if (currentInRange != null)
            {
                for (int i = 0; i < currentInRange.Count; i++)
                {
                    OperatorBase op = currentInRange[i];
                    if (op != null && op.IsDeployed && op.CurrentHP > 0 && !appliedAuraAllies.Contains(op))
                    {
                        op.ModifyDEF(AuraDefBonus);
                        op.ModifyRES(AuraResBonus);
                        appliedAuraAllies.Add(op);
                    }
                }
            }
        }

        public void ClearDefResAura()
        {
            foreach (var op in appliedAuraAllies)
            {
                if (op != null && op.IsDeployed && op.CurrentHP > 0)
                {
                    op.ModifyDEF(-AuraDefBonus);
                    op.ModifyRES(-AuraResBonus);
                }
            }
            appliedAuraAllies.Clear();
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
            float maxDist = (data != null && data.attackRange > 0) ? data.attackRange + 0.5f : 1.5f;

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
    }
}
