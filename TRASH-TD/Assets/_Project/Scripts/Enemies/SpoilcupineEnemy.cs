using UnityEngine;
using TrashTD.Combat;
using TrashTD.Data;
using TrashTD.Operators;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Spoilcupine - Grunt enemy with high HP, low RES, low DEF, and speed 1.0.
    /// Ability (Quill Retaliation):
    /// When receiving damage greater than 25% of its max HP in a single hit,
    /// it reflects back 25% of that damage to the attacker.
    /// </summary>
    public class SpoilcupineEnemy : EnemyBase
    {
        public const float DefaultThresholdPercent = 0.25f;
        public const float DefaultReflectionPercent = 0.25f;

        [Header("Spoilcupine Reflection Settings")]
        [Range(0f, 1f)]
        [SerializeField] private float thresholdPercent = DefaultThresholdPercent;

        [Range(0f, 1f)]
        [SerializeField] private float reflectionPercent = DefaultReflectionPercent;

        [SerializeField] private DamageType reflectDamageType = DamageType.Physical;

        [System.NonSerialized] private bool isReflecting;

        public float ThresholdPercent
        {
            get => thresholdPercent;
            set => thresholdPercent = Mathf.Clamp01(value);
        }

        public float ReflectionPercent
        {
            get => reflectionPercent;
            set => reflectionPercent = Mathf.Clamp01(value);
        }

        public DamageType ReflectDamageType
        {
            get => reflectDamageType;
            set => reflectDamageType = value;
        }

        public override bool TryTakeAttackDamage(int damage, DamageType damageType, OperatorBase attacker = null, bool canHitAir = false)
        {
            if (isDead || IsInsideHole) return false;

            int threshold = Mathf.RoundToInt(maxHP * thresholdPercent);
            bool shouldReflect = damage > threshold && damage > 0 && !isReflecting;

            bool hit = base.TryTakeAttackDamage(damage, damageType, attacker, canHitAir);
            if (hit && shouldReflect)
            {
                TriggerRetaliation(damage, attacker);
            }

            return hit;
        }

        private void TriggerRetaliation(int incomingDamage, OperatorBase attacker)
        {
            OperatorBase targetOperator = attacker ?? blockingOperator;
            if (targetOperator == null || !targetOperator.IsDeployed || targetOperator.CurrentHP <= 0)
                return;

            isReflecting = true;
            try
            {
                int reflectDamage = Mathf.Max(1, Mathf.RoundToInt(incomingDamage * reflectionPercent));

                // Floating text indicator
                FloatingCombatNumber.ShowText(VisualPosition, "REFLECT!", new Color(1f, 0.45f, 0.15f));
                PlayAttackSound();

                Vector3 startPos = VisualPosition;
                Vector3 targetPos = targetOperator.transform.position;
                float distance = Vector2.Distance(startPos, targetPos);
                Color quillColor = new Color(0.95f, 0.45f, 0.15f, 1f);

                if (distance <= 1.5f)
                {
                    MeleeSwipeVisual.Play(startPos, targetPos, quillColor);
                    targetOperator.TakeDamage(reflectDamage, reflectDamageType, this);
                }
                else
                {
                    CombatProjectileVisual.Fire(
                        ProjectileOrigin,
                        targetPos,
                        quillColor,
                        14f,
                        0.09f,
                        0.04f,
                        false,
                        () =>
                        {
                            if (targetOperator != null && targetOperator.IsDeployed && targetOperator.CurrentHP > 0)
                            {
                                targetOperator.TakeDamage(reflectDamage, reflectDamageType, this);
                            }
                        },
                        ProjectileStyle.Quill);
                }
            }
            finally
            {
                isReflecting = false;
            }
        }
    }
}

