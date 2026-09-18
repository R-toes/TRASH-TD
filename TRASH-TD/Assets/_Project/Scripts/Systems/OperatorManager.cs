using System;
using System.Collections.Generic;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Operators;

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
        private readonly Dictionary<string, float> redeployCooldowns = new Dictionary<string, float>();

        public IReadOnlyList<OperatorBase> DeployedOperators => deployedOperators;
        public int DeployedCount => deployedOperators.Count;
        public int SquadLimit { get; set; } = 8;

        public event Action<OperatorBase> OnOperatorDeployed;
        public event Action<OperatorBase> OnOperatorRetreated;

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

        private void Update()
        {
            // Tick redeploy cooldowns
            if (redeployCooldowns.Count > 0)
            {
                var keys = new List<string>(redeployCooldowns.Keys);
                foreach (var key in keys)
                {
                    redeployCooldowns[key] -= Time.deltaTime;
                    if (redeployCooldowns[key] <= 0f)
                    {
                        redeployCooldowns.Remove(key);
                    }
                }
            }
        }

        /// <summary>
        /// Check if an operator is on redeployment cooldown.
        /// </summary>
        public bool IsOnRedeployCooldown(string operatorName)
        {
            return redeployCooldowns.ContainsKey(operatorName) && redeployCooldowns[operatorName] > 0f;
        }

        public float GetRemainingRedeployCooldown(string operatorName)
        {
            return redeployCooldowns.TryGetValue(operatorName, out float cd) ? Mathf.Max(0f, cd) : 0f;
        }

        /// <summary>
        /// Attempts to deploy an operator instance to the specified grid coordinates.
        /// </summary>
        public bool TryDeployOperator(OperatorData opData, OperatorRarity rarity, Vector2Int gridPos, out OperatorBase deployedInstance)
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

            opComp.Initialize(opData, rarity);
            if (!opComp.Deploy(targetCell))
            {
                Destroy(opObj);
                return false;
            }

            deployedOperators.Add(opComp);
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

            if (op.Data != null)
            {
                redeployCooldowns[op.Data.operatorName] = op.Data.redeployCooldown;
            }

            deployedOperators.Remove(op);
            OnOperatorRetreated?.Invoke(op);
            op.Retreat();
            Destroy(op.gameObject);
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
            for (int i = deployedOperators.Count - 1; i >= 0; i--)
            {
                if (deployedOperators[i] != null)
                {
                    deployedOperators[i].Retreat();
                    Destroy(deployedOperators[i].gameObject);
                }
            }
            deployedOperators.Clear();
            redeployCooldowns.Clear();
        }
    }
}
