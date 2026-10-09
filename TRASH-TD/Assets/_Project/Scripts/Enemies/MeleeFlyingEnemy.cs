using UnityEngine;
using TrashTD.Data;
using TrashTD.Operators;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Melee air enemy: single-target physical strike on the tile below or any adjacent tile.
    /// Hunts ranged operators (Snipers, Medics), then Defenders, then Guards; ignores Casters.
    /// Stats (high DEF, low RES, Grunt speed) live on Enemy_Flying_Melee.asset.
    /// </summary>
    public sealed class MeleeFlyingEnemy : AirCombatEnemy
    {
        protected override bool IsTargetable(OperatorBase op)
        {
            return op.OperatorClass != OperatorClass.Caster;
        }

        protected override int GetTargetPriority(OperatorBase op)
        {
            if (op.OperatorClass == OperatorClass.Sniper || op.OperatorClass == OperatorClass.Medic) return 0;
            if (op.OperatorClass == OperatorClass.Defender) return 1;
            return 2;
        }

        protected override bool IsTargetInRange(OperatorBase op)
        {
            return IsOnOrAdjacent(op);
        }

        protected override void PerformAirAttack(OperatorBase target)
        {
            PlayAirMeleeAttack(target);
        }

        protected override Color GetAirColor()
        {
            return new Color(1f, 0.55f, 0.45f, 1f);
        }
    }
}
