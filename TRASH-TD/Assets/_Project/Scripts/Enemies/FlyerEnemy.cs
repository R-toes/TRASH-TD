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
        protected override void Update()
        {
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
