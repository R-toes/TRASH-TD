namespace TrashTD.Enemies
{
    /// <summary>
    /// Flyer enemy — ignores ground path (GDD 1.5).
    /// Uses Air movement type, flies directly toward exit.
    /// Countered by anti-air (ranged) units only.
    /// 
    /// Movement type is set to Air via EnemyData.movementType.
    /// The A* pathfinder handles air pathing separately (GDD 1.4.2).
    /// </summary>
    public class FlyerEnemy : EnemyBase
    {
        public override void Initialize(Data.EnemyData enemyData, int difficultyLevel)
        {
            base.Initialize(enemyData, difficultyLevel);
            // Ensure movement type is Air (should already be set in EnemyData)
            // Flyers use the air pathfinding mode which ignores ground obstacles
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
