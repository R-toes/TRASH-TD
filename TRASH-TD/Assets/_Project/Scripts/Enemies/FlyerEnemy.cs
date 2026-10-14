namespace TrashTD.Enemies
{
    /// <summary>
    /// Flyer enemy — follows the shared ground route but remains an air target.
    /// Countered by ranged operators.
    /// 
    /// Movement type is set to Air via EnemyData.movementType.
    /// </summary>
    public class FlyerEnemy : EnemyBase
    {
        /// <summary>
        /// True for flyers that run their own attack logic (AirCombatEnemy); they only need the
        /// base movement/status update, not this class's default projectile attack.
        /// </summary>
        protected virtual bool HandlesOwnAttacks => false;

        protected override void Update()
        {
            if (HandlesOwnAttacks)
            {
                // Flyers are never blocked, so the base update only animates, applies status and moves.
                base.Update();
                return;
            }

            UpdateRangedBehavior(prioritizeRangedOperators: true);
        }

        /// <summary>
        /// Flyers ignore blocking — they fly over ground operators.
        /// </summary>
        public override void OnBlocked(Operators.OperatorBase blocker)
        {
            // Flyers cannot be blocked — they fly over everything
        }
    }
}
