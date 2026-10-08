using UnityEngine;
using TrashTD.Combat;
using TrashTD.Data;
using TrashTD.Enemies;

namespace TrashTD.Operators
{
    /// <summary>
    /// A front-line guard who channels a chi paw strike and knocks enemies back every fourth hit.
    /// </summary>
    public sealed class QiFuOperator : GuardOperator
    {
        private const int HitsPerKnockback = 4;
        private int hitsSinceKnockback;

        public override void Initialize(OperatorData operatorData, OperatorRarity rarity, OperatorFacing operatorFacing = OperatorFacing.Right)
        {
            base.Initialize(operatorData, rarity, operatorFacing);
            hitsSinceKnockback = 0;
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed)
                return;

            PlayAttackSound();
            QiFuChiAttackVisual.Fire(
                transform.position,
                target,
                () => ApplyChiHit(target));
        }

        private bool ApplyChiHit(EnemyBase target)
        {
            if (target == null || target.IsDead || !isDeployed)
                return false;

            int damage = DamageCalculator.CalculateDamage(currentATK, GetTargetMitigation(target));
            if (!target.TryTakeAttackDamage(damage, Data.damageType))
                return false;

            hitsSinceKnockback++;
            if (hitsSinceKnockback < HitsPerKnockback)
                return false;

            hitsSinceKnockback = 0;
            return !target.IsDead && target.PushBack(1, QiFuChiAttackVisual.PushbackDuration);
        }
    }
}
