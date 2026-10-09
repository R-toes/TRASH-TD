using TrashTD.Operators;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Enemy Caster — ranged enemy that ignores blockers (GDD 1.5).
    /// Attacks operators from range. Countered by kill priority / range denial.
    /// </summary>
    public class EnemyCaster : EnemyBase
    {
        protected override void Update()
        {
            UpdateRangedBehavior(prioritizeRangedOperators: false);
        }

        public override void OnBlocked(OperatorBase blocker)
        {
            // Enemy casters are immune to blocking per GDD 1.4.4 & 1.5
        }
    }
}
