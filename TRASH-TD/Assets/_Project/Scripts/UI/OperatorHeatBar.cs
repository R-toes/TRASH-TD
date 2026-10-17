using UnityEngine;
using UnityEngine.UI;
using TrashTD.Operators;

namespace TrashTD.UI
{
    /// <summary>
    /// Displays a 5-segment heat gauge above an operator on heat-hazard stages (Stage 4).
    /// Indicates heat accumulation and pulses with a fire banner when overheated/burning.
    /// </summary>
    public sealed class OperatorHeatBar : MonoBehaviour
    {
        private const int MaxPips = 5;
        private static readonly Color EmptyPipColor = new Color(0.16f, 0.18f, 0.22f, 0.65f);
        private static readonly Color BarBgColor = new Color(0.06f, 0.07f, 0.09f, 0.95f);
        private static readonly Color[] HeatColors = new Color[]
        {
            new Color(1.0f, 0.85f, 0.20f, 1f), // 1: Warm Yellow
            new Color(1.0f, 0.65f, 0.15f, 1f), // 2: Amber
            new Color(1.0f, 0.45f, 0.10f, 1f), // 3: Orange
            new Color(1.0f, 0.25f, 0.05f, 1f), // 4: Red-Orange
            new Color(1.0f, 0.10f, 0.05f, 1f)  // 5: Blazing Crimson
        };

        private OperatorBase targetOperator;
        private Image[] pipImages = new Image[MaxPips];
        private GameObject overheatBanner;
        private Text overheatText;
        private RectTransform barRoot;
        private float verticalOffset = 0.65f;
        private int currentStacks;
        private bool isBurning;

        public static OperatorHeatBar ShowFor(OperatorBase op)
        {
            if (op == null) return null;

            OperatorHeatBar bar = op.GetComponentInChildren<OperatorHeatBar>();
            if (bar == null)
            {
                GameObject barObj = new GameObject("OperatorHeatBar");
                barObj.transform.SetParent(op.transform, false);
                bar = barObj.AddComponent<OperatorHeatBar>();
                bar.Initialize(op);
            }

            bar.UpdateDisplay(op.HeatStacks, op.IsBurning);
            return bar;
        }

        public static void RemoveFrom(OperatorBase op)
        {
            if (op == null) return;
            OperatorHeatBar bar = op.GetComponentInChildren<OperatorHeatBar>();
            if (bar != null)
            {
                Destroy(bar.gameObject);
            }
        }

        public void Initialize(OperatorBase op)
        {
            targetOperator = op;

            SpriteRenderer sr = op.GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
            {
                verticalOffset = sr.bounds.extents.y + 0.35f;
            }

            BuildUI();
        }

        private void BuildUI()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 55;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 32f;

            barRoot = GetComponent<RectTransform>();
            barRoot.sizeDelta = new Vector2(50f, 10f);
            transform.localScale = Vector3.one * 0.012f;
            transform.localPosition = new Vector3(0f, verticalOffset, -0.08f);

            // Background frame
            GameObject bgObj = new GameObject("BarBackground", typeof(RectTransform), typeof(Image));
            bgObj.transform.SetParent(transform, false);
            RectTransform bgRect = bgObj.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            Image bgImg = bgObj.GetComponent<Image>();
            bgImg.color = BarBgColor;
            bgImg.raycastTarget = false;

            Outline outline = bgObj.AddComponent<Outline>();
            outline.effectColor = new Color(0.25f, 0.28f, 0.32f, 0.8f);
            outline.effectDistance = new Vector2(1f, -1f);

            // Container for 5 pips
            GameObject pipsContainer = new GameObject("PipsContainer", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            pipsContainer.transform.SetParent(bgObj.transform, false);
            RectTransform pipsRect = pipsContainer.GetComponent<RectTransform>();
            pipsRect.anchorMin = Vector2.zero;
            pipsRect.anchorMax = Vector2.one;
            pipsRect.offsetMin = new Vector2(2f, 2f);
            pipsRect.offsetMax = new Vector2(-2f, -2f);

            HorizontalLayoutGroup layout = pipsContainer.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 1.5f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            for (int i = 0; i < MaxPips; i++)
            {
                GameObject pipObj = new GameObject($"Pip_{i}", typeof(RectTransform), typeof(Image));
                pipObj.transform.SetParent(pipsContainer.transform, false);
                Image pipImg = pipObj.GetComponent<Image>();
                pipImg.color = EmptyPipColor;
                pipImg.raycastTarget = false;
                pipImages[i] = pipImg;
            }

            // Overheat warning text
            overheatBanner = new GameObject("OverheatBanner", typeof(RectTransform));
            overheatBanner.transform.SetParent(transform, false);
            RectTransform bannerRect = overheatBanner.GetComponent<RectTransform>();
            bannerRect.anchorMin = new Vector2(0.5f, 1f);
            bannerRect.anchorMax = new Vector2(0.5f, 1f);
            bannerRect.pivot = new Vector2(0.5f, 0f);
            bannerRect.anchoredPosition = new Vector2(0f, 3f);
            bannerRect.sizeDelta = new Vector2(80f, 14f);

            GameObject textObj = new GameObject("OverheatText", typeof(RectTransform), typeof(Text));
            textObj.transform.SetParent(overheatBanner.transform, false);
            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            overheatText = textObj.GetComponent<Text>();
            overheatText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            overheatText.text = "BURNING";
            overheatText.fontSize = 9;
            overheatText.alignment = TextAnchor.MiddleCenter;
            overheatText.color = new Color(1f, 0.25f, 0.05f, 1f);
            overheatText.fontStyle = FontStyle.Bold;
            overheatText.raycastTarget = false;

            Outline textOutline = textObj.AddComponent<Outline>();
            textOutline.effectColor = new Color(0.1f, 0f, 0f, 1f);
            textOutline.effectDistance = new Vector2(1f, -1f);

            overheatBanner.SetActive(false);
        }

        public void UpdateDisplay(int stacks, bool burning)
        {
            currentStacks = Mathf.Clamp(stacks, 0, MaxPips);
            isBurning = burning;

            for (int i = 0; i < MaxPips; i++)
            {
                if (pipImages[i] == null) continue;

                if (i < currentStacks)
                {
                    pipImages[i].color = isBurning ? HeatColors[MaxPips - 1] : HeatColors[i];
                }
                else
                {
                    pipImages[i].color = EmptyPipColor;
                }
            }

            if (overheatBanner != null)
            {
                overheatBanner.SetActive(isBurning);
            }
        }

        private void Update()
        {
            if (targetOperator == null || !targetOperator.IsDeployed)
            {
                Destroy(gameObject);
                return;
            }

            // Pulsing effect when burning
            if (isBurning && overheatText != null)
            {
                float pulse = 0.8f + 0.2f * Mathf.Sin(Time.time * 9f);
                Color c = new Color(1f, 0.15f + 0.15f * pulse, 0.05f, 1f);
                overheatText.color = c;

                for (int i = 0; i < MaxPips; i++)
                {
                    if (pipImages[i] != null)
                    {
                        pipImages[i].color = Color.Lerp(HeatColors[3], HeatColors[4], pulse);
                    }
                }
            }
        }
    }
}
