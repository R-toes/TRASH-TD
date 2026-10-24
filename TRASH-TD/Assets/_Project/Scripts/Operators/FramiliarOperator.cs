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

        private bool isCopied = false;
        private DraftCard copiedCard = null;
        private OperatorSpriteAnimation spriteAnim;

        private static Material s_GrayscaleMaterial;

        public bool IsCopied => isCopied;
        public DraftCard CopiedCard => copiedCard;
        public Sprite[] FramiliarFrames => framiliarFrames;

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

        public static void ApplyGrayscale(GameObject go)
        {
            if (go == null) return;
            var mat = GetGrayscaleMaterial();
            var renderers = go.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    if (mat != null)
                    {
                        renderers[i].material = mat;
                    }
                    else
                    {
                        renderers[i].color = new Color(0.7f, 0.7f, 0.7f, 1f);
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
            return CopyOperator(targetCard.operatorData, targetCard.rarity);
        }

        /// <summary>
        /// Transforms Framiliar into a copy of the target OperatorData and rarity.
        /// If Framiliar is upgraded, the copied operator inherits that upgrade level.
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

            // Inherit Framiliar's upgrade levels (e.g. 3 copies merged -> +1 upgrade tier)
            int framiliarUpgradeLevels = Mathf.Max(0, (int)CurrentRarity - (int)(OriginalFramiliarData != null ? OriginalFramiliarData.baseRarity : (data != null ? data.baseRarity : OperatorRarity.Star2)));
            int targetCardUpgradeLevels = Mathf.Max(0, (int)rarity - (int)targetData.baseRarity);
            int effectiveUpgradeLevel = Mathf.Max(framiliarUpgradeLevels, targetCardUpgradeLevels);
            OperatorRarity effectiveRarity = (OperatorRarity)Mathf.Clamp((int)targetData.baseRarity + effectiveUpgradeLevel, (int)targetData.baseRarity, (int)OperatorRarity.Star5);

            if (OperatorManager.Instance != null)
            {
                return OperatorManager.Instance.TransformFramiliar(this, targetData, effectiveRarity);
            }

            // Standalone fallback for unit tests without OperatorManager
            isCopied = true;
            data = targetData;
            currentRarity = effectiveRarity;
            maxHP = targetData.GetScaledHP(effectiveRarity);
            currentHP = maxHP;
            currentATK = targetData.GetScaledATK(effectiveRarity);
            currentDEF = targetData.GetScaledDEF(effectiveRarity);
            currentRES = targetData.GetScaledRES(effectiveRarity);

            IsCopiedFromFramiliar = true;
            ApplyGrayscale(gameObject);
            return this;
        }
    }
}
