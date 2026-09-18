using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Systems
{
    [Serializable]
    public class OperatorSaveEntry
    {
        public string operatorName;
        public int highestRarity;
        public int copyCount;
    }

    [Serializable]
    public class StageProgressEntry
    {
        public string stageId;
        public int difficulty; // 0=Easy, 1=Normal, 2=Hard
        public int starsEarned; // 1-3
        public bool isCleared;
    }

    [Serializable]
    public class SaveData
    {
        // TODO: economy beyond card-draft — awaiting design input from Raim (currencies, shop, etc.)
        // TODO: persistence model — awaiting design input (whether progression persists between runs or resets per run)
        public int currency = 0;
        public List<OperatorSaveEntry> roster = new List<OperatorSaveEntry>();
        public List<StageProgressEntry> stageProgress = new List<StageProgressEntry>();
    }

    /// <summary>
    /// Save system providing persistent JSON storage for progression, roster, and stage records (GDD 1.9).
    /// </summary>
    public static class SaveSystem
    {
        private static readonly string SaveFileName = "trash_td_save.json";

        private static string SaveFilePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        public static void Save(SaveData data)
        {
            try
            {
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(SaveFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"SaveSystem: Failed to save game data: {ex.Message}");
            }
        }

        public static SaveData Load()
        {
            try
            {
                if (File.Exists(SaveFilePath))
                {
                    string json = File.ReadAllText(SaveFilePath);
                    return JsonUtility.FromJson<SaveData>(json) ?? new SaveData();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"SaveSystem: Failed to load game data: {ex.Message}");
            }

            return new SaveData();
        }

        public static void DeleteSave()
        {
            if (File.Exists(SaveFilePath))
            {
                File.Delete(SaveFilePath);
            }
        }
    }
}
