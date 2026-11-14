using System.Collections.Generic;
using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Operators
{
    public enum FramiliarTransitionFrame
    {
        NormalFramiliar = 0,
        WhiteFramiliar1 = 1,
        WhiteCopied1 = 2,
        WhiteFramiliar2 = 3,
        WhiteCopied2 = 4,
        WhiteFramiliar3 = 5,
        WhiteCopied3 = 6,
        WhiteFramiliar4 = 7,
        WhiteCopied4 = 8,
        MonochromeCopied = 9
    }

    /// <summary>
    /// Glitch / morph visual transition when Framiliar transforms into another operator.
    /// Sequence:
    /// 1. Normal Framiliar
    /// 2. Full white silhouette of Framiliar
    /// 3. Full white silhouette of copied operator
    /// 4. Full white silhouette of Framiliar
    /// 5. Full white silhouette of copied operator
    /// 6. Full white silhouette of Framiliar
    /// 7. Full white silhouette of copied operator
    /// 8. Full white silhouette of Framiliar
    /// 9. Full white silhouette of copied operator
    /// 10. Monochrome of the copied operator
    /// </summary>
    public class FramiliarTransformTransition : MonoBehaviour
    {
        public const float DefaultStepDuration = 0.055f;

        public static readonly FramiliarTransitionFrame[] Sequence = new[]
        {
            FramiliarTransitionFrame.NormalFramiliar,
            FramiliarTransitionFrame.WhiteFramiliar1,
            FramiliarTransitionFrame.WhiteCopied1,
            FramiliarTransitionFrame.WhiteFramiliar2,
            FramiliarTransitionFrame.WhiteCopied2,
            FramiliarTransitionFrame.WhiteFramiliar3,
            FramiliarTransitionFrame.WhiteCopied3,
            FramiliarTransitionFrame.WhiteFramiliar4,
            FramiliarTransitionFrame.WhiteCopied4,
            FramiliarTransitionFrame.MonochromeCopied
        };

        private Sprite framiliarSprite;
        private Sprite copiedSprite;
        private SpriteRenderer primaryRenderer;
        private SpriteRenderer[] otherRenderers;
        private bool[] otherRenderersInitialState;
        private MonoBehaviour[] pausedAnimators;

        private Material defaultMaterial;
        private Material whiteSilhouetteMaterial;
        private Material grayscaleMaterial;

        [Header("Audio")]
        [Tooltip("Custom sound played during transformation. If null, checks caller parameter, FramiliarOperator slot, conventional SFX/Operators/Specialist/Framiliar/Ability_Transform, or card upgrade fallback.")]
        [SerializeField] private AudioClip transformSound;
        [SerializeField] private AudioClip[] transformSounds;

        public AudioClip TransformSound
        {
            get => transformSound;
            set => transformSound = value;
        }

        public AudioClip[] TransformSounds
        {
            get => transformSounds;
            set => transformSounds = value;
        }

        private float stepDuration = DefaultStepDuration;
        private float elapsed = 0f;
        private int currentStep = 0;
        private bool isFinished = false;

        public int CurrentStep => currentStep;
        public bool IsFinished => isFinished;
        public float StepDuration { get => stepDuration; set => stepDuration = Mathf.Max(0.01f, value); }
        public Sprite FramiliarSprite => framiliarSprite;
        public Sprite CopiedSprite => copiedSprite;
        public SpriteRenderer PrimaryRenderer => primaryRenderer;

        public static FramiliarTransformTransition Play(
            OperatorBase targetOperator,
            Sprite framiliarSprite,
            Sprite copiedSprite,
            float stepDuration = DefaultStepDuration,
            AudioClip customSound = null,
            AudioClip[] customSounds = null,
            OperatorData sourceData = null)
        {
            if (targetOperator == null) return null;
            return Play(targetOperator.gameObject, framiliarSprite, copiedSprite, stepDuration, customSound, customSounds, sourceData, targetOperator);
        }

        public static FramiliarTransformTransition Play(
            GameObject targetObject,
            Sprite framiliarSprite,
            Sprite copiedSprite,
            float stepDuration = DefaultStepDuration,
            AudioClip customSound = null,
            AudioClip[] customSounds = null,
            OperatorData sourceData = null,
            OperatorBase targetOperator = null)
        {
            if (targetObject == null) return null;

            var existing = targetObject.GetComponent<FramiliarTransformTransition>();
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing);
                else DestroyImmediate(existing);
            }

            var transition = targetObject.AddComponent<FramiliarTransformTransition>();
            transition.Initialize(framiliarSprite, copiedSprite, stepDuration, customSound, customSounds, sourceData, targetOperator);
            return transition;
        }

        public void Initialize(
            Sprite fSprite,
            Sprite cSprite,
            float duration = DefaultStepDuration,
            AudioClip customSound = null,
            AudioClip[] customSounds = null,
            OperatorData sourceData = null,
            OperatorBase targetOperator = null)
        {
            framiliarSprite = fSprite;
            copiedSprite = cSprite;
            stepDuration = Mathf.Max(0.01f, duration);
            elapsed = 0f;
            currentStep = 0;
            isFinished = false;

            // Find primary SpriteRenderer
            primaryRenderer = GetComponent<SpriteRenderer>();
            if (primaryRenderer == null)
            {
                primaryRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            if (primaryRenderer != null)
            {
                defaultMaterial = primaryRenderer.sharedMaterial;
                if (defaultMaterial == null)
                {
                    defaultMaterial = FramiliarOperator.GetDefaultSpriteMaterial();
                }
            }

            whiteSilhouetteMaterial = FramiliarOperator.GetWhiteSilhouetteMaterial();
            grayscaleMaterial = FramiliarOperator.GetGrayscaleMaterial();

            // Store other child renderers and hide them during glitching silhouettes
            var allRenderers = GetComponentsInChildren<SpriteRenderer>(true);
            var otherList = new List<SpriteRenderer>();
            var otherState = new List<bool>();
            for (int i = 0; i < allRenderers.Length; i++)
            {
                var r = allRenderers[i];
                if (r != null && r != primaryRenderer)
                {
                    otherList.Add(r);
                    otherState.Add(r.enabled);
                    r.enabled = false;
                }
            }
            otherRenderers = otherList.ToArray();
            otherRenderersInitialState = otherState.ToArray();

            // Pause animators so they don't overwrite sprite frames during transition
            var animList = new List<MonoBehaviour>();
            foreach (var m in GetComponentsInChildren<MonoBehaviour>())
            {
                if (m == null || m == this) continue;
                string typeName = m.GetType().Name;
                if (typeName.Contains("SpriteAnimation") && m.enabled)
                {
                    m.enabled = false;
                    animList.Add(m);
                }
            }
            pausedAnimators = animList.ToArray();

            // Play transition sound (custom slot -> UnitSfx convention -> fallback)
            PlayTransformSound(customSound, customSounds, sourceData, targetOperator ?? GetComponent<OperatorBase>());

            // Apply starting frame (NormalFramiliar)
            ApplyStep(0);
        }

        public void PlayTransformSound(
            AudioClip customSound = null,
            AudioClip[] customSounds = null,
            OperatorData sourceData = null,
            OperatorBase targetOperator = null)
        {
            if (customSounds != null && customSounds.Length > 0)
            {
                AudioClip clip = customSounds[Random.Range(0, customSounds.Length)];
                if (clip != null)
                {
                    TrashTD.Audio.AudioManager.Instance?.PlaySfx(clip);
                    return;
                }
            }

            if (customSound != null)
            {
                TrashTD.Audio.AudioManager.Instance?.PlaySfx(customSound);
                return;
            }

            if (transformSounds != null && transformSounds.Length > 0)
            {
                AudioClip clip = transformSounds[Random.Range(0, transformSounds.Length)];
                if (clip != null)
                {
                    TrashTD.Audio.AudioManager.Instance?.PlaySfx(clip);
                    return;
                }
            }

            if (transformSound != null)
            {
                TrashTD.Audio.AudioManager.Instance?.PlaySfx(transformSound);
                return;
            }

            if (sourceData != null && (TrashTD.Audio.UnitSfx.PlayAbility(sourceData, "Transform") ||
                                       TrashTD.Audio.UnitSfx.Play(sourceData, TrashTD.Audio.UnitSfxEvent.Ability)))
            {
                return;
            }

            if (targetOperator != null && targetOperator.OriginalFramiliarData != null &&
                (TrashTD.Audio.UnitSfx.PlayAbility(targetOperator.OriginalFramiliarData, "Transform") ||
                 TrashTD.Audio.UnitSfx.Play(targetOperator.OriginalFramiliarData, TrashTD.Audio.UnitSfxEvent.Ability)))
            {
                return;
            }

            if (targetOperator != null && targetOperator.Data != null &&
                (TrashTD.Audio.UnitSfx.PlayAbility(targetOperator.Data, "Transform") ||
                 TrashTD.Audio.UnitSfx.Play(targetOperator.Data, TrashTD.Audio.UnitSfxEvent.Ability)))
            {
                return;
            }

            // Check conventional folder SFX/Operators/Specialist/Framiliar directly
            if (TrashTD.Audio.UnitSfx.PlayFromFolder("SFX/Operators/Specialist/Framiliar", "Ability_Transform") ||
                TrashTD.Audio.UnitSfx.PlayFromFolder("SFX/Operators/Specialist/Framiliar", "Transform") ||
                TrashTD.Audio.UnitSfx.PlayFromFolder("SFX/Operators/Specialist/Framiliar", "Ability"))
            {
                return;
            }

            TrashTD.Audio.AudioManager.Instance?.PlaySfx(TrashTD.Audio.SfxId.GameplayCardUpgrade);
        }

        public void ApplyStep(int stepIndex)
        {
            if (stepIndex < 0) stepIndex = 0;
            if (stepIndex >= Sequence.Length) stepIndex = Sequence.Length - 1;

            currentStep = stepIndex;
            FramiliarTransitionFrame frame = Sequence[stepIndex];
            ApplyFrame(frame);
        }

        public void ApplyFrame(FramiliarTransitionFrame frame)
        {
            if (primaryRenderer == null) return;

            switch (frame)
            {
                case FramiliarTransitionFrame.NormalFramiliar:
                    if (framiliarSprite != null) primaryRenderer.sprite = framiliarSprite;
                    if (defaultMaterial != null) primaryRenderer.sharedMaterial = defaultMaterial;
                    primaryRenderer.color = Color.white;
                    break;

                case FramiliarTransitionFrame.WhiteFramiliar1:
                case FramiliarTransitionFrame.WhiteFramiliar2:
                case FramiliarTransitionFrame.WhiteFramiliar3:
                case FramiliarTransitionFrame.WhiteFramiliar4:
                    if (framiliarSprite != null) primaryRenderer.sprite = framiliarSprite;
                    if (whiteSilhouetteMaterial != null) primaryRenderer.sharedMaterial = whiteSilhouetteMaterial;
                    primaryRenderer.color = Color.white;
                    break;

                case FramiliarTransitionFrame.WhiteCopied1:
                case FramiliarTransitionFrame.WhiteCopied2:
                case FramiliarTransitionFrame.WhiteCopied3:
                case FramiliarTransitionFrame.WhiteCopied4:
                    if (copiedSprite != null) primaryRenderer.sprite = copiedSprite;
                    if (whiteSilhouetteMaterial != null) primaryRenderer.sharedMaterial = whiteSilhouetteMaterial;
                    primaryRenderer.color = Color.white;
                    break;

                case FramiliarTransitionFrame.MonochromeCopied:
                    if (copiedSprite != null) primaryRenderer.sprite = copiedSprite;
                    if (grayscaleMaterial != null) primaryRenderer.sharedMaterial = grayscaleMaterial;
                    primaryRenderer.color = Color.white;
                    break;
            }
        }

        public void SimulateTime(float deltaTime)
        {
            Advance(deltaTime);
        }

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        private void Advance(float deltaTime)
        {
            if (isFinished) return;

            elapsed += deltaTime;
            int step = Mathf.FloorToInt(elapsed / stepDuration);

            if (step >= Sequence.Length - 1)
            {
                Finish();
                return;
            }

            if (step != currentStep)
            {
                ApplyStep(step);
            }
        }

        public void Finish()
        {
            if (isFinished) return;
            isFinished = true;
            currentStep = Sequence.Length - 1;

            // Apply final monochrome state
            ApplyFrame(FramiliarTransitionFrame.MonochromeCopied);

            // Restore other child renderers and apply grayscale to all
            if (otherRenderers != null)
            {
                for (int i = 0; i < otherRenderers.Length; i++)
                {
                    if (otherRenderers[i] != null)
                    {
                        otherRenderers[i].enabled = otherRenderersInitialState != null && i < otherRenderersInitialState.Length
                            ? otherRenderersInitialState[i]
                            : true;
                    }
                }
            }

            FramiliarOperator.ApplyGrayscale(gameObject);

            // Re-enable paused animators
            if (pausedAnimators != null)
            {
                for (int i = 0; i < pausedAnimators.Length; i++)
                {
                    if (pausedAnimators[i] != null)
                    {
                        pausedAnimators[i].enabled = true;
                    }
                }
            }

            // Sync specialized animators / visuals for copied operators
            if (TryGetComponent<ScrapperOperator>(out var scrapperOp) && scrapperOp != null)
            {
                scrapperOp.SpriteAnimation?.CancelAttack();
            }
            if (TryGetComponent<ResicochetOperator>(out var resiOp) && resiOp != null)
            {
                resiOp.UpdateChargeVisuals();
                resiOp.SetCrystalAuraEnabled(true);
                resiOp.UpdateCrystalAuras();
            }
            if (TryGetComponent<PompothOperator>(out var pompothOp) && pompothOp != null)
            {
                pompothOp.UpdateIndicatorVisuals();
            }
            if (TryGetComponent<OperatorBase>(out var opBase) && opBase != null)
            {
                opBase.ApplyFacingVisuals();
            }

            if (Application.isPlaying) Destroy(this);
            else DestroyImmediate(this);
        }

        private void OnDestroy()
        {
            // Do not destroy shared materials (whiteSilhouetteMaterial, grayscaleMaterial)
            // as they are cached and reused across transitions and assigned to active renderers.
            whiteSilhouetteMaterial = null;
            grayscaleMaterial = null;
        }
    }
}
