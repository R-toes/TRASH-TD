using System;
using System.Collections;
using UnityEngine;

namespace TrashTD.Operators
{
    [RequireComponent(typeof(PompothSpriteAnimation))]
    public sealed class PompothPowerVisuals : MonoBehaviour
    {
        private const float TierEpsilon = 0.001f;

        [SerializeField] private Sprite[] tier0Frames;
        [SerializeField] private Sprite[] tier1Frames;
        [SerializeField] private Sprite[] tier2Frames;
        [SerializeField, Min(0f)] private float downgradeDelay = 0.35f;

        public event Action<int> OnTierChanged;
        public int CurrentTier { get; private set; }

        private PompothSpriteAnimation animation;
        private Coroutine pendingDowngrade;
        private int pendingTier;

        private void OnEnable()
        {
            CancelPendingDowngrade();
            CurrentTier = 0;
            GetAnimation().SetFramesPreservingPhase(tier0Frames);
        }

        private void OnDisable()
        {
            ResetToTierZero();
        }

        public void SetPassiveState(int enemyCount, float bonus, float cap)
        {
            if (enemyCount <= 0)
            {
                ResetToTierZero();
                return;
            }

            int requestedTier = GetPowerTier(bonus, cap);
            if (requestedTier >= CurrentTier)
            {
                CancelPendingDowngrade();
                ApplyTier(requestedTier);
                return;
            }

            if (pendingDowngrade != null && pendingTier == requestedTier) return;
            CancelPendingDowngrade();
            pendingTier = requestedTier;
            if (downgradeDelay <= 0f)
            {
                ApplyTier(requestedTier);
                return;
            }

            pendingDowngrade = StartCoroutine(ApplyDowngradeAfterDelay(requestedTier));
        }

        public void ResetToTierZero()
        {
            CancelPendingDowngrade();
            ApplyTier(0);
        }

        public static int GetPowerTier(float bonus, float cap)
        {
            if (cap <= 0f || float.IsNaN(bonus) || float.IsNaN(cap)) return 0;

            float ratio = Mathf.Clamp01(bonus / cap);
            if (ratio + TierEpsilon >= 1f) return 2;
            if (ratio + TierEpsilon >= 0.5f) return 1;
            return 0;
        }

        private IEnumerator ApplyDowngradeAfterDelay(int requestedTier)
        {
            yield return new WaitForSeconds(downgradeDelay);
            pendingDowngrade = null;
            ApplyTier(requestedTier);
        }

        private void ApplyTier(int tier)
        {
            if (tier == CurrentTier) return;

            Sprite[] frames = tier switch
            {
                1 => tier1Frames,
                2 => tier2Frames,
                _ => tier0Frames
            };
            if (!GetAnimation().SetFramesPreservingPhase(frames)) return;

            CurrentTier = tier;
            OnTierChanged?.Invoke(tier);
        }

        private PompothSpriteAnimation GetAnimation()
        {
            if (animation == null) animation = GetComponent<PompothSpriteAnimation>();
            return animation;
        }

        private void CancelPendingDowngrade()
        {
            if (pendingDowngrade == null) return;
            StopCoroutine(pendingDowngrade);
            pendingDowngrade = null;
        }
    }
}
