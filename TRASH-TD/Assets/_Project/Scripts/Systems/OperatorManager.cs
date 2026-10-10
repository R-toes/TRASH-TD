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
        /// Attempts to deploy the specified deck card to the given grid coordinates.
        /// </summary>
        public bool TryDeployOperator(DraftCard card, Vector2Int gridPos, OperatorFacing facing, out OperatorBase deployedInstance)
        {
            deployedInstance = null;

            if (card == null || card.cooldownRoundsRemaining > 0) return false;
            OperatorData opData = card.operatorData;
            if (opData == null || gridManager == null) return false;
            if (deployedOperators.Count >= SquadLimit) return false;

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
            if (opComp == null || ShouldReplaceWithSpecializedComponent(opComp, opData))
            {
                if (opComp != null)
                {
                    DestroyImmediate(opComp);
                }
                opComp = AddClassComponent(opObj, opData);
            }

            opComp.Initialize(opData, card.rarity, facing);
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

        private bool ShouldReplaceWithSpecializedComponent(OperatorBase opComp, OperatorData opData)
        {
            if (opComp == null || opData == null) return false;
            return (opData.operatorName == "Mossmo" && !(opComp is MossmoOperator)) ||
                   (opData.operatorName == "Bubblets" && !(opComp is BubbletsOperator)) ||
                   (opData.operatorName == "Progeny" && !(opComp is ProgenyOperator)) ||
                   (opData.operatorName == "Basurocket" && !(opComp is BasurocketOperator)) ||
                   ((opData.operatorName == "Stag-ger" || opData.operatorName == "Stagger") && !(opComp is StaggerOperator)) ||
                   (opData.operatorName == "Thornchin" && !(opComp is ThornchinOperator)) ||
                   (opData.operatorName == "Scrapper" && !(opComp is ScrapperOperator)) ||
                   (opData.operatorName == "Bulkhead" && !(opComp is BulkheadOperator)) ||
                   (opData.operatorName == "Pyrolite" && !(opComp is PyroliteOperator)) ||
                   (opData.operatorName == "Deadeye" && !(opComp is DeadeyeOperator)) ||
                   (opData.operatorName == "Chillpath" && !(opComp is ChillpathOperator)) ||
                   (opData.operatorName == "Coalesce" && !(opComp is CoalesceOperator)) ||
                   ((opData.operatorName == "Proxishot" || opData.operatorName == "Proxyshot") && !(opComp is ProxishotOperator));
        }

        private OperatorBase AddClassComponent(GameObject obj, OperatorData opData)
        {
            if (opData != null && opData.operatorName == "Mossmo")
            {
                return obj.AddComponent<MossmoOperator>();
            }
            if (opData != null && opData.operatorName == "Bubblets")
            {
                return obj.AddComponent<BubbletsOperator>();
            }
            if (opData != null && opData.operatorName == "Progeny")
            {
                return obj.AddComponent<ProgenyOperator>();
            }
            if (opData != null && opData.operatorName == "Basurocket")
            {
                return obj.AddComponent<BasurocketOperator>();
            }
            if (opData != null && (opData.operatorName == "Stag-ger" || opData.operatorName == "Stagger"))
            {
                return obj.AddComponent<StaggerOperator>();
            }
            if (opData != null && opData.operatorName == "Thornchin")
            {
                return obj.AddComponent<ThornchinOperator>();
            }
            if (opData != null && opData.operatorName == "Scrapper")
            {
                return obj.AddComponent<ScrapperOperator>();
            }
            if (opData != null && opData.operatorName == "Bulkhead")
            {
                return obj.AddComponent<BulkheadOperator>();
            }
            if (opData != null && opData.operatorName == "Pyrolite")
            {
                return obj.AddComponent<PyroliteOperator>();
            }
            if (opData != null && opData.operatorName == "Deadeye")
            {
                return obj.AddComponent<DeadeyeOperator>();
            }
            if (opData != null && opData.operatorName == "Chillpath")
            {
                return obj.AddComponent<ChillpathOperator>();
            }
            if (opData != null && opData.operatorName == "Coalesce")
            {
                return obj.AddComponent<CoalesceOperator>();
            }
            if (opData != null && (opData.operatorName == "Proxishot" || opData.operatorName == "Proxyshot"))
            {
                return obj.AddComponent<ProxishotOperator>();
            }

            OperatorClass opClass = opData != null ? opData.operatorClass : OperatorClass.Guard;
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

            int cooldownRounds = op.Data != null ? GetRetreatCooldownRounds(op.OperatorClass) : 0;

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
        }
    }
}
