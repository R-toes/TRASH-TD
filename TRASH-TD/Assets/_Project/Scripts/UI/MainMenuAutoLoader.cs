using UnityEngine;
using UnityEngine.SceneManagement;
using TrashTD.Core.GameLoop;

namespace TrashTD.UI
{
    /// <summary>
    /// Ensures the game opens on the title screen without requiring manual editor setup.
    /// </summary>
    public static class MainMenuAutoLoader
    {
        private const string MenuSceneName = "MainMenu";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Object.FindAnyObjectByType<MainMenuController>() != null)
            {
                return;
            }

            // Play was pressed in a scene that runs a stage (it has a StageBootstrapper, e.g. GameplayTest in the editor).
            // Going to the title screen means loading the real menu scene. Creating the menu inside this scene let the
            // stage start underneath it, so its Card Pick draft showed over the title screen before any stage was chosen.
            StageBootstrapper[] stages = Object.FindObjectsByType<StageBootstrapper>();
            if (stages.Length > 0)
            {
                foreach (StageBootstrapper stage in stages) stage.enabled = false; // so no stage starts before the switch
                SceneManager.LoadScene(MenuSceneName);
                return;
            }

            var root = new GameObject("MainMenuController");
            root.AddComponent<MainMenuController>();
        }
    }
}
