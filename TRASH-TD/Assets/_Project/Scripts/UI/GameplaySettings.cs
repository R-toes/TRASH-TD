using UnityEngine;

namespace TrashTD.UI
{
    /// <summary>
    /// Player gameplay options (Settings → Gameplay), saved in PlayerPrefs.
    /// </summary>
    public static class GameplaySettings
    {
        private const string OpeningRerollNoticeKey = "gameplay.openingRerollNotice";

        /// <summary>
        /// Warn before rerolling the opening draft, which only deals 1★ operators (CardDraftOverlayUI). The
        /// notice's "don't show again" box turns it off; Settings → Gameplay turns it back on.
        /// </summary>
        public static bool ShowOpeningRerollNotice
        {
            get => PlayerPrefs.GetInt(OpeningRerollNoticeKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(OpeningRerollNoticeKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
    }
}
