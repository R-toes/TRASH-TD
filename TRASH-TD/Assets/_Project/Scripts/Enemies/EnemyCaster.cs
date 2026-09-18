using UnityEngine;
using TrashTD.Combat;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Enemy Caster — ranged enemy that ignores blockers (GDD 1.5).
    /// Attacks operators from range. Countered by kill priority / range denial.
    /// </summary>
    public class EnemyCaster : EnemyBase
    {
        public override void OnBlocked(OperatorBase blocker)
        {
            // Enemy casters are immune to blocking per GDD 1.4.4 & 1.5
        }

        protected override void Update()
        {
            if (isDead) return;

            // Continues moving along path unimpeded
            MoveAlongPath();

            // Attacks operators at range while moving
            attackTimer += Time.deltaTime;
            if (attackTimer >= data.attackInterval)
            {
                attackTimer = 0f;
                AttackNearestOperator();
            }
        }

        private void AttackNearestOperator()
        {
            if (OperatorManager.Instance == null || data == null) return;

            float range = data.attackRange > 0 ? data.attackRange : 2.5f;
            float sqrRange = range * range;

            OperatorBase closest = null;
            float closestSqrDist = float.MaxValue;

            var deployed = OperatorManager.Instance.DeployedOperators;
            for (int i = 0; i < deployed.Count; i++)
            {
                var op = deployed[i];
                if (op == null || !op.IsDeployed) continue;

                float sqrDist = (op.transform.position - transform.position).sqrMagnitude;
                if (sqrDist <= sqrRange && sqrDist < closestSqrDist)
                {
                    closestSqrDist = sqrDist;
                    closest = op;
                }
            }

            if (closest != null)
            {
                closest.TakeDamage(currentATK, data.damageType);
            }
        }
    }
}
