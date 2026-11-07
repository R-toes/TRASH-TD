using System;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Operators;

namespace TrashTD.Combat
{
    /// <summary>
    /// Healing projectile spat from Coalesce's mouth:
    /// - Follows the green meteor trail of Panacea's vial, but without the vial sprite.
    /// - Led by a glowing pixelated green star/sparkle head with a hot-white core dot.
    /// - Leaves a trail of hot lime/white pixel cores, dense emerald motes with drag, and sparkle crosses.
    /// - Arcs gracefully in a parabolic curve from Coalesce's mouth to the target ally.
    /// - On impact, heals the target ally and bursts into healing droplets and sparkle crosses.
    /// </summary>
    public sealed class CoalesceHealProjectile : MonoBehaviour
    {
        private const float DefaultSpeed = 10f;
        private const float MinDuration = 0.16f;
        private const float MaxDuration = 0.45f;

        private Vector3 startPos;
        private Vector3 targetPos;
        private OperatorBase targetOperator;
        private int healAmount;

        private float duration;
        private float elapsed;
        private float arcHeight;
        private float spinAngle;

        private GameObject visualRoot;
        private SpriteRenderer glowRenderer;
        private SpriteRenderer coreRenderer;

        private Vector3 prevPos;
        private float trailDistance;

        public float ArcHeight => arcHeight;
        public bool IsCurved => arcHeight > 0.01f;
        public float Duration => duration;
        public SpriteRenderer GlowRenderer => glowRenderer;
        public SpriteRenderer CoreRenderer => coreRenderer;

        public static CoalesceHealProjectile Spawn(
            Vector3 start,
            OperatorBase target,
            Vector3 targetPosition,
            int heal,
            float extraArc = 0f)
        {
            var obj = new GameObject("CoalesceHealProjectile");
            var proj = obj.AddComponent<CoalesceHealProjectile>();
            proj.Initialize(start, target, targetPosition, heal, extraArc);
            return proj;
        }

        public void Initialize(
            Vector3 start,
            OperatorBase target,
            Vector3 targetPosition,
            int heal,
            float extraArc = 0f)
        {
            startPos = start;
            targetOperator = target;
            targetPos = targetPosition;
            healAmount = heal;

            transform.position = startPos;
            prevPos = startPos;
            trailDistance = 0f;
            elapsed = 0f;

            Vector3 delta = targetPos - startPos;
            float dist = delta.magnitude;
            float dx = delta.x;

            duration = dist > 0.01f ? Mathf.Clamp(dist / DefaultSpeed, MinDuration, MaxDuration) : 0.2f;

            bool isDirectlyVertical = Mathf.Abs(dx) < 0.25f;
            if (isDirectlyVertical)
            {
                arcHeight = 0f;
            }
            else
            {
                arcHeight = Mathf.Clamp(dist * 0.28f + extraArc, 0.35f, 1.2f);
            }

            SetupVisuals();
        }

        private void SetupVisuals()
        {
            visualRoot = new GameObject("VisualRoot");
            visualRoot.transform.SetParent(transform, false);
            visualRoot.transform.localPosition = Vector3.zero;

            // Crisp pixelated green head star glow (matching Panacea's meteor head)
            var glowObj = new GameObject("MeteorHeadGlow");
            glowObj.transform.SetParent(visualRoot.transform, false);
            glowRenderer = glowObj.AddComponent<SpriteRenderer>();
            glowRenderer.sortingLayerID = GridSorting.DefaultLayerId;
            glowRenderer.sortingOrder = 40;
            glowRenderer.sprite = CombatFxArt.Flash5();
            glowRenderer.color = new Color(0.4f, 1.0f, 0.5f, 0.85f);
            glowRenderer.transform.localScale = Vector3.one;

            // Bright hot-white core dot (no vial)
            var coreObj = new GameObject("MeteorHeadCore");
            coreObj.transform.SetParent(visualRoot.transform, false);
            coreRenderer = coreObj.AddComponent<SpriteRenderer>();
            coreRenderer.sortingLayerID = GridSorting.DefaultLayerId;
            coreRenderer.sortingOrder = 41;
            coreRenderer.sprite = CombatFxArt.Dot2();
            coreRenderer.color = new Color(0.9f, 1.0f, 0.8f, 0.95f);
            coreRenderer.transform.localScale = Vector3.one;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Calculate current trajectory position
            Vector3 linear = Vector3.Lerp(startPos, targetPos, t);
            float arc = 4f * t * (1f - t) * arcHeight;
            Vector3 currentPos = linear + new Vector3(0f, arc, 0f);
            transform.position = currentPos;

            // Emit crisp pixelated green meteor trail (identical to Panacea's vial trail)
            EmitPixelMeteorTrail(prevPos, currentPos);
            prevPos = currentPos;

            // Subtle rotation of the head glow
            if (visualRoot != null)
            {
                spinAngle += 360f * Time.deltaTime;
                visualRoot.transform.localRotation = Quaternion.Euler(0f, 0f, spinAngle);
            }

            if (t >= 1.0f)
            {
                OnImpact();
            }
        }

        private void EmitPixelMeteorTrail(Vector3 from, Vector3 to)
        {
            float spacing = 1.5f * CombatFxArt.Pixel; // Emit pixel motes every 1.5 map pixels (~0.047 units)
            float length = Vector3.Distance(from, to);
            if (length <= 0.0001f) return;

            Vector3 delta = to - from;
            Vector3 direction = delta.normalized;

            trailDistance += length;
            while (trailDistance >= spacing)
            {
                trailDistance -= spacing;
                Vector3 point = to - direction * trailDistance;
                EmitMeteorPixel(point, direction);
            }
        }

        private void EmitMeteorPixel(Vector3 point, Vector3 direction)
        {
            // 1. Hot pixel core: bright lime/white 2x2 dot
            PixelParticles.Emit(new PixelParticle
            {
                Position = point,
                Life = 0.16f,
                StartColor = new Color(0.85f, 1.0f, 0.6f, 0.95f),
                EndColor = new Color(0.2f, 0.95f, 0.35f, 0f),
                Sprite = CombatFxArt.Dot2(),
                SortingOrder = 38
            });

            // 2. Dense tapered pixel tail: emerald green trailing motes with drag
            Vector2 jitter = UnityEngine.Random.insideUnitCircle * (CombatFxArt.Pixel * 1.5f);
            PixelParticles.Emit(new PixelParticle
            {
                Position = (Vector2)point + jitter,
                Velocity = (Vector2)(-direction * 0.45f) + UnityEngine.Random.insideUnitCircle * 0.12f,
                Drag = 2.5f,
                Life = UnityEngine.Random.Range(0.22f, 0.38f),
                StartColor = new Color(0.25f, 0.95f, 0.4f, 0.9f),
                EndColor = new Color(0.05f, 0.55f, 0.15f, 0f),
                Sprite = UnityEngine.Random.value < 0.4f ? CombatFxArt.Dot2() : CombatFxArt.Dot1(),
                SortingOrder = 37
            });

            // 3. Occasional pixel sparkle cross (~25% chance per step)
            if (UnityEngine.Random.value < 0.25f)
            {
                PixelParticles.Emit(new PixelParticle
                {
                    Position = (Vector2)point + UnityEngine.Random.insideUnitCircle * (CombatFxArt.Pixel * 2f),
                    Velocity = UnityEngine.Random.insideUnitCircle * 0.2f,
                    Drag = 1.5f,
                    Life = UnityEngine.Random.Range(0.2f, 0.35f),
                    StartColor = new Color(0.9f, 1.0f, 0.7f, 0.95f),
                    EndColor = new Color(0.15f, 0.85f, 0.3f, 0f),
                    Sprite = CombatFxArt.Plus3(),
                    SortingOrder = 39
                });
            }
        }

        private void OnImpact()
        {
            if (targetOperator != null && targetOperator.IsDeployed && targetOperator.CurrentHP > 0)
            {
                targetOperator.Heal(healAmount);
            }

            CombatFx.Flash(targetPos, new Color(0.45f, 1.0f, 0.5f), true);
            CombatFx.Droplets(targetPos, new Color(0.25f, 0.95f, 0.35f), 6, 2.5f);
            CombatFx.Crosses(targetPos, new Color(0.35f, 1.0f, 0.45f), 3, 0.25f, large: false, life: 0.5f);
            CombatFx.Pop(targetPos, new Color(0.5f, 1.0f, 0.5f));

            Destroy(gameObject);
        }
    }
}
