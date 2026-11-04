using UnityEngine;
using TrashTD.Enemies;

namespace TrashTD.Combat
{
    /// <summary>
    /// Enraged status effect applied to an enemy blocked and stalled by Mossmo for 20 seconds.
    /// - Grants +50% ATK
    /// - Takes 5% Max HP as True Damage every 1.0 second tick until defeated.
    /// - Visual cues: floating combat text "ENRAGED!"
    /// </summary>
    public class MossmoEnragedEffect : MonoBehaviour
    {
        public const float TickInterval = 1.0f;
        public const float AtkMultiplier = 0.50f;
        public const float HpTickPercent = 0.05f;

        [SerializeField] private EnemyBase targetEnemy;
        [SerializeField] private int bonusAtk;
        private float tickTimer;

        public EnemyBase TargetEnemy => targetEnemy;
        public int BonusAtk => bonusAtk;

        public static MossmoEnragedEffect Apply(EnemyBase enemy)
        {
            if (enemy == null || enemy.IsDead) return null;

            var existing = enemy.GetComponent<MossmoEnragedEffect>();
            if (existing != null) return existing;

            var effect = enemy.gameObject.AddComponent<MossmoEnragedEffect>();
            effect.targetEnemy = enemy;
            enemy.IsEnraged = true;

            effect.bonusAtk = Mathf.Max(1, Mathf.RoundToInt(enemy.CurrentATK * AtkMultiplier));
            enemy.ModifyATK(effect.bonusAtk);

            effect.tickTimer = TickInterval;
            FloatingCombatNumber.ShowText(enemy.VisualPosition, "ENRAGED!", new Color(1f, 0.22f, 0.15f));

            return effect;
        }

        private void Update()
        {
            if (targetEnemy == null || targetEnemy.IsDead)
            {
                Destroy(this);
                return;
            }

            tickTimer -= Time.deltaTime;
            if (tickTimer <= 0f)
            {
                tickTimer += TickInterval;
                int damage = Mathf.Max(1, Mathf.RoundToInt(targetEnemy.MaxHP * HpTickPercent));
                targetEnemy.TakeTrueDamage(damage);
            }
        }
    }
}

