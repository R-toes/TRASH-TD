using UnityEngine;
using TrashTD.Enemies;

namespace TrashTD.Combat
{
    /// <summary>
    /// Violet poison bubble visual effect for Thornchin's Retaliation Spine DoT Arts.
    /// Displays floating, rising violet bubbles (multiple sizes) with specular highlights,
    /// upward-drifting purple plus/cross sparkles and motes, and a soft pulsing poison glow
    /// hugging the enemy's silhouette.
    /// Scaled with DoT stacks (1 to 4 stacks) and emits bursting pop effects on damage ticks.
    /// </summary>
    public class ThornchinPoisonVisual : MonoBehaviour
    {
        public const string GlowOverlayName = "PoisonGlowOverlay";

        public static readonly Color PoisonViolet = new Color(0.55f, 0.16f, 0.82f, 1f);
        public static readonly Color PoisonLavender = new Color(0.85f, 0.58f, 1f, 1f);
        public static readonly Color PoisonDark = new Color(0.28f, 0.06f, 0.42f, 1f);

        private EnemyBase enemy;
        private SpriteRenderer targetRenderer;
        private SpriteRenderer glowOverlay;
        private int currentStacks = 1;
        private float bubbleTimer;
        private float sparkleTimer;
        private float age;
        private float seed;

        public int CurrentStacks => currentStacks;

        public static ThornchinPoisonVisual Apply(EnemyBase target, int stacks)
        {
            if (target == null || target.IsDead) return null;

            var visual = target.GetComponent<ThornchinPoisonVisual>();
            if (visual == null)
            {
                visual = target.gameObject.AddComponent<ThornchinPoisonVisual>();
                visual.Initialize(target, stacks);
            }
            else
            {
                visual.SetStacks(stacks);
            }

            return visual;
        }

        public static void Remove(EnemyBase target)
        {
            if (target == null) return;
            var visual = target.GetComponent<ThornchinPoisonVisual>();
            if (visual != null)
            {
                Destroy(visual);
            }
        }

        public void Initialize(EnemyBase target, int stacks)
        {
            enemy = target;
            currentStacks = Mathf.Clamp(stacks, 1, ThornchinSpikeDOT.MaxStacks);
            targetRenderer = enemy != null ? enemy.GetPrimaryVisualRenderer() : GetComponentInChildren<SpriteRenderer>();
            seed = (GetHashCode() & 0xFFFF) * 0.023f;
            bubbleTimer = Random.Range(0f, 0.15f);
            sparkleTimer = Random.Range(0f, 0.2f);
            age = 0f;

            EnsureOverlay();
        }

        public void SetStacks(int stacks)
        {
            currentStacks = Mathf.Clamp(stacks, 1, ThornchinSpikeDOT.MaxStacks);
        }

        private void EnsureOverlay()
        {
            if (glowOverlay != null) return;

            Transform existing = transform.Find(GlowOverlayName);
            if (existing != null)
            {
                glowOverlay = existing.GetComponent<SpriteRenderer>();
            }

            if (glowOverlay == null)
            {
                var go = new GameObject(GlowOverlayName);
                go.transform.SetParent(transform, false);
                glowOverlay = go.AddComponent<SpriteRenderer>();
            }
        }

        private void OnDestroy()
        {
            if (glowOverlay != null)
            {
                Destroy(glowOverlay.gameObject);
                glowOverlay = null;
            }
        }

        public void OnDamageTick(int stacks)
        {
            SetStacks(stacks);
            if (enemy == null || enemy.IsDead) return;

            Vector2 center = enemy.VisualPosition;
            CombatFx.PoisonBubbleBurst(center, PoisonViolet, 3 + currentStacks);
            CombatFx.Sparks(center, PoisonLavender, 3, 1.1f, 0.22f);
        }

        private void Update()
        {
            if (enemy == null || enemy.IsDead)
            {
                Destroy(this);
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            age += dt;

            if (targetRenderer == null)
            {
                targetRenderer = enemy.GetPrimaryVisualRenderer();
                if (targetRenderer == null) targetRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            // Bubble spawn interval scales with stack count:
            // Stack 1: ~0.28s, Stack 2: ~0.20s, Stack 3: ~0.14s, Stack 4: ~0.08s
            float bubbleInterval = Mathf.Lerp(0.28f, 0.08f, (currentStacks - 1) / 3f);
            bubbleTimer -= dt;
            while (bubbleTimer <= 0f)
            {
                bubbleTimer += bubbleInterval * Random.Range(0.85f, 1.25f);
                EmitPoisonBubble();
            }

            // Sparkle motes / plus crosses
            float sparkleInterval = Mathf.Lerp(0.32f, 0.12f, (currentStacks - 1) / 3f);
            sparkleTimer -= dt;
            while (sparkleTimer <= 0f)
            {
                sparkleTimer += sparkleInterval * Random.Range(0.85f, 1.25f);
                EmitPoisonSparkle();
            }
        }

        private void EmitPoisonBubble()
        {
            Vector2 origin = GetBodySpawnPoint();
            float stackProgress = (currentStacks - 1) / 3f;

            // Pick bubble size: larger bubbles appear more often at higher stack counts
            float roll = Random.value;
            Sprite sprite;
            if (roll < 0.20f + 0.15f * stackProgress)
            {
                sprite = CombatFxArt.PoisonBubbleLarge(PoisonViolet);
            }
            else if (roll < 0.65f)
            {
                sprite = CombatFxArt.PoisonBubbleMedium(PoisonViolet);
            }
            else
            {
                sprite = CombatFxArt.PoisonBubbleSmall(PoisonViolet);
            }

            // Upward drift with gentle sinusoidal horizontal wobble
            float wobble = Mathf.Sin(age * 5.5f + seed) * 0.12f + Random.Range(-0.06f, 0.06f);
            float upwardSpeed = Random.Range(0.45f, 0.85f);
            float life = Random.Range(0.55f, 0.90f);

            PixelParticles.Emit(new PixelParticle
            {
                Position = origin,
                Velocity = new Vector2(wobble, upwardSpeed),
                Life = life,
                Gravity = -0.45f, // Floats upward
                Drag = 0.6f,
                StartColor = Color.white,
                EndColor = new Color(1f, 1f, 1f, 0f),
                Sprite = sprite,
                SortingOrder = CombatFx.ImpactSortingOrder + 2
            });
        }

        private void EmitPoisonSparkle()
        {
            Vector2 origin = GetBodySpawnPoint();
            bool isCross = Random.value < 0.6f;
            Sprite sprite = isCross ? CombatFxArt.Plus3() : CombatFxArt.Dot1();

            PixelParticles.Emit(new PixelParticle
            {
                Position = origin,
                Velocity = new Vector2(Random.Range(-0.08f, 0.08f), Random.Range(0.35f, 0.65f)),
                Life = Random.Range(0.4f, 0.65f),
                Gravity = -0.25f,
                Drag = 0.9f,
                StartColor = PoisonLavender,
                EndColor = new Color(PoisonDark.r, PoisonDark.g, PoisonDark.b, 0f),
                Sprite = sprite,
                SortingOrder = CombatFx.ImpactSortingOrder + 1
            });
        }

        private Vector2 GetBodySpawnPoint()
        {
            if (targetRenderer != null && targetRenderer.sprite != null)
            {
                Bounds bounds = targetRenderer.bounds;
                float x = bounds.center.x + Random.Range(-bounds.extents.x * 0.7f, bounds.extents.x * 0.7f);
                float y = bounds.min.y + Random.Range(bounds.size.y * 0.1f, bounds.size.y * 0.85f);
                return new Vector2(x, y);
            }

            if (enemy != null)
            {
                return (Vector2)enemy.VisualPosition + Random.insideUnitCircle * 0.2f;
            }

            return (Vector2)transform.position + Random.insideUnitCircle * 0.2f;
        }

        private void LateUpdate()
        {
            if (glowOverlay == null || targetRenderer == null || targetRenderer.sprite == null) return;

            bool visible = targetRenderer.enabled && targetRenderer.gameObject.activeInHierarchy;
            glowOverlay.enabled = visible;
            if (!visible) return;

            Transform t = targetRenderer.transform;
            glowOverlay.sprite = targetRenderer.sprite;
            glowOverlay.flipX = targetRenderer.flipX;
            glowOverlay.flipY = targetRenderer.flipY;
            glowOverlay.sortingLayerID = targetRenderer.sortingLayerID;
            glowOverlay.sortingOrder = targetRenderer.sortingOrder + 1;
            glowOverlay.transform.SetPositionAndRotation(t.position, t.rotation);

            Vector3 lossy = t.lossyScale;
            Vector3 parentLossy = glowOverlay.transform.parent != null ? glowOverlay.transform.parent.lossyScale : Vector3.one;
            glowOverlay.transform.localScale = new Vector3(
                parentLossy.x != 0f ? lossy.x / parentLossy.x : 1f,
                parentLossy.y != 0f ? lossy.y / parentLossy.y : 1f,
                1f);

            float pulse = 0.5f + 0.5f * Mathf.Sin(age * 5f + seed);
            float stackProgress = (currentStacks - 1) / 3f;
            float baseAlpha = Mathf.Lerp(0.12f, 0.26f, stackProgress);
            float alpha = baseAlpha + 0.06f * pulse;

            glowOverlay.color = new Color(0.62f, 0.15f, 0.88f, alpha);
        }
    }
}
