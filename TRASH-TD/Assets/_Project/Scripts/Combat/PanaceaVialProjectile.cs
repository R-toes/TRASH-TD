using System;
using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Operators;

namespace TrashTD.Combat
{
    /// <summary>
    /// Thrown 16x16 healing vial projectile for Panacea:
    /// - Plays the 8-frame liquid swirl animation while flying.
    /// - Spins continuously along its trajectory.
    /// - Leaves a pixelated green "meteor" trail composed of crisp stage-art pixel motes and sparkle crosses.
    /// - Arcs gracefully in a parabolic curve unless throwing directly up or down (same vertical line).
    /// - Shatters into green healing droplets, sparkles and heals the target on impact.
    /// </summary>
    public sealed class PanaceaVialProjectile : MonoBehaviour
    {
        private const float DefaultSpeed = 7.5f;
        private const float MinDuration = 0.35f;
        private const float MaxDuration = 0.75f;
        private const float FrameRate = 0.05f; // ~20 fps for 8-frame swirl animation
        private const float SpinSpeed = 720f;   // 2 rotations per second

        private Vector3 startPos;
        private Vector3 targetPos;
        private OperatorBase targetOperator;
        private PanaceaOperator sourceOperator;
        private int healAmount;
        private Sprite[] frames;

        private float duration;
        private float elapsed;
        private float arcHeight;
        private float spinAngle;
        private float spinDirection = -1f;
        private float animTimer;
        private int currentFrame;

        private GameObject visualRoot;
        private SpriteRenderer vialRenderer;
        private SpriteRenderer glowRenderer;

        private Vector3 prevPos;
        private float trailDistance;

        public float ArcHeight => arcHeight;
        public bool IsCurved => arcHeight > 0.01f;
        public float Duration => duration;
        public SpriteRenderer VialRenderer => vialRenderer;
        public TrailRenderer MeteorTrail => null;

        public static PanaceaVialProjectile Spawn(
            Vector3 start,
            OperatorBase target,
            Vector3 targetPosition,
            int heal,
            Sprite[] vialAnimationFrames,
            PanaceaOperator source = null)
        {
            var obj = new GameObject("PanaceaVialProjectile");
            var proj = obj.AddComponent<PanaceaVialProjectile>();
            proj.Initialize(start, target, targetPosition, heal, vialAnimationFrames, source);
            return proj;
        }

        public void Initialize(
            Vector3 start,
            OperatorBase target,
            Vector3 targetPosition,
            int heal,
            Sprite[] vialAnimationFrames,
            PanaceaOperator source = null)
        {
            startPos = start;
            targetOperator = target;
            targetPos = targetPosition;
            healAmount = heal;
            frames = vialAnimationFrames;
            sourceOperator = source;

            transform.position = startPos;
            prevPos = startPos;
            trailDistance = 0f;
            elapsed = 0f;
            currentFrame = 0;
            animTimer = 0f;

            Vector3 delta = targetPos - startPos;
            float dist = delta.magnitude;
            float dx = delta.x;

            // Throw duration clamped between 0.22s and 0.65s
            duration = dist > 0.01f ? Mathf.Clamp(dist / DefaultSpeed, MinDuration, MaxDuration) : 0.3f;

            // Curved parabolic throw unless going directly up or down
            bool isDirectlyVertical = Mathf.Abs(dx) < 0.35f;
            if (isDirectlyVertical)
            {
                arcHeight = 0f;
            }
            else
            {
                arcHeight = Mathf.Clamp(dist * 0.28f, 0.45f, 1.4f);
            }

            spinDirection = dx >= 0f ? -1f : 1f;

            SetupVisuals();
        }

        private void SetupVisuals()
        {
            visualRoot = new GameObject("VisualRoot");
            visualRoot.transform.SetParent(transform, false);
            visualRoot.transform.localPosition = Vector3.zero;

            // Main 16x16 vial sprite
            var vialObj = new GameObject("VialSprite");
            vialObj.transform.SetParent(visualRoot.transform, false);
            vialRenderer = vialObj.AddComponent<SpriteRenderer>();
            vialRenderer.sortingLayerID = GridSorting.DefaultLayerId;
            vialRenderer.sortingOrder = 40;
            if (frames != null && frames.Length > 0 && frames[0] != null)
            {
                vialRenderer.sprite = frames[0];
            }

            // Crisp pixelated green head star glow
            var glowObj = new GameObject("MeteorHeadGlow");
            glowObj.transform.SetParent(visualRoot.transform, false);
            glowRenderer = glowObj.AddComponent<SpriteRenderer>();
            glowRenderer.sortingLayerID = GridSorting.DefaultLayerId;
            glowRenderer.sortingOrder = 39;
            glowRenderer.sprite = CombatFxArt.Flash5();
            glowRenderer.color = new Color(0.4f, 1.0f, 0.5f, 0.7f);
            glowRenderer.transform.localScale = Vector3.one;
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            elapsed += dt;
            float t = Mathf.Clamp01(elapsed / duration);

            // Calculate current trajectory position
            Vector3 linear = Vector3.Lerp(startPos, targetPos, t);
            float arc = 4f * t * (1f - t) * arcHeight;
            Vector3 currentPos = linear + new Vector3(0f, arc, 0f);
            transform.position = currentPos;

            // Emit crisp pixelated green meteor trail
            EmitPixelMeteorTrail(prevPos, currentPos);
            prevPos = currentPos;

            // Vial 8-frame swirl animation
            if (frames != null && frames.Length > 0)
            {
                animTimer += dt;
                if (animTimer >= FrameRate)
                {
                    animTimer -= FrameRate;
                    currentFrame = (currentFrame + 1) % frames.Length;
                    if (vialRenderer != null && frames[currentFrame] != null)
                    {
                        vialRenderer.sprite = frames[currentFrame];
                    }
                }
            }

            // Spinning rotation
            if (visualRoot != null)
            {
                spinAngle += spinDirection * SpinSpeed * dt;
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
            // Apply healing to target
            if (targetOperator != null && targetOperator.IsDeployed && targetOperator.CurrentHP > 0)
            {
                targetOperator.Heal(healAmount);
            }

            // Play vial impact sound effect (custom slot -> UnitSfx convention -> fallback)
            if (sourceOperator != null)
            {
                sourceOperator.PlayVialImpactSound();
            }
            else
            {
                if (!TrashTD.Audio.UnitSfx.PlayFromFolder("SFX/Operators/Medic/Panacea", "Impact"))
                {
                    TrashTD.Audio.AudioManager.Instance?.PlaySfx(TrashTD.Audio.SfxId.GameplayMagicAttack, 0.65f, 1.3f);
                }
            }

            // Green potion splash and cross sparkles
            CombatFx.Flash(targetPos, new Color(0.45f, 1.0f, 0.5f), true);
            CombatFx.Droplets(targetPos, new Color(0.25f, 0.95f, 0.35f), 9, 3.5f);
            CombatFx.Crosses(targetPos, new Color(0.35f, 1.0f, 0.45f), 4, 0.28f, large: true, life: 0.65f);
            CombatFx.Pop(targetPos, new Color(0.5f, 1.0f, 0.5f));

            Destroy(gameObject);
        }
    }
}
