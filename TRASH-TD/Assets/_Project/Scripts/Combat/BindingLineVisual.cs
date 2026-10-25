using System;
using UnityEngine;
using TrashTD.Operators;

namespace TrashTD.Combat
{
    public enum WireColorType
    {
        Red,
        Blue,
        Yellow
    }

    public static class WireColorPalette
    {
        // Sampled from Hextension pixel art and user reference:
        // Red wire: vibrant red-orange with dark red-black outline
        public static readonly Color RedCore = new Color(1.0f, 0.235f, 0.01f, 1f);       // #FF3C01
        public static readonly Color RedOutline = new Color(0.28f, 0.04f, 0.01f, 1f);

        // Blue wire: electric cobalt blue with dark navy outline
        public static readonly Color BlueCore = new Color(0.027f, 0.537f, 0.796f, 1f);    // #0789CB
        public static readonly Color BlueOutline = new Color(0.01f, 0.17f, 0.26f, 1f);

        // Yellow wire: electrical gold-yellow with dark bronze outline
        public static readonly Color YellowCore = new Color(0.95f, 0.68f, 0.05f, 1f);    // #FFB703
        public static readonly Color YellowOutline = new Color(0.28f, 0.20f, 0.01f, 1f);

        public static Color GetCoreColor(WireColorType type)
        {
            switch (type)
            {
                case WireColorType.Blue: return BlueCore;
                case WireColorType.Yellow: return YellowCore;
                default: return RedCore;
            }
        }

        public static Color GetOutlineColor(WireColorType type)
        {
            switch (type)
            {
                case WireColorType.Blue: return BlueOutline;
                case WireColorType.Yellow: return YellowOutline;
                default: return RedOutline;
            }
        }

        public static WireColorType GetRandomWireColor()
        {
            return (WireColorType)UnityEngine.Random.Range(0, 3);
        }
    }

    /// <summary>
    /// Visual representation of Hextension's wire bind.
    /// Features:
    /// - Actual physical wire look: sagged catenary curve with natural gravity and subtle slack.
    /// - Dual-layer insulated cable rendering: dark outline jacket + solid colored core.
    /// - Randomized Hextension wire colors: Red, Blue, or Yellow.
    /// - Wire originates from Hextension's arm (blue and yellow wires at right lower-mid part, mirrored when facing left).
    /// - Operator wire wraps: bold, enlarged tightly coiled wire bands wrapped around the target operator's body (reference Image 1).
    /// </summary>
    public sealed class BindingLineVisual : MonoBehaviour
    {
        private const int TetherSegments = 16;
        private static Material sharedMaterial;

        private Transform source;
        private TrashTD.Enemies.HextensionEnemy sourceHextension;
        private OperatorBase target;
        private WireColorType wireColorType;
        private Color coreColor;
        private Color outlineColor;

        // Tether lines
        private LineRenderer tetherOutline;
        private LineRenderer tetherCore;

        // Operator wraps
        private GameObject wrapsObject;
        private LineRenderer[] wrapOutlines;
        private LineRenderer[] wrapCores;

        public WireColorType CurrentWireColor => wireColorType;
        public Color CoreColor => coreColor;
        public Color OutlineColor => outlineColor;
        public GameObject WrapsObject => wrapsObject;
        public LineRenderer TetherOutline => tetherOutline;
        public LineRenderer TetherCore => tetherCore;
        public LineRenderer[] WrapOutlines => wrapOutlines;
        public LineRenderer[] WrapCores => wrapCores;

        public static BindingLineVisual Create(Transform source, OperatorBase target, WireColorType? wireColor = null)
        {
            if (source == null || target == null)
                return null;

            WireColorType chosenType = wireColor ?? WireColorPalette.GetRandomWireColor();
            Color core = WireColorPalette.GetCoreColor(chosenType);
            Color outline = WireColorPalette.GetOutlineColor(chosenType);

            return CreateInternal(source, target, chosenType, core, outline);
        }

        public static BindingLineVisual Create(Transform source, OperatorBase target, Color? legacyColor)
        {
            if (source == null || target == null)
                return null;

            if (legacyColor.HasValue)
            {
                Color c = legacyColor.Value;
                WireColorType type = WireColorType.Red;
                if (c.b > c.r && c.b > c.g) type = WireColorType.Blue;
                else if (c.r > 0.6f && c.g > 0.5f && c.b < 0.4f) type = WireColorType.Yellow;

                Color core = WireColorPalette.GetCoreColor(type);
                Color outline = WireColorPalette.GetOutlineColor(type);
                return CreateInternal(source, target, type, core, outline);
            }

            return Create(source, target, (WireColorType?)null);
        }

        private static BindingLineVisual CreateInternal(Transform source, OperatorBase target, WireColorType type, Color core, Color outline)
        {
            var rootObj = new GameObject("BindingLineVisual_Wire");
            var visual = rootObj.AddComponent<BindingLineVisual>();
            visual.source = source;
            visual.sourceHextension = source != null ? source.GetComponent<TrashTD.Enemies.HextensionEnemy>() : null;
            visual.target = target;
            visual.wireColorType = type;
            visual.coreColor = core;
            visual.outlineColor = outline;

            visual.SetupTetherLines();
            visual.SetupOperatorWraps();
            visual.UpdateVisuals();

            return visual;
        }

        private void SetupTetherLines()
        {
            Material mat = GetMaterial();

            // 1. Tether Outline (Darker jacket / outline)
            var outlineObj = new GameObject("Tether_Outline");
            outlineObj.transform.SetParent(transform, false);
            tetherOutline = outlineObj.AddComponent<LineRenderer>();
            ConfigureLineRenderer(tetherOutline, mat, TetherSegments, 0.095f, 0.08f, outlineColor, 41);

            // 2. Tether Core (Vibrant insulated wire)
            var coreObj = new GameObject("Tether_Core");
            coreObj.transform.SetParent(transform, false);
            tetherCore = coreObj.AddComponent<LineRenderer>();
            ConfigureLineRenderer(tetherCore, mat, TetherSegments, 0.06f, 0.048f, coreColor, 42);
        }

        private static void ConfigureLineRenderer(LineRenderer lr, Material mat, int segments, float startW, float endW, Color col, int sortingOrder)
        {
            lr.positionCount = segments;
            lr.useWorldSpace = true;
            lr.startWidth = startW;
            lr.endWidth = endW;
            lr.startColor = col;
            lr.endColor = col;
            lr.sortingOrder = sortingOrder;
            lr.sharedMaterial = mat;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 4;
        }

        private void SetupOperatorWraps()
        {
            if (target == null) return;

            Material mat = GetMaterial();
            wrapsObject = new GameObject("WireWraps_BoundOperator");
            wrapsObject.transform.SetParent(target.transform, false);
            wrapsObject.transform.localPosition = Vector3.zero;

            // Define 6 distinct, prominent wire wrap bands across the body (enlarged, matching reference image)
            Vector3[][] wrapPatterns = new Vector3[][]
            {
                // 1. High chest band
                new Vector3[] { new Vector3(-0.35f, 0.28f, 0f), new Vector3(0f, 0.24f, 0f), new Vector3(0.35f, 0.26f, 0f) },
                // 2. Upper diagonal wrap (top-left across chest down to mid-right)
                new Vector3[] { new Vector3(-0.38f, 0.20f, 0f), new Vector3(0.04f, 0.10f, 0f), new Vector3(0.38f, 0.00f, 0f) },
                // 3. Counter diagonal wrap (mid-left across chest up to top-right)
                new Vector3[] { new Vector3(-0.38f, 0.02f, 0f), new Vector3(-0.02f, 0.13f, 0f), new Vector3(0.36f, 0.22f, 0f) },
                // 4. Mid-torso / stomach belt wrap
                new Vector3[] { new Vector3(-0.39f, -0.04f, 0f), new Vector3(0f, -0.07f, 0f), new Vector3(0.39f, -0.05f, 0f) },
                // 5. Lower diagonal cross (mid-left down to lower-right)
                new Vector3[] { new Vector3(-0.37f, -0.10f, 0f), new Vector3(0.02f, -0.18f, 0f), new Vector3(0.37f, -0.24f, 0f) },
                // 6. Lower waist / hips band
                new Vector3[] { new Vector3(-0.36f, -0.24f, 0f), new Vector3(0f, -0.28f, 0f), new Vector3(0.36f, -0.25f, 0f) }
            };

            wrapOutlines = new LineRenderer[wrapPatterns.Length];
            wrapCores = new LineRenderer[wrapPatterns.Length];

            for (int i = 0; i < wrapPatterns.Length; i++)
            {
                var bandObj = new GameObject($"WrapBand_{i}");
                bandObj.transform.SetParent(wrapsObject.transform, false);

                // Outline (prominent, thick jacket)
                var outlineLine = bandObj.AddComponent<LineRenderer>();
                ConfigureLineRenderer(outlineLine, mat, wrapPatterns[i].Length, 0.12f, 0.12f, outlineColor, 43);
                outlineLine.useWorldSpace = false;
                for (int p = 0; p < wrapPatterns[i].Length; p++)
                {
                    outlineLine.SetPosition(p, wrapPatterns[i][p]);
                }
                wrapOutlines[i] = outlineLine;

                // Core (thick vibrant wire)
                var coreChild = new GameObject("Core");
                coreChild.transform.SetParent(bandObj.transform, false);
                var coreLine = coreChild.AddComponent<LineRenderer>();
                ConfigureLineRenderer(coreLine, mat, wrapPatterns[i].Length, 0.075f, 0.075f, coreColor, 44);
                coreLine.useWorldSpace = false;
                for (int p = 0; p < wrapPatterns[i].Length; p++)
                {
                    coreLine.SetPosition(p, wrapPatterns[i][p]);
                }
                wrapCores[i] = coreLine;
            }
        }

        public Vector3 GetSourceWireOrigin()
        {
            if (sourceHextension != null)
            {
                return sourceHextension.GetWireOriginPosition();
            }
            return source != null ? source.position : Vector3.zero;
        }

        private void Update()
        {
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (source == null || target == null || !target.IsDeployed || target.CurrentHP <= 0)
            {
                Stop();
                return;
            }

            Vector3 startPos = GetSourceWireOrigin();

            // Connect into the operator's wire wrap coils towards the incoming wire side
            float targetSideX = (startPos.x < target.transform.position.x) ? -0.32f : 0.32f;
            Vector3 endPos = target.transform.position + new Vector3(targetSideX, -0.02f, 0f);

            float dist = Vector3.Distance(startPos, endPos);
            // Realistic wire sag: proportional to distance, with subtle natural slack
            float sag = Mathf.Clamp(dist * 0.12f, 0.07f, 0.32f);

            if (tetherOutline != null && tetherCore != null)
            {
                for (int i = 0; i < TetherSegments; i++)
                {
                    float t = i / (float)(TetherSegments - 1);
                    Vector3 pt = Vector3.Lerp(startPos, endPos, t);

                    // Parabolic gravitational sag
                    float sagDrop = 4f * t * (1f - t) * sag;
                    pt.y -= sagDrop;

                    // Subtle dynamic cable sway / tension
                    float sway = Mathf.Sin(Time.time * 6f + t * 4f) * 0.012f * Mathf.Sin(t * Mathf.PI);
                    pt.y += sway;

                    tetherOutline.SetPosition(i, pt);
                    tetherCore.SetPosition(i, pt);
                }
            }
        }

        public void Stop()
        {
            Cleanup();
            if (Application.isPlaying)
            {
                Destroy(gameObject);
            }
            else
            {
                DestroyImmediate(gameObject);
            }
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            if (wrapsObject != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(wrapsObject);
                }
                else
                {
                    DestroyImmediate(wrapsObject);
                }
                wrapsObject = null;
            }
        }

        private static Material GetMaterial()
        {
            if (sharedMaterial != null) return sharedMaterial;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            sharedMaterial = new Material(shader)
            {
                name = "BindingLineMaterial"
            };
            return sharedMaterial;
        }
    }
}
