namespace TrashTD.Enemies
{
    /// <summary>
    /// Tank enemy — high HP/DEF (GDD 1.5).
    /// Slow but very durable. Countered by sustained DPS and Arts damage.
    /// </summary>
    public class TankEnemy : EnemyBase
    {
        public override void Initialize(Data.EnemyData enemyData, int difficultyLevel)
        {
            base.Initialize(enemyData, difficultyLevel);
            // Tank enemies are inherently slower but tougher
            // Their high HP/DEF comes from the EnemyData asset values
        }

        // Tank uses default behavior — durability comes from high stat values
        // set in the EnemyData ScriptableObject, not from code overrides.
        // This is intentional: data-driven design per GDD 1.10.
    }
}
