using UnityEngine;

namespace TrashTD.Combat
{
    public sealed class MeleeSwipeVisual : MonoBehaviour
    {
        private const float Lifetime = 0.16f;
        private static Material sharedMaterial;

        private LineRenderer primarySlash;
        private LineRenderer secondarySlash;
        private LineRenderer stabLine;
        private LineRenderer stabGlow;
        private Color slashColor;
        private float elapsed;
        private Vector3 stabStart;
        private Vector3 stabEnd;
        private bool isStab;

        public static void Play(Vector3 attackerPosition, Vector3 targetPosition, Color color, float sizeMultiplier = 1f)
        {
            GameObject visualObject = new GameObject("MeleeSwipeVisual");
            MeleeSwipeVisual visual = visualObject.AddComponent<MeleeSwipeVisual>();
            visual.Initialize(attackerPosition, targetPosition, color, sizeMultiplier);
        }

        public static void PlayStab(Vector3 attackerPosition, Vector3 targetPosition, Color color)
        {
            GameObject visualObject = new GameObject("MeleeStabVisual");
            MeleeSwipeVisual visual = visualObject.AddComponent<MeleeSwipeVisual>();
            visual.InitializeStab(attackerPosition, targetPosition, color);
        }

        private void Initialize(Vector3 attackerPosition, Vector3 targetPosition, Color color, float sizeMultiplier)
        {
            slashColor = color;
            sizeMultiplier = Mathf.Max(0.1f, sizeMultiplier);
            Vector2 forward = (targetPosition - attackerPosition).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = Vector2.right;
            Vector2 perpendicular = new Vector2(-forward.y, forward.x);
            Vector3 center = targetPosition - (Vector3)forward * 0.06f;
            center.z = targetPosition.z - 0.1f;

            primarySlash = CreateSlash("PrimarySlash", 0.09f * sizeMultiplier);
            secondarySlash = CreateSlash("SecondarySlash", 0.045f * sizeMultiplier);
            SetSlashPoints(primarySlash, center, perpendicular, forward, 0f, sizeMultiplier);
            SetSlashPoints(secondarySlash, center, perpendicular, forward, 0.045f * sizeMultiplier, sizeMultiplier);
        }

        private void InitializeStab(Vector3 attackerPosition, Vector3 targetPosition, Color color)
        {
            isStab = true;
            slashColor = color;
            stabStart = attackerPosition;
            stabEnd = attackerPosition + (targetPosition - attackerPosition) * 1.2f;
            stabStart.z = targetPosition.z - 0.1f;
            stabEnd.z = targetPosition.z - 0.1f;

            GameObject glowObject = new GameObject("StabGlow");
            glowObject.transform.SetParent(transform, false);
            stabGlow = glowObject.AddComponent<LineRenderer>();
            stabGlow.useWorldSpace = true;
            stabGlow.positionCount = 2;
            stabGlow.startWidth = 0.85f;
            stabGlow.endWidth = 0.32f;
            stabGlow.numCapVertices = 3;
            stabGlow.sortingOrder = 79;
            stabGlow.sharedMaterial = GetSharedMaterial();
            stabGlow.startColor = new Color(0.85f, 0.015f, 0.025f, 0.85f);
            stabGlow.endColor = new Color(1f, 0.12f, 0.08f, 0.55f);
            stabGlow.SetPosition(0, stabStart);
            stabGlow.SetPosition(1, stabStart);

            GameObject lineObject = new GameObject("StabLine");
            lineObject.transform.SetParent(transform, false);
            stabLine = lineObject.AddComponent<LineRenderer>();
            stabLine.useWorldSpace = true;
            stabLine.positionCount = 2;
            stabLine.startWidth = 0.48f;
            stabLine.endWidth = 0.16f;
            stabLine.numCapVertices = 3;
            stabLine.sortingOrder = 80;
            stabLine.sharedMaterial = GetSharedMaterial();
            stabLine.startColor = color;
            stabLine.endColor = new Color(0.48f, 0.035f, 0.045f, 1f);
            stabLine.SetPosition(0, stabStart);
            stabLine.SetPosition(1, stabStart);
        }

        private LineRenderer CreateSlash(string objectName, float width)
        {
            GameObject slashObject = new GameObject(objectName);
            slashObject.transform.SetParent(transform, false);

            LineRenderer slash = slashObject.AddComponent<LineRenderer>();
            slash.useWorldSpace = true;
            slash.positionCount = 7;
            slash.startWidth = width;
            slash.endWidth = width * 0.35f;
            slash.numCapVertices = 3;
            slash.numCornerVertices = 3;
            slash.sortingOrder = 55;
            slash.sharedMaterial = GetSharedMaterial();
            return slash;
        }

        private void SetSlashPoints(
            LineRenderer slash,
            Vector3 center,
            Vector2 perpendicular,
            Vector2 forward,
            float offset,
            float sizeMultiplier)
        {
            const int pointCount = 7;
            for (int i = 0; i < pointCount; i++)
            {
                float t = (i / (pointCount - 1f)) * 2f - 1f;
                Vector2 point = (Vector2)center +
                    perpendicular * (t * 0.38f * sizeMultiplier) +
                    forward * (0.14f * (1f - t * t) * sizeMultiplier + offset);
                slash.SetPosition(i, new Vector3(point.x, point.y, center.z - 0.01f));
            }
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - Mathf.Clamp01(elapsed / Lifetime);
            if (isStab)
            {
                Vector3 tip = Vector3.Lerp(stabStart, stabEnd, Mathf.Clamp01(elapsed / (Lifetime * 0.65f)));
                stabLine.SetPosition(1, tip);
                stabGlow.SetPosition(1, tip);
                Color startColor = slashColor;
                startColor.a = alpha;
                Color endColor = new Color(0.48f, 0.035f, 0.045f, 1f);
                endColor.a = alpha;
                stabLine.startColor = startColor;
                stabLine.endColor = endColor;
                Color glowStart = new Color(0.85f, 0.015f, 0.025f, 0.85f * alpha);
                Color glowEnd = new Color(1f, 0.12f, 0.08f, 0.55f * alpha);
                stabGlow.startColor = glowStart;
                stabGlow.endColor = glowEnd;
                if (elapsed >= Lifetime) Destroy(gameObject);
                return;
            }

            Color primaryColor = slashColor;
            primaryColor.a = alpha;
            Color secondaryColor = Color.Lerp(slashColor, Color.white, 0.5f);
            secondaryColor.a = alpha;
            primarySlash.startColor = primaryColor;
            primarySlash.endColor = primaryColor;
            secondarySlash.startColor = secondaryColor;
            secondarySlash.endColor = secondaryColor;

            if (elapsed >= Lifetime)
            {
                Destroy(gameObject);
            }
        }

        private static Material GetSharedMaterial()
        {
            if (sharedMaterial != null) return sharedMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            sharedMaterial = new Material(shader);
            return sharedMaterial;
        }
    }
}