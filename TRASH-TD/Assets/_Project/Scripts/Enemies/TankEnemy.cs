using TrashTD.Combat;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Tank enemy — high HP/DEF (GDD 1.5).
    /// Slow but very durable. Countered by sustained DPS and Arts damage.
    /// A tank whose prefab has attack frames (Pollution Tank) swings its blocker on the animation's hit frame.
    /// </summary>
    public class TankEnemy : EnemyBase
    {
        private TankSpriteAnimation tankAnimation;

        protected override void Update()
        {
            // A frozen tank holds its attack pose and finishes the swing once thawed; a stun drops the attack
            if (IsStunned && tankAnimation != null)
                tankAnimation.CancelAttack();

            if (data != null && data.frontAreaAttackTiles > 0)
            {
                UpdateFrontalAreaAttackBehavior(data.frontAreaAttackTiles);
                return;
            }

            base.Update();
        }

        public override void Initialize(Data.EnemyData enemyData, int difficultyLevel)
        {
            base.Initialize(enemyData, difficultyLevel);
            tankAnimation = GetComponentInChildren<TankSpriteAnimation>();
            // Tank enemies are inherently slower but tougher
            // Their high HP/DEF comes from the EnemyData asset values
        }

        protected override void AttackBlocker()
        {
            // Without an active attack animation (none authored, or SandHulk's own sprite disabled it) use the default instant hit.
            if (tankAnimation == null || !tankAnimation.enabled || !tankAnimation.HasAttackAnimation)
            {
                base.AttackBlocker();
                return;
            }

            if (blockingOperator == null || tankAnimation.IsAttacking) return;

            attackTimer += UnityEngine.Time.deltaTime;
            if (attackTimer < GetEffectiveAttackInterval()) return;

            attackTimer = 0f;
            tankAnimation.PlayAttack(DealBlockerHit);
        }

        /// <summary>Lands the swing on the animation's hit frame.</summary>
        private void DealBlockerHit()
        {
            if (isDead || !isBlocked || blockingOperator == null) return;

            PlayAttackSound();
            MeleeSwipeVisual.Play(VisualPosition, blockingOperator.transform.position,
                new UnityEngine.Color(1f, 0.35f, 0.25f, 1f), 1f, MeleeStyle.Bash);
            blockingOperator.TryTakeAttackDamage(currentATK, data.damageType, this);
        }

        public override void OnUnblocked()
        {
            if (tankAnimation != null) tankAnimation.CancelAttack();
            base.OnUnblocked();
        }
    }
}
