using UnityEngine;

namespace TrashTD.Combat
{
    public sealed class CombatProjectileVisual : MonoBehaviour
    {
        private static Sprite sharedProjectileSprite;
        private static Material sharedTrailMaterial;

        private Vector3 targetPosition;
        private float speed;
        private SpriteRenderer spriteRenderer;
        private bool burstOnImpact;
        private bool isImpacting;
        private float impactTimer;

        public static void Fire(
            Vector3 start,
            Vector3 target,
            Color color,
            float speed,
            float size,
            float trailWidth,
            bool burstOnImpact = false)
        {
            GameObject projectile = new GameObject("CombatProjectileVisual");
            projectile.transform.position = start;
            projectile.transform.localScale = Vector3.one * size;

            SpriteRenderer spriteRenderer = projectile.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = GetProjectileSprite();
            spriteRenderer.color = color;
            spriteRenderer.sortingOrder = 40;

            TrailRenderer trail = projectile.AddComponent<TrailRenderer>();
            trail.time = 0.16f;
            trail.startWidth = trailWidth;
            trail.endWidth = 0f;
            trail.numCapVertices = 3;
            trail.numCornerVertices = 3;
            trail.sharedMaterial = GetTrailMaterial();
            trail.sortingOrder = 39;
            trail.colorGradient = CreateTrailGradient(color);

            CombatProjectileVisual visual = projectile.AddComponent<CombatProjectileVisual>();
            visual.targetPosition = target;
            visual.speed = speed;
            visual.spriteRenderer = spriteRenderer;
            visual.burstOnImpact = burstOnImpact;
        }

        public static void FireShotgunSpread(
            Vector3 start,
            Vector3 direction,
            float range,
            float minSpreadOffset,
            float maxSpreadOffset,
            Color color,
            float speed,
            float size,
            float trailWidth,
            int pelletCount)
        {
            if (pelletCount <= 0 || range <= 0f || direction.sqrMagnitude <= Mathf.Epsilon) return;

            direction.Normalize();
            Vector3 perpendicular = Vector3.Cross(direction, Vector3.forward).normalized;
            for (int i = 0; i < pelletCount; i++)
            {
                float normalizedPosition = pelletCount == 1
                    ? 0.5f
                    : i / (float)(pelletCount - 1);
                float lateralOffset = Mathf.Lerp(minSpreadOffset, maxSpreadOffset, normalizedPosition);
                Vector3 pelletTarget = start + direction * range + perpendicular * lateralOffset;
                Fire(start, pelletTarget, color, speed, size, trailWidth);
            }
        }

        private void Update()
        {
            if (isImpacting)
            {
                impactTimer -= Time.deltaTime;
                float progress = 1f - Mathf.Clamp01(impactTimer / 0.2f);
                transform.localScale = Vector3.one * Mathf.Lerp(0.2f, 0.55f, progress);
                Color impactColor = spriteRenderer.color;
                impactColor.a = 1f - progress;
                spriteRenderer.color = impactColor;
                if (impactTimer <= 0f)
                {
                    Destroy(gameObject);
                }
                return;
            }

            Vector3 toTarget = targetPosition - transform.position;
            float step = speed * Time.deltaTime;
            if (toTarget.sqrMagnitude <= step * step)
            {
                transform.position = targetPosition;
                if (burstOnImpact)
                {
                    isImpacting = true;
                    impactTimer = 0.2f;
                    transform.localScale = Vector3.one * 0.2f;
                }
                else
                {
                    Destroy(gameObject);
                }
                return;
            }

            transform.position += toTarget.normalized * step;
        }

        private static Sprite GetProjectileSprite()
        {
            if (sharedProjectileSprite != null) return sharedProjectileSprite;

            const int textureSize = 16;
            var texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            Vector2 center = new Vector2((textureSize - 1) * 0.5f, (textureSize - 1) * 0.5f);
            float radius = textureSize * 0.45f;
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    bool inside = (new Vector2(x, y) - center).sqrMagnitude <= radius * radius;
                    texture.SetPixel(x, y, inside ? Color.white : Color.clear);
                }
            }

            texture.Apply();
            sharedProjectileSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, textureSize, textureSize),
                new Vector2(0.5f, 0.5f),
                100f);
            return sharedProjectileSprite;
        }

        private static Material GetTrailMaterial()
        {
            if (sharedTrailMaterial != null) return sharedTrailMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            sharedTrailMaterial = new Material(shader);
            return sharedTrailMaterial;
        }

        private static Gradient CreateTrailGradient(Color color)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(color, 0f),
                    new GradientColorKey(color, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.8f, 0f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }
    }
}