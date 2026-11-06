using UnityEngine;
using TrashTD.Enemies;

namespace TrashTD.Combat
{
    /// <summary>
    /// Enraged status effect applied to an enemy blocked and stalled by Mossmo for 20 seconds.
    /// - Grants +35% ATK (or +25% if applied by 2x Upgraded Mossmo)
    /// - Takes 5% Max HP as True Damage every 1.0 second tick until defeated.
    /// - Visual cues: overhead animated EnragedVisual sprite sheet.
    /// </summary>
    public class MossmoEnragedEffect : MonoBehaviour
    {
        public const float TickInterval = 1.0f;
        public const float DefaultAtkMultiplier = 0.35f;
        public const float UpgradedAtkMultiplier = 0.25f;
        public const float HpTickPercent = 0.05f;

        [SerializeField] private EnemyBase targetEnemy;
        [SerializeField] private int bonusAtk;
        private float tickTimer;
        private float atkMultiplier = DefaultAtkMultiplier;

        public EnemyBase TargetEnemy => targetEnemy;
        public int BonusAtk => bonusAtk;
        public float AtkMultiplier => atkMultiplier;

        public static MossmoEnragedEffect Apply(EnemyBase enemy, float multiplier = DefaultAtkMultiplier)
        {
            if (enemy == null || enemy.IsDead) return null;

            var existing = enemy.GetComponent<MossmoEnragedEffect>();
            if (existing != null) return existing;

            var effect = enemy.gameObject.AddComponent<MossmoEnragedEffect>();
            effect.targetEnemy = enemy;
            effect.atkMultiplier = multiplier;
            enemy.IsEnraged = true;

            effect.bonusAtk = Mathf.Max(1, Mathf.RoundToInt(enemy.CurrentATK * multiplier));
            enemy.ModifyATK(effect.bonusAtk);

            effect.tickTimer = TickInterval;
            EnragedVisual.Show(enemy.gameObject, enemy.GetPrimaryVisualRenderer());

            return effect;
        }

        private void Update()
        {
            if (targetEnemy == null || targetEnemy.IsDead)
            {
                EnragedVisual.Hide(gameObject);
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

        private void OnDestroy()
        {
            if (targetEnemy != null)
            {
                EnragedVisual.Hide(targetEnemy.gameObject);
                targetEnemy.IsEnraged = false;
            }
            else
            {
                EnragedVisual.Hide(gameObject);
            }
        }
    }
}

