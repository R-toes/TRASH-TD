using UnityEngine;
using TrashTD.Enemies;
using TrashTD.Operators;

namespace TrashTD.Combat
{
    /// <summary>
    /// Enraged status effect applied to an enemy blocked and stalled by Mossmo for 20 seconds.
    /// - Grants +35% ATK (or +25% if applied by 2x Upgraded Mossmo)
    /// - Takes 5% Max HP as True Damage every 1.0 second tick until defeated.
    /// - Visual cues: overhead animated EnragedVisual sprite sheet.
    /// - Removed when the Mossmo who gave/triggered this effect dies (or retreats).
    /// </summary>
    public class MossmoEnragedEffect : MonoBehaviour
    {
        public const float TickInterval = 1.0f;
        public const float DefaultAtkMultiplier = 0.35f;
        public const float UpgradedAtkMultiplier = 0.25f;
        public const float HpTickPercent = 0.05f;

        [SerializeField] private EnemyBase targetEnemy;
        [SerializeField] private MossmoOperator sourceMossmo;
        [SerializeField] private int bonusAtk;
        private bool hasSourceMossmo;
        private float tickTimer;
        private float atkMultiplier = DefaultAtkMultiplier;

        public EnemyBase TargetEnemy => targetEnemy;
        public MossmoOperator SourceMossmo => sourceMossmo;
        public bool HasSourceMossmo => hasSourceMossmo;
        public int BonusAtk => bonusAtk;
        public float AtkMultiplier => atkMultiplier;

        public static MossmoEnragedEffect Apply(EnemyBase enemy, float multiplier = DefaultAtkMultiplier, MossmoOperator source = null)
        {
            if (enemy == null || enemy.IsDead) return null;

            var existing = enemy.GetComponent<MossmoEnragedEffect>();
            if (existing != null)
            {
                if (source != null && existing.sourceMossmo == null)
                {
                    existing.sourceMossmo = source;
                    existing.hasSourceMossmo = true;
                    source.RegisterEnragedEnemy(enemy);
                }
                return existing;
            }

            var effect = enemy.gameObject.AddComponent<MossmoEnragedEffect>();
            effect.targetEnemy = enemy;
            effect.atkMultiplier = multiplier;
            effect.sourceMossmo = source;
            effect.hasSourceMossmo = source != null;
            if (source != null)
            {
                source.RegisterEnragedEnemy(enemy);
            }
            enemy.IsEnraged = true;

            effect.bonusAtk = multiplier > 0f ? Mathf.Max(1, Mathf.RoundToInt(enemy.CurrentATK * multiplier)) : 0;
            if (effect.bonusAtk > 0)
            {
                enemy.ModifyATK(effect.bonusAtk);
            }

            effect.tickTimer = TickInterval;
            EnragedVisual.Show(enemy.gameObject, enemy.GetPrimaryVisualRenderer());

            return effect;
        }

        public void Remove()
        {
            if (targetEnemy != null)
            {
                if (bonusAtk > 0)
                {
                    targetEnemy.ModifyATK(-bonusAtk);
                    bonusAtk = 0;
                }
                EnragedVisual.Hide(targetEnemy.gameObject);
                targetEnemy.IsEnraged = false;
            }
            else
            {
                EnragedVisual.Hide(gameObject);
            }

            if (hasSourceMossmo && sourceMossmo != null)
            {
                sourceMossmo.UnregisterEnragedEnemy(targetEnemy);
            }

            if (Application.isPlaying)
            {
                Destroy(this);
            }
            else
            {
                DestroyImmediate(this);
            }
        }

        public static void Remove(EnemyBase enemy)
        {
            if (enemy == null) return;
            var effect = enemy.GetComponent<MossmoEnragedEffect>();
            if (effect != null)
            {
                effect.Remove();
            }
        }

        private void Update()
        {
            if (targetEnemy == null || targetEnemy.IsDead)
            {
                Remove();
                return;
            }

            if (hasSourceMossmo && (sourceMossmo == null || sourceMossmo.CurrentHP <= 0 || !sourceMossmo.IsDeployed))
            {
                Remove();
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
                if (bonusAtk > 0)
                {
                    targetEnemy.ModifyATK(-bonusAtk);
                    bonusAtk = 0;
                }
                EnragedVisual.Hide(targetEnemy.gameObject);
                targetEnemy.IsEnraged = false;
            }
            else
            {
                EnragedVisual.Hide(gameObject);
            }

            if (hasSourceMossmo && sourceMossmo != null)
            {
                sourceMossmo.UnregisterEnragedEnemy(targetEnemy);
            }
        }
    }
}

