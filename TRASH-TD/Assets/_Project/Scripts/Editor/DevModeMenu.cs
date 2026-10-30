using UnityEditor;
using UnityEngine;
using TrashTD.Core.GameLoop;
using TrashTD.Development;

namespace TrashTD.Editor
{
    /// <summary>
    /// TRASH TD → Dev Mode toggles developer tools in the Editor (see <see cref="DevMode"/>).
    /// TRASH TD → Reset Player Progress makes this machine a first-time player again (tutorial, stage unlocks, stars).
    /// </summary>
    public static class DevModeMenu
    {
        private const string DevModeMenuPath = "TRASH TD/Dev Mode";

        [MenuItem(DevModeMenuPath, false, 0)]
        private static void ToggleDevMode()
        {
            bool enabled = !DevMode.IsEnabled;
            EditorPrefs.SetBool(DevMode.EditorPrefKey, enabled);
            Debug.Log(enabled
                ? "[DevMode] ON: QA Sandbox and the developer picker are available."
                : "[DevMode] OFF: player mode. Dev tools are hidden; re-enter the menu or press Play to see it.");
        }

        [MenuItem(DevModeMenuPath, true)]
        private static bool ToggleDevModeValidate()
        {
            Menu.SetChecked(DevModeMenuPath, DevMode.IsEnabled);
            return true;
        }

        [MenuItem("TRASH TD/Reset Player Progress", false, 1)]
        private static void ResetPlayerProgress()
        {
            if (!EditorUtility.DisplayDialog("Reset Player Progress",
                    "Clear saved progress on this machine (tutorial completion, stage clears and stars, enemies met in Intel)?\nAudio and graphics settings are kept.",
                    "Reset", "Cancel"))
                return;

            TutorialDirector.ResetTutorialProgress();
            StageProgress.ResetAll();
            EnemyIntelProgress.ResetAll();
            Debug.Log("[DevMode] Player progress reset: tutorial, stage unlocks and Intel enemy files start over (turn Dev Mode off to see the locks).");
        }
    }
}
