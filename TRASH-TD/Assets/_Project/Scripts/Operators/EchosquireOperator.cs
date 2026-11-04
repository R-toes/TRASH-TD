using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.GameLoop;
using TrashTD.Data;
using TrashTD.Enemies;

namespace TrashTD.Operators
{
    /// <summary>
    /// Vampire-themed guard who restores a portion of the physical damage dealt by each hit.
    /// </summary>
    public class EchosquireOperator : GuardOperator
    {
        private const float LifestealRatio = 0.2f;
        private const float MaxLowHealthBonus = 0.60f;
        private const float DodgeDuration = 0.5f;
        private const float SecondChanceHealthRatio = 0.5f;

        private float dodgeTimer;
        private int secondChanceWave = int.MinValue;
        private bool usedSecondChanceThisWave;

        public override bool CanReceiveHealing => false;
        public override int CurrentATK => Mathf.RoundToInt(currentATK * LowHealthStatMultiplier);
        public override int CurrentDEF => Mathf.RoundToInt(base.CurrentDEF * LowHealthStatMultiplier);
        public override int CurrentRES => Mathf.RoundToInt(currentRES * LowHealthStatMultiplier);

        private float LowHealthStatMultiplier
        {
            get
            {
                if (maxHP <= 0) return 1f;
                float missingHealthRatio = 1f - Mathf.Clamp01((float)currentHP / maxHP);
                return 1f + Mathf.Min(MaxLowHealthBonus, missingHealthRatio);
            }
        }

        protected override void Update()
        {
            if (dodgeTimer > 0f && !IsCombatPaused)
                dodgeTimer = Mathf.Max(0f, dodgeTimer - Time.deltaTime);

            base.Update();
        }

        public override void TakeDamage(int rawATK, DamageType damageType, EnemyBase attacker = null)
        {
            if (IsCombatPaused || currentHP <= 0 || rawATK <= 0) return;
            if (attacker != null && dodgeTimer > 0f)
            {
                ShowDodge();
                return;
            }

            base.TakeDamage(rawATK, damageType, attacker);
        }

        public override bool TakeTrueDamage(int damage, bool ignoreShieldNegation = false, bool canDodge = false)
        {
            if (IsCombatPaused || currentHP <= 0 || damage <= 0) return false;
            if ((ignoreShieldNegation || canDodge) && dodgeTimer > 0f)
            {
                ShowDodge();
                return false;
            }

            return base.TakeTrueDamage(damage, ignoreShieldNegation, canDodge);
        }

        protected override void Die()
        {
            if (currentRarity >= OperatorRarity.Star4 && TryUseSecondChance())
            {
                currentHP = Mathf.Max(1, Mathf.CeilToInt(maxHP * SecondChanceHealthRatio));
                WorldHealthBar.UpdateFor(gameObject, currentHP, maxHP);
                FloatingCombatNumber.ShowText(transform.position, "SECOND CHANCE!", new Color(0.9f, 0.2f, 0.3f));
                return;
            }

            base.Die();
        }

        private bool TryUseSecondChance()
        {
            WaveManager waveManager = FindAnyObjectByType<WaveManager>();
            int currentWave = waveManager == null
                ? 0
                : Mathf.Max(waveManager.CurrentWaveNumber, waveManager.SandboxWaveNumber);

            if (secondChanceWave != currentWave)
            {
                secondChanceWave = currentWave;
                usedSecondChanceThisWave = false;
            }

            if (usedSecondChanceThisWave) return false;
            usedSecondChanceThisWave = true;
            return true;
        }

        private void ShowDodge()
        {
            FloatingCombatNumber.ShowText(transform.position, "DODGE!", new Color(0.2f, 0.85f, 1f));
            PlayAbilitySound("Dodge");
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            MeleeSwipeVisual.Play(
                transform.position,
                target.VisualPosition,
                new Color(0.55f, 0.08f, 0.16f, 1f),
                1.8f);
            PlayAttackSound();

            CombatProjectileVisual.Fire(
                ProjectileOrigin,
                target,
                new Color(0.9f, 0.12f, 0.24f),
                12f,
                0.1f,
                0.05f,
                false,
                () => ApplyLifestealHit(target));
        }

        private void ApplyLifestealHit(EnemyBase target)
        {
            if (target == null || target.IsDead || !isDeployed) return;

            int hpBeforeHit = target.CurrentHP;
            int damage = DamageCalculator.CalculateDamage(CurrentATK, target.CurrentDEF);
            if (!target.TryTakeAttackDamage(damage, Data.damageType, this)) return;

            if (currentRarity >= OperatorRarity.Star3)
                dodgeTimer = DodgeDuration;

            int actualDamage = Mathf.Min(damage, hpBeforeHit);
            int healing = Mathf.FloorToInt(actualDamage * LifestealRatio);
            HealSelf(healing);
        }
    }
}
