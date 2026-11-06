using System;
using System.Collections.Generic;
using UnityEngine;
using TrashTD.Audio;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Progeny — 2-Star Medic.
    /// Base Range: covers own tile and 4 adjacent cardinal tiles (5 tiles total).
    /// Can heal herself (self-heal counts as a bounce/target), but is not included as a target
    /// in the bouncing line animation (lines only connect from operator to operator).
    /// Ability (Chain Heal):
    /// - Base (2★): Heal bounces between 2 operators (1 bounce, healing reduced by 50% when it bounces).
    /// - 1st Upgrade (3★): Additional 1 bounce (2 bounces total, up to 3 operators healed),
    ///   and increases range by 1 tile at every adjacent tile (9 tiles total).
    /// - 2nd Upgrade (4★): Healing bounces are no longer reduced (100% healing on all bounces).
    /// Visuals:
    /// - Every attack randomizes its source from the 6 red floating pods in Progeny's sprite.
    /// - Bouncing line is a single continuous, seamless fading beam with a leading red ArtsOrb.
    /// - Healed operators receive a restorative splash of healing crosses and glistening droplets (no AoE circle).
    /// </summary>
    public class ProgenyOperator : MedicOperator
    {
        public static readonly Color BrightRedColor = ProgenyHealTetherVisual.BrightRed;

        /// <summary>
        /// Local offsets of the 6 red floating pods relative to Progeny's center when facing left.
        /// Mirrored horizontally when facing right.
        /// </summary>
        public static readonly Vector3[] RedPodLocalOffsets = new[]
        {
            new Vector3(-0.33f,  0.31f, 0f), // 1. Upper-Left pod
            new Vector3( 0.40f,  0.25f, 0f), // 2. Upper-Right pod
            new Vector3(-0.38f,  0.13f, 0f), // 3. Mid-Left pod
            new Vector3( 0.38f,  0.06f, 0f), // 4. Mid-Right pod
            new Vector3(-0.27f, -0.07f, 0f), // 5. Lower-Left pod
            new Vector3( 0.24f, -0.11f, 0f)  // 6. Lower-Right pod
        };

        /// <summary>
        /// 1x Upgraded range pattern: increases range by 1 tile at every adjacent tile (length 2 cross, 9 tiles total).
        /// </summary>
        public static readonly Vector2Int[] UpgradedRangePattern = new[]
        {
            new Vector2Int(0, 0),
            new Vector2Int(1, 0),
            new Vector2Int(2, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(-2, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, 2),
            new Vector2Int(0, -1),
            new Vector2Int(0, -2)
        };

        public bool HasFirstUpgrade => currentRarity >= OperatorRarity.Star3;
        public bool HasSecondUpgrade => currentRarity >= OperatorRarity.Star4;

        public int MaxBounces => HasFirstUpgrade ? 2 : 1;

        public override Vector2Int[] GetRangePattern()
        {
            if (data == null || data.rangePattern == null) return null;
            return HasFirstUpgrade ? UpgradedRangePattern : data.rangePattern;
        }

        public static Vector2Int[] GetRangePatternForRarity(OperatorData operatorData, OperatorRarity rarity)
        {
            if (operatorData == null || operatorData.rangePattern == null) return null;
            return rarity >= OperatorRarity.Star3 ? UpgradedRangePattern : operatorData.rangePattern;
        }

        /// <summary>
        /// Gets the world-space origin of one of the 6 red pods by index (0..5).
        /// </summary>
        public Vector3 GetPodOrigin(int index)
        {
            if (index < 0 || index >= RedPodLocalOffsets.Length) index = 0;
            Vector3 offset = RedPodLocalOffsets[index];
            if (facing == OperatorFacing.Right)
            {
                offset.x = -offset.x;
            }
            Vector3 scale = transform.lossyScale;
            return transform.position + new Vector3(offset.x * scale.x, offset.y * scale.y, 0f);
        }

        /// <summary>
        /// Picks a random pod origin among the 6 red floating pods for the heal beam/projectile source.
        /// </summary>
        public Vector3 GetRandomPodOrigin()
        {
            int index = UnityEngine.Random.Range(0, RedPodLocalOffsets.Length);
            return GetPodOrigin(index);
        }

        /// <summary>
        /// Progeny searches for the most injured ally in range. She can heal herself if she has the lowest HP ratio.
        /// </summary>
        protected override OperatorBase FindHealTarget()
        {
            if (OperatorManager.Instance == null || deployedCell == null || data == null)
                return null;

            var gridManager = FindAnyObjectByType<GridManager>();
            if (gridManager == null) return null;

            var rangePattern = GetRangePattern();
            if (rangePattern == null) return null;

            var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, rangePattern, Facing);
            var candidates = OperatorManager.Instance.GetOperatorsInCells(rangeCells);
            if (candidates == null) return null;

            OperatorBase mostInjured = null;
            float lowestRatio = 1.0f; // Only heal units under 100% HP

            for (int i = 0; i < candidates.Count; i++)
            {
                var ally = candidates[i];
                if (ally == null || !ally.IsDeployed || !ally.CanReceiveHealing ||
                    ally.CurrentHP >= ally.MaxHP) continue;

                float ratio = (float)ally.CurrentHP / ally.MaxHP;
                if (ratio < lowestRatio)
                {
                    lowestRatio = ratio;
                    mostInjured = ally;
                }
            }

            return mostInjured;
        }

        /// <summary>
        /// Calculates the heal amount for a bounce:
        /// - 2x Upgraded: no reduction (100% of primary heal).
        /// - Base / 1x Upgraded: reduced by 50% on bounce.
        /// </summary>
        public int GetBounceHealAmount(int primaryHeal, int bounceIndex)
        {
            if (HasSecondUpgrade)
            {
                return primaryHeal;
            }

            return Mathf.Max(1, Mathf.RoundToInt(primaryHeal * 0.5f));
        }

        public struct HealChainHop
        {
            public OperatorBase Target;
            public int HealAmount;
            public bool IsSelf;
        }

        public override void Heal(OperatorBase target)
        {
            if (target == null || !isDeployed) return;

            int primaryHeal = GetHealAmount();
            Audio.UnitSfx.Play(data, Audio.UnitSfxEvent.Attack);

            // Resolve the full chain of targets and bounce amounts (including self-heal as a bounce/target)
            var hops = ResolveChain(target, primaryHeal);

            if (!Application.isPlaying)
            {
                ExecuteChainImmediate(hops);
                return;
            }

            // Build connected seamless bouncing line points and callbacks
            var linePoints = new List<Vector3>();
            var callbacks = new List<Action<int>>();

            // First point is the random pod on Progeny
            linePoints.Add(GetRandomPodOrigin());
            callbacks.Add(null); // point 0 is the pod, no callback

            for (int i = 0; i < hops.Count; i++)
            {
                var hop = hops[i];
                if (hop.IsSelf)
                {
                    // Self-heal: receives heal directly with cross splash, no bounce line into self
                    if (hop.Target != null && hop.Target.IsDeployed)
                    {
                        hop.Target.Heal(hop.HealAmount);
                        CombatFx.HealCrossSplash(hop.Target.transform.position, BrightRedColor);
                    }
                }
                else
                {
                    // Allied operator: connected into the single seamless multi-segment line
                    linePoints.Add(hop.Target.transform.position);
                    var capturedHop = hop;
                    callbacks.Add((pointIndex) =>
                    {
                        if (capturedHop.Target != null && capturedHop.Target.IsDeployed)
                        {
                            capturedHop.Target.Heal(capturedHop.HealAmount);
                            CombatFx.HealCrossSplash(capturedHop.Target.transform.position, BrightRedColor);
                            PlayAbilitySound();
                        }
                    });
                }
            }

            // Spawn the seamless connected fading line
            if (linePoints.Count >= 2)
            {
                ProgenyHealTetherVisual.PlayChain(linePoints.ToArray(), callbacks.ToArray(), BrightRedColor);
            }
        }

        /// <summary>
        /// Resolves the chain of operators to be healed. Self-heal counts toward the bounce limit.
        /// </summary>
        public List<HealChainHop> ResolveChain(OperatorBase primaryTarget, int primaryHeal)
        {
            var hops = new List<HealChainHop>();
            if (primaryTarget == null || !primaryTarget.IsDeployed) return hops;

            var visited = new HashSet<OperatorBase> { primaryTarget };
            hops.Add(new HealChainHop
            {
                Target = primaryTarget,
                HealAmount = primaryHeal,
                IsSelf = ReferenceEquals(primaryTarget, this)
            });

            OperatorBase previousTarget = primaryTarget;
            int maxBounces = MaxBounces;

            for (int bounce = 1; bounce <= maxBounces; bounce++)
            {
                OperatorBase nextTarget = FindBounceTarget(previousTarget, primaryTarget, visited);
                if (nextTarget == null) break;

                visited.Add(nextTarget);
                int bounceHeal = GetBounceHealAmount(primaryHeal, bounce);
                hops.Add(new HealChainHop
                {
                    Target = nextTarget,
                    HealAmount = bounceHeal,
                    IsSelf = ReferenceEquals(nextTarget, this)
                });
                previousTarget = nextTarget;
            }

            return hops;
        }

        /// <summary>
        /// Immediate synchronous resolution of the heal bounce chain for edit-mode tests.
        /// </summary>
        public void ExecuteChainImmediate(List<HealChainHop> hops)
        {
            if (hops == null) return;
            for (int i = 0; i < hops.Count; i++)
            {
                var hop = hops[i];
                if (hop.Target != null && hop.Target.IsDeployed)
                {
                    hop.Target.Heal(hop.HealAmount);
                    CombatFx.HealCrossSplash(hop.Target.transform.position, BrightRedColor);
                }
            }
        }

        /// <summary>
        /// Finds the most injured allied operator (or Progeny herself if damaged) in the surrounding 8 tiles.
        /// If none is found, falls back to checking the surrounding 8 tiles of <paramref name="fallbackTarget"/>.
        /// </summary>
        public OperatorBase FindBounceTarget(OperatorBase referenceTarget, OperatorBase fallbackTarget, HashSet<OperatorBase> visited)
        {
            OperatorBase best = FindBounceTargetInSurrounding(referenceTarget, visited);
            if (best == null && fallbackTarget != null && !ReferenceEquals(fallbackTarget, referenceTarget))
            {
                best = FindBounceTargetInSurrounding(fallbackTarget, visited);
            }
            return best;
        }

        private OperatorBase FindBounceTargetInSurrounding(OperatorBase originTarget, HashSet<OperatorBase> visited)
        {
            if (originTarget == null || originTarget.DeployedCell == null || OperatorManager.Instance == null)
            {
                return null;
            }

            GridManager gridManager = FindAnyObjectByType<GridManager>();
            if (gridManager == null)
            {
                return null;
            }

            Vector2Int center = originTarget.DeployedCell.GridPosition;
            List<GridCell> surroundingCells = new List<GridCell>(8);

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;

                    GridCell cell = gridManager.GetCell(center.x + dx, center.y + dy);
                    if (cell != null)
                    {
                        surroundingCells.Add(cell);
                    }
                }
            }

            var candidates = OperatorManager.Instance.GetOperatorsInCells(surroundingCells);
            OperatorBase bestTarget = null;
            float lowestRatio = 1.0f; // Only heal injured allies below 100% HP

            for (int i = 0; i < candidates.Count; i++)
            {
                var ally = candidates[i];
                if (ally == null || !ally.IsDeployed || !ally.CanReceiveHealing) continue;
                if (visited != null && visited.Contains(ally)) continue;
                if (ally.CurrentHP >= ally.MaxHP) continue;

                float ratio = (float)ally.CurrentHP / ally.MaxHP;
                if (ratio < lowestRatio)
                {
                    lowestRatio = ratio;
                    bestTarget = ally;
                }
            }

            return bestTarget;
        }
    }
}
