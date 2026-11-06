using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Combat
{
    /// <summary>
    /// Seamless connected fading moving line visual for Progeny's chain heal.
    /// Draws a single continuous multi-segment glowing beam (LineRenderer with corner rounding)
    /// that streams from Progeny's red pod through each bounce target in sequence.
    /// As the leading head reaches each target, the onReachTarget callback triggers the heal
    /// and restorative cross splash particles.
    /// </summary>
    public sealed class ProgenyHealTetherVisual : MonoBehaviour
    {
        public const float DefaultLifetime = 0.35f;
        public const float TravelSpeed = 16f; // units per second
        public const float FadeDuration = 0.22f; // fade after head arrives
        private const float DefaultWidth = 0.07f;

        public static readonly Color BrightRed = new Color(1f, 0.16f, 0.22f, 1f);
        public static readonly Color CoreColor = new Color(1f, 0.82f, 0.85f, 1f);

        private static Material sharedMaterial;

        private LineRenderer mainLine;
        private LineRenderer coreLine;
        private SpriteRenderer leadingOrb;
        private SpriteRenderer leadingHalo;

        private Vector3[] pathPoints;
        private float[] segmentLengths;
        private float[] prefixLengths;
        private float totalLength;

        private Action<int>[] targetCallbacks;
        private bool[] targetReached;

        private float headDistance;
        private float tailDistance;
        private float headArrivalTimer;
        private bool isHeadArrived;
        private float elapsed;

        private readonly List<Vector3> subPoints = new List<Vector3>(8);

        public static ProgenyHealTetherVisual PlayChain(
            Vector3[] points,
            Action<int>[] onReachPoint = null,
            Color? color = null)
        {
            if (points == null || points.Length < 2) return null;

            GameObject obj = new GameObject("ProgenyHealChainVisual");
            ProgenyHealTetherVisual visual = obj.AddComponent<ProgenyHealTetherVisual>();
            visual.Initialize(points, onReachPoint, color ?? BrightRed);
            return visual;
        }

        public static ProgenyHealTetherVisual Play(
            Vector3 start,
            Vector3 end,
            Color? color = null,
            float lifetime = DefaultLifetime,
            float width = DefaultWidth)
        {
            return PlayChain(new[] { start, end }, null, color);
        }

        private void Initialize(Vector3[] points, Action<int>[] callbacks, Color color)
        {
            pathPoints = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                pathPoints[i] = new Vector3(points[i].x, points[i].y, -0.15f);
            }

            targetCallbacks = callbacks;
            targetReached = new bool[points.Length];

            int segCount = points.Length - 1;
            segmentLengths = new float[segCount];
            prefixLengths = new float[points.Length];
            totalLength = 0f;

            prefixLengths[0] = 0f;
            for (int i = 0; i < segCount; i++)
            {
                segmentLengths[i] = Vector2.Distance(pathPoints[i], pathPoints[i + 1]);
                totalLength += segmentLengths[i];
                prefixLengths[i + 1] = totalLength;
            }

            Material mat = GetSharedMaterial();

            // Main outer glowing line (numCornerVertices = 4 ensures perfectly smooth rounded seamless corners)
            GameObject mainObj = new GameObject("ChainMainLine");
            mainObj.transform.SetParent(transform, false);
            mainLine = mainObj.AddComponent<LineRenderer>();
            mainLine.useWorldSpace = true;
            mainLine.numCapVertices = 4;
            mainLine.numCornerVertices = 4;
            mainLine.startWidth = DefaultWidth;
            mainLine.endWidth = DefaultWidth;
            mainLine.sortingOrder = 80;
            mainLine.sharedMaterial = mat;
            mainLine.startColor = color;
            mainLine.endColor = color;

            // Inner hot core
            GameObject coreObj = new GameObject("ChainCoreLine");
            coreObj.transform.SetParent(transform, false);
            coreLine = coreObj.AddComponent<LineRenderer>();
            coreLine.useWorldSpace = true;
            coreLine.numCapVertices = 4;
            coreLine.numCornerVertices = 4;
            coreLine.startWidth = DefaultWidth * 0.45f;
            coreLine.endWidth = DefaultWidth * 0.45f;
            coreLine.sortingOrder = 81;
            coreLine.sharedMaterial = mat;
            coreLine.startColor = CoreColor;
            coreLine.endColor = CoreColor;

            // Leading red ArtsOrb at the head of the beam
            GameObject orbObj = new GameObject("LeadingOrb");
            orbObj.transform.SetParent(transform, false);
            leadingOrb = orbObj.AddComponent<SpriteRenderer>();
            leadingOrb.sprite = CombatFxArt.Orb(color);
            leadingOrb.sortingOrder = 85;

            GameObject haloObj = new GameObject("LeadingHalo");
            haloObj.transform.SetParent(orbObj.transform, false);
            leadingHalo = haloObj.AddComponent<SpriteRenderer>();
            leadingHalo.sprite = CombatFxArt.PopRingFrames()[3];
            leadingHalo.color = new Color(color.r, color.g, color.b, 0.5f);
            leadingHalo.sortingOrder = 84;

            headDistance = 0f;
            tailDistance = 0f;
            headArrivalTimer = 0f;
            isHeadArrived = false;
            elapsed = 0f;

            UpdatePositions();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            elapsed += dt;

            // Advance head
            if (!isHeadArrived)
            {
                headDistance += TravelSpeed * dt;
                if (headDistance >= totalLength)
                {
                    headDistance = totalLength;
                    isHeadArrived = true;
                }

                // Check if head reached any target points (index 1..points.Length-1)
                for (int i = 1; i < pathPoints.Length; i++)
                {
                    if (!targetReached[i] && headDistance >= prefixLengths[i] - 0.08f)
                    {
                        targetReached[i] = true;
                        targetCallbacks?[i]?.Invoke(i);
                    }
                }
            }

            // Tail follows smoothly: beam stays connected across segments
            float beamLength = Mathf.Min(1.8f, totalLength * 0.75f);
            if (isHeadArrived)
            {
                headArrivalTimer += dt;
                float catchUpSpeed = TravelSpeed * 1.5f;
                tailDistance = Mathf.Min(totalLength, tailDistance + catchUpSpeed * dt);
            }
            else
            {
                tailDistance = Mathf.Max(0f, headDistance - beamLength);
            }

            // Calculate alpha fade
            float alpha = 1f;
            if (isHeadArrived)
            {
                alpha = Mathf.Clamp01(1f - (headArrivalTimer / FadeDuration));
            }

            // Update line colors
            Color cMain = BrightRed;
            cMain.a = alpha;
            mainLine.startColor = cMain;
            mainLine.endColor = cMain;

            Color cCore = CoreColor;
            cCore.a = alpha;
            coreLine.startColor = cCore;
            coreLine.endColor = cCore;

            if (leadingOrb != null)
            {
                Color oCol = Color.white;
                oCol.a = alpha;
                leadingOrb.color = oCol;
            }

            if (leadingHalo != null)
            {
                Color hCol = new Color(BrightRed.r, BrightRed.g, BrightRed.b, 0.5f * alpha);
                leadingHalo.color = hCol;
            }

            UpdatePositions();

            // Destroy when completely finished and faded
            if (isHeadArrived && (tailDistance >= totalLength || headArrivalTimer >= FadeDuration))
            {
                Destroy(gameObject);
            }
        }

        private void UpdatePositions()
        {
            subPoints.Clear();

            Vector3 tailPos = SamplePosition(tailDistance);
            Vector3 headPos = SamplePosition(headDistance);

            subPoints.Add(tailPos);

            // Add all intermediate original vertices that lie strictly between tailDistance and headDistance
            for (int i = 1; i < pathPoints.Length - 1; i++)
            {
                float d = prefixLengths[i];
                if (d > tailDistance + 0.01f && d < headDistance - 0.01f)
                {
                    subPoints.Add(pathPoints[i]);
                }
            }

            if (Vector3.Distance(tailPos, headPos) > 0.01f)
            {
                subPoints.Add(headPos);
            }
            else if (subPoints.Count == 1)
            {
                subPoints.Add(headPos + new Vector3(0.001f, 0f, 0f));
            }

            mainLine.positionCount = subPoints.Count;
            coreLine.positionCount = subPoints.Count;
            for (int i = 0; i < subPoints.Count; i++)
            {
                mainLine.SetPosition(i, subPoints[i]);
                coreLine.SetPosition(i, subPoints[i]);
            }

            if (leadingOrb != null)
            {
                leadingOrb.transform.position = headPos;
            }
        }

        private Vector3 SamplePosition(float dist)
        {
            if (dist <= 0f) return pathPoints[0];
            if (dist >= totalLength) return pathPoints[pathPoints.Length - 1];

            for (int i = 0; i < segmentLengths.Length; i++)
            {
                if (dist <= prefixLengths[i + 1])
                {
                    float segDist = dist - prefixLengths[i];
                    float t = segmentLengths[i] > 0.0001f ? segDist / segmentLengths[i] : 0f;
                    return Vector3.Lerp(pathPoints[i], pathPoints[i + 1], t);
                }
            }

            return pathPoints[pathPoints.Length - 1];
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
