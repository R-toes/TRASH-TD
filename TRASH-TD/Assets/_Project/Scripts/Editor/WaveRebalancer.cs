using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Editor
{
    /// <summary>
    /// Rebuilds stage waves from the shared difficulty curve (WaveCurveSettings + WaveCurveBuilder).
    /// Keeps each stage's wave counts, roster (minus excluded enemies) and spawn points.
    /// All changes are undoable and saved to the stage assets.
    /// </summary>
    public static class WaveRebalancer
    {
        public const string SettingsPath = "Assets/_Project/Data/Waves/WaveCurveSettings.asset";

        // Enemies not fully configured yet, which must never be placed in waves (none at the moment).
        private static readonly string[] DefaultExcludedEnemyPaths = { };

        public static WaveCurveSettings LoadOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<WaveCurveSettings>(SettingsPath);
            if (settings != null) return settings;

            string folder = System.IO.Path.GetDirectoryName(SettingsPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/_Project/Data", "Waves");

            settings = ScriptableObject.CreateInstance<WaveCurveSettings>();
            foreach (string path in DefaultExcludedEnemyPaths)
            {
                var enemy = AssetDatabase.LoadAssetAtPath<EnemyData>(path);
                if (enemy != null) settings.excludedEnemies.Add(enemy);
            }

            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        [MenuItem("TRASH TD/Rebalance All Stage Waves")]
        public static void RebalanceAllStagesMenu()
        {
            var stages = new List<StageData>();
            foreach (string guid in AssetDatabase.FindAssets("t:StageData"))
            {
                var stage = AssetDatabase.LoadAssetAtPath<StageData>(AssetDatabase.GUIDToAssetPath(guid));
                if (stage != null && stage.stageId != "SANDBOX") stages.Add(stage);
            }

            if (!EditorUtility.DisplayDialog(
                    "Rebalance all stage waves",
                    $"Rebuild the Easy/Normal/Hard waves of {stages.Count} stage(s) from the shared difficulty curve?\n\n" +
                    "Wave counts, enemy rosters and spawn points are kept; enemy counts, timing and introductions are replaced. " +
                    "Undo with Ctrl+Z.",
                    "Rebalance", "Cancel"))
            {
                return;
            }

            foreach (StageData stage in stages) RebalanceStage(stage);
            Debug.Log($"<color=green>TRASH TD: Rebalanced waves for {stages.Count} stage(s).</color>");
        }

        /// <summary>Rebalances all difficulties of one stage. Returns false if the stage has no usable roster.</summary>
        public static bool RebalanceStage(StageData stage)
        {
            return Rebalance(stage, new[] { StageDifficulty.Easy, StageDifficulty.Normal, StageDifficulty.Hard });
        }

        public static bool Rebalance(StageData stage, IEnumerable<StageDifficulty> difficulties)
        {
            if (stage == null) return false;

            WaveCurveSettings settings = LoadOrCreateSettings();
            List<EnemyData> roster = WaveCurveBuilder.OrderRoster(WaveCurveBuilder.CollectRoster(stage), settings);
            if (roster.Count == 0)
            {
                Debug.LogWarning($"TRASH TD: {stage.name} has no usable enemies in its waves; add some before rebalancing.");
                return false;
            }

            Undo.RecordObject(stage, $"Rebalance waves of {stage.name}");
            foreach (StageDifficulty difficulty in difficulties)
            {
                WaveData[] current = stage.GetWaves(difficulty);
                int waveCount = current != null ? current.Length : 0;
                if (waveCount == 0) continue;

                WaveData[] rebuilt = WaveCurveBuilder.BuildWaves(stage, waveCount, roster, settings);
                switch (difficulty)
                {
                    case StageDifficulty.Easy: stage.wavesEasy = rebuilt; break;
                    case StageDifficulty.Normal: stage.wavesNormal = rebuilt; break;
                    default: stage.wavesHard = rebuilt; break;
                }
            }

            EditorUtility.SetDirty(stage);
            AssetDatabase.SaveAssetIfDirty(stage);
            return true;
        }
    }
}
