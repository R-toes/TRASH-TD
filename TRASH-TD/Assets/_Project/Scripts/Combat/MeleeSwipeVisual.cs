using UnityEngine;

namespace TrashTD.Combat
{
    public sealed class MeleeSwipeVisual : MonoBehaviour
    {
        private const float Lifetime = 0.16f;
        private static Material sharedMaterial;

        private LineRenderer primarySlash;
        private LineRenderer secondarySlash;
        private Color slashColor;
        private float elapsed;

        public static void Play(Vector3 attackerPosition, Vector3 targetPosition, Color color)
        {
            GameObject visualObject = new GameObject("MeleeSwipeVisual");
            MeleeSwipeVisual visual = visualObject.AddComponent<MeleeSwipeVisual>();
            visual.Initialize(attackerPosition, targetPosition, color);
        }

        private void Initialize(Vector3 attackerPosition, Vector3 targetPosition, Color color)
        {
            slashColor = color;
            Vector2 forward = (targetPosition - attackerPosition).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = Vector2.right;
            Vector2 perpendicular = new Vector2(-forward.y, forward.x);
            Vector3 center = targetPosition - (Vector3)forward * 0.06f;
            center.z = targetPosition.z - 0.1f;

            primarySlash = CreateSlash("PrimarySlash", 0.09f);
            secondarySlash = CreateSlash("SecondarySlash", 0.045f);
            SetSlashPoints(primarySlash, center, perpendicular, forward, 0f);
            SetSlashPoints(secondarySlash, center, perpendicular, forward, 0.045f);
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

        private void SetSlashPoints(LineRenderer slash, Vector3 center, Vector2 perpendicular, Vector2 forward, float offset)
        {
            const int pointCount = 7;
            for (int i = 0; i < pointCount; i++)
            {
                float t = (i / (pointCount - 1f)) * 2f - 1f;
                Vector2 point = (Vector2)center + perpendicular * (t * 0.38f) + forward * (0.14f * (1f - t * t) + offset);
                slash.SetPosition(i, new Vector3(point.x, point.y, center.z - 0.01f));
            }
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - Mathf.Clamp01(elapsed / Lifetime);
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