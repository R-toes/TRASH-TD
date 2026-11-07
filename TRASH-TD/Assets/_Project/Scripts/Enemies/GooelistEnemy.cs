using UnityEngine;
using TrashTD.Combat;
using TrashTD.Data;
using TrashTD.Operators;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Gooelist — Specialized duelist enemy.
    /// Characteristics:
    /// - Physical damage type, High DEF, Low RES.
    /// - Ability: Duel & Armor Shred when blocked:
    ///   - When blocked, this unit only receives damage from the unit blocking it.
    ///   - All other attacks/damage sources from non-blockers are deflected.
    ///   - When blocked, DEF is reduced: 75% reduction for Gooelist, 50% reduction for the blocking operator.
    /// </summary>
    public class GooelistEnemy : EnemyBase
    {
        public const float DefaultSelfDefReductionPercent = 0.75f;
        public const float DefaultOperatorDefReductionPercent = 0.50f;

        [Header("Gooelist Duel Settings")]
        [Range(0f, 1f)]
        [SerializeField] private float selfDefReductionPercent = DefaultSelfDefReductionPercent;

        [Range(0f, 1f)]
        [SerializeField] private float operatorDefReductionPercent = DefaultOperatorDefReductionPercent;

        private PersistentStatusText duelStatusText;

        public PersistentStatusText DuelStatusText => duelStatusText;

        public float SelfDefReductionPercent
        {
            get => selfDefReductionPercent;
            set => selfDefReductionPercent = Mathf.Clamp01(value);
        }

        public float OperatorDefReductionPercent
        {
            get => operatorDefReductionPercent;
            set => operatorDefReductionPercent = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Backward-compatible alias for self reduction percent.
        /// </summary>
        public float DefReductionPercent
        {
            get => selfDefReductionPercent;
            set => selfDefReductionPercent = Mathf.Clamp01(value);
        }

        public override int CurrentDEF
        {
            get
            {
                int def = currentDEF;
                if (isBlocked && blockingOperator != null)
                {
                    def = Mathf.RoundToInt(def * (1f - selfDefReductionPercent));
                }
                return Mathf.Max(0, def);
            }
        }

        public override bool CanReceiveDamageFrom(OperatorBase attacker)
        {
            if (isBlocked && blockingOperator != null)
            {
                return attacker == blockingOperator;
            }
            return true;
        }

        public override bool TryTakeAttackDamage(int damage, DamageType damageType, OperatorBase attacker = null, bool canHitAir = false)
        {
            if (!CanReceiveDamageFrom(attacker))
            {
                FloatingCombatNumber.ShowText(VisualPosition, "DUEL", Color.white, this);
                return false;
            }

            return base.TryTakeAttackDamage(damage, damageType, attacker, canHitAir);
        }

        public override void OnBlocked(OperatorBase blocker)
        {
            base.OnBlocked(blocker);
            ClearDuelStatusText();
            duelStatusText = PersistentStatusText.Show(
                gameObject,
                "DUEL",
                Color.white,
                0.75f,
                "Duel",
                new Color(1f, 0.45f, 0.2f));
        }

        public override void OnUnblocked()
        {
            base.OnUnblocked();
            ClearDuelStatusText();
        }

        protected override void Die()
        {
            ClearDuelStatusText();
            base.Die();
        }

        protected virtual void OnDestroy()
        {
            ClearDuelStatusText();
        }

        protected override void Update()
        {
            base.Update();
            if (duelStatusText != null && (!isBlocked || blockingOperator == null || isDead))
            {
                ClearDuelStatusText();
            }
        }

        private void ClearDuelStatusText()
        {
            if (duelStatusText != null)
            {
                duelStatusText.Dismiss();
                duelStatusText = null;
            }
            PersistentStatusText.Hide(gameObject, "Duel");
        }
    }
}
