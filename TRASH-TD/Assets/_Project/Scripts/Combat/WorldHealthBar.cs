using UnityEngine;

namespace TrashTD.Combat
{
    [DisallowMultipleComponent]
    public sealed class WorldHealthBar : MonoBehaviour
    {
        private const float BarWidth = 0.8f;
        private const float BarThickness = 0.055f;
        private const float BarOffset = 0.15f;

        private static Material sharedMaterial;

        private LineRenderer background;
        private LineRenderer fill;
        private float verticalOffset = 0.55f;
        private float healthRatio = 1f;
        private int sortingLayerId;
        private int sortingOrder = 30;

        public static void UpdateFor(GameObject target, int currentHP, int maxHP)
        {
            if (target == null || maxHP <= 0) return;

            WorldHealthBar healthBar = target.GetComponent<WorldHealthBar>();
            if (healthBar == null) healthBar = target.AddComponent<WorldHealthBar>();
            healthBar.UpdateHealth(currentHP, maxHP);
        }

        private void Awake()
        {
            SpriteRenderer spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                verticalOffset = spriteRenderer.bounds.extents.y + BarOffset;
                sortingLayerId = spriteRenderer.sortingLayerID;
                sortingOrder = Mathf.Max(sortingOrder, spriteRenderer.sortingOrder + 1);
            }

            background = CreateLine("HealthBarBackground", new Color(0.035f, 0.045f, 0.05f, 0.95f), BarThickness);
            fill = CreateLine("HealthBarFill", Color.green, BarThickness * 0.62f);
            fill.sortingOrder = sortingOrder + 1;
        }

        private void LateUpdate()
        {
            if (background == null || !background.gameObject.activeSelf) return;

            Vector3 center = transform.position + Vector3.up * verticalOffset;
            center.z = transform.position.z - 0.05f;
            background.SetPosition(0, center + Vector3.left * (BarWidth * 0.5f));
            background.SetPosition(1, center + Vector3.right * (BarWidth * 0.5f));

            Vector3 fillStart = center + Vector3.left * (BarWidth * 0.5f);
            fill.SetPosition(0, fillStart);
            fill.SetPosition(1, fillStart + Vector3.right * (BarWidth * healthRatio));
        }

        private LineRenderer CreateLine(string objectName, Color color, float width)
        {
            GameObject lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(transform, false);

            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            line.numCapVertices = 2;
            line.sortingLayerID = sortingLayerId;
            line.sortingOrder = sortingOrder;
            line.sharedMaterial = GetSharedMaterial();
            line.gameObject.SetActive(false);
            return line;
        }

        private void UpdateHealth(int currentHP, int maxHP)
        {
            if (currentHP >= maxHP)
            {
                background.gameObject.SetActive(false);
                fill.gameObject.SetActive(false);
                return;
            }

            healthRatio = Mathf.Clamp01((float)currentHP / maxHP);
            fill.startColor = GetHealthColor(healthRatio);
            fill.endColor = fill.startColor;
            background.gameObject.SetActive(true);
            fill.gameObject.SetActive(true);
        }

        private static Color GetHealthColor(float ratio)
        {
            if (ratio <= 0.3f) return new Color(1f, 0.2f, 0.18f);
            if (ratio <= 0.6f) return new Color(1f, 0.75f, 0.15f);
            return new Color(0.25f, 0.95f, 0.36f);
        }

        private static Material GetSharedMaterial()
        {
            if (sharedMaterial != null) return sharedMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            sharedMaterial = new Material(shader);
            if (sharedMaterial.HasProperty("_Color")) sharedMaterial.SetColor("_Color", Color.white);
            if (sharedMaterial.HasProperty("_BaseColor")) sharedMaterial.SetColor("_BaseColor", Color.white);
            return sharedMaterial;
        }
    }
}