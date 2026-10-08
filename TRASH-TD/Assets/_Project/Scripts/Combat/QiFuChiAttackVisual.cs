using System;
using UnityEngine;
using TrashTD.Enemies;

namespace TrashTD.Combat
{
    public sealed class QiFuChiAttackVisual : MonoBehaviour
    {
        private const float ImpactDuration = 0.16f;
        public const float PushbackDuration = 0.45f;
        private static Sprite pawSprite;

        private SpriteRenderer pawRenderer;
        private SpriteRenderer glowRenderer;
        private Vector3 startPosition;
        private Vector3 targetPosition;
        private Transform target;
        private float travelDuration;
        private float elapsed;
        private bool isImpacting;
        private bool isFollowingPushback;
        private Func<bool> onImpact;

        public static void Fire(Vector3 start, EnemyBase target, Func<bool> onImpact)
        {
            if (target == null)
                return;

            var visualObject = new GameObject("QiFuChiPawVisual");
            visualObject.transform.position = start;

            var visual = visualObject.AddComponent<QiFuChiAttackVisual>();
            visual.Initialize(start, target, onImpact);
        }

        private void Initialize(Vector3 start, EnemyBase enemyTarget, Func<bool> callback)
        {
            startPosition = start;
            target = enemyTarget.transform;
            targetPosition = GetFollowPosition();
            travelDuration = Mathf.Clamp(Vector3.Distance(start, targetPosition) / 12f, 0.06f, 0.14f);
            onImpact = callback;

            Vector3 direction = targetPosition - start;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
            transform.localScale = Vector3.one * 0.38f;

            glowRenderer = CreatePawLayer("ChiGlow", new Color(0.12f, 1f, 0.42f, 0.28f), 79);
            glowRenderer.transform.localScale = Vector3.one * 1.45f;
            pawRenderer = CreatePawLayer("ChiPaw", new Color(0.45f, 1f, 0.66f, 0.98f), 80);
        }

        private SpriteRenderer CreatePawLayer(string objectName, Color color, int sortingOrder)
        {
            var layerObject = new GameObject(objectName);
            layerObject.transform.SetParent(transform, false);

            var renderer = layerObject.AddComponent<SpriteRenderer>();
            renderer.sprite = GetPawSprite();
            renderer.color = color;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;

            if (!isImpacting)
            {
                float progress = Mathf.Clamp01(elapsed / travelDuration);
                transform.position = Vector3.Lerp(startPosition, targetPosition, progress);
                if (progress < 1f)
                    return;

                transform.position = targetPosition;
                isImpacting = true;
                elapsed = 0f;
                isFollowingPushback = onImpact != null && onImpact();
                onImpact = null;
                return;
            }

            if (isFollowingPushback && target != null)
            {
                transform.position = GetFollowPosition();
                if (elapsed < PushbackDuration)
                    return;

                isFollowingPushback = false;
                elapsed = 0f;
            }

            float impactProgress = Mathf.Clamp01(elapsed / ImpactDuration);
            transform.localScale = Vector3.one * Mathf.Lerp(0.38f, 0.72f, impactProgress);
            SetAlpha(pawRenderer, 1f - impactProgress);
            SetAlpha(glowRenderer, 0.28f * (1f - impactProgress));
            if (impactProgress >= 1f)
                Destroy(gameObject);
        }

        private Vector3 GetFollowPosition()
        {
            Vector3 position = target != null ? target.position : targetPosition;
            position.z -= 0.15f;
            return position;
        }

        private static void SetAlpha(SpriteRenderer renderer, float alpha)
        {
            Color color = renderer.color;
            color.a = alpha;
            renderer.color = color;
        }

        private static Sprite GetPawSprite()
        {
            if (pawSprite != null)
                return pawSprite;

            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "QiFuChiPawTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pixel = new Vector2(x, y);
                    float distance = Mathf.Min(
                        Mathf.Min(
                            Vector2.Distance(pixel, new Vector2(32f, 25f)) - 15f,
                            Vector2.Distance(pixel, new Vector2(14f, 45f)) - 7.5f),
                        Mathf.Min(
                            Mathf.Min(
                                Vector2.Distance(pixel, new Vector2(26f, 52f)) - 7.5f,
                                Vector2.Distance(pixel, new Vector2(39f, 52f)) - 7.5f),
                            Vector2.Distance(pixel, new Vector2(51f, 45f)) - 7.5f));
                    float alpha = 1f - Mathf.SmoothStep(-1f, 1f, distance);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            pawSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
            pawSprite.name = "QiFuChiPaw";
            return pawSprite;
        }
    }
}
