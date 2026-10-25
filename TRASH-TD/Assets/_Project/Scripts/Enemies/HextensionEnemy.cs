using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Enemies
{
    public enum HextensionState
    {
        SearchingForBind,
        Binding,
        NormalCaster
    }

    /// <summary>
    /// Hextension - Enemy Caster archetype.
    /// Characteristics:
    /// - Arts damage type.
    /// - Low DEF (5), high RES (40).
    /// - Unblockable.
    /// - Ability: Binds 1 operator in range for the first time:
    ///   - Stops in place and casts a 15-second channel.
    ///   - Same range pattern as Askalaser (Yellow for all units, Green for Ranged only).
    ///   - Prioritizes operator with lowest RES in range (respecting taunt).
    ///   - Operator cannot attack, block, or use abilities while bound (blocked enemies released).
    ///   - Deals chip Arts damage over time.
    ///   - Tethered by a visual line connecting Hextension and the target operator.
    ///   - Interaction with Proxyshot: bind stops immediately if used against Proxyshot or if target becomes camouflaged.
    ///   - Interaction with Bubblets: bind continues when Bubblets' bubble shield absorbs damage.
    ///   - After the 15s bind finishes (or breaks), Hextension permanently transitions to normal caster behavior.
    /// </summary>
    public class HextensionEnemy : EnemyBase
    {
        public const float BindDuration = 15.0f;
        public const int ChipDamageAmount = 25;
        public const float ChipDamageInterval = 1.0f;
        public const float PostBindAttackMultiplier = 1.6f;
        public const float InvisibleAlpha = 0.45f;

        [Header("Hextension State")]
        [SerializeField] private HextensionState state = HextensionState.SearchingForBind;
        [SerializeField] private OperatorBase boundTarget;
        [SerializeField] private float bindDurationRemaining;
        [SerializeField] private float chipDamageTimer;
        [SerializeField] private bool hasCastBind;

        [Header("Wire Bind")]
        [SerializeField] private WireColorType currentWireColor;
        private bool hasManualWireColor;
        [SerializeField] private Vector2 wireOriginOffset = new Vector2(0.31f, -0.14f);

        private BindingLineVisual lineVisual;
        private GridManager cachedGridManager;

        public HextensionState CurrentState => state;
        public OperatorBase BoundTarget => boundTarget;
        public float BindDurationRemaining => bindDurationRemaining;
        public bool HasCastBind => hasCastBind;
        public bool IsInvisibleToRanged => !hasCastBind;
        public WireColorType CurrentWireColor => currentWireColor;
        public BindingLineVisual LineVisual => lineVisual;
        public Vector2 WireOriginOffset
        {
            get => wireOriginOffset;
            set => wireOriginOffset = value;
        }

        public Vector3 GetWireOriginPosition()
        {
            float xOffset = isFacingLeft ? -wireOriginOffset.x : wireOriginOffset.x;
            return transform.position + new Vector3(xOffset, wireOriginOffset.y, 0f);
        }

        public void SetWireColor(WireColorType wireColor)
        {
            currentWireColor = wireColor;
            hasManualWireColor = true;
        }

        public override bool CanBeTargetedBy(OperatorPosition opPosition)
        {
            if (IsInvisibleToRanged && opPosition == OperatorPosition.Ranged)
                return false;

            return base.CanBeTargetedBy(opPosition);
        }

        public override void Initialize(EnemyData enemyData, int difficultyLevel)
        {
            base.Initialize(enemyData, difficultyLevel);
            UpdateInvisibilityVisual();
        }

        public void UpdateInvisibilityVisual()
        {
            float targetAlpha = IsInvisibleToRanged ? InvisibleAlpha : 1.0f;
            SetSpriteAlpha(targetAlpha);
        }

        private void SetSpriteAlpha(float alpha)
        {
            var renderers = animationRenderers ?? GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    Color c = renderers[i].color;
                    if (!Mathf.Approximately(c.a, alpha))
                    {
                        c.a = alpha;
                        renderers[i].color = c;
                    }
                }
            }
        }

        public GridManager EffectiveGridManager
        {
            get
            {
                if (cachedGridManager == null)
                    cachedGridManager = FindAnyObjectByType<GridManager>();
                return cachedGridManager;
            }
            set => cachedGridManager = value;
        }

        public void SetGridManagerInstance(GridManager gm)
        {
            SetGridManager(gm);
            cachedGridManager = gm;
        }

        // ==========================================
        // Range Pattern Offsets (Identical to Askalaser)
        // ==========================================

        public static readonly Vector2Int[] YellowOffsets = new Vector2Int[]
        {
            new Vector2Int(0, 2),
            new Vector2Int(-1, 1), new Vector2Int(0, 1), new Vector2Int(1, 1),
            new Vector2Int(-2, 0), new Vector2Int(-1, 0), new Vector2Int(1, 0), new Vector2Int(2, 0),
            new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
            new Vector2Int(0, -2)
        };

        public static readonly Vector2Int[] GreenOffsets = new Vector2Int[]
        {
            new Vector2Int(-1, 2), new Vector2Int(1, 2),
            new Vector2Int(-2, 1), new Vector2Int(2, 1),
            new Vector2Int(-2, -1), new Vector2Int(2, -1),
            new Vector2Int(-1, -2), new Vector2Int(1, -2)
        };

        public static bool IsInYellowPattern(Vector2Int offset)
        {
            if (offset.y == 2 && offset.x == 0) return true;
            if (offset.y == 1 && offset.x >= -1 && offset.x <= 1) return true;
            if (offset.y == 0 && ((offset.x >= -2 && offset.x <= -1) || (offset.x >= 1 && offset.x <= 2))) return true;
            if (offset.y == -1 && offset.x >= -1 && offset.x <= 1) return true;
            if (offset.y == -2 && offset.x == 0) return true;
            return false;
        }

        public static bool IsInGreenPattern(Vector2Int offset)
        {
            if (offset.y == 2 && (offset.x == -1 || offset.x == 1)) return true;
            if (offset.y == 1 && (offset.x == -2 || offset.x == 2)) return true;
            if (offset.y == -1 && (offset.x == -2 || offset.x == 2)) return true;
            if (offset.y == -2 && (offset.x == -1 || offset.x == 1)) return true;
            return false;
        }

        public Vector2Int GetOperatorGridPos(OperatorBase op)
        {
            if (op == null) return Vector2Int.zero;
            if (op.DeployedCell != null)
                return op.DeployedCell.GridPosition;

            var gm = EffectiveGridManager;
            if (gm != null)
                return gm.WorldToGridPosition(op.transform.position);

            return new Vector2Int(Mathf.RoundToInt(op.transform.position.x), Mathf.RoundToInt(op.transform.position.y));
        }

        public Vector2Int GetMyGridPos()
        {
            var gm = EffectiveGridManager;
            if (gm != null)
                return gm.WorldToGridPosition(transform.position);

            return new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.y));
        }

        public bool IsTargetInBindRange(OperatorBase op)
        {
            if (op == null || !op.IsDeployed || op.CurrentHP <= 0 || !op.IsTargetable || op.IsCamouflaged || op.IsBound)
                return false;

            Vector2Int opPos = GetOperatorGridPos(op);
            Vector2Int myPos = GetMyGridPos();
            Vector2Int offset = opPos - myPos;

            if (IsInYellowPattern(offset))
                return true;

            if (IsInGreenPattern(offset))
                return op.Data != null && op.Data.position == OperatorPosition.Ranged;

            return false;
        }

        private IReadOnlyList<OperatorBase> GetDeployedOperators()
        {
            if (OperatorManager.Instance != null)
                return OperatorManager.Instance.DeployedOperators;

            return FindObjectsByType<OperatorBase>();
        }

        public OperatorBase FindBindTarget()
        {
            var deployed = GetDeployedOperators();
            if (deployed == null || deployed.Count == 0) return null;

            OperatorBase bestTarget = null;
            int lowestRes = int.MaxValue;
            float closestDist = float.MaxValue;

            OperatorBase tauntTarget = null;
            float tauntDist = float.MaxValue;

            for (int i = 0; i < deployed.Count; i++)
            {
                OperatorBase op = deployed[i];
                if (op == null || !op.IsDeployed || op.CurrentHP <= 0 || op.IsBound) continue;
                if (!IsTargetInBindRange(op)) continue;

                float dist = (op.transform.position - transform.position).sqrMagnitude;

                if (op.ShouldTauntAttacker(transform.position, EffectiveGridManager))
                {
                    if (dist < tauntDist)
                    {
                        tauntDist = dist;
                        tauntTarget = op;
                    }
                    continue;
                }

                int opRes = op.CurrentRES;
                if (opRes < lowestRes || (opRes == lowestRes && dist < closestDist))
                {
                    lowestRes = opRes;
                    closestDist = dist;
                    bestTarget = op;
                }
            }

            return tauntTarget != null ? tauntTarget : bestTarget;
        }

        public void StartBind(OperatorBase target)
        {
            if (target == null || target.IsBound) return;

            // Turn Hextension to face the operator being bound so its wire arm aligns properly
            float dx = target.transform.position.x - transform.position.x;
            UpdateFacing(dx);

            hasCastBind = true;
            UpdateInvisibilityVisual();
            boundTarget = target;
            bindDurationRemaining = BindDuration;
            chipDamageTimer = 0f;
            state = HextensionState.Binding;

            // Proxyshot breaks the bind immediately
            if (target is ProxishotOperator || target.IsCamouflaged)
            {
                FloatingCombatNumber.ShowText(target.transform.position, "BIND BROKEN!", new Color(0.6f, 0.9f, 1f));
                StopBindAndTransition();
                return;
            }

            if (!hasManualWireColor)
            {
                currentWireColor = WireColorPalette.GetRandomWireColor();
            }

            target.ApplyBind(BindDuration, this);
            lineVisual = BindingLineVisual.Create(transform, target, currentWireColor);
        }

        public void StopBindAndTransition()
        {
            hasManualWireColor = false;
            if (boundTarget != null)
            {
                boundTarget.RemoveBind();
                boundTarget = null;
            }

            if (lineVisual != null)
            {
                lineVisual.Stop();
                lineVisual = null;
            }

            if (hasCastBind && currentATK > 0)
            {
                currentATK = Mathf.RoundToInt(currentATK * PostBindAttackMultiplier);
            }

            UpdateInvisibilityVisual();
            state = HextensionState.NormalCaster;
        }

        protected override void MoveAlongPath()
        {
            if (state == HextensionState.Binding)
                return;

            base.MoveAlongPath();
        }

        protected override void Update()
        {
            if (IsDead) return;

            if (state == HextensionState.NormalCaster)
            {
                UpdateRangedBehavior(prioritizeRangedOperators: false);
                return;
            }

            base.Update();

            if (IsDead) return;

            if (IsInsideHole || IsFrozen || IsStunned || IsPushingBack)
            {
                if (state == HextensionState.Binding)
                {
                    StopBindAndTransition();
                }
                return;
            }

            if (state == HextensionState.SearchingForBind)
            {
                UpdateSearching(Time.deltaTime);
            }
            else if (state == HextensionState.Binding)
            {
                UpdateBinding(Time.deltaTime);
            }
        }

        public void UpdateSearching(float deltaTime)
        {
            if (IsDead) return;

            UpdateInvisibilityVisual();

            OperatorBase target = FindBindTarget();
            if (target != null)
            {
                StartBind(target);
            }
        }

        public void UpdateBinding(float deltaTime)
        {
            if (IsDead) return;

            if (boundTarget == null || !boundTarget.IsDeployed || boundTarget.CurrentHP <= 0 || boundTarget is ProxishotOperator || boundTarget.IsCamouflaged)
            {
                StopBindAndTransition();
                return;
            }

            // Chip damage
            chipDamageTimer += deltaTime;
            while (chipDamageTimer >= ChipDamageInterval)
            {
                chipDamageTimer -= ChipDamageInterval;
                if (boundTarget != null && boundTarget.IsDeployed && boundTarget.CurrentHP > 0)
                {
                    boundTarget.TakeDamage(ChipDamageAmount, DamageType.Arts, this);
                    if (boundTarget == null || !boundTarget.IsDeployed || boundTarget.CurrentHP <= 0)
                    {
                        StopBindAndTransition();
                        return;
                    }
                }
            }

            bindDurationRemaining -= deltaTime;
            if (bindDurationRemaining <= 0f)
            {
                StopBindAndTransition();
            }
        }

        protected override void Die()
        {
            if (state == HextensionState.Binding)
            {
                StopBindAndTransition();
            }
            base.Die();
        }

        protected virtual void OnDestroy()
        {
            if (lineVisual != null)
            {
                lineVisual.Stop();
                lineVisual = null;
            }
            if (boundTarget != null)
            {
                boundTarget.RemoveBind();
                boundTarget = null;
            }
        }
    }
}
