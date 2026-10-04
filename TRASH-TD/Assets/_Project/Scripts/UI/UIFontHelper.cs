using UnityEngine;
using UnityEngine.UI;

namespace TrashTD.UI
{
    public static class UIFontHelper
    {
        private const string PixelFontPath = "Fonts/PressStart2P-Regular";
        private static Font cachedFont;

        public static Font GetPixelFont()
        {
            if (cachedFont == null)
            {
                cachedFont = Resources.Load<Font>(PixelFontPath);
            }

            if (cachedFont == null)
            {
                Debug.LogError($"Could not load UI font at Resources/{PixelFontPath}.ttf.");
                return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            return cachedFont;
        }

        public static void Apply(Text text, FontStyle style = FontStyle.Normal)
        {
            if (text == null)
            {
                return;
            }

            text.font = GetPixelFont();
            text.fontStyle = style;
        }

        public static void Apply(TextMesh textMesh, FontStyle style = FontStyle.Normal)
        {
            if (textMesh == null)
            {
                return;
            }

            Font font = GetPixelFont();
            textMesh.font = font;
            textMesh.fontStyle = style;

            MeshRenderer meshRenderer = textMesh.GetComponent<MeshRenderer>();
            if (meshRenderer != null && font.material != null)
            {
                meshRenderer.sharedMaterial = font.material;
            }
        }
    }
}
