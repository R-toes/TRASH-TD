using UnityEngine;

namespace TrashTD.Combat
{
    /// <summary>
    /// Shadeslice's thrust: a crimson line with a dark glow that extends from the attacker through the target
    /// and fades out. Played through <see cref="MeleeSwipeVisual.PlayStab"/>.
    /// </summary>
    public sealed class MeleeStabVisual : MonoBehaviour
    {
        private const float Lifetime = 0.16f;
        private static Material sharedMaterial;

        private LineRenderer stabLine;
        private LineRenderer stabGlow;
        private Color slashColor;
        private float elapsed;
        private Vector3 stabStart;
        private Vector3 stabEnd;

        public static void Play(Vector3 attackerPosition, Vector3 targetPosition, Color color)
        {
            GameObject visualObject = new GameObject("MeleeStabVisual");
            MeleeStabVisual visual = visualObject.AddComponent<MeleeStabVisual>();
            visual.Initialize(attackerPosition, targetPosition, color);
        }

        private void Initialize(Vector3 attackerPosition, Vector3 targetPosition, Color color)
        {
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

        private void Update()
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - Mathf.Clamp01(elapsed / Lifetime);

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
