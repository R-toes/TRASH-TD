using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Combat
{
    public sealed class FloatingCombatNumber : MonoBehaviour
    {
        private const float Lifetime = 0.9f;
        private const float RiseDistance = 0.55f;

        private TextMesh label;
        private Vector3 startPosition;
        private Color baseColor;
        private float elapsed;

        public static void Show(Vector3 position, int amount, DamageType damageType, bool healing = false)
        {
            if (amount <= 0) return;

            GameObject numberObject = new GameObject("FloatingCombatNumber");
            numberObject.transform.position = position + Vector3.up * 0.35f + Vector3.right * Random.Range(-0.12f, 0.12f);

            TextMesh text = numberObject.AddComponent<TextMesh>();
            text.text = healing ? $"+{amount}" : $"-{amount}";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 48;
            text.characterSize = 0.075f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontStyle = FontStyle.Bold;
            text.color = healing
                ? new Color(0.25f, 1f, 0.38f)
                : damageType == DamageType.Arts ? new Color(0.72f, 0.38f, 1f) : Color.white;

            MeshRenderer meshRenderer = numberObject.GetComponent<MeshRenderer>();
            meshRenderer.sortingOrder = 50;

            FloatingCombatNumber animation = numberObject.AddComponent<FloatingCombatNumber>();
            animation.Initialize(text);
        }

        private void Initialize(TextMesh text)
        {
            label = text;
            startPosition = transform.position;
            baseColor = label.color;
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / Lifetime);
            transform.position = startPosition + Vector3.up * (RiseDistance * normalizedTime);
            label.color = new Color(baseColor.r, baseColor.g, baseColor.b, 1f - normalizedTime);

            if (elapsed >= Lifetime)
            {
                Destroy(gameObject);
            }
        }
    }
}