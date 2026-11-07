using UnityEngine;
using TrashTD.Operators;

namespace TrashTD.Combat
{
    /// <summary>
    /// Slippery Effect applied to an operator by an Oculoid Carrier.
    /// When an Oculoid Carrier is blocked, it inflicts a block count reduction (default -1) via this effect.
    /// While the carrier is actively blocked, the effect is held (isFading = false) and does not count down.
    /// When the carrier is no longer blocked (killed or unblocked), the 10-second fading timer begins.
    /// After 10 seconds, the effect expires and the operator's block count is restored.
    /// </summary>
    public class SlipperyEffect : MonoBehaviour
    {
        [SerializeField] private OperatorBase targetOperator;
        [SerializeField] private int blockReduction = 1;
        [SerializeField] private float duration = 10f;
        [SerializeField] private float durationRemaining = 10f;
        [SerializeField] private bool isFading = false;
        private bool isExpired = false;
        private PersistentStatusText statusText;

        public OperatorBase TargetOperator => targetOperator;
        public int BlockReduction => blockReduction;
        public float Duration => duration;
        public float DurationRemaining => durationRemaining;
        public bool IsFading => isFading;
        public bool IsExpired => isExpired;
        public PersistentStatusText StatusText => statusText;

        public static SlipperyEffect Apply(OperatorBase target, int reduction = 1, float fadeDuration = 10f)
        {
            if (target == null) return null;

            var effect = target.gameObject.AddComponent<SlipperyEffect>();
            effect.targetOperator = target;
            effect.blockReduction = reduction;
            effect.duration = fadeDuration;
            effect.durationRemaining = fadeDuration;
            effect.isFading = false;
            effect.isExpired = false;

            target.ModifyBlockCount(-reduction);
            effect.statusText = PersistentStatusText.Show(
                target.gameObject,
                $"-{reduction} BLOCK",
                Color.white,
                0.7f,
                "SlipperyBlock",
                new Color(0.4f, 0.85f, 1f));

            return effect;
        }

        public void StartFading(float customDuration = -1f)
        {
            if (isExpired) return;

            if (customDuration > 0f)
            {
                duration = customDuration;
                durationRemaining = customDuration;
            }

            if (!isFading)
            {
                isFading = true;
            }
        }

        private void Update()
        {
            if (isExpired) return;

            if (targetOperator == null || !targetOperator.IsDeployed)
            {
                ClearWithoutRestoring();
                return;
            }

            if (isFading)
            {
                Tick(Time.deltaTime);
            }
        }

        public void Tick(float dt)
        {
            if (isExpired || !isFading) return;

            durationRemaining -= dt;
            if (durationRemaining <= 0f)
            {
                Expire();
            }
        }

        public void Expire()
        {
            if (isExpired) return;
            isExpired = true;

            if (targetOperator != null)
            {
                targetOperator.ModifyBlockCount(blockReduction);
            }

            SafeDestroy();
        }

        public void ClearWithoutRestoring()
        {
            if (isExpired) return;
            isExpired = true;
            SafeDestroy();
        }

        private void SafeDestroy()
        {
            ClearStatusText();
            if (Application.isPlaying)
            {
                Destroy(this);
            }
            else
            {
                DestroyImmediate(this);
            }
        }

        private void OnDestroy()
        {
            ClearStatusText();
        }

        private void ClearStatusText()
        {
            if (statusText != null)
            {
                statusText.Dismiss();
                statusText = null;
            }
            if (targetOperator != null)
            {
                PersistentStatusText.Hide(targetOperator.gameObject, "SlipperyBlock");
            }
        }
    }
}

