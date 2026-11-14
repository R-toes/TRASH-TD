using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Framiliar — 2-Star Specialist (Mimic / Copy Operator).
    /// Ability:
    /// - Copies any operator that the player has in the deck.
    /// - After deployment, clicking Framiliar displays a menu mirroring the 8-slot deck.
    /// - When an operator is chosen from the menu, Framiliar copies it and renders with a grayscaled sprite.
    /// - If retreated during the same preparation phase, returns back to Framiliar.
    /// - If retreated in the next preparation phase (or after wave combat), permanently returns as the copied operator.
    /// </summary>
    public class FramiliarOperator : SpecialistOperator
    {
        [Header("Framiliar Animation Frames")]
        [SerializeField] private Sprite[] framiliarFrames;

        [Header("Audio")]
        [Tooltip("Custom sound played when Framiliar transforms into another operator. If null, conventional resource SFX/Operators/Specialist/Framiliar/Ability_Transform is used with card upgrade fallback.")]
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

        private bool isCopied = false;
        private DraftCard copiedCard = null;
        private OperatorSpriteAnimation spriteAnim;

        private static Material s_GrayscaleMaterial;
        private static Material s_WhiteSilhouetteMaterial;
        private static Material s_DefaultSpriteMaterial;

        public bool IsCopied => isCopied;
        public DraftCard CopiedCard => copiedCard;
        public Sprite[] FramiliarFrames => framiliarFrames;

        public bool HasFirstUpgrade => CurrentRarity >= OperatorRarity.Star3 || OriginalFramiliarRarity >= OperatorRarity.Star3;
        public bool HasSecondUpgrade => CurrentRarity >= OperatorRarity.Star4 || OriginalFramiliarRarity >= OperatorRarity.Star4;

        public int UpgradeLevel
        {
            get
            {
                OperatorRarity r = OriginalFramiliarRarity != 0 ? OriginalFramiliarRarity : CurrentRarity;
                int baseR = OriginalFramiliarData != null ? (int)OriginalFramiliarData.baseRarity : (int)OperatorRarity.Star2;
                return Mathf.Clamp((int)r - baseR, 0, 2);
            }
        }

        public static int CalculateEffectiveUpgradeLevel(int framiliarUpgradeLevel, int targetCardUpgradeLevel)
        {
            return Mathf.Max(Mathf.Clamp(framiliarUpgradeLevel, 0, 2), Mathf.Clamp(targetCardUpgradeLevel, 0, 2));
        }

        public static OperatorRarity CalculateEffectiveRarity(OperatorData targetData, int framiliarUpgradeLevel, int targetCardUpgradeLevel)
        {
            if (targetData == null) return OperatorRarity.Star1;
            int effectiveUpgrade = CalculateEffectiveUpgradeLevel(framiliarUpgradeLevel, targetCardUpgradeLevel);
            int maxRarity = Mathf.Min((int)OperatorRarity.Star5, (int)targetData.baseRarity + 2);
            return (OperatorRarity)Mathf.Clamp((int)targetData.baseRarity + effectiveUpgrade, (int)targetData.baseRarity, maxRarity);
        }

        public override string GetSkillDescription() =>
            GetSkillDescription(OriginalFramiliarData != null ? OriginalFramiliarData : data,
                OriginalFramiliarRarity != 0 ? OriginalFramiliarRarity : currentRarity);

        public void ConfigureAnimationFrames(Sprite[] frames)
        {
            framiliarFrames = frames;
            if (spriteAnim == null)
            {
                spriteAnim = GetComponent<OperatorSpriteAnimation>();
            }
            if (spriteAnim != null && frames != null && frames.Length > 0)
            {
                spriteAnim.Configure(frames);
            }
        }

        public override void Initialize(OperatorData operatorData, OperatorRarity rarity, OperatorFacing operatorFacing = OperatorFacing.Right)
        {
            base.Initialize(operatorData, rarity, operatorFacing);
            OriginalFramiliarData = operatorData;
            OriginalFramiliarRarity = rarity;
            isCopied = false;
            copiedCard = null;
            EnsureFramesLoaded();
        }

        protected override void OnDeployed()
        {
            base.OnDeployed();
            EnsureFramesLoaded();
        }

        private void Awake()
        {
            EnsureFramesLoaded();
        }

        public void EnsureFramesLoaded()
        {
            if (spriteAnim == null)
            {
                spriteAnim = GetComponent<OperatorSpriteAnimation>();
            }

            if (framiliarFrames != null && framiliarFrames.Length > 0)
            {
                if (spriteAnim != null)
                {
                    spriteAnim.Configure(framiliarFrames);
                }
                return;
            }

#if UNITY_EDITOR
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Operators/Framiliar-Sheet.png");
            if (assets != null && assets.Length > 0)
            {
                var frames = new List<Sprite>();
                foreach (var a in assets)
                {
                    if (a is Sprite s) frames.Add(s);
                }
                if (frames.Count > 0)
                {
                    framiliarFrames = frames.ToArray();
                    if (spriteAnim != null)
                    {
                        spriteAnim.Configure(framiliarFrames);
                    }
                    var sr = GetComponent<SpriteRenderer>();
                    if (sr != null && sr.sprite == null && framiliarFrames.Length > 0)
                    {
                        sr.sprite = framiliarFrames[0];
                    }
                    return;
                }
            }
#endif

            var renderer = GetComponent<SpriteRenderer>();
            Texture2D tex = renderer != null && renderer.sprite != null ? renderer.sprite.texture : null;
#if UNITY_EDITOR
            if (tex == null)
            {
                tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/Operators/Framiliar-Sheet.png");
            }
#endif
            if (tex != null && tex.width >= 32 && tex.height >= 32)
            {
                SliceFramesFromTexture(tex);
            }
        }

        private void SliceFramesFromTexture(Texture2D tex)
        {
            int cols = Mathf.Max(1, tex.width / 32);
            framiliarFrames = new Sprite[cols];
            for (int i = 0; i < cols; i++)
            {
                framiliarFrames[i] = Sprite.Create(
                    tex,
                    new Rect(i * 32, 0, 32, 32),
                    new Vector2(0.5f, 0.5f),
                    32f);
                framiliarFrames[i].name = $"Framiliar-Sheet_{i}";
            }

            if (spriteAnim != null && framiliarFrames.Length > 0)
            {
                spriteAnim.Configure(framiliarFrames);
            }
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null && framiliarFrames.Length > 0)
            {
                sr.sprite = framiliarFrames[0];
            }
        }

        public static Material GetGrayscaleMaterial()
        {
            if (s_GrayscaleMaterial == null)
            {
                Shader shader = Shader.Find("TrashTD/GrayscaleSprite");
                if (shader == null)
                {
                    shader = Resources.Load<Shader>("Shaders/GrayscaleSprite");
                }
                if (shader == null)
                {
                    shader = Shader.Find("Sprites/Default");
                }

                if (shader != null)
                {
                    s_GrayscaleMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                }
            }
            return s_GrayscaleMaterial;
        }

        public static Material GetWhiteSilhouetteMaterial()
        {
            if (s_WhiteSilhouetteMaterial == null)
            {
                Shader shader = Shader.Find("TrashTD/SpriteFlash");
                if (shader == null)
                {
                    shader = Resources.Load<Shader>("Shaders/SpriteFlash");
                }
                if (shader == null)
                {
                    shader = Shader.Find("Sprites/Default");
                }

                if (shader != null)
                {
                    s_WhiteSilhouetteMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                }
            }
            return s_WhiteSilhouetteMaterial;
        }

        public static Material GetDefaultSpriteMaterial()
        {
            if (s_DefaultSpriteMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    s_DefaultSpriteMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                }
            }
            return s_DefaultSpriteMaterial;
        }

        public Sprite GetCurrentSprite()
        {
            var sr = GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null) return sr.sprite;
            if (framiliarFrames != null && framiliarFrames.Length > 0 && framiliarFrames[0] != null) return framiliarFrames[0];
            if (data != null && data.portrait != null) return data.portrait;
            return null;
        }

        public static Sprite GetOperatorSprite(OperatorBase op, OperatorData opData)
        {
            if (op != null)
            {
                if (op is ResicochetOperator resi && resi.Charge1Frames != null && resi.Charge1Frames.Length > 0 && resi.Charge1Frames[0] != null)
                {
                    return resi.Charge1Frames[0];
                }

                var sr = op.GetComponent<SpriteRenderer>();
                if (sr != null && sr.sprite != null) return sr.sprite;
                var framesInterface = op.GetComponent<IOperatorSpriteFrames>();
                if (framesInterface != null && framesInterface.Frames != null && framesInterface.Frames.Length > 0 && framesInterface.Frames[0] != null) return framesInterface.Frames[0];
                var anim = op.GetComponent<OperatorSpriteAnimation>();
                if (anim != null && anim.Frames != null && anim.Frames.Length > 0 && anim.Frames[0] != null) return anim.Frames[0];
                var childSr = op.GetComponentInChildren<SpriteRenderer>();
                if (childSr != null && childSr.sprite != null) return childSr.sprite;
            }
            if (opData != null)
            {
                if (opData.operatorPrefab != null)
                {
                    if (opData.operatorPrefab.TryGetComponent<ResicochetOperator>(out var resiPrefab) &&
                        resiPrefab.Charge1Frames != null && resiPrefab.Charge1Frames.Length > 0 && resiPrefab.Charge1Frames[0] != null)
                    {
                        return resiPrefab.Charge1Frames[0];
                    }

                    var sr = opData.operatorPrefab.GetComponent<SpriteRenderer>();
                    if (sr != null && sr.sprite != null) return sr.sprite;
                    var framesInterface = opData.operatorPrefab.GetComponent<IOperatorSpriteFrames>();
                    if (framesInterface != null && framesInterface.Frames != null && framesInterface.Frames.Length > 0 && framesInterface.Frames[0] != null) return framesInterface.Frames[0];
                    var anim = opData.operatorPrefab.GetComponent<OperatorSpriteAnimation>();
                    if (anim != null && anim.Frames != null && anim.Frames.Length > 0 && anim.Frames[0] != null) return anim.Frames[0];
                    var childSr = opData.operatorPrefab.GetComponentInChildren<SpriteRenderer>();
                    if (childSr != null && childSr.sprite != null) return childSr.sprite;
                }
                return opData.portrait;
            }
            return null;
        }

        public static void ApplyGrayscale(GameObject go)
        {
            if (go == null) return;
            var mat = GetGrayscaleMaterial();
            var renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r != null)
                {
                    string rName = r.gameObject.name;
                    // Preserve external bubble shields, stun stars, procedural indicator/glow renderers, and Resicochet crystal auras
                    if (rName.StartsWith("BubbleShield") || rName.StartsWith("PompothAttackOrigin") || rName.StartsWith("StunStars") ||
                        rName.StartsWith("CrystalAura") || rName.StartsWith("CrystalOutline"))
                    {
                        continue;
                    }

                    if (mat != null)
                    {
                        r.sharedMaterial = mat;
                    }
                    else
                    {
                        r.color = new Color(0.7f, 0.7f, 0.7f, 1f);
                    }
                }
            }
        }

        /// <summary>
        /// Transforms Framiliar into a copy of the specified deck card.
        /// Replaces Framiliar on the field with a grayscaled instance of the copied operator.
        /// </summary>
        public OperatorBase CopyOperator(DraftCard targetCard)
        {
            if (targetCard == null || targetCard.operatorData == null) return null;
            copiedCard = targetCard;
            return CopyOperator(targetCard.operatorData, targetCard.rarity);
        }

        /// <summary>
        /// Transforms Framiliar into a copy of the target OperatorData and rarity.
        /// Always checks which upgrade level is higher: Framiliar or the target operator, picking the highest.
        /// Ground compatibility is strictly enforced (no high ground op on low ground, and vice versa).
        /// </summary>
        public OperatorBase CopyOperator(OperatorData targetData, OperatorRarity rarity)
        {
            if (targetData == null) return null;

            // Enforce ground/height compatibility
            GridCell cell = DeployedCell;
            if (cell == null && OperatorManager.Instance != null && OperatorManager.Instance.GridManager != null)
            {
                cell = OperatorManager.Instance.GridManager.GetCell(OperatorManager.Instance.GridManager.WorldToGridPosition(transform.position));
            }
            if (cell != null && !cell.SupportsPosition(targetData.position))
            {
                return null;
            }

            // Always check which upgrade is higher: Framiliar or the target operator. Pick the highest always.
            int framiliarUpgradeLevels = UpgradeLevel;
            int targetCardUpgradeLevels = Mathf.Clamp((int)rarity - (int)targetData.baseRarity, 0, 2);
            int effectiveUpgradeLevel = CalculateEffectiveUpgradeLevel(framiliarUpgradeLevels, targetCardUpgradeLevels);
            OperatorRarity effectiveRarity = CalculateEffectiveRarity(targetData, framiliarUpgradeLevels, targetCardUpgradeLevels);

            if (OperatorManager.Instance != null)
            {
                return OperatorManager.Instance.TransformFramiliar(this, targetData, effectiveRarity);
            }

            // Standalone fallback for unit tests without OperatorManager
            Sprite framiliarSprite = GetCurrentSprite();
            Sprite copiedSprite = GetOperatorSprite(this, targetData);

            int harmonyStacks = HarmonyStacks;
            SetHarmonyStacks(0);
            isCopied = true;
            data = targetData;
            currentRarity = effectiveRarity;
            maxHP = targetData.GetScaledHP(effectiveRarity);
            currentHP = maxHP;
            currentATK = targetData.GetScaledATK(effectiveRarity);
            currentDEF = targetData.GetScaledDEF(effectiveRarity);
            currentRES = targetData.GetScaledRES(effectiveRarity);
            SetHarmonyStacks(harmonyStacks); // Re-applied for the copied class

            IsCopiedFromFramiliar = true;
            FramiliarTransformTransition.Play(
                this,
                framiliarSprite,
                copiedSprite,
                FramiliarTransformTransition.DefaultStepDuration,
                transformSound,
                transformSounds,
                OriginalFramiliarData != null ? OriginalFramiliarData : data);
            return this;
        }
    }
}
