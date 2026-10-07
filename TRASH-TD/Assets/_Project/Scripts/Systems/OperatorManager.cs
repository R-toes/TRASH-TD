using System;
using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Operators;
using TrashTD.UI;

namespace TrashTD.Systems
{
    /// <summary>
    /// Manages deployed operators, squad size constraints, deployment costs,
    /// retreat actions, and redeployment timers.
    /// </summary>
    public class OperatorManager : MonoBehaviour
    {
        public static OperatorManager Instance { get; private set; }

        [SerializeField] private GridManager gridManager;

        private readonly List<OperatorBase> deployedOperators = new List<OperatorBase>();
        private readonly Dictionary<string, int> redeployCooldownRounds = new Dictionary<string, int>();

        public IReadOnlyList<OperatorBase> DeployedOperators => deployedOperators;
        public int DeployedCount => deployedOperators.Count;
        public int SquadLimit { get; set; } = 8;
        public bool IsAtSquadLimit => DeployedCount >= SquadLimit;
        public OperatorBase SelectedOperator { get; private set; }

        public event Action<OperatorBase> OnOperatorDeployed;
        public event Action<OperatorBase> OnOperatorRetreated;
        public event Action<OperatorBase> OnOperatorSelected;

        public bool SelectOperator(OperatorBase op)
        {
            if (op != null && (!op.IsDeployed || !deployedOperators.Contains(op))) return false;
            if (SelectedOperator == op) return true;

            SelectedOperator = op;
            OnOperatorSelected?.Invoke(op);
            return true;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (gridManager == null)
            {
                gridManager = FindFirstObjectByType<GridManager>();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Check if an operator is on redeployment cooldown.
        /// </summary>
        public bool IsOnRedeployCooldown(string operatorName)
        {
            return redeployCooldownRounds.TryGetValue(operatorName, out int rounds) && rounds > 0;
        }

        public int GetRemainingRedeployCooldownRounds(string operatorName)
        {
            return redeployCooldownRounds.TryGetValue(operatorName, out int rounds) ? Mathf.Max(0, rounds) : 0;
        }

        public void AdvanceRedeployCooldownsOneRound()
        {
            if (redeployCooldownRounds.Count == 0) return;

            var keys = new List<string>(redeployCooldownRounds.Keys);
            foreach (string key in keys)
            {
                int roundsRemaining = redeployCooldownRounds[key] - 1;
                if (roundsRemaining <= 0) redeployCooldownRounds.Remove(key);
                else redeployCooldownRounds[key] = roundsRemaining;
            }
        }

        /// <summary>
        /// Attempts to deploy an operator instance to the specified grid coordinates.
        /// </summary>
        public bool TryDeployOperator(OperatorData opData, OperatorRarity rarity, Vector2Int gridPos, out OperatorBase deployedInstance)
        {
            return TryDeployOperator(opData, rarity, gridPos, OperatorFacing.Right, out deployedInstance);
        }

        public bool TryDeployOperator(OperatorData opData, OperatorRarity rarity, Vector2Int gridPos, OperatorFacing facing, out OperatorBase deployedInstance)
        {
            deployedInstance = null;

            if (opData == null || gridManager == null) return false;
            if (deployedOperators.Count >= SquadLimit) return false;
            if (IsOnRedeployCooldown(opData.operatorName)) return false;

            GridCell targetCell = gridManager.GetCell(gridPos);
            if (targetCell == null || !targetCell.CanDeploy(opData.position)) return false;

            // Instantiate operator
            GameObject opObj;
            if (opData.operatorPrefab != null)
            {
                opObj = Instantiate(opData.operatorPrefab, targetCell.WorldPosition, Quaternion.identity);
            }
            else
            {
                // Fallback placeholder GameObject
                opObj = new GameObject($"Operator_{opData.operatorName}");
                opObj.transform.position = targetCell.WorldPosition;
                var sr = opObj.AddComponent<SpriteRenderer>();
                if (opData.portrait != null)
                {
                    sr.sprite = opData.portrait;
                }
            }

            OperatorBase opComp = opObj.GetComponent<OperatorBase>();
            if (opComp == null)
            {
                opComp = AddClassComponent(opObj, opData.operatorClass);
            }

            opComp.Initialize(opData, rarity, facing);
            if (!opComp.Deploy(targetCell))
            {
                Destroy(opObj);
                return false;
            }

            deployedOperators.Add(opComp);
            opComp.ApplyFacingVisuals();
            OperatorUpgradeBadge.Show(opComp);
            deployedInstance = opComp;
            OnOperatorDeployed?.Invoke(opComp);
            return true;
        }

        private OperatorBase AddClassComponent(GameObject obj, OperatorClass opClass)
        {
            return opClass switch
            {
                OperatorClass.Guard => obj.AddComponent<GuardOperator>(),
                OperatorClass.Defender => obj.AddComponent<DefenderOperator>(),
                OperatorClass.Sniper => obj.AddComponent<SniperOperator>(),
                OperatorClass.Caster => obj.AddComponent<CasterOperator>(),
                OperatorClass.Medic => obj.AddComponent<MedicOperator>(),
                _ => obj.AddComponent<GuardOperator>()
            };
        }

        /// <summary>
        /// Retreats an active operator and starts their redeploy cooldown.
        /// </summary>
        public void RetreatOperator(OperatorBase op)
        {
            if (op == null || !deployedOperators.Contains(op)) return;

            if (SelectedOperator == op) SelectOperator(null);

            int cooldownRounds = 0;
            if (op.Data != null)
            {
                cooldownRounds = GetRetreatCooldownRounds(op.OperatorClass);
                if (cooldownRounds > 0) redeployCooldownRounds[op.Data.operatorName] = cooldownRounds;
                else redeployCooldownRounds.Remove(op.Data.operatorName);
            }

            DraftCard returnedCard = op.Data != null
                ? new DraftCard(op.Data, op.CurrentRarity, cooldownRounds)
                : null;
            deployedOperators.Remove(op);
            OnOperatorRetreated?.Invoke(op);
            op.Retreat();
            Destroy(op.gameObject);
            PlayerDeck.Instance?.AddReturnedCard(returnedCard);
        }

        /// <summary>
        /// Removes a dead operator from the squad without returning its card.
        /// </summary>
        public void HandleOperatorDeath(OperatorBase op)
        {
            if (op == null || !deployedOperators.Remove(op)) return;

            if (SelectedOperator == op) SelectOperator(null);
            OnOperatorRetreated?.Invoke(op);
        }

        private static int GetRetreatCooldownRounds(OperatorClass operatorClass)
        {
            return operatorClass switch
            {
                OperatorClass.Guard => 0,
                OperatorClass.Sniper => 1,
                OperatorClass.Caster => 1,
                OperatorClass.Defender => 2,
                OperatorClass.Medic => 3,
                _ => 0
            };
        }

        /// <summary>
        /// Get deployed operators situated inside specified grid cells.
        /// Useful for Medic range queries.
        /// </summary>
        public List<OperatorBase> GetOperatorsInCells(IEnumerable<GridCell> cells)
        {
            var result = new List<OperatorBase>();
            if (cells == null) return result;

            var cellSet = new HashSet<GridCell>(cells);
            foreach (var op in deployedOperators)
            {
                if (op != null && op.IsDeployed && cellSet.Contains(op.DeployedCell))
                {
                    result.Add(op);
                }
            }

            return result;
        }

        /// <summary>
        /// Clear all deployed operators (e.g. stage exit / reset).
        /// </summary>
        public void ClearAll()
        {
            SelectOperator(null);

            for (int i = deployedOperators.Count - 1; i >= 0; i--)
            {
                if (deployedOperators[i] != null)
                {
                    deployedOperators[i].Retreat();
                    Destroy(deployedOperators[i].gameObject);
                }
            }
            deployedOperators.Clear();
            redeployCooldownRounds.Clear();
        }
    }
}
