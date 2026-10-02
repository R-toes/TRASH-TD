using UnityEngine;
using UnityEngine.UI;
using TrashTD.Operators;

namespace TrashTD.UI
{
    public static class OperatorUpgradeBadge
    {
        private static readonly Color BadgeColor = new Color(1f, 0.78f, 0.2f, 1f);
        private static readonly Color BadgeBackground = new Color(0.08f, 0.07f, 0.035f, 0.94f);

        public static void Show(OperatorBase op)
        {
            if (op == null || op.Data == null) return;

            int upgradeLevels = Mathf.Clamp((int)op.CurrentRarity - (int)op.Data.baseRarity, 0, 2);
            if (upgradeLevels <= 0) return;

            GameObject badgeObject = new GameObject("OperatorUpgradeBadge", typeof(RectTransform), typeof(Canvas));
            badgeObject.transform.SetParent(op.transform, false);
            badgeObject.transform.localScale = Vector3.one * 0.008f;

            SpriteRenderer spriteRenderer = op.GetComponentInChildren<SpriteRenderer>();
            float horizontalOffset = spriteRenderer != null ? spriteRenderer.bounds.extents.x * 0.55f : 0.25f;
            float verticalOffset = spriteRenderer != null ? spriteRenderer.bounds.extents.y * 0.45f : 0.25f;
            badgeObject.transform.localPosition = new Vector3(horizontalOffset, verticalOffset, -0.1f);

            Canvas canvas = badgeObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 60;

            RectTransform badgeRect = badgeObject.GetComponent<RectTransform>();
            badgeRect.sizeDelta = new Vector2(38f, 24f);

            GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            backgroundObject.transform.SetParent(badgeObject.transform, false);
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;

            Image background = backgroundObject.GetComponent<Image>();
            background.color = BadgeBackground;
            background.raycastTarget = false;

            GameObject arrowObject = new GameObject("Arrows", typeof(RectTransform), typeof(CanvasRenderer));
            arrowObject.AddComponent<UpgradeArrowGraphic>().SetArrowCount(upgradeLevels);
            arrowObject.transform.SetParent(badgeObject.transform, false);
            RectTransform arrowRect = arrowObject.GetComponent<RectTransform>();
            arrowRect.anchorMin = Vector2.zero;
            arrowRect.anchorMax = Vector2.one;
            arrowRect.offsetMin = new Vector2(2f, 2f);
            arrowRect.offsetMax = new Vector2(-2f, -2f);
            arrowObject.GetComponent<UpgradeArrowGraphic>().color = BadgeColor;
        }
    }
}