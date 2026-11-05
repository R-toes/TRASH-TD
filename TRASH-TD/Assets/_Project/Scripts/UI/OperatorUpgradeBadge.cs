using UnityEngine;
using UnityEngine.UI;
using TrashTD.Core.Grid;
using TrashTD.Operators;

namespace TrashTD.UI
{
    public static class OperatorUpgradeBadge
    {
        private static readonly Color BadgeColor = new Color(1f, 0.78f, 0.2f, 1f);
        private static readonly Color BadgeBackground = new Color(0.08f, 0.07f, 0.035f, 0.94f);

        private const string BadgeObjectName = "OperatorUpgradeBadge";

        public static void Show(OperatorBase op)
        {
            if (op == null || op.Data == null) return;

            Transform existing = op.transform.Find(BadgeObjectName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            int upgradeLevels = Mathf.Clamp((int)op.CurrentRarity - (int)op.Data.baseRarity, 0, 2);
            if (upgradeLevels <= 0) return;

            GameObject badgeObject = new GameObject(BadgeObjectName, typeof(RectTransform), typeof(Canvas));
            badgeObject.transform.SetParent(op.transform, false);
            badgeObject.transform.localScale = Vector3.one * 0.008f;

            float horizontalOffset = op.BodyExtents.x * 0.55f;
            float verticalOffset = op.BodyExtents.y * 0.45f;
            badgeObject.transform.localPosition = new Vector3(horizontalOffset, verticalOffset, -0.1f);

            Canvas canvas = badgeObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 60;
            GridSorting.SortAboveUnits(badgeObject, 60);

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