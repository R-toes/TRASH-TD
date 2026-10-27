using System.Collections.Generic;
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
    /// Hextension's bind wire, drawn as real pixel art: every piece is rasterised onto the stage-art pixel grid
    /// (32 px per tile) and built into a mesh of 1-pixel quads each frame, so it stays crisp next to the sprites.
    ///   - Tether: a sagging, gently swaying cable from Hextension's arm to a plug on the bound operator. 2 px core
    ///     with a 1 px dark outline, a highlight row on top and a shade row underneath. Bright pulses travel along
    ///     it toward Hextension (the drain). On bind it lashes out from Hextension to the target.
    ///   - Coils: three wire bands wrapped around the operator (ends darkened where they turn behind the body),
    ///     tightening one by one after the lash, plus the plug where the tether connects. They flash when the wire
    ///     sparks.
    ///   - Sparks crackle at the plug about once a second; when the bind ends the wire snaps with a spark burst.
    /// Colours come from <see cref="WireColorPalette"/> (red / blue / yellow per Hextension).
    /// </summary>
    public sealed class BindingLineVisual : MonoBehaviour
    {
        private const float ArtPixel = 1f / 32f;        // one stage-art pixel in world units
        private const float LashTime = 0.18f;           // tether shoots out from Hextension to the target
        private const float CoilStagger = 0.07f;        // each coil tightens a little after the previous one
        private const int TetherSamples = 28;
        private const int TetherSortingOrder = 41;      // above units, like projectiles
        private const int WrapSortingOrder = 43;        // inside the operator's sorting group, above its sprite
        private const float PulseSpeed = 70f;           // pixels per second, toward Hextension
        private const int PulseSpacing = 20;
        private const int PulseLength = 3;
        private const int PlugOffsetPx = 13;            // plug distance from the operator's centre

        // Coils across the operator's body, in its local pixel grid (sprite is 32x32 around the centre)
        private static readonly Vector2Int[][] CoilPaths =
        {
            new[] { new Vector2Int(-12, 7), new Vector2Int(0, 5), new Vector2Int(12, 4) },
            new[] { new Vector2Int(-12, -1), new Vector2Int(0, -3), new Vector2Int(12, -5) },
            new[] { new Vector2Int(-11, -9), new Vector2Int(0, -11), new Vector2Int(11, -12) }
        };

        private static readonly Vector2Int[] CoreBrush =
        {
            new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1)
        };

        private static Material sharedMaterial;

        private Transform source;
        private TrashTD.Enemies.HextensionEnemy sourceHextension;
        private OperatorBase target;
        private WireColorType wireColorType;
        private Color coreColor;
        private Color outlineColor;
        private Color highlightColor;
        private Color shadeColor;
        private Color pulseColor;

        private Mesh tetherMesh;
        private GameObject wrapsObject;
        private Mesh wrapMesh;

        private float age;
        private float sparkTimer = 0.6f;
        private float flash;
        private int plugSide = 1;
        private Vector3 lastPlugWorld;
        private Vector3 lastMidWorld;
        private bool stopped;

        // Scratch buffers reused every frame
        private readonly List<Vector2Int> path = new List<Vector2Int>(256);
        private readonly Dictionary<Vector2Int, int> core = new Dictionary<Vector2Int, int>(512);
        private readonly HashSet<Vector2Int> outline = new HashSet<Vector2Int>();
        private readonly HashSet<int> darkIndices = new HashSet<int>();
        private readonly List<Vector3> vertices = new List<Vector3>(4096);
        private readonly List<Color32> colors = new List<Color32>(4096);
        private readonly List<int> triangles = new List<int>(6144);

        public WireColorType CurrentWireColor => wireColorType;
        public Color CoreColor => coreColor;
        public Color OutlineColor => outlineColor;
        public GameObject WrapsObject => wrapsObject;

        public static BindingLineVisual Create(Transform source, OperatorBase target, WireColorType? wireColor = null)
        {
            if (source == null || target == null)
                return null;

            WireColorType chosenType = wireColor ?? WireColorPalette.GetRandomWireColor();
            return CreateInternal(source, target, chosenType);
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
                return CreateInternal(source, target, type);
            }

            return Create(source, target, (WireColorType?)null);
        }

        private static BindingLineVisual CreateInternal(Transform source, OperatorBase target, WireColorType type)
        {
            var rootObj = new GameObject("BindingLineVisual_Wire");
            var visual = rootObj.AddComponent<BindingLineVisual>();
            visual.source = source;
            visual.sourceHextension = source != null ? source.GetComponent<TrashTD.Enemies.HextensionEnemy>() : null;
            visual.target = target;
            visual.wireColorType = type;
            visual.coreColor = WireColorPalette.GetCoreColor(type);
            visual.outlineColor = WireColorPalette.GetOutlineColor(type);
            visual.highlightColor = Color.Lerp(visual.coreColor, Color.white, 0.45f);
            visual.shadeColor = Color.Lerp(visual.coreColor, visual.outlineColor, 0.45f);
            visual.pulseColor = Color.Lerp(visual.coreColor, Color.white, 0.8f);

            visual.tetherMesh = CreateMeshObject(rootObj.transform, "Tether", TetherSortingOrder, out _);
            visual.SetupOperatorWraps();
            visual.UpdateVisuals(0f);

            return visual;
        }

        private void SetupOperatorWraps()
        {
            if (target == null) return;

            wrapsObject = new GameObject("WireWraps_BoundOperator");
            wrapsObject.transform.SetParent(target.transform, false);
            wrapsObject.transform.localPosition = Vector3.zero;
            wrapMesh = CreateMeshObject(wrapsObject.transform, "Coils", WrapSortingOrder, out _);
        }

        private static Mesh CreateMeshObject(Transform parent, string name, int sortingOrder, out MeshRenderer renderer)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            var mesh = new Mesh { name = "PixelWire_" + name };
            mesh.MarkDynamic();
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetMaterial();
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return mesh;
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
            UpdateVisuals(Time.deltaTime);
        }

        private void UpdateVisuals(float dt)
        {
            if (stopped) return;
            if (source == null || target == null || !target.IsDeployed || target.CurrentHP <= 0)
            {
                Stop();
                return;
            }

            age += dt;
            flash = Mathf.Max(0f, flash - dt * 6f);

            Vector3 startPos = GetSourceWireOrigin();
            plugSide = startPos.x < target.transform.position.x ? -1 : 1;
            Vector3 plugPos = target.transform.position + new Vector3(plugSide * PlugOffsetPx * ArtPixel, 0f, 0f);
            lastPlugWorld = plugPos;
            lastMidWorld = (startPos + plugPos) * 0.5f;

            float lash = Mathf.Clamp01(age / LashTime);
            BuildTether(startPos, plugPos, 1f - (1f - lash) * (1f - lash));
            BuildCoils();

            // Crackle at the plug about once a second (the drain ticks)
            if (lash >= 1f && Application.isPlaying)
            {
                sparkTimer -= dt;
                if (sparkTimer <= 0f)
                {
                    sparkTimer = Random.Range(0.8f, 1.3f);
                    flash = 1f;
                    CombatFx.Sparks(plugPos, highlightColor, 5, 1.4f, 0.2f, gravity: 3f);
                }
            }
        }

        // ─────────────────────────────────────────────────────
        //  Tether
        // ─────────────────────────────────────────────────────

        private void BuildTether(Vector3 startWorld, Vector3 endWorld, float extent)
        {
            float distance = Vector2.Distance(startWorld, endWorld);
            float sag = Mathf.Clamp(distance * 0.12f, 0.07f, 0.32f);
            float time = Time.time;

            path.Clear();
            Vector2Int previous = default;
            int samples = Mathf.Max(2, Mathf.CeilToInt(TetherSamples * extent));
            for (int s = 0; s <= samples; s++)
            {
                float t = extent * s / samples;
                Vector3 point = Vector3.Lerp(startWorld, endWorld, t);
                point.y -= 4f * t * (1f - t) * sag;                                             // gravity sag
                point.y += Mathf.Sin(time * 5f + t * 5f) * 1.4f * ArtPixel * Mathf.Sin(t * Mathf.PI); // slow sway
                Vector2Int cell = ToPixel(point);

                if (s == 0) path.Add(cell);
                else AppendLine(previous, cell);
                previous = cell;
            }

            // Bright pulses flow along the wire toward Hextension (index 0)
            int pulseOffset = Mathf.FloorToInt(time * PulseSpeed);
            BuildDilated(path, (cell, index) =>
            {
                int phase = ((index + pulseOffset) % PulseSpacing + PulseSpacing) % PulseSpacing;
                return phase < PulseLength ? pulseColor : (Color?)null;
            });
            WriteMesh(tetherMesh);
        }

        // ─────────────────────────────────────────────────────
        //  Coils + plug (operator local pixel grid)
        // ─────────────────────────────────────────────────────

        private void BuildCoils()
        {
            if (wrapMesh == null) return;

            path.Clear();
            darkIndices.Clear();
            for (int c = 0; c < CoilPaths.Length; c++)
            {
                float tighten = Mathf.Clamp01((age - LashTime - c * CoilStagger) / 0.12f);
                if (tighten <= 0f) continue;

                // The coil wraps in from the plug side, so it grows across the body from there
                Vector2Int[] points = CoilPaths[c];
                int first = path.Count;
                for (int p = 1; p < points.Length; p++)
                {
                    Vector2Int a = Mirror(points[p - 1]);
                    Vector2Int b = Mirror(points[p]);
                    if (p == 1) path.Add(a);
                    AppendLine(a, b);
                }

                // Points run away from the plug side and end next to it, so the coil grows from the plug end
                int count = path.Count - first;
                int keep = Mathf.Max(1, Mathf.RoundToInt(count * tighten));
                path.RemoveRange(first, count - keep);

                // Ends turn behind the body: darken the 2 pixels at each end
                int start = first;
                int end = path.Count - 1;
                for (int k = 0; k < 2 && start + k <= end; k++)
                {
                    darkIndices.Add(start + k);
                    darkIndices.Add(end - k);
                }
            }

            // Plug where the tether connects (a short stub on the incoming side)
            if (age >= LashTime)
            {
                Vector2Int plug = new Vector2Int(plugSide * PlugOffsetPx, -1);
                path.Add(plug);
                path.Add(plug + new Vector2Int(plugSide, 0));
            }

            Color coilFlash = Color.Lerp(coreColor, Color.white, 0.6f * flash);
            BuildDilated(path, (cell, index) => darkIndices.Contains(index) ? shadeColor : flash > 0f ? coilFlash : (Color?)null);

            // Local mesh: pixel grid relative to the operator's centre
            WriteMesh(wrapMesh);
        }

        /// <summary>Coil points are authored for a plug on the right; mirror them when the plug is on the left.</summary>
        private Vector2Int Mirror(Vector2Int point) => plugSide > 0 ? point : new Vector2Int(-point.x, point.y);

        // ─────────────────────────────────────────────────────
        //  Rasterising
        // ─────────────────────────────────────────────────────

        private static Vector2Int ToPixel(Vector3 world)
        {
            return new Vector2Int(Mathf.FloorToInt(world.x / ArtPixel), Mathf.FloorToInt(world.y / ArtPixel));
        }

        /// <summary>Bresenham line from <paramref name="from"/> (exclusive) to <paramref name="to"/> (inclusive) into <see cref="path"/>.</summary>
        private void AppendLine(Vector2Int from, Vector2Int to)
        {
            int dx = Mathf.Abs(to.x - from.x);
            int dy = -Mathf.Abs(to.y - from.y);
            int sx = from.x < to.x ? 1 : -1;
            int sy = from.y < to.y ? 1 : -1;
            int error = dx + dy;
            int x = from.x;
            int y = from.y;

            while (x != to.x || y != to.y)
            {
                int e2 = 2 * error;
                if (e2 >= dy) { error += dy; x += sx; }
                if (e2 <= dx) { error += dx; y += sy; }
                path.Add(new Vector2Int(x, y));
            }
        }

        /// <summary>
        /// Thickens <paramref name="centreLine"/> into a 2 px core with a 1 px outline, shaded like a cable: a
        /// highlight row on top, a shade row underneath. <paramref name="overrideColor"/> can recolour core pixels
        /// (by cell and path index) for pulses and flashes.
        /// </summary>
        private void BuildDilated(List<Vector2Int> centreLine, System.Func<Vector2Int, int, Color?> overrideColor)
        {
            core.Clear();
            outline.Clear();
            vertices.Clear();
            colors.Clear();
            triangles.Clear();

            for (int i = 0; i < centreLine.Count; i++)
            {
                foreach (Vector2Int offset in CoreBrush)
                {
                    Vector2Int cell = centreLine[i] + offset;
                    if (!core.TryGetValue(cell, out int existing) || i < existing) core[cell] = i;
                }
            }

            foreach (Vector2Int cell in core.Keys)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        var neighbour = new Vector2Int(cell.x + ox, cell.y + oy);
                        if (!core.ContainsKey(neighbour)) outline.Add(neighbour);
                    }
                }
            }

            // Outline first so the core draws over it
            foreach (Vector2Int cell in outline) AddPixel(cell, outlineColor);
            foreach (KeyValuePair<Vector2Int, int> pair in core)
            {
                Vector2Int cell = pair.Key;
                Color color;
                if (!core.ContainsKey(cell + Vector2Int.up)) color = highlightColor;
                else if (!core.ContainsKey(cell + Vector2Int.down)) color = shadeColor;
                else color = coreColor;

                Color? custom = overrideColor?.Invoke(cell, pair.Value);
                if (custom.HasValue) color = custom.Value;
                AddPixel(cell, color);
            }
        }

        private void AddPixel(Vector2Int cell, Color color)
        {
            int index = vertices.Count;
            float x = cell.x * ArtPixel;
            float y = cell.y * ArtPixel;
            vertices.Add(new Vector3(x, y, 0f));
            vertices.Add(new Vector3(x, y + ArtPixel, 0f));
            vertices.Add(new Vector3(x + ArtPixel, y + ArtPixel, 0f));
            vertices.Add(new Vector3(x + ArtPixel, y, 0f));
            Color32 c = color;
            colors.Add(c);
            colors.Add(c);
            colors.Add(c);
            colors.Add(c);
            triangles.Add(index);
            triangles.Add(index + 1);
            triangles.Add(index + 2);
            triangles.Add(index);
            triangles.Add(index + 2);
            triangles.Add(index + 3);
        }

        /// <summary>
        /// Writes the current pixel buffers into <paramref name="mesh"/>. The tether lives at the world origin, so
        /// its pixels sit on the world art grid; coils are local to the operator (its centre is a pixel corner).
        /// </summary>
        private void WriteMesh(Mesh mesh)
        {
            if (mesh == null) return;
            mesh.Clear();
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        // ─────────────────────────────────────────────────────
        //  Lifetime
        // ─────────────────────────────────────────────────────

        public void Stop()
        {
            if (!stopped && Application.isPlaying && age > 0f)
            {
                // The wire snaps: sparks at the plug and along the middle
                CombatFx.Sparks(lastPlugWorld, highlightColor, 8, 2f, 0.25f, gravity: 4f);
                CombatFx.Sparks(lastMidWorld, coreColor, 6, 1.6f, 0.3f, gravity: 5f);
            }
            stopped = true;

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
            DestroyMesh(ref tetherMesh);
        }

        private void Cleanup()
        {
            DestroyMesh(ref wrapMesh);
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

        private static void DestroyMesh(ref Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
            mesh = null;
        }

        private static Material GetMaterial()
        {
            if (sharedMaterial != null) return sharedMaterial;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            sharedMaterial = new Material(shader)
            {
                name = "PixelWireMaterial"
            };
            return sharedMaterial;
        }
    }
}
