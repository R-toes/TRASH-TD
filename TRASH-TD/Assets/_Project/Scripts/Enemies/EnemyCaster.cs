namespace TrashTD.Enemies
{
    /// <summary>
    /// Enemy Caster — ranged enemy (GDD 1.5). Operators can block it (weight 1): a blocked caster stops in
    /// front of its blocker but keeps attacking its usual targets from range instead of meleeing the blocker.
    /// Countered by kill priority / range denial.
    /// </summary>
    public class EnemyCaster : EnemyBase
    {
        protected override void Update()
        {
            UpdateRangedBehavior(prioritizeRangedOperators: false);
        }
    }
}
