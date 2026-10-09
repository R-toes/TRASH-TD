using UnityEngine;

namespace TrashTD.Combat
{
    public sealed class SandHulkSlamVisual : MonoBehaviour
    {
        private const float LifetimeSeconds = 0.52f;
        private static Sprite slamSprite;

        private SpriteRenderer spriteRenderer;
        private float elapsed;
        private Color sandColor;

        public static void Play(Vector3 center, Vector2Int direction, float cellSize, int tileCount)
        {
            if (direction == Vector2Int.zero || cellSize <= 0f || tileCount <= 0)
                return;

            var visualObject = new GameObject("SandHulkSlamVisual");
            visualObject.transform.position = center + Vector3.back * 0.2f;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            visualObject.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            visualObject.transform.localScale = new Vector3(
                cellSize,
                cellSize * tileCount * 0.75f,
                1f);

            var visual = visualObject.AddComponent<SandHulkSlamVisual>();
            visual.Initialize();
        }

        private void Initialize()
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = GetSlamSprite();
            spriteRenderer.sortingOrder = 56;
            sandColor = Color.white;
            spriteRenderer.color = sandColor;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            if (elapsed >= LifetimeSeconds)
            {
                Destroy(gameObject);
                return;
            }

            float fade = Mathf.Clamp01(elapsed / LifetimeSeconds);
            Color color = sandColor;
            color.a *= 1f - fade;
            spriteRenderer.color = color;
        }

        private static Sprite GetSlamSprite()
        {
            if (slamSprite != null)
                return slamSprite;

            const int width = 96;
            const int height = 128;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Sand Hulk Slam Texture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float horizontal = (x + 0.5f) / width * 2f - 1f;
                    float vertical = (y + 0.5f) / height * 2f - 1f;
                    float front = 0.48f - horizontal * horizontal * 0.48f;
                    float behindFront = front - vertical;
                    float edgeFade = Mathf.Clamp01((1f - Mathf.Abs(horizontal)) * 4f);
                    float dustNoise = PixelNoise(x / 3 + 29, y / 4 + 71);
                    float dustBody = Mathf.Clamp01((0.38f - behindFront) * 3f) *
                        Mathf.Clamp01((behindFront + 0.03f) * 10f);
                    float frontRidge = Mathf.Clamp01(
                        1f - Mathf.Abs(behindFront - 0.015f) / 0.11f);
                    float innerRidge = Mathf.Clamp01(
                        1f - Mathf.Abs(behindFront - 0.16f) / 0.06f) * 0.7f;
                    float outerRidge = Mathf.Clamp01(
                        1f - Mathf.Abs(behindFront - 0.29f) / 0.05f) * 0.45f;
                    float opacity = Mathf.Max(
                        dustBody * (0.62f + dustNoise * 0.25f),
                        Mathf.Max(frontRidge, Mathf.Max(innerRidge, outerRidge)));
                    opacity *= edgeFade;
                    opacity = Mathf.Clamp01(opacity * 1.35f);
                    if (opacity <= 0.01f)
                    {
                        texture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    Color sand = dustNoise < 0.35f
                        ? new Color(0.72f, 0.49f, 0.24f, opacity)
                        : dustNoise < 0.72f
                            ? new Color(0.86f, 0.65f, 0.36f, opacity)
                            : new Color(0.98f, 0.79f, 0.48f, opacity);
                    if (frontRidge > 0.65f)
                        sand = Color.Lerp(sand, new Color(1f, 0.88f, 0.59f, opacity), 0.65f);
                    texture.SetPixel(x, y, sand);
                }
            }

            texture.Apply();
            slamSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                64f);
            return slamSprite;
        }

        private static float PixelNoise(int x, int y)
        {
            unchecked
            {
                uint hash = (uint)(x * 374761393 + y * 668265263);
                hash = (hash ^ (hash >> 13)) * 1274126177;
                return (hash ^ (hash >> 16)) / (float)uint.MaxValue;
            }
        }
    }
}
