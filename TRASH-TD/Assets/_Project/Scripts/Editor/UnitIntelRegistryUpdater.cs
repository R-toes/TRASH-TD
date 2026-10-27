using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Editor
{
    /// <summary>
    /// Keeps <see cref="UnitIntelRegistry"/> (Resources/Intel) listing every OperatorData and EnemyData asset.
    /// Runs after any .asset is imported, deleted or moved, and once after each script reload, so a new unit
    /// shows up in Reclaimers' Intel (including builds) without touching the registry by hand.
    /// </summary>
    public sealed class UnitIntelRegistryUpdater : AssetPostprocessor
    {
        private static bool rebuildQueued;

        [InitializeOnLoadMethod]
        private static void OnScriptsReloaded() => QueueRebuild();

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (TouchesUnitAssets(imported) || TouchesUnitAssets(deleted) || TouchesUnitAssets(moved) || TouchesUnitAssets(movedFrom))
                QueueRebuild();
        }

        private static bool TouchesUnitAssets(string[] paths)
        {
            foreach (string path in paths)
            {
                if (path.EndsWith(".asset") && path != UnitIntelRegistry.AssetPath) return true;
            }
            return false;
        }

        private static void QueueRebuild()
        {
            if (rebuildQueued) return;
            rebuildQueued = true;
            EditorApplication.delayCall += () =>
            {
                rebuildQueued = false;
                Rebuild();
            };
        }

        [MenuItem("TRASH TD/Rebuild Reclaimers' Intel Registry")]
        public static void Rebuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            List<OperatorData> operators = UnitIntelRegistry.FindProjectAssets<OperatorData>();
            List<EnemyData> enemies = UnitIntelRegistry.FindProjectAssets<EnemyData>();

            var registry = AssetDatabase.LoadAssetAtPath<UnitIntelRegistry>(UnitIntelRegistry.AssetPath);
            if (registry == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(UnitIntelRegistry.AssetPath));
                registry = ScriptableObject.CreateInstance<UnitIntelRegistry>();
                registry.operators = operators;
                registry.enemies = enemies;
                AssetDatabase.CreateAsset(registry, UnitIntelRegistry.AssetPath);
                AssetDatabase.SaveAssets();
                return;
            }

            if (SameItems(registry.operators, operators) && SameItems(registry.enemies, enemies)) return;

            registry.operators = operators;
            registry.enemies = enemies;
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssetIfDirty(registry);
        }

        private static bool SameItems<T>(List<T> current, List<T> fresh) where T : Object
        {
            if (current == null || current.Count != fresh.Count) return false;
            for (int i = 0; i < fresh.Count; i++)
            {
                if (current[i] != fresh[i]) return false;
            }
            return true;
        }
    }
}
