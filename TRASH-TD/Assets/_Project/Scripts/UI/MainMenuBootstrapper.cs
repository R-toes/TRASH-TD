using UnityEngine;
using UnityEngine.SceneManagement;

namespace TrashTD.UI
{
    /// <summary>
    /// Bootstraps the menu scene and ensures a menu is present when the scene loads.
    /// </summary>
    public class MainMenuBootstrapper : MonoBehaviour
    {
        private void Awake()
        {
            if (FindFirstObjectByType<MainMenuController>() == null)
            {
                var controller = new GameObject("MainMenuController");
                controller.AddComponent<MainMenuController>();
            }
        }

        private void Start()
        {
            // Keep this scene isolated for menu use.
        }
    }
}
