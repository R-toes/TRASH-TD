using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Combat
{
    /// <summary>
    /// Short-lived indicator that shows the exact area hit by an AoE attack.
    /// Circle blasts match world-radius splash; cell blasts match grid-cell splash.
    /// </summary>
    public sealed class AoeBlastVisual : MonoBehaviour
    {
        private const float Lifetime = 0.35f;
        private const float ScaleInPortion = 0.2f;
        private const float StartScale = 0.85f;
        private const int SortingOrder = 25;

        private static Sprite sharedCircleSprite;
        private static Sprite sharedCellSprite;

        private readonly List<SpriteRenderer> renderers = new List<SpriteRenderer>();
        private Color blastColor;
        private float elapsed;

        public static void PlayCircle(Vector3 center, float radius, Color color)
        {
            if (radius <= 0f) return;

            GameObject visualObject = new GameObject("AoeBlastVisual");
            visualObject.transform.position = center;
            AoeBlastVisual visual = visualObject.AddComponent<AoeBlastVisual>();
            visual.blastColor = color;
            visual.AddPiece(Vector3.zero, radius * 2f, GetCircleSprite());
            visual.ApplyAnimation();
        }

        public static void PlayCells(IList<Vector3> cellCenters, float cellSize, Color color)
        {
            if (cellCenters == null || cellCenters.Count == 0 || cellSize <= 0f) return;

            Vector3 center = Vector3.zero;
            for (int i = 0; i < cellCenters.Count; i++)
                center += cellCenters[i];
            center /= cellCenters.Count;

            GameObject visualObject = new GameObject("AoeBlastVisual");
            visualObject.transform.position = center;
            AoeBlastVisual visual = visualObject.AddComponent<AoeBlastVisual>();
            visual.blastColor = color;
            for (int i = 0; i < cellCenters.Count; i++)
                visual.AddPiece(cellCenters[i] - center, cellSize, GetCellSprite());
            visual.ApplyAnimation();
        }

        private void AddPiece(Vector3 localPosition, float size, Sprite sprite)
        {
            GameObject piece = new GameObject("BlastPiece");
            piece.transform.SetParent(transform, false);
            piece.transform.localPosition = localPosition;
            piece.transform.localScale = Vector3.one * size;

            SpriteRenderer spriteRenderer = piece.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            spriteRenderer.sortingOrder = SortingOrder;
            renderers.Add(spriteRenderer);
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            ApplyAnimation();
            if (elapsed >= Lifetime)
                Destroy(gameObject);
        }

        private void ApplyAnimation()
        {
            float progress = Mathf.Clamp01(elapsed / Lifetime);
            float scaleProgress = Mathf.Clamp01(progress / ScaleInPortion);
            transform.localScale = Vector3.one * Mathf.Lerp(StartScale, 1f, scaleProgress);

            float fadeProgress = Mathf.Clamp01((progress - ScaleInPortion) / (1f - ScaleInPortion));
            Color color = blastColor;
            color.a = blastColor.a * (1f - fadeProgress);
            for (int i = 0; i < renderers.Count; i++)
                renderers[i].color = color;
        }

        private static Sprite GetCircleSprite()
        {
            if (sharedCircleSprite != null) return sharedCircleSprite;

            const int textureSize = 64;
            const float rimWidth = 3f;
            var texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            Vector2 center = new Vector2((textureSize - 1) * 0.5f, (textureSize - 1) * 0.5f);
            float radius = textureSize * 0.5f - 0.5f;
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    float distance = (new Vector2(x, y) - center).magnitude;
                    float alpha;
                    if (distance > radius) alpha = 0f;
                    else if (distance > radius - rimWidth) alpha = 1f;
                    else alpha = Mathf.Lerp(0.15f, 0.4f, distance / radius);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            sharedCircleSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, textureSize, textureSize),
                new Vector2(0.5f, 0.5f),
                textureSize);
            return sharedCircleSprite;
        }

        private static Sprite GetCellSprite()
        {
            if (sharedCellSprite != null) return sharedCellSprite;

            const int textureSize = 32;
            const int borderWidth = 2;
            var texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    bool isBorder = x < borderWidth || y < borderWidth ||
                                    x >= textureSize - borderWidth || y >= textureSize - borderWidth;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, isBorder ? 1f : 0.35f));
                }
            }

            texture.Apply();
            sharedCellSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, textureSize, textureSize),
                new Vector2(0.5f, 0.5f),
                textureSize);
            return sharedCellSprite;
        }
    }
}
