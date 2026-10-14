using System;
using System.Reflection;
using UnityEngine;

namespace TrashTD.Tests
{
    /// <summary>
    /// Edit-mode tests don't get Unity's Awake call for components added with AddComponent,
    /// so singletons that register themselves in Awake (GameManager.Instance, PlayerDeck.Instance, ...)
    /// stay null. These helpers run Awake the same way play mode would.
    /// </summary>
    internal static class TestUtility
    {
        public static T AddAwakened<T>(GameObject gameObject) where T : Component
        {
            T component = gameObject.AddComponent<T>();
            CallAwake(component);
            return component;
        }

        public static void CallAwake(Component component)
        {
            for (Type type = component.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
            {
                MethodInfo awake = type.GetMethod(
                    "Awake",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);
                if (awake == null) continue;

                awake.Invoke(component, null);
                return;
            }
        }

        /// <summary>
        /// Game code destroys objects with Object.Destroy, which Unity reports as an error in edit mode.
        /// Call before an action that destroys an object so the expected message doesn't fail the test.
        /// </summary>
        public static void ExpectEditModeDestroy()
        {
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("Destroy may not be called from edit mode"));
        }

        /// <summary>
        /// Clears the static Instance of the game's singletons. OnDestroy (which normally clears them)
        /// does not run in edit mode, and `Instance?.` calls would still reach a destroyed instance,
        /// leaking state into later tests.
        /// </summary>
        public static void ClearSingletons()
        {
            ClearInstance(typeof(TrashTD.Core.GameLoop.GameManager));
            ClearInstance(typeof(TrashTD.Systems.PlayerDeck));
            ClearInstance(typeof(TrashTD.Systems.RarityUpgradeSystem));
            ClearInstance(typeof(TrashTD.Systems.OperatorManager));
        }

        private static void ClearInstance(Type type)
        {
            PropertyInfo instance = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            instance?.GetSetMethod(true)?.Invoke(null, new object[] { null });
        }
    }
}
