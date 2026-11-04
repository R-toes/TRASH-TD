using System.Collections.Generic;
using UnityEngine;
using TrashTD.Audio;
using TrashTD.Combat;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Panacea — 3-Star Medic with global range.
    /// Every attack interval (5 s) it heals the operator with the lowest HP% anywhere on the field (itself
    /// included); ties go to the lower absolute HP.
    /// Throws a spinning 16x16 potion vial with an 8-frame liquid swirl animation and a green meteor trail.
    /// The throw curves in a parabolic arc to the target unless throwing directly up or down.
    /// On arrival, the vial shatters in green droplets and sparkles, restoring ATK as health.
    /// </summary>
    public class PanaceaOperator : MedicOperator
    {
        [Header("Panacea 16x16 Vial Frames (8 frames)")]
        [SerializeField] private Sprite[] vialFrames;

        private static Sprite[] cachedVialFrames;

        public Sprite[] VialFrames => vialFrames;

        public void ConfigureVialFrames(Sprite[] frames)
        {
            vialFrames = frames;
        }

        private void Awake()
        {
            EnsureVialFramesLoaded();
        }

        private void EnsureVialFramesLoaded()
        {
            if (vialFrames != null && vialFrames.Length >= 8) return;

            if (cachedVialFrames != null && cachedVialFrames.Length >= 8)
            {
                vialFrames = cachedVialFrames;
                return;
            }

#if UNITY_EDITOR
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Operators/Panacea-Vials.png");
            if (assets != null && assets.Length > 0)
            {
                var list = new List<Sprite>();
                foreach (var a in assets)
                {
                    if (a is Sprite s) list.Add(s);
                }
                list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                if (list.Count >= 8)
                {
                    cachedVialFrames = list.ToArray();
                    vialFrames = cachedVialFrames;
                    return;
                }
            }
#endif

            // Fallback: load raw bytes from PNG
            string path = System.IO.Path.Combine(Application.dataPath, "_Project/Art/Operators/Panacea-Vials.png");
            if (System.IO.File.Exists(path))
            {
                try
                {
                    byte[] bytes = System.IO.File.ReadAllBytes(path);
                    Texture2D tex = new Texture2D(128, 16, TextureFormat.RGBA32, false);
                    tex.filterMode = FilterMode.Point;
                    tex.LoadImage(bytes);

                    cachedVialFrames = new Sprite[8];
                    Vector2 pivot = new Vector2(0.5f, 0.5f);
                    float ppu = 32f;
                    for (int i = 0; i < 8; i++)
                    {
                        cachedVialFrames[i] = Sprite.Create(tex, new Rect(i * 16, 0, 16, 16), pivot, ppu);
                    }
                    vialFrames = cachedVialFrames;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[PanaceaOperator] Could not load vial frames: {ex.Message}");
                }
            }
        }

        protected override OperatorBase FindHealTarget()
        {
            return OperatorManager.Instance != null ? ChooseHealTarget(OperatorManager.Instance.DeployedOperators) : null;
        }

        public override void Heal(OperatorBase target)
        {
            if (target == null || isStunned || isBound) return;

            UnitSfx.Play(data, UnitSfxEvent.Attack);
            EnsureVialFramesLoaded();

            Vector3 startPos = transform.position + new Vector3(0f, 0.25f, 0f);
            Vector3 targetPos = target.transform.position + new Vector3(0f, 0.25f, 0f);
            int healAmount = GetHealAmount();

            PanaceaVialProjectile.Spawn(startPos, target, targetPos, healAmount, vialFrames);
        }

        /// <summary>
        /// The injured operator (below 100% HP) with the lowest HP%; ties go to the lower absolute HP.
        /// Null when nobody needs healing.
        /// </summary>
        public static OperatorBase ChooseHealTarget(IReadOnlyList<OperatorBase> operators)
        {
            if (operators == null) return null;

            OperatorBase best = null;
            float bestRatio = 1f;
            for (int i = 0; i < operators.Count; i++)
            {
                OperatorBase op = operators[i];
                if (op == null || !op.IsDeployed || !op.CanReceiveHealing || op.MaxHP <= 0) continue;
                if (op.CurrentHP <= 0 || op.CurrentHP >= op.MaxHP) continue;

                float ratio = (float)op.CurrentHP / op.MaxHP;
                if (best == null || ratio < bestRatio || (Mathf.Approximately(ratio, bestRatio) && op.CurrentHP < best.CurrentHP))
                {
                    best = op;
                    bestRatio = ratio;
                }
            }
            return best;
        }
    }
}
