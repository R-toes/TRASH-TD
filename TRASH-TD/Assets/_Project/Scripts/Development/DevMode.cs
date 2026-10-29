using UnityEngine;

namespace TrashTD.Development
{
    /// <summary>
    /// Developer mode: gates tools players must never reach (QA Sandbox, the draft's developer picker).
    /// In the Editor it follows the TRASH TD → Dev Mode menu toggle (per machine, on by default).
    /// In a build it is on only for Development Builds; a release build is always player mode.
    /// Read it when building UI, not once at startup, so the Editor toggle applies on the next screen.
    /// </summary>
    public static class DevMode
    {
        public const string EditorPrefKey = "TrashTD.DevMode";

        public static bool IsEnabled
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetBool(EditorPrefKey, true);
#else
                return Debug.isDebugBuild;
#endif
            }
        }
    }
}
