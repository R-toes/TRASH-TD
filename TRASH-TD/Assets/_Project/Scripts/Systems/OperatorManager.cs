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
        public GridManager GridManager => gridManager;

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
                gridManager = FindAnyObjectByType<GridManager>();
            }

            SubscribeToGameManager();
        }

        private void Start()
        {
            SubscribeToGameManager();
        }

        public void Initialize(GridManager grid)
        {
            gridManager = grid;
            SubscribeToGameManager();
        }

        public void SubscribeToGameManager()
        {
            if (TrashTD.Core.GameLoop.GameManager.Instance != null)
            {
                TrashTD.Core.GameLoop.GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
                TrashTD.Core.GameLoop.GameManager.Instance.OnPhaseChanged += HandlePhaseChanged;
            }
        }

        public void HandlePhaseChanged(TrashTD.Core.GameLoop.StagePhase phase)
        {
            if (phase == TrashTD.Core.GameLoop.StagePhase.WaveActive)
            {
                MarkAllDeployedOperatorsAsFought();
            }
        }

        public void MarkAllDeployedOperatorsAsFought()
        {
            for (int i = 0; i < deployedOperators.Count; i++)
            {
                if (deployedOperators[i] != null)
                {
                    deployedOperators[i].HasFoughtInWave = true;
                    deployedOperators[i].DeployedDuringCurrentPreparation = false;
                }
            }
        }

        public void RegisterOperator(OperatorBase op)
        {
            if (op != null && !deployedOperators.Contains(op))
            {
                deployedOperators.Add(op);
            }
        }

        private void OnDestroy()
        {
            if (TrashTD.Core.GameLoop.GameManager.Instance != null)
            {
                TrashTD.Core.GameLoop.GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
            }

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

            bool isWaveActive = TrashTD.Core.GameLoop.GameManager.Instance != null &&
                TrashTD.Core.GameLoop.GameManager.Instance.CurrentPhase == TrashTD.Core.GameLoop.StagePhase.WaveActive;
            opComp.HasFoughtInWave = isWaveActive;
            opComp.DeployedDuringCurrentPreparation = !isWaveActive;

            // A recalled operator comes back with the HP it left with, not full HP
            if (card.HasSavedHP) opComp.RestoreSavedHP(card.savedHP);

            opComp.HeatStacks = card != null ? card.heatStacks : 0;
            opComp.IsBurning = opComp.HeatStacks >= 5;
            deployedOperators.Add(opComp);
            opComp.ApplyFacingVisuals();
            OperatorUpgradeBadge.Show(opComp);
            if (TrashTD.Combat.StageCombatModifiers.IsHeatHazardActive)
            {
                TrashTD.UI.OperatorHeatBar.ShowFor(opComp);
            }
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
                   (opData.operatorName == "Pompoth" && !(opComp is PompothOperator)) ||
                   (opData.operatorName == "Deadeye" && !(opComp is DeadeyeOperator)) ||
                   (opData.operatorName == "Chillpath" && !(opComp is ChillpathOperator)) ||
                   (opData.operatorName == "Coalesce" && !(opComp is CoalesceOperator)) ||
                   ((opData.operatorName == "Proxishot" || opData.operatorName == "Proxyshot") && !(opComp is ProxishotOperator)) ||
                   (opData.operatorName == "Ignisaur" && !(opComp is IgnisaurOperator)) ||
                   (opData.operatorName == "Shadeslice" && !(opComp is ShadesliceOperator)) ||
                   (opData.operatorName == "Resicochet" && !(opComp is ResicochetOperator)) ||
                   (opData.operatorName == "Castiel" && !(opComp is CastielOperator)) ||
                   (opData.operatorName == "Echosquire" && !(opComp is EchosquireOperator)) ||
                   (opData.operatorName == "QiFu" && !(opComp is QiFuOperator)) ||
                   (opData.operatorName == "Framiliar" && !(opComp is FramiliarOperator));
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
            if (opData != null && opData.operatorName == "Pompoth")
            {
                return obj.AddComponent<PompothOperator>();
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
            if (opData != null && opData.operatorName == "Ignisaur")
            {
                return obj.AddComponent<IgnisaurOperator>();
            }
            if (opData != null && opData.operatorName == "Shadeslice")
            {
                return obj.AddComponent<ShadesliceOperator>();
            }
            if (opData != null && opData.operatorName == "Resicochet")
            {
                return obj.AddComponent<ResicochetOperator>();
            }
            if (opData != null && opData.operatorName == "Castiel")
            {
                return obj.AddComponent<CastielOperator>();
            }
            if (opData != null && opData.operatorName == "Echosquire")
            {
                return obj.AddComponent<EchosquireOperator>();
            }
            if (opData != null && opData.operatorName == "QiFu")
            {
                return obj.AddComponent<QiFuOperator>();
            }
            if (opData != null && opData.operatorName == "Framiliar")
            {
                return obj.AddComponent<FramiliarOperator>();
            }

            OperatorClass opClass = opData != null ? opData.operatorClass : OperatorClass.Guard;
            return opClass switch
            {
                OperatorClass.Guard => obj.AddComponent<GuardOperator>(),
                OperatorClass.Defender => obj.AddComponent<DefenderOperator>(),
                OperatorClass.Sniper => obj.AddComponent<SniperOperator>(),
                OperatorClass.Caster => obj.AddComponent<CasterOperator>(),
                OperatorClass.Medic => obj.AddComponent<MedicOperator>(),
                OperatorClass.Specialist => obj.AddComponent<SpecialistOperator>(),
                _ => obj.AddComponent<GuardOperator>()
            };
        }

        /// <summary>
        /// Transforms Framiliar on the battlefield into a grayscaled copy of target operator.
        /// Strictly enforces tile/ground compatibility (no high ground on low ground, and vice versa).
        /// Inherits Framiliar's merged upgrade levels if Framiliar was upgraded.
        /// </summary>
        public OperatorBase TransformFramiliar(FramiliarOperator framiliar, OperatorData targetData, OperatorRarity rarity)
        {
            if (framiliar == null || targetData == null) return null;

            GridCell cell = framiliar.DeployedCell;
            if (cell == null && gridManager != null)
            {
                cell = gridManager.GetCell(gridManager.WorldToGridPosition(framiliar.transform.position));
            }
            if (cell == null) return null;

            // Ground/height compatibility check
            if (!cell.SupportsPosition(targetData.position)) return null;

            OperatorFacing facing = framiliar.Facing;
            bool fought = framiliar.HasFoughtInWave;
            bool duringPrep = framiliar.DeployedDuringCurrentPreparation;
            OperatorData originalData = framiliar.OriginalFramiliarData != null ? framiliar.OriginalFramiliarData : framiliar.Data;
            OperatorRarity originalRarity = framiliar.OriginalFramiliarRarity;
            int heat = framiliar.HeatStacks;
            int currentHP = framiliar.CurrentHP;
            int framiliarMaxHP = framiliar.MaxHP;

            GameObject newOpObj;
            if (targetData.operatorPrefab != null)
            {
                newOpObj = Instantiate(targetData.operatorPrefab, cell.WorldPosition, Quaternion.identity);
            }
            else
            {
                newOpObj = new GameObject($"Operator_{targetData.operatorName}_Copy");
                newOpObj.transform.position = cell.WorldPosition;
                var sr = newOpObj.AddComponent<SpriteRenderer>();
                if (targetData.portrait != null) sr.sprite = targetData.portrait;
            }

            OperatorBase newOp = newOpObj.GetComponent<OperatorBase>();
            if (newOp == null || ShouldReplaceWithSpecializedComponent(newOp, targetData))
            {
                if (newOp != null)
                {
                    DestroyImmediate(newOp);
                }
                newOp = AddClassComponent(newOpObj, targetData);
            }

            cell.Vacate();
            newOp.Initialize(targetData, rarity, facing);
            bool deployed = newOp.Deploy(cell);
            if (!deployed)
            {
                // Safety rollback if deployment failed
                cell.Deploy(framiliar.gameObject);
                Destroy(newOpObj);
                return null;
            }

            newOp.IsCopiedFromFramiliar = true;
            newOp.OriginalFramiliarData = originalData;
            newOp.OriginalFramiliarRarity = originalRarity;
            newOp.HasFoughtInWave = fought;
            newOp.DeployedDuringCurrentPreparation = duringPrep;
            newOp.HeatStacks = heat;
            newOp.IsBurning = heat >= 5;

            float hpRatio = framiliarMaxHP > 0 ? (float)currentHP / framiliarMaxHP : 1f;
            newOp.RestoreSavedHP(Mathf.Max(1, Mathf.RoundToInt(newOp.MaxHP * hpRatio)));

            FramiliarOperator.ApplyGrayscale(newOpObj);

            int idx = deployedOperators.IndexOf(framiliar);
            if (idx >= 0)
            {
                deployedOperators[idx] = newOp;
            }
            else
            {
                deployedOperators.Add(newOp);
            }

            SelectOperator(newOp);

            newOp.ApplyFacingVisuals();
            OperatorUpgradeBadge.Show(newOp);
            if (TrashTD.Combat.StageCombatModifiers.IsHeatHazardActive)
            {
                TrashTD.UI.OperatorHeatBar.ShowFor(newOp);
            }

            Destroy(framiliar.gameObject);
            OnOperatorDeployed?.Invoke(newOp);
            return newOp;
        }

        public void RetreatOperator(OperatorBase op)
        {
            if (op == null || !deployedOperators.Contains(op)) return;

            if (SelectedOperator == op) SelectOperator(null);

            bool isSandbox = TrashTD.Core.GameLoop.GameManager.Instance != null && TrashTD.Core.GameLoop.GameManager.Instance.IsSandbox;
            // Card Pick counts too: operators can be deployed there from the minimized draft, and no combat happens.
            bool isPreparing = isSandbox || TrashTD.Core.GameLoop.GameManager.Instance == null ||
                TrashTD.Core.GameLoop.GameManager.Instance.CurrentPhase == TrashTD.Core.GameLoop.StagePhase.Preparation ||
                TrashTD.Core.GameLoop.GameManager.Instance.CurrentPhase == TrashTD.Core.GameLoop.StagePhase.CardPick;
            bool isPreparationRefund = isSandbox || (isPreparing && op.DeployedDuringCurrentPreparation && !op.HasFoughtInWave);

            int cooldownRounds = (!isPreparationRefund && op.Data != null)
                ? GetRetreatCooldownRounds(op.OperatorClass)
                : 0;

            // The card keeps the operator's exact HP so a redeploy doesn't heal it
            DraftCard returnedCard = (op.IsCopiedFromFramiliar && op.DeployedDuringCurrentPreparation && !op.HasFoughtInWave)
                ? new DraftCard(op.OriginalFramiliarData != null ? op.OriginalFramiliarData : op.Data, op.OriginalFramiliarRarity, 0)
                : (op.Data != null ? new DraftCard(op.Data, op.CurrentRarity, cooldownRounds, op.HeatStacks, op.CurrentHP) : null);


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
                OperatorClass.Specialist => 0,
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
