using System.Collections.Generic;
using UnityEngine;
using TrashTD.Audio;
using TrashTD.Combat;
using TrashTD.Data;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Panacea — 3-Star Medic with global range.
    /// Base (3★): Every 5s, heals the operator with the lowest HP% anywhere on the field.
    /// 1x Upgraded (4★): Cooldown changed to 3s. Heals up to 2 operators.
    /// 2x Upgraded (5★): Restores HP of all operators on the map by a fixed +5 HP per second.
    /// Throws a spinning 16x16 potion vial with an 8-frame liquid swirl animation and a green meteor trail.
    /// When attacking, plays a 12-frame throw animation followed by a 12-frame reload animation from
    /// "Panacea-Throw-Reload.png", returning to idle animation when both finish.
    /// </summary>
    public class PanaceaOperator : MedicOperator
    {
        [Header("Panacea 16x16 Vial Frames (8 frames)")]
        [SerializeField] private Sprite[] vialFrames;

        private static Sprite[] cachedVialFrames;
        private PanaceaSpriteAnimation spriteAnimation;
        private float globalRegenTimer = 0f;

        public Sprite[] VialFrames => vialFrames;
        public bool HasFirstUpgrade => currentRarity >= OperatorRarity.Star4;
        public bool HasSecondUpgrade => currentRarity >= OperatorRarity.Star5;

        public PanaceaSpriteAnimation SpriteAnimation
        {
            get
            {
                if (spriteAnimation == null)
                {
                    spriteAnimation = GetComponent<PanaceaSpriteAnimation>();
                    if (spriteAnimation == null && Application.isPlaying)
                    {
                        spriteAnimation = gameObject.AddComponent<PanaceaSpriteAnimation>();
                    }
                }
                return spriteAnimation;
            }
        }

        public override Vector3 VisualCenter => transform.position;
        public override Vector2 BodyExtents => new Vector2(0.5f, 0.5f);

        public void SetRarityForTesting(OperatorRarity rarity)
        {
            currentRarity = rarity;
        }

        public void ConfigureVialFrames(Sprite[] frames)
        {
            vialFrames = frames;
        }

        private void Awake()
        {
            EnsureVialFramesLoaded();
            EnsureSpriteAnimation();
        }

        private void EnsureSpriteAnimation()
        {
            var opAnim = GetComponent<OperatorSpriteAnimation>();
            if (opAnim != null)
            {
                DestroyImmediate(opAnim);
            }
            if (spriteAnimation == null)
            {
                spriteAnimation = GetComponent<PanaceaSpriteAnimation>();
            }
            if (spriteAnimation == null && Application.isPlaying)
            {
                spriteAnimation = gameObject.AddComponent<PanaceaSpriteAnimation>();
            }
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

        public override float GetAttackInterval()
        {
            // 1x Upgrade: Change cooldown to 3
            float baseInterval = HasFirstUpgrade ? 3.0f : (data != null ? data.attackInterval : 5.0f);
            return StageCombatModifiers.GetAttackInterval(baseInterval) / HarmonyAttackSpeedMultiplier;
        }

        protected override void OnRetreated()
        {
            SpriteAnimation?.CancelAnimation();
            base.OnRetreated();
        }

        protected override void Update()
        {
            if (!isDeployed) return;

            float interval = GetAttackInterval();
            attackTimer = Mathf.Min(attackTimer + Time.deltaTime, interval);

            // No heals between waves
            if (IsCombatPaused) return;

            if (isStunned || isBound)
            {
                if (isStunned) UpdateStunStatus(Time.deltaTime);
                if (isBound) UpdateBindStatus(Time.deltaTime);
                SpriteAnimation?.CancelAnimation();
                return;
            }

            equippedSkill?.UpdateSkill(Time.deltaTime);

            // 2x Upgrade: Restores HP of all operators on the map by fixed +5 HP per second
            if (HasSecondUpgrade)
            {
                UpdateGlobalRegen(Time.deltaTime);
            }

            if (attackTimer >= interval)
            {
                int maxTargets = HasFirstUpgrade ? 2 : 1;
                var targets = FindHealTargets(maxTargets);
                if (targets != null && targets.Count > 0)
                {
                    attackTimer = 0f;
                    PerformHeal(targets);
                }
            }
        }

        private void UpdateGlobalRegen(float dt)
        {
            globalRegenTimer += dt;
            while (globalRegenTimer >= 1.0f)
            {
                globalRegenTimer -= 1.0f;
                ApplyGlobalRegen();
            }
        }

        public void ApplyGlobalRegen()
        {
            if (OperatorManager.Instance == null) return;

            var deployed = OperatorManager.Instance.DeployedOperators;
            if (deployed == null) return;

            for (int i = 0; i < deployed.Count; i++)
            {
                var op = deployed[i];
                if (op == null || !op.IsDeployed || op.CurrentHP <= 0 || op.CurrentHP >= op.MaxHP)
                    continue;

                // Exclude Drawgoo (and any unhealable units)
                if (!op.CanReceiveHealing)
                    continue;
                if (op.Data != null && (op.Data.cannotBeHealed || op.Data.operatorName == "Drawgoo"))
                    continue;

                op.Heal(5);
            }
        }

        protected virtual List<OperatorBase> FindHealTargets(int maxTargets)
        {
            return OperatorManager.Instance != null
                ? ChooseHealTargets(OperatorManager.Instance.DeployedOperators, maxTargets)
                : new List<OperatorBase>();
        }

        protected override OperatorBase FindHealTarget()
        {
            var list = FindHealTargets(1);
            return list.Count > 0 ? list[0] : null;
        }

        public void PerformHeal(List<OperatorBase> targets)
        {
            if (targets == null || targets.Count == 0 || isStunned || isBound) return;

            EnsureVialFramesLoaded();

            float cooldown = GetAttackInterval();
            PanaceaSpriteAnimation anim = SpriteAnimation;

            if (!Application.isPlaying || anim == null)
            {
                UnitSfx.Play(data, UnitSfxEvent.Attack);
                for (int i = 0; i < targets.Count; i++)
                {
                    SpawnVialForTarget(targets[i]);
                }
                return;
            }

            anim.PlayHealAnimation(cooldown, onThrow: () =>
            {
                UnitSfx.Play(data, UnitSfxEvent.Attack);
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i] != null && targets[i].IsDeployed && targets[i].CurrentHP > 0)
                    {
                        SpawnVialForTarget(targets[i]);
                    }
                }
            });
        }

        public Vector3 GetThrowOrigin()
        {
            bool facingRight = spriteAnimation != null && spriteAnimation.GetComponent<SpriteRenderer>() != null &&
                               spriteAnimation.GetComponent<SpriteRenderer>().flipX;
            float dir = facingRight ? 1f : -1f;
            return transform.position + new Vector3(dir * 0.42f, 0.08f, 0f);
        }

        public PanaceaVialProjectile SpawnVialForTarget(OperatorBase target)
        {
            if (target == null) return null;
            Vector3 startPos = GetThrowOrigin();
            Vector3 targetPos = target.transform.position + new Vector3(0f, 0.25f, 0f);
            int healAmount = GetHealAmount();

            return PanaceaVialProjectile.Spawn(startPos, target, targetPos, healAmount, vialFrames);
        }

        public override void Heal(OperatorBase target)
        {
            if (target == null || isStunned || isBound) return;
            PerformHeal(new List<OperatorBase> { target });
        }

        /// <summary>
        /// Selects up to <paramref name="maxTargets"/> injured operators (below 100% HP) with the lowest HP%;
        /// ties go to the lower absolute HP.
        /// </summary>
        public static List<OperatorBase> ChooseHealTargets(IReadOnlyList<OperatorBase> operators, int maxTargets)
        {
            if (operators == null || maxTargets <= 0) return new List<OperatorBase>();

            var candidates = new List<OperatorBase>();
            for (int i = 0; i < operators.Count; i++)
            {
                OperatorBase op = operators[i];
                if (op == null || !op.IsDeployed || !op.CanReceiveHealing || op.MaxHP <= 0) continue;
                if (op.CurrentHP <= 0 || op.CurrentHP >= op.MaxHP) continue;
                candidates.Add(op);
            }

            candidates.Sort((a, b) =>
            {
                float ratioA = (float)a.CurrentHP / a.MaxHP;
                float ratioB = (float)b.CurrentHP / b.MaxHP;
                if (!Mathf.Approximately(ratioA, ratioB))
                {
                    return ratioA.CompareTo(ratioB);
                }
                return a.CurrentHP.CompareTo(b.CurrentHP);
            });

            if (candidates.Count > maxTargets)
            {
                candidates.RemoveRange(maxTargets, candidates.Count - maxTargets);
            }
            return candidates;
        }

        /// <summary>
        /// The injured operator (below 100% HP) with the lowest HP%; ties go to the lower absolute HP.
        /// Null when nobody needs healing.
        /// </summary>
        public static OperatorBase ChooseHealTarget(IReadOnlyList<OperatorBase> operators)
        {
            var list = ChooseHealTargets(operators, 1);
            return list.Count > 0 ? list[0] : null;
        }
    }
}
