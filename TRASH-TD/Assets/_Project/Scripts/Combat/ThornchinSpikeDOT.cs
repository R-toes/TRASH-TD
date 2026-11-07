using System.Collections.Generic;
using UnityEngine;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;

namespace TrashTD.Combat
{
    /// <summary>
    /// Thornchin's Retaliation Spine DOT:
    /// Deals 5 Arts damage every second for 5 seconds per stack.
    /// Stacks up to 4 times (up to 20 Arts damage per second).
    /// </summary>
    [DisallowMultipleComponent]
    public class ThornchinSpikeDOT : MonoBehaviour
    {
        public const int MaxStacks = 4;
        public const float Duration = 5.0f;
        public const int DamagePerSecond = 5;

        private sealed class StackInfo
        {
            public float remainingTime;
        }

        private readonly List<StackInfo> stacks = new List<StackInfo>(MaxStacks);
        private EnemyBase enemy;
        private OperatorBase sourceOperator;
        private float tickTimer = 0f;

        public int CurrentStacks => stacks.Count;

        public static ThornchinSpikeDOT GetOn(EnemyBase target)
        {
            return target != null ? target.GetComponent<ThornchinSpikeDOT>() : null;
        }

        public static void Apply(EnemyBase target, OperatorBase source)
        {
            if (target == null || target.IsDead) return;

            var dot = target.GetComponent<ThornchinSpikeDOT>();
            if (dot == null)
            {
                dot = target.gameObject.AddComponent<ThornchinSpikeDOT>();
                dot.enemy = target;
            }

            dot.sourceOperator = source;
            dot.AddStack();
        }

        public void AddStack()
        {
            if (stacks.Count < MaxStacks)
            {
                stacks.Add(new StackInfo { remainingTime = Duration });
            }
            else
            {
                // Refresh oldest stack
                int oldestIdx = 0;
                float minTime = stacks[0].remainingTime;
                for (int i = 1; i < stacks.Count; i++)
                {
                    if (stacks[i].remainingTime < minTime)
                    {
                        minTime = stacks[i].remainingTime;
                        oldestIdx = i;
                    }
                }
                stacks[oldestIdx].remainingTime = Duration;
            }
        }

        private void Update()
        {
            if (enemy == null || enemy.IsDead)
            {
                Destroy(this);
                return;
            }

            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            if (enemy == null || enemy.IsDead) return;

            // Decay stack timers
            for (int i = stacks.Count - 1; i >= 0; i--)
            {
                stacks[i].remainingTime -= dt;
                if (stacks[i].remainingTime <= 0f)
                {
                    stacks.RemoveAt(i);
                }
            }

            if (stacks.Count == 0)
            {
                Destroy(this);
                return;
            }

            // Tick damage every 1.0 second
            tickTimer += dt;
            while (tickTimer >= 1.0f)
            {
                tickTimer -= 1.0f;
                int rawDamage = DamagePerSecond * stacks.Count;
                int calculatedDamage = DamageCalculator.CalculateDamage(rawDamage, enemy.CurrentRES);
                enemy.TryTakeAttackDamage(calculatedDamage, DamageType.Arts, sourceOperator, canHitAir: true);
                CombatFx.Sparks(enemy.VisualPosition, new Color(0.75f, 0.2f, 0.85f), 2, 0.8f, 0.15f);
            }
        }
    }
}
