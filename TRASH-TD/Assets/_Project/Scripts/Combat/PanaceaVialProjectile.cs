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
    /// - Leaves a green "meteor" trail with tapering width and particle motes.
    /// - Arcs gracefully in a parabolic curve unless throwing directly up or down (same vertical line).
    /// - Shatters into green healing droplets, sparkles and heals the target on impact.
    /// </summary>
    public sealed class PanaceaVialProjectile : MonoBehaviour
    {
        private const float DefaultSpeed = 12f;
        private const float MinDuration = 0.22f;
        private const float MaxDuration = 0.65f;
        private const float FrameRate = 0.05f; // ~20 fps for 8-frame swirl animation
        private const float SpinSpeed = 720f;   // 2 rotations per second

        private Vector3 startPos;
        private Vector3 targetPos;
        private OperatorBase targetOperator;
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
        private TrailRenderer meteorTrail;
        private float particleTimer;

        public float ArcHeight => arcHeight;
        public bool IsCurved => arcHeight > 0.01f;
        public float Duration => duration;
        public SpriteRenderer VialRenderer => vialRenderer;
        public TrailRenderer MeteorTrail => meteorTrail;

        public static PanaceaVialProjectile Spawn(
            Vector3 start,
            OperatorBase target,
            Vector3 targetPosition,
            int heal,
            Sprite[] vialAnimationFrames)
        {
            var obj = new GameObject("PanaceaVialProjectile");
            var proj = obj.AddComponent<PanaceaVialProjectile>();
            proj.Initialize(start, target, targetPosition, heal, vialAnimationFrames);
            return proj;
        }

        public void Initialize(
            Vector3 start,
            OperatorBase target,
            Vector3 targetPosition,
            int heal,
            Sprite[] vialAnimationFrames)
        {
            startPos = start;
            targetOperator = target;
            targetPos = targetPosition;
            healAmount = heal;
            frames = vialAnimationFrames;

            transform.position = startPos;
            elapsed = 0f;
            currentFrame = 0;
            animTimer = 0f;

            Vector3 delta = targetPos - startPos;
            float dist = delta.magnitude;
            float dx = delta.x;

            // Throw duration clamped between 0.22s and 0.65s
            duration = dist > 0.01f ? Mathf.Clamp(dist / DefaultSpeed, MinDuration, MaxDuration) : 0.3f;

            // "make the throw a bit curved to the target unless going directly down or up"
            bool isDirectlyVertical = Mathf.Abs(dx) < 0.35f;
            if (isDirectlyVertical)
            {
                // Going directly up or down -> straight line throw
                arcHeight = 0f;
            }
            else
            {
                // Curved parabolic throw
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

            // Green Meteor Trail
            var trailObj = new GameObject("MeteorTrail");
            trailObj.transform.SetParent(transform, false);
            meteorTrail = trailObj.AddComponent<TrailRenderer>();
            meteorTrail.time = 0.28f;
            meteorTrail.startWidth = 0.22f;
            meteorTrail.endWidth = 0.0f;
            meteorTrail.autodestruct = false;
            meteorTrail.sortingLayerID = GridSorting.DefaultLayerId;
            meteorTrail.sortingOrder = 38;

            Material trailMat = GetTrailMaterial();
            meteorTrail.sharedMaterial = trailMat;

            // Meteor gradient: Lime/white head -> Emerald body -> Deep transparent tail
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.75f, 1.0f, 0.45f), 0.0f),
                    new GradientColorKey(new Color(0.20f, 0.95f, 0.35f), 0.3f),
                    new GradientColorKey(new Color(0.05f, 0.65f, 0.15f), 1.0f)
                },
                new[]
                {
                    new GradientAlphaKey(0.95f, 0.0f),
                    new GradientAlphaKey(0.80f, 0.35f),
                    new GradientAlphaKey(0.0f, 1.0f)
                }
            );
            meteorTrail.colorGradient = gradient;

            // Soft green glowing head
            var glowObj = new GameObject("MeteorHeadGlow");
            glowObj.transform.SetParent(visualRoot.transform, false);
            glowRenderer = glowObj.AddComponent<SpriteRenderer>();
            glowRenderer.sortingLayerID = GridSorting.DefaultLayerId;
            glowRenderer.sortingOrder = 39;
            glowRenderer.sprite = CombatFxArt.Dot2();
            glowRenderer.color = new Color(0.35f, 1.0f, 0.45f, 0.65f);
            glowRenderer.transform.localScale = Vector3.one * 1.8f;
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

            // Vial 8-frame swirl animation
            if (frames != null && frames.Length > 0)
            {
                animTimer += Time.deltaTime;
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
                spinAngle += spinDirection * SpinSpeed * Time.deltaTime;
                visualRoot.transform.localRotation = Quaternion.Euler(0f, 0f, spinAngle);
            }

            // Meteor particle trail (green sparks & droplets)
            particleTimer += Time.deltaTime;
            if (particleTimer >= 0.025f)
            {
                particleTimer = 0f;
                CombatFx.Sparks(currentPos, new Color(0.35f, 1f, 0.45f), 1, 0.4f, life: 0.18f);
                CombatFx.Droplets(currentPos, new Color(0.2f, 0.95f, 0.35f), 1, 0.8f);
            }

            if (t >= 1.0f)
            {
                OnImpact();
            }
        }

        private void OnImpact()
        {
            // Apply healing to target
            if (targetOperator != null && targetOperator.IsDeployed && targetOperator.CurrentHP > 0)
            {
                targetOperator.Heal(healAmount);
            }

            // Green potion splash and cross sparkles
            CombatFx.Flash(targetPos, new Color(0.45f, 1.0f, 0.5f), true);
            CombatFx.Droplets(targetPos, new Color(0.25f, 0.95f, 0.35f), 9, 3.5f);
            CombatFx.Crosses(targetPos, new Color(0.35f, 1.0f, 0.45f), 4, 0.28f, large: true, life: 0.65f);
            CombatFx.Pop(targetPos, new Color(0.5f, 1.0f, 0.5f));

            Destroy(gameObject);
        }

        private static Material sharedTrailMaterial;
        private static Material GetTrailMaterial()
        {
            if (sharedTrailMaterial != null) return sharedTrailMaterial;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            sharedTrailMaterial = new Material(shader);
            return sharedTrailMaterial;
        }
    }
}

