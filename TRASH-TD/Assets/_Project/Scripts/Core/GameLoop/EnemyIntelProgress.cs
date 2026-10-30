using System.Collections.Generic;
using UnityEngine;
using TrashTD.Data;
using TrashTD.Development;

namespace TrashTD.Core.GameLoop
{
    /// <summary>
    /// Which enemies the player has met, and on which stages (PlayerPrefs). Reclaimers' Intel keeps an enemy's file
    /// locked (silhouette, scrambled name, no data) until that enemy has spawned in one of the player's runs, and
    /// lists only the stages where it was met under SIGHTED AT.
    /// - "TrashTD.Intel.Met.&lt;enemy asset&gt;" = 1 once met anywhere, the tutorial included.
    /// - "TrashTD.Intel.Sighted.&lt;enemy asset&gt;" = the stage ids where it was met, '|' separated, in the order
    ///   they were first met. The tutorial is a training drill, so it unlocks the file but isn't listed.
    /// - "TrashTD.Intel.Index" lists every recorded enemy so <see cref="ResetAll"/> can clear them.
    /// The sandbox is a dev tool and records nothing. Dev mode shows every file as met.
    /// </summary>
    public static class EnemyIntelProgress
    {
        private const string MetPrefix = "TrashTD.Intel.Met.";
        private const string SightedPrefix = "TrashTD.Intel.Sighted.";
        private const string IndexKey = "TrashTD.Intel.Index";
        private const char Separator = '|';
        private const string SandboxStageId = "SANDBOX";

        // Enemy + stage pairs already saved this session, so repeat spawns cost a set lookup instead of PlayerPrefs reads
        private static readonly HashSet<string> recorded = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => recorded.Clear();

        /// <summary>True once the player has met this enemy (always true in dev mode).</summary>
        public static bool IsEncountered(EnemyData enemy)
        {
            if (enemy == null) return false;
            return DevMode.IsEnabled || PlayerPrefs.GetInt(MetPrefix + KeyOf(enemy), 0) == 1;
        }

        /// <summary>Stage ids where the player has met this enemy, in the order they were first met.</summary>
        public static List<string> GetSightedStageIds(EnemyData enemy)
        {
            var ids = new List<string>();
            if (enemy == null) return ids;

            string saved = PlayerPrefs.GetString(SightedPrefix + KeyOf(enemy), string.Empty);
            foreach (string id in saved.Split(Separator))
            {
                if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
            return ids;
        }

        /// <summary>Called for every enemy that spawns (EnemyManager.SpawnEnemy). Saves only what is new.</summary>
        public static void RecordEncounter(EnemyData enemy, StageData stage)
        {
            if (enemy == null || stage == null || string.IsNullOrEmpty(stage.stageId)) return;
            string stageId = stage.stageId;
            if (stageId == SandboxStageId) return;

            string key = KeyOf(enemy);
            if (!recorded.Add(key + Separator + stageId)) return;

            bool changed = false;
            if (PlayerPrefs.GetInt(MetPrefix + key, 0) != 1)
            {
                PlayerPrefs.SetInt(MetPrefix + key, 1);
                AddToIndex(key);
                changed = true;
            }

            if (stageId != TutorialDirector.TutorialStageId)
            {
                List<string> sighted = GetSightedStageIds(enemy);
                if (!sighted.Contains(stageId))
                {
                    sighted.Add(stageId);
                    PlayerPrefs.SetString(SightedPrefix + key, string.Join(Separator.ToString(), sighted));
                    changed = true;
                }
            }

            if (changed) PlayerPrefs.Save();
        }

        /// <summary>Forgets every encounter (TRASH TD → Reset Player Progress).</summary>
        public static void ResetAll()
        {
            foreach (string key in PlayerPrefs.GetString(IndexKey, string.Empty).Split(Separator))
            {
                if (string.IsNullOrEmpty(key)) continue;
                PlayerPrefs.DeleteKey(MetPrefix + key);
                PlayerPrefs.DeleteKey(SightedPrefix + key);
            }

            PlayerPrefs.DeleteKey(IndexKey);
            PlayerPrefs.Save();
            recorded.Clear();
        }

        private static void AddToIndex(string key)
        {
            string index = PlayerPrefs.GetString(IndexKey, string.Empty);
            foreach (string existing in index.Split(Separator))
            {
                if (existing == key) return;
            }
            PlayerPrefs.SetString(IndexKey, string.IsNullOrEmpty(index) ? key : index + Separator + key);
        }

        // Asset names are unique and stable; display names can change
        private static string KeyOf(EnemyData enemy) => enemy.name;
    }
}
