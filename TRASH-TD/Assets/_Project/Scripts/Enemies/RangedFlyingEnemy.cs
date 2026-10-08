using UnityEngine;
using TrashTD.Data;
using TrashTD.Operators;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Ranged air enemy: Arts bolt with a very small blast. Hunts Casters, then Snipers,
    /// then Guards; ignores Defenders and Medics.
    /// Stats (high RES, no DEF, fast) live on Enemy_Flying_Ranged.asset.
    /// </summary>
    public sealed class RangedFlyingEnemy : AirCombatEnemy
    {
        // EnemyData.attackRange is an int, so the 1.5-tile circle is defined here.
        private const float AttackRadiusInTiles = 1.5f;
        // Just reaches orthogonally adjacent tiles (diagonals are ~1.41 away).
        private const float BlastRadiusInTiles = 1.05f;
        private const float CollateralRatio = 0.5f;

        protected override bool IsTargetable(OperatorBase op)
        {
            return op.OperatorClass == OperatorClass.Caster ||
                   op.OperatorClass == OperatorClass.Sniper ||
                   op.OperatorClass == OperatorClass.Guard;
        }

        protected override int GetTargetPriority(OperatorBase op)
        {
            if (op.OperatorClass == OperatorClass.Caster) return 0;
            if (op.OperatorClass == OperatorClass.Sniper) return 1;
            return 2;
        }

        protected override bool IsTargetInRange(OperatorBase op)
        {
            return IsWithinCircularRange(op, AttackRadiusInTiles);
        }

        protected override void PerformAirAttack(OperatorBase target)
        {
            PlayAirRangedAttack(target, BlastRadiusInTiles, CollateralRatio);
        }

        protected override Color GetAirColor()
        {
            return new Color(0.55f, 0.9f, 1f, 1f);
        }
    }
}
