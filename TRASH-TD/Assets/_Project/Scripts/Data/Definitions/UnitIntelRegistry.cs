using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Data
{
    /// <summary>
    /// Every OperatorData and EnemyData asset in the project, for Reclaimers' Intel.
    /// Kept in sync automatically by the editor (<c>UnitIntelRegistryUpdater</c>) whenever a unit asset is added,
    /// removed or moved, so new units appear without manual updates. Builds read the saved asset from Resources;
    /// the Editor always scans the project, so it is current even before the asset is re-saved.
    /// </summary>
    public class UnitIntelRegistry : ScriptableObject
    {
        public const string ResourcePath = "Intel/UnitIntelRegistry";
        public const string AssetPath = "Assets/Resources/Intel/UnitIntelRegistry.asset";

        public List<OperatorData> operators = new List<OperatorData>();
        public List<EnemyData> enemies = new List<EnemyData>();

        public static IReadOnlyList<OperatorData> GetOperators()
        {
#if UNITY_EDITOR
            return FindProjectAssets<OperatorData>();
#else
            UnitIntelRegistry registry = Resources.Load<UnitIntelRegistry>(ResourcePath);
            return registry != null ? WithoutMissing(registry.operators) : new List<OperatorData>();
#endif
        }

        public static IReadOnlyList<EnemyData> GetEnemies()
        {
#if UNITY_EDITOR
            return FindProjectAssets<EnemyData>();
#else
            UnitIntelRegistry registry = Resources.Load<UnitIntelRegistry>(ResourcePath);
            return registry != null ? WithoutMissing(registry.enemies) : new List<EnemyData>();
#endif
        }

        private static List<T> WithoutMissing<T>(List<T> source) where T : Object
        {
            var result = new List<T>();
            if (source == null) return result;
            foreach (T item in source)
            {
                if (item != null) result.Add(item);
            }
            return result;
        }

#if UNITY_EDITOR
        /// <summary>All assets of type <typeparamref name="T"/> outside test folders, sorted by path.</summary>
        public static List<T> FindProjectAssets<T>() where T : Object
        {
            var result = new List<T>();
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { "Assets" });
            var paths = new List<string>();
            foreach (string guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("/Tests/")) paths.Add(path);
            }
            paths.Sort(System.StringComparer.Ordinal);

            foreach (string path in paths)
            {
                T asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null && !result.Contains(asset)) result.Add(asset);
            }
            return result;
        }
#endif
    }
}
