using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Combat
{
    /// <summary>
    /// Seamless connected fading moving pixel-art line visual for Progeny's chain heal.
    /// Draws a continuous multi-segment glowing pixel-art line (rasterized onto the 32 PPU grid
    /// using Bresenham's line algorithm into 1-pixel quads) that streams from Progeny's red pod
    /// through each bounce target in sequence.
    /// A white-hot core (CoreColor) is bordered by a 1px vivid red glow (BrightRed), with a leading
    /// ArtsOrb at the head. As the leading head reaches each target, the onReachPoint callback
    /// triggers the heal and restorative cross splash particles.
    /// </summary>
    public sealed class ProgenyHealTetherVisual : MonoBehaviour
    {
        public const float ArtPixel = 1f / 32f; // one stage-art pixel in world units (32 PPU)
        public const float DefaultLifetime = 0.35f;
        public const float TravelSpeed = 16f; // units per second
        public const float FadeDuration = 0.66f; // fade after head arrives (x3 slower so it doesn't fade too fast at 3x game speed)
        public const float DefaultWidth = 0.07f; // legacy width reference (~2.24 art pixels)

        // Darker rich red palette matching Progeny's pods (#CC3636 and #761F28) — no pale/pink washed-out tones
        public static readonly Color BrightRed = new Color(0.85f, 0.12f, 0.16f, 1f);
        public static readonly Color DarkRed = new Color(0.46f, 0.08f, 0.11f, 1f);
        public static readonly Color CoreColor = new Color(0.85f, 0.12f, 0.16f, 1f);

        private static readonly Vector2Int[] CardinalNeighbors = new[]
        {
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0)
        };

        private static Material sharedMaterial;

        private Mesh lineMesh;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private SpriteRenderer leadingOrb;
        private SpriteRenderer leadingHalo;

        private Color baseColor = BrightRed;
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

        // Reused scratch buffers to eliminate garbage collection allocations
        private readonly List<Vector3> subPoints = new List<Vector3>(16);
        private readonly HashSet<Vector2Int> corePixels = new HashSet<Vector2Int>();
        private readonly HashSet<Vector2Int> borderPixels = new HashSet<Vector2Int>();
        private readonly List<Vector3> vertices = new List<Vector3>(1024);
        private readonly List<Color32> colors = new List<Color32>(1024);
        private readonly List<int> triangles = new List<int>(1536);

        public MeshRenderer LineMeshRenderer => meshRenderer;
        public Mesh LineMesh => lineMesh;
        public SpriteRenderer LeadingOrb => leadingOrb;
        public SpriteRenderer LeadingHalo => leadingHalo;
        public bool IsHeadArrived => isHeadArrived;
        public float HeadDistance => headDistance;
        public float TailDistance => tailDistance;
        public float TotalLength => totalLength;

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

        public void Initialize(Vector3[] points, Action<int>[] callbacks, Color color)
        {
            baseColor = color;
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

            // Dynamic pixel-art line mesh (1-pixel quads on the 32 PPU grid)
            GameObject meshObj = new GameObject("PixelLineMesh");
            meshObj.transform.SetParent(transform, false);
            meshFilter = meshObj.AddComponent<MeshFilter>();
            meshRenderer = meshObj.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = mat;
            meshRenderer.sortingOrder = 80;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            lineMesh = new Mesh { name = "ProgenyPixelLine" };
            lineMesh.MarkDynamic();
            meshFilter.sharedMesh = lineMesh;

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

            UpdatePositions(1f);
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
                float catchUpSpeed = beamLength / FadeDuration;
                tailDistance = Mathf.Min(totalLength, tailDistance + catchUpSpeed * dt);
            }
            else
            {
                tailDistance = Mathf.Max(0f, headDistance - beamLength);
            }

            // Calculate alpha fade (x3 slower)
            float alpha = 1f;
            if (isHeadArrived)
            {
                alpha = Mathf.Clamp01(1f - (headArrivalTimer / FadeDuration));
            }

            // Update leading orb & halo alpha
            if (leadingOrb != null)
            {
                leadingOrb.color = new Color(1f, 1f, 1f, alpha);
            }

            if (leadingHalo != null)
            {
                leadingHalo.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.5f * alpha);
            }

            UpdatePositions(alpha);

            // Destroy when completely finished and faded
            if (isHeadArrived && headArrivalTimer >= FadeDuration)
            {
                if (Application.isPlaying)
                {
                    Destroy(gameObject);
                }
                else
                {
                    DestroyImmediate(gameObject);
                }
            }
        }

        public void UpdatePositions(float alpha)
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

            if (Vector3.Distance(tailPos, headPos) > 0.005f)
            {
                subPoints.Add(headPos);
            }

            // Rasterize into pixel grid
            corePixels.Clear();
            borderPixels.Clear();
            vertices.Clear();
            colors.Clear();
            triangles.Clear();

            if (subPoints.Count >= 2)
            {
                for (int i = 0; i < subPoints.Count - 1; i++)
                {
                    Vector2Int from = ToPixel(subPoints[i]);
                    Vector2Int to = ToPixel(subPoints[i + 1]);
                    AppendBresenham(from, to, corePixels);
                }

                // Generate 1-pixel border around core pixels
                foreach (Vector2Int cell in corePixels)
                {
                    for (int k = 0; k < CardinalNeighbors.Length; k++)
                    {
                        Vector2Int neighbor = cell + CardinalNeighbors[k];
                        if (!corePixels.Contains(neighbor))
                        {
                            borderPixels.Add(neighbor);
                        }
                    }
                }

                // Outline / border quads (drawn first) - darker crimson red shade (no pale tones)
                Color bColor = new Color(baseColor.r * 0.52f, baseColor.g * 0.52f, baseColor.b * 0.52f, baseColor.a * alpha);
                foreach (Vector2Int cell in borderPixels)
                {
                    AddPixelQuad(cell, bColor);
                }

                // Inner core quads (drawn over outline) - pure rich red
                Color cColor = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * alpha);
                foreach (Vector2Int cell in corePixels)
                {
                    AddPixelQuad(cell, cColor);
                }
            }

            WriteMesh(lineMesh);

            // Update leading orb position snapped to art pixel grid
            if (leadingOrb != null)
            {
                Vector2Int headPixel = ToPixel(headPos);
                leadingOrb.transform.position = new Vector3(
                    (headPixel.x + 0.5f) * ArtPixel,
                    (headPixel.y + 0.5f) * ArtPixel,
                    -0.16f);
            }
        }

        private static Vector2Int ToPixel(Vector3 world)
        {
            return new Vector2Int(Mathf.FloorToInt(world.x / ArtPixel), Mathf.FloorToInt(world.y / ArtPixel));
        }

        private static void AppendBresenham(Vector2Int from, Vector2Int to, HashSet<Vector2Int> set)
        {
            int dx = Mathf.Abs(to.x - from.x);
            int dy = -Mathf.Abs(to.y - from.y);
            int sx = from.x < to.x ? 1 : -1;
            int sy = from.y < to.y ? 1 : -1;
            int error = dx + dy;
            int x = from.x;
            int y = from.y;

            while (true)
            {
                set.Add(new Vector2Int(x, y));
                if (x == to.x && y == to.y) break;
                int e2 = 2 * error;
                if (e2 >= dy) { error += dy; x += sx; }
                if (e2 <= dx) { error += dx; y += sy; }
            }
        }

        private void AddPixelQuad(Vector2Int cell, Color color)
        {
            int index = vertices.Count;
            float x = cell.x * ArtPixel;
            float y = cell.y * ArtPixel;

            vertices.Add(new Vector3(x, y, -0.15f));
            vertices.Add(new Vector3(x, y + ArtPixel, -0.15f));
            vertices.Add(new Vector3(x + ArtPixel, y + ArtPixel, -0.15f));
            vertices.Add(new Vector3(x + ArtPixel, y, -0.15f));

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

        private void OnDestroy()
        {
            if (lineMesh != null)
            {
                if (Application.isPlaying) Destroy(lineMesh);
                else DestroyImmediate(lineMesh);
                lineMesh = null;
            }
        }

        private static Material GetSharedMaterial()
        {
            if (sharedMaterial != null) return sharedMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            sharedMaterial = new Material(shader)
            {
                name = "ProgenyPixelLineMaterial"
            };
            return sharedMaterial;
        }
    }
}
