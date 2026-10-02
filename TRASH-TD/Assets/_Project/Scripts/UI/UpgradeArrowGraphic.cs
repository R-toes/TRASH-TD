using UnityEngine;
using UnityEngine.UI;

namespace TrashTD.UI
{
    public sealed class UpgradeArrowGraphic : Graphic
    {
        private int arrowCount;

        public void SetArrowCount(int count)
        {
            arrowCount = Mathf.Clamp(count, 0, 2);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (arrowCount == 0) return;

            Rect rect = rectTransform.rect;
            float slotWidth = rect.width / arrowCount;
            float shaftWidth = slotWidth * 0.2f;
            float shaftBottom = rect.yMin + rect.height * 0.08f;
            float shaftTop = rect.yMin + rect.height * 0.58f;
            float arrowTip = rect.yMin + rect.height * 0.94f;
            float arrowBase = rect.yMin + rect.height * 0.48f;

            for (int i = 0; i < arrowCount; i++)
            {
                float centerX = rect.xMin + slotWidth * (i + 0.5f);
                AddQuad(vertexHelper, centerX - shaftWidth * 0.5f, shaftBottom,
                    centerX + shaftWidth * 0.5f, shaftTop);

                int firstVertex = vertexHelper.currentVertCount;
                vertexHelper.AddVert(new Vector3(centerX, arrowTip), color, Vector2.zero);
                vertexHelper.AddVert(new Vector3(centerX - slotWidth * 0.38f, arrowBase), color, Vector2.zero);
                vertexHelper.AddVert(new Vector3(centerX + slotWidth * 0.38f, arrowBase), color, Vector2.zero);
                vertexHelper.AddTriangle(firstVertex, firstVertex + 1, firstVertex + 2);
            }
        }

        private void AddQuad(VertexHelper vertexHelper, float left, float bottom, float right, float top)
        {
            int firstVertex = vertexHelper.currentVertCount;
            vertexHelper.AddVert(new Vector3(left, bottom), color, Vector2.zero);
            vertexHelper.AddVert(new Vector3(left, top), color, Vector2.zero);
            vertexHelper.AddVert(new Vector3(right, top), color, Vector2.zero);
            vertexHelper.AddVert(new Vector3(right, bottom), color, Vector2.zero);
            vertexHelper.AddTriangle(firstVertex, firstVertex + 1, firstVertex + 2);
            vertexHelper.AddTriangle(firstVertex, firstVertex + 2, firstVertex + 3);
        }
    }
}