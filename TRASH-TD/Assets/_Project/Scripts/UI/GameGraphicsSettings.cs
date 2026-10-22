using System;
using UnityEngine;

namespace TrashTD.UI
{
    public enum CrtStyle
    {
        /// <summary>The whole game on a CRT: scanline beams, RGB slot mask, halation (CrtScreen.Television).</summary>
        FullTv = 0,
        /// <summary>The first, crisper CRT version (light scanlines and RGB tint) over the whole game.</summary>
        Classic = 1,
        /// <summary>No CRT at all; the picture is shown as-is (brightness still applies).</summary>
        Off = 2
    }

    /// <summary>
    /// Player graphics options (Settings → Graphics), saved in PlayerPrefs. CrtDisplay, the stage selection
    /// screen and the stage preview listen to <see cref="Changed"/> and switch live.
    /// </summary>
    public static class GameGraphicsSettings
    {
        private const string CrtStyleKey = "graphics.crtStyle";
        private const string BrightnessKey = "graphics.brightness";

        public const float MinBrightness = 0.5f;
        public const float MaxBrightness = 1.5f;
        public const float DefaultBrightness = 1f;

        private static bool loaded;
        private static CrtStyle crtStyle;
        private static float brightness;

        /// <summary>Raised after any option changes.</summary>
        public static event Action Changed;

        public static CrtStyle CrtStyle
        {
            get { Load(); return crtStyle; }
            set
            {
                Load();
                if (crtStyle == value) return;
                crtStyle = value;
                PlayerPrefs.SetInt(CrtStyleKey, (int)value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        /// <summary>Overall picture brightness multiplier (1 = as designed).</summary>
        public static float Brightness
        {
            get { Load(); return brightness; }
            set
            {
                Load();
                float clamped = Mathf.Clamp(value, MinBrightness, MaxBrightness);
                if (Mathf.Approximately(brightness, clamped)) return;
                brightness = clamped;
                PlayerPrefs.SetFloat(BrightnessKey, clamped);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlayMode()
        {
            loaded = false;
            Changed = null;
        }

        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            int style = PlayerPrefs.GetInt(CrtStyleKey, (int)CrtStyle.FullTv);
            crtStyle = Enum.IsDefined(typeof(CrtStyle), style) ? (CrtStyle)style : CrtStyle.FullTv;
            brightness = Mathf.Clamp(PlayerPrefs.GetFloat(BrightnessKey, DefaultBrightness), MinBrightness, MaxBrightness);
        }
    }
}
