using UnityEngine;

namespace TrashTD.UI
{
    /// <summary>
    /// Ensures the menu scene boots automatically without requiring manual editor setup.
    /// </summary>
    public static class MainMenuAutoLoader
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Object.FindFirstObjectByType<MainMenuController>() != null)
            {
                return;
            }

            var root = new GameObject("MainMenuController");
            root.AddComponent<MainMenuController>();
        }
    }
}
