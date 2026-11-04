using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Combat
{
    /// <summary>
    /// Flashy laser/beam visual for Resicochet's attack and bounce lines.
    /// Draws 1, 2, or 3 colored parallel lines matching the attack's charge level:
    /// - 1 charge: 1 orange line from Crystal 1
    /// - 2 charges: 2 green lines from Crystals 1 and 2 (offset)
    /// - 3 charges: 3 blue lines from Crystals 1, 2, and 3 (offset)
    /// </summary>
    public sealed class ResicochetAttackBeamVisual : MonoBehaviour
    {
        public const float DefaultLifetime = 3.0f;
        private const float DefaultWidth = 0.05f;
        private static Material sharedMaterial;

        private readonly List<LineRenderer> mainLines = new List<LineRenderer>();
        private readonly List<LineRenderer> coreLines = new List<LineRenderer>();
        private Color baseColor;
        private float lifetime;
        private float elapsed;

        public static ResicochetAttackBeamVisual Play(
            Vector3[] starts,
            Vector3[] ends,
            Color color,
            float lifetime = DefaultLifetime,
            float width = DefaultWidth)
        {
            if (starts == null || ends == null || starts.Length == 0 || ends.Length == 0) return null;

            GameObject visualObj = new GameObject("ResicochetAttackBeamVisual");
            ResicochetAttackBeamVisual visual = visualObj.AddComponent<ResicochetAttackBeamVisual>();
            visual.Initialize(starts, ends, color, lifetime, width);
            return visual;
        }

        public static ResicochetAttackBeamVisual PlayFromCrystals(
            Vector3[] crystalPositions,
            Vector3 targetPos,
            Color color,
            float targetOffsetSpacing = 0.08f,
            float lifetime = DefaultLifetime,
            float width = DefaultWidth)
        {
            if (crystalPositions == null || crystalPositions.Length == 0) return null;

            int count = crystalPositions.Length;
            Vector3 avgStart = Vector3.zero;
            for (int i = 0; i < count; i++) avgStart += crystalPositions[i];
            avgStart /= count;

            Vector3 dir = (targetPos - avgStart).normalized;
            Vector3 normal = new Vector3(-dir.y, dir.x, 0f);

            Vector3[] ends = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float offset = GetLineOffset(i, count, targetOffsetSpacing);
                ends[i] = targetPos + normal * offset;
            }

            return Play(crystalPositions, ends, color, lifetime, width);
        }

        public static ResicochetAttackBeamVisual PlayParallel(
            Vector3 start,
            Vector3 end,
            int lineCount,
            Color color,
            float offsetSpacing = 0.08f,
            float lifetime = DefaultLifetime,
            float width = DefaultWidth)
        {
            int count = Mathf.Max(1, lineCount);
            Vector3 dir = (end - start).normalized;
            Vector3 normal = new Vector3(-dir.y, dir.x, 0f);

            Vector3[] starts = new Vector3[count];
            Vector3[] ends = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                float offset = GetLineOffset(i, count, offsetSpacing);
                starts[i] = start + normal * offset;
                ends[i] = end + normal * offset;
            }

            return Play(starts, ends, color, lifetime, width);
        }

        public static float GetLineOffset(int index, int totalCount, float spacing)
        {
            if (totalCount <= 1) return 0f;
            if (totalCount == 2) return index == 0 ? -spacing * 0.5f : spacing * 0.5f;
            // 3 lines: index 0 -> -spacing, index 1 -> 0, index 2 -> +spacing
            return (index - 1) * spacing;
        }

        private void Initialize(Vector3[] starts, Vector3[] ends, Color color, float life, float width)
        {
            baseColor = color;
            lifetime = life > 0f ? life : DefaultLifetime;
            elapsed = 0f;

            int pairCount = Mathf.Min(starts.Length, ends.Length);
            Material mat = GetSharedMaterial();

            for (int i = 0; i < pairCount; i++)
            {
                Vector3 p0 = starts[i];
                Vector3 p1 = ends[i];
                p0.z = -0.1f;
                p1.z = -0.1f;

                // Main outer beam
                GameObject mainObj = new GameObject($"BeamLine_{i}");
                mainObj.transform.SetParent(transform, false);
                LineRenderer lr = mainObj.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.startWidth = width;
                lr.endWidth = width;
                lr.numCapVertices = 4;
                lr.sortingOrder = 80;
                lr.sharedMaterial = mat;
                lr.startColor = color;
                lr.endColor = color;
                lr.SetPosition(0, p0);
                lr.SetPosition(1, p1);
                mainLines.Add(lr);

                // Brighter core
                GameObject coreObj = new GameObject($"BeamCore_{i}");
                coreObj.transform.SetParent(transform, false);
                LineRenderer coreLr = coreObj.AddComponent<LineRenderer>();
                coreLr.useWorldSpace = true;
                coreLr.positionCount = 2;
                coreLr.startWidth = width * 0.4f;
                coreLr.endWidth = width * 0.4f;
                coreLr.numCapVertices = 4;
                coreLr.sortingOrder = 81;
                coreLr.sharedMaterial = mat;
                Color coreColor = Color.Lerp(color, Color.white, 0.75f);
                coreLr.startColor = coreColor;
                coreLr.endColor = coreColor;
                coreLr.SetPosition(0, p0);
                coreLr.SetPosition(1, p1);
                coreLines.Add(coreLr);
            }
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / lifetime);
            float alpha = progress < 0.8f ? 1f : Mathf.Lerp(1f, 0f, (progress - 0.8f) / 0.2f);

            Color currentMain = baseColor;
            currentMain.a = alpha;

            Color currentCore = Color.Lerp(baseColor, Color.white, 0.75f);
            currentCore.a = alpha;

            for (int i = 0; i < mainLines.Count; i++)
            {
                if (mainLines[i] != null)
                {
                    mainLines[i].startColor = currentMain;
                    mainLines[i].endColor = currentMain;
                }
            }

            for (int i = 0; i < coreLines.Count; i++)
            {
                if (coreLines[i] != null)
                {
                    coreLines[i].startColor = currentCore;
                    coreLines[i].endColor = currentCore;
                }
            }

            if (elapsed >= lifetime)
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

