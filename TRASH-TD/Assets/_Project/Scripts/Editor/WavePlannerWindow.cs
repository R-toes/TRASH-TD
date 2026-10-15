using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Editor
{
    /// <summary>
    /// Overview and editor for every stage's waves (TRASH TD -> Wave Planner).
    /// Shows a per-wave strength graph, a wave list with totals, a spawn timeline for the selected
    /// wave, and warnings for entries WaveManager would skip. Edits go through SerializedObject,
    /// so they support Undo and are saved to the stage asset like Inspector edits.
    /// </summary>
    public sealed class WavePlannerWindow : EditorWindow
    {
        private const string StagePrefKey = "TrashTD.WavePlanner.StageGuid";
        private const string DifficultyPrefKey = "TrashTD.WavePlanner.Difficulty";
        private const float GraphHeight = 90f;
        private const float ListWidth = 400f;
        private const float RowHeight = 36f;
        private const float TimelineLabelWidth = 170f;

        private static readonly string[] DifficultyNames = { "Easy", "Normal", "Hard" };

        private static readonly Color GraphBackground = new Color(0.13f, 0.13f, 0.15f);
        private static readonly Color BarColor = new Color(0.35f, 0.55f, 0.8f);
        private static readonly Color SelectedBarColor = new Color(1f, 0.8f, 0.3f);
        private static readonly Color WarningColor = new Color(0.9f, 0.3f, 0.25f);
        private static readonly Color RowColor = new Color(0f, 0f, 0f, 0.12f);
        private static readonly Color SelectedRowColor = new Color(0.25f, 0.45f, 0.75f, 0.45f);
        private static readonly Color GroundTickColor = new Color(1f, 0.75f, 0.4f);
        private static readonly Color AirTickColor = new Color(0.55f, 0.8f, 1f);

        private StageData[] stages = new StageData[0];
        private string[] stageLabels = new string[0];
        private StageData stage;
        private SerializedObject serializedStage;
        private StageDifficulty difficulty = StageDifficulty.Normal;
        private int selectedWave;
        private int copyTargetIndex;
        private Vector2 listScroll;
        private Vector2 detailScroll;

        [MenuItem("TRASH TD/Wave Planner")]
        public static void Open()
        {
            GetWindow<WavePlannerWindow>("Wave Planner").Show();
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            difficulty = (StageDifficulty)Mathf.Clamp(
                EditorPrefs.GetInt(DifficultyPrefKey, (int)StageDifficulty.Normal), 0, DifficultyNames.Length - 1);
            RefreshStages();
            Undo.undoRedoPerformed += Repaint;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Repaint;
        }

        private void OnProjectChange()
        {
            RefreshStages();
            Repaint();
        }

        // ============================
        // Stage selection
        // ============================

        private void RefreshStages()
        {
            var found = new List<StageData>();
            foreach (string guid in AssetDatabase.FindAssets("t:StageData"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<StageData>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) found.Add(asset);
            }

            found.Sort((a, b) => string.CompareOrdinal(a.stageId ?? a.name, b.stageId ?? b.name));
            stages = found.ToArray();
            stageLabels = new string[stages.Length];
            for (int i = 0; i < stages.Length; i++)
            {
                StageData s = stages[i];
                stageLabels[i] = string.IsNullOrEmpty(s.mapName) ? s.name : $"{s.stageId}  -  {s.mapName}";
            }

            if (stage == null)
            {
                string savedPath = AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(StagePrefKey, string.Empty));
                StageData saved = string.IsNullOrEmpty(savedPath) ? null : AssetDatabase.LoadAssetAtPath<StageData>(savedPath);
                SelectStage(saved != null ? saved : (stages.Length > 0 ? stages[0] : null));
            }
        }

        private void SelectStage(StageData newStage)
        {
            stage = newStage;
            serializedStage = stage != null ? new SerializedObject(stage) : null;
            selectedWave = 0;
            if (stage != null)
                EditorPrefs.SetString(StagePrefKey, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(stage)));
        }

        private string WavesFieldName => difficulty switch
        {
            StageDifficulty.Easy => nameof(StageData.wavesEasy),
            StageDifficulty.Hard => nameof(StageData.wavesHard),
            _ => nameof(StageData.wavesNormal)
        };

        // ============================
        // Main layout
        // ============================

        private void OnGUI()
        {
            DrawToolbar();

            if (stage == null)
            {
                EditorGUILayout.HelpBox("No StageData assets found. Create one or run TRASH TD > Generate Sample Data Assets.", MessageType.Info);
                return;
            }

            if (serializedStage == null || serializedStage.targetObject != stage)
                serializedStage = new SerializedObject(stage);
            serializedStage.Update();

            SerializedProperty waves = serializedStage.FindProperty(WavesFieldName);
            selectedWave = Mathf.Clamp(selectedWave, 0, Mathf.Max(0, waves.arraySize - 1));
            WaveAnalysis.WaveSummary[] summaries = WaveAnalysis.SummarizeAll(stage, difficulty);

            DrawStageHeader(summaries);
            DrawStrengthGraph(summaries);

            EditorGUILayout.BeginHorizontal();
            DrawWaveList(waves, summaries);
            DrawWaveDetail(waves, summaries);
            EditorGUILayout.EndHorizontal();

            serializedStage.ApplyModifiedProperties();

            if (Event.current.type == EventType.MouseMove) Repaint();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            int currentIndex = Array.IndexOf(stages, stage);
            int newIndex = EditorGUILayout.Popup(currentIndex, stageLabels, EditorStyles.toolbarPopup, GUILayout.Width(280f));
            if (newIndex != currentIndex && newIndex >= 0) SelectStage(stages[newIndex]);

            if (stage != null)
            {
                var labels = new string[DifficultyNames.Length];
                for (int i = 0; i < labels.Length; i++)
                {
                    WaveData[] list = stage.GetWaves((StageDifficulty)i);
                    labels[i] = $"{DifficultyNames[i]} ({(list != null ? list.Length : 0)})";
                }

                int newDifficulty = GUILayout.Toolbar((int)difficulty, labels, EditorStyles.toolbarButton, GUILayout.Width(300f));
                if (newDifficulty != (int)difficulty)
                {
                    difficulty = (StageDifficulty)newDifficulty;
                    selectedWave = 0;
                    EditorPrefs.SetInt(DifficultyPrefKey, newDifficulty);
                }
            }

            GUILayout.FlexibleSpace();
            if (stage != null && GUILayout.Button("Select Asset", EditorStyles.toolbarButton))
            {
                Selection.activeObject = stage;
                EditorGUIUtility.PingObject(stage);
            }
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton)) RefreshStages();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStageHeader(WaveAnalysis.WaveSummary[] summaries)
        {
            int spawnPoints = stage.spawnPoints != null ? stage.spawnPoints.Length : 0;
            int paths = stage.enemyPaths != null ? stage.enemyPaths.Length : 0;

            EditorGUILayout.LabelField(
                $"Spawn points: {spawnPoints}    Explicit paths: {paths}    Lives: {stage.GetLifePoints(difficulty)}    " +
                $"Squad limit: {stage.squadSizeLimit}",
                EditorStyles.miniLabel);

            string effect = GetStageEffectNote(stage.stageId);
            if (effect != null) EditorGUILayout.LabelField(effect, EditorStyles.miniLabel);

            DrawRebalanceControls();

            int badWaves = 0;
            foreach (var summary in summaries)
                if (summary.HasWarnings) badWaves++;
            if (badWaves > 0)
                EditorGUILayout.HelpBox($"{badWaves} wave(s) have problems. They are marked red below.", MessageType.Warning);
        }

        private void DrawRebalanceControls()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Difficulty curve", EditorStyles.miniBoldLabel, GUILayout.Width(95f));

            string difficultyName = DifficultyNames[(int)difficulty];
            if (GUILayout.Button($"Rebalance {difficultyName}", EditorStyles.miniButtonLeft, GUILayout.Width(130f)))
                RunRebalance($"Rebuild the {difficultyName} waves of {stage.name} from the difficulty curve?", new[] { difficulty });
            if (GUILayout.Button("Rebalance whole stage", EditorStyles.miniButtonMid, GUILayout.Width(150f)))
                RunRebalance($"Rebuild the Easy, Normal and Hard waves of {stage.name} from the difficulty curve?",
                    new[] { StageDifficulty.Easy, StageDifficulty.Normal, StageDifficulty.Hard });
            if (GUILayout.Button("Curve settings", EditorStyles.miniButtonRight, GUILayout.Width(100f)))
            {
                Selection.activeObject = WaveRebalancer.LoadOrCreateSettings();
                EditorGUIUtility.PingObject(Selection.activeObject);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void RunRebalance(string question, StageDifficulty[] difficulties)
        {
            if (!EditorUtility.DisplayDialog("Rebalance waves",
                    question + "\n\nWave counts, enemy roster and spawn points are kept; enemy counts, timing and " +
                    "introductions are replaced. Undo with Ctrl+Z.",
                    "Rebalance", "Cancel"))
            {
                return;
            }

            serializedStage.ApplyModifiedProperties();
            WaveRebalancer.Rebalance(stage, difficulties);
            serializedStage.Update();
            GUIUtility.ExitGUI();
        }

        /// <summary>Stage effects are still keyed by stage id in code; surfaced here so they are not forgotten while balancing.</summary>
        private static string GetStageEffectNote(string stageId)
        {
            return stageId switch
            {
                "STAGE_03" => "Stage effect: Sandstorm (attack interval / miss chance, see StageCombatModifiers).",
                "STAGE_06" => "Stage effect: Acid rain damage over time (see StageBootstrapper).",
                _ => null
            };
        }

        // ============================
        // Strength graph
        // ============================

        private void DrawStrengthGraph(WaveAnalysis.WaveSummary[] summaries)
        {
            Rect rect = GUILayoutUtility.GetRect(10f, GraphHeight, GUILayout.ExpandWidth(true));
            rect = new Rect(rect.x + 4f, rect.y + 2f, rect.width - 8f, rect.height - 4f);
            EditorGUI.DrawRect(rect, GraphBackground);

            if (summaries.Length == 0)
            {
                GUI.Label(rect, "No waves for this difficulty.", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            int maxHP = 1;
            foreach (var summary in summaries) maxHP = Mathf.Max(maxHP, summary.TotalHP);

            const float topPadding = 14f;
            float barWidth = rect.width / summaries.Length;
            for (int i = 0; i < summaries.Length; i++)
            {
                float height = (rect.height - topPadding) * summaries[i].TotalHP / maxHP;
                var bar = new Rect(rect.x + i * barWidth + 1f, rect.yMax - height, Mathf.Max(1f, barWidth - 2f), height);
                Color color = i == selectedWave ? SelectedBarColor : (summaries[i].HasWarnings ? WarningColor : BarColor);
                EditorGUI.DrawRect(bar, color);
            }

            GUI.Label(new Rect(rect.x + 4f, rect.y, rect.width, 14f),
                $"Total enemy HP per wave (max {maxHP:N0}). Click a bar to select.", EditorStyles.miniLabel);

            Event e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;

            int hovered = Mathf.Clamp((int)((e.mousePosition.x - rect.x) / barWidth), 0, summaries.Length - 1);
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                selectedWave = hovered;
                GUI.FocusControl(null);
                e.Use();
            }
            else if (e.type == EventType.Repaint)
            {
                var content = new GUIContent($"Wave {hovered + 1}: {summaries[hovered].TotalHP:N0} HP, {summaries[hovered].EnemyCount} enemies");
                Vector2 size = EditorStyles.helpBox.CalcSize(content);
                float x = Mathf.Min(e.mousePosition.x + 12f, rect.xMax - size.x);
                EditorStyles.helpBox.Draw(new Rect(x, rect.y + 16f, size.x, size.y), content, false, false, false, false);
            }
        }

        // ============================
        // Wave list
        // ============================

        private void DrawWaveList(SerializedProperty waves, WaveAnalysis.WaveSummary[] summaries)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(ListWidth));
            listScroll = EditorGUILayout.BeginScrollView(listScroll);

            for (int i = 0; i < waves.arraySize; i++)
            {
                WaveAnalysis.WaveSummary summary = i < summaries.Length ? summaries[i] : null;
                string waveName = waves.GetArrayElementAtIndex(i).FindPropertyRelative(nameof(WaveData.waveName)).stringValue;

                Rect row = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(new Rect(row.x, row.y + 1f, row.width, row.height - 2f), i == selectedWave ? SelectedRowColor : RowColor);
                if (summary != null && summary.HasWarnings)
                    EditorGUI.DrawRect(new Rect(row.x, row.y + 1f, 4f, row.height - 2f), WarningColor);

                GUI.Label(new Rect(row.x + 8f, row.y + 2f, row.width - 12f, 16f), $"{i + 1}. {waveName}", EditorStyles.boldLabel);
                if (summary != null)
                {
                    string details = $"{summary.EnemyCount} enemies ({summary.AirCount} air)   HP {summary.TotalHP:N0}   " +
                                     $"{summary.SpawnDuration:0.#}s   spawns {FormatSpawnPoints(summary.SpawnPointMask)}" +
                                     (summary.HasWarnings ? $"   ! {summary.Warnings.Count}" : string.Empty);
                    GUI.Label(new Rect(row.x + 8f, row.y + 18f, row.width - 12f, 16f), details, EditorStyles.miniLabel);
                }

                if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
                {
                    selectedWave = i;
                    GUI.FocusControl(null);
                    Event.current.Use();
                }
            }

            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("+ Add Wave"))
            {
                waves.arraySize++;
                SerializedProperty added = waves.GetArrayElementAtIndex(waves.arraySize - 1);
                added.FindPropertyRelative(nameof(WaveData.waveName)).stringValue = $"Wave {waves.arraySize}";
                selectedWave = waves.arraySize - 1;
                ApplyStructuralChange();
            }

            EditorGUILayout.EndVertical();
        }

        private static string FormatSpawnPoints(int mask)
        {
            if (mask == 0) return "-";

            var parts = new List<string>();
            for (int i = 0; i < 31; i++)
                if ((mask & (1 << i)) != 0) parts.Add(i.ToString());
            return string.Join(",", parts);
        }

        // ============================
        // Selected wave
        // ============================

        private void DrawWaveDetail(SerializedProperty waves, WaveAnalysis.WaveSummary[] summaries)
        {
            EditorGUILayout.BeginVertical();

            if (waves.arraySize == 0)
            {
                EditorGUILayout.HelpBox("This difficulty has no waves yet. Use + Add Wave, or copy another difficulty's waves below.", MessageType.Info);
                DrawCopyDifficulty();
                EditorGUILayout.EndVertical();
                return;
            }

            SerializedProperty wave = waves.GetArrayElementAtIndex(selectedWave);
            WaveAnalysis.WaveSummary summary = selectedWave < summaries.Length ? summaries[selectedWave] : null;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Wave {selectedWave + 1} of {waves.arraySize}", EditorStyles.boldLabel);
            if (GUILayout.Button("Duplicate", GUILayout.Width(75f)))
            {
                waves.InsertArrayElementAtIndex(selectedWave);
                selectedWave++;
                SerializedProperty copyName = waves.GetArrayElementAtIndex(selectedWave).FindPropertyRelative(nameof(WaveData.waveName));
                copyName.stringValue += " (copy)";
                ApplyStructuralChange();
            }
            using (new EditorGUI.DisabledScope(selectedWave == 0))
            {
                if (GUILayout.Button("Up", GUILayout.Width(40f)))
                {
                    waves.MoveArrayElement(selectedWave, selectedWave - 1);
                    selectedWave--;
                    ApplyStructuralChange();
                }
            }
            using (new EditorGUI.DisabledScope(selectedWave >= waves.arraySize - 1))
            {
                if (GUILayout.Button("Down", GUILayout.Width(50f)))
                {
                    waves.MoveArrayElement(selectedWave, selectedWave + 1);
                    selectedWave++;
                    ApplyStructuralChange();
                }
            }
            if (GUILayout.Button("Delete", GUILayout.Width(60f)))
            {
                waves.DeleteArrayElementAtIndex(selectedWave);
                selectedWave = Mathf.Max(0, selectedWave - 1);
                ApplyStructuralChange();
            }
            EditorGUILayout.EndHorizontal();

            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);

            if (summary != null)
            {
                foreach (string warning in summary.Warnings)
                    EditorGUILayout.HelpBox(warning, MessageType.Warning);

                EditorGUILayout.LabelField(
                    $"{summary.EnemyCount} enemies ({summary.AirCount} air)    Total HP {summary.TotalHP:N0}    " +
                    $"Spawning lasts {summary.SpawnDuration:0.#}s    Max leak cost {summary.MaxLeakCost} lives",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.PropertyField(wave.FindPropertyRelative(nameof(WaveData.waveName)));
            EditorGUILayout.PropertyField(wave.FindPropertyRelative(nameof(WaveData.preWaveDelay)));

            WaveData[] waveObjects = stage.GetWaves(difficulty);
            if (waveObjects != null && selectedWave < waveObjects.Length)
                DrawSpawnTimeline(waveObjects[selectedWave], summary);

            EditorGUILayout.PropertyField(wave.FindPropertyRelative(nameof(WaveData.entries)), true);

            EditorGUILayout.EndScrollView();

            DrawCopyDifficulty();
            EditorGUILayout.EndVertical();
        }

        private void DrawSpawnTimeline(WaveData wave, WaveAnalysis.WaveSummary summary)
        {
            if (wave == null || wave.entries == null || wave.entries.Length == 0) return;

            float duration = Mathf.Max(1f, summary != null ? summary.SpawnDuration : 0f);
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                $"Spawn timeline: 0s to {duration:0.#}s (starts after the {wave.preWaveDelay:0.#}s pre-wave delay)",
                EditorStyles.miniBoldLabel);

            foreach (WaveEntry entry in wave.entries)
            {
                Rect row = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
                string enemyName = entry != null && entry.enemyData != null ? entry.enemyData.enemyName : "(no enemy)";
                string label = entry != null ? $"{enemyName} x{entry.count}  @ spawn {entry.spawnPointIndex}" : enemyName;
                GUI.Label(new Rect(row.x, row.y, TimelineLabelWidth, row.height), label, EditorStyles.miniLabel);

                var track = new Rect(row.x + TimelineLabelWidth, row.y + 3f, row.width - TimelineLabelWidth - 4f, row.height - 6f);
                EditorGUI.DrawRect(track, GraphBackground);
                if (entry == null || entry.enemyData == null || entry.count <= 0) continue;

                Color tickColor = entry.enemyData.movementType == EnemyMovementType.Air ? AirTickColor : GroundTickColor;
                float start = Mathf.Max(0f, entry.startDelay);
                float interval = Mathf.Max(0f, entry.spawnInterval);
                for (int i = 0; i < entry.count; i++)
                {
                    float x = track.x + Mathf.Clamp01((start + i * interval) / duration) * (track.width - 3f);
                    EditorGUI.DrawRect(new Rect(x, track.y, 3f, track.height), tickColor);
                }
            }

            EditorGUILayout.Space(4f);
        }

        private void DrawCopyDifficulty()
        {
            var targets = new List<int>();
            var targetNames = new List<string>();
            for (int i = 0; i < DifficultyNames.Length; i++)
            {
                if (i == (int)difficulty) continue;
                targets.Add(i);
                targetNames.Add(DifficultyNames[i]);
            }
            copyTargetIndex = Mathf.Clamp(copyTargetIndex, 0, targets.Count - 1);

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Copy all {DifficultyNames[(int)difficulty]} waves to", GUILayout.Width(170f));
            copyTargetIndex = EditorGUILayout.Popup(copyTargetIndex, targetNames.ToArray(), GUILayout.Width(90f));
            if (GUILayout.Button("Copy...", GUILayout.Width(70f)))
                CopyWaves(difficulty, (StageDifficulty)targets[copyTargetIndex]);
            EditorGUILayout.EndHorizontal();
        }

        private void CopyWaves(StageDifficulty from, StageDifficulty to)
        {
            WaveData[] source = stage.GetWaves(from);
            WaveData[] existing = stage.GetWaves(to);
            int existingCount = existing != null ? existing.Length : 0;

            if (!EditorUtility.DisplayDialog(
                    "Copy waves",
                    $"Replace the {existingCount} {DifficultyNames[(int)to]} wave(s) of {stage.name} with a copy of the " +
                    $"{(source != null ? source.Length : 0)} {DifficultyNames[(int)from]} wave(s)? You can undo this with Ctrl+Z.",
                    "Replace", "Cancel"))
            {
                return;
            }

            serializedStage.ApplyModifiedProperties();
            Undo.RecordObject(stage, $"Copy {DifficultyNames[(int)from]} waves to {DifficultyNames[(int)to]}");
            WaveData[] copies = WaveAnalysis.CloneAll(source);
            switch (to)
            {
                case StageDifficulty.Easy: stage.wavesEasy = copies; break;
                case StageDifficulty.Normal: stage.wavesNormal = copies; break;
                default: stage.wavesHard = copies; break;
            }
            EditorUtility.SetDirty(stage);
            serializedStage.Update();
            GUIUtility.ExitGUI();
        }

        /// <summary>Commits an array change and restarts the GUI pass so layout matches the new wave count.</summary>
        private void ApplyStructuralChange()
        {
            serializedStage.ApplyModifiedProperties();
            GUI.FocusControl(null);
            GUIUtility.ExitGUI();
        }
    }
}
