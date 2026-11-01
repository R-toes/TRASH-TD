using System;
using UnityEngine;

namespace TrashTD.UI
{
    /// <summary>
    /// How an operator is put down once its card is on a tile (dragged there, or the card clicked and then a tile).
    /// Values are saved, so don't reorder them.
    /// </summary>
    public enum PlacementControlScheme
    {
        /// <summary>Arrow buttons around the tile set the facing; PLACE confirms and CANCEL backs out.</summary>
        Precise = 0,
        /// <summary>Hold on the operator and drag toward the facing; letting go places it (the original controls).</summary>
        Swipe = 1,
        /// <summary>Arrow buttons around the tile; pressing one sets the facing and places the operator at once.</summary>
        Quick = 2
    }

    /// <summary>
    /// Player control options (Settings → Controls), saved in PlayerPrefs. StageBootstrapper and the HUD read the
    /// scheme whenever a placement starts, so a change applies to the next operator placed.
    /// </summary>
    public static class ControlSettings
    {
        private const string SchemeKey = "controls.placementScheme";
        private const string ChosenKey = "controls.placementSchemeChosen";

        public const PlacementControlScheme DefaultScheme = PlacementControlScheme.Swipe;

        private static bool loaded;
        private static PlacementControlScheme scheme;

        /// <summary>Raised after the scheme changes.</summary>
        public static event Action Changed;

        public static PlacementControlScheme Scheme
        {
            get { Load(); return scheme; }
            set
            {
                Load();
                PlayerPrefs.SetInt(ChosenKey, 1);
                if (scheme == value)
                {
                    PlayerPrefs.Save();
                    return;
                }
                scheme = value;
                PlayerPrefs.SetInt(SchemeKey, (int)value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        /// <summary>The player has picked a scheme (in the tutorial or Settings) at least once.</summary>
        public static bool HasChosen => PlayerPrefs.GetInt(ChosenKey, 0) == 1;

        public static string GetName(PlacementControlScheme value) => value switch
        {
            PlacementControlScheme.Precise => "PRECISE",
            PlacementControlScheme.Quick => "QUICK",
            _ => "SWIPE"
        };

        public static string GetDescription(PlacementControlScheme value) => value switch
        {
            PlacementControlScheme.Precise => "Tap an arrow to face, then PLACE. CANCEL backs out.",
            PlacementControlScheme.Quick => "Tap an arrow: it faces that way and goes down at once.",
            _ => "Hold the operator and drag toward its facing. Let go to place."
        };

        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            int saved = PlayerPrefs.GetInt(SchemeKey, (int)DefaultScheme);
            scheme = Enum.IsDefined(typeof(PlacementControlScheme), saved) ? (PlacementControlScheme)saved : DefaultScheme;
        }
    }
}
