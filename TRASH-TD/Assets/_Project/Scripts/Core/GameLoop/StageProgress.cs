using UnityEngine;
using TrashTD.Data;
using TrashTD.Development;

namespace TrashTD.Core.GameLoop
{
    /// <summary>
    /// Saved stage progress: the best star rating per numbered stage and difficulty (PlayerPrefs,
    /// "TrashTD.Progress.STAGE_03.Hard" = 0-3, 0 = not cleared), and the unlock rules built on it:
    /// - STAGE_01 is open on every difficulty.
    /// - Clearing a stage on Easy or Normal opens Easy and Normal of the next stage.
    /// - Only clearing it on Hard opens Hard of the next stage (and Easy / Normal too).
    /// Dev mode opens everything. The sandbox and the tutorial aren't numbered, so they are neither tracked nor locked here.
    /// </summary>
    public static class StageProgress
    {
        private const string KeyPrefix = "TrashTD.Progress.";
        // Highest stage number ResetAll clears; well above the six stages there are
        private const int MaxTrackedStageNumber = 99;

        private static readonly StageDifficulty[] Difficulties = { StageDifficulty.Easy, StageDifficulty.Normal, StageDifficulty.Hard };

        /// <summary>STAGE_03 → 3; 0 for ids without a number (SANDBOX, TUTORIAL).</summary>
        public static int GetStageNumber(string stageId)
        {
            if (string.IsNullOrEmpty(stageId)) return 0;
            int underscore = stageId.LastIndexOf('_');
            return underscore >= 0 && int.TryParse(stageId.Substring(underscore + 1), out int number) ? number : 0;
        }

        public static string GetStageId(int stageNumber) => $"STAGE_{stageNumber:00}";

        /// <summary>Best stars earned on this stage and difficulty (0 = never cleared).</summary>
        public static int GetBestStars(string stageId, StageDifficulty difficulty)
        {
            if (GetStageNumber(stageId) <= 0) return 0;
            return Mathf.Clamp(PlayerPrefs.GetInt(Key(stageId, difficulty), 0), 0, 3);
        }

        public static bool IsCleared(string stageId, StageDifficulty difficulty) => GetBestStars(stageId, difficulty) > 0;

        /// <summary>Called on victory; keeps the best rating. Unnumbered stages (sandbox, tutorial) are ignored.</summary>
        public static void RecordClear(string stageId, StageDifficulty difficulty, int stars)
        {
            if (GetStageNumber(stageId) <= 0) return;
            stars = Mathf.Clamp(stars, 1, 3);
            if (stars <= GetBestStars(stageId, difficulty)) return;

            PlayerPrefs.SetInt(Key(stageId, difficulty), stars);
            PlayerPrefs.Save();
        }

        public static bool IsUnlocked(string stageId, StageDifficulty difficulty)
        {
            if (DevMode.IsEnabled) return true;

            int number = GetStageNumber(stageId);
            if (number <= 1) return true;

            string previous = GetStageId(number - 1);
            if (difficulty == StageDifficulty.Hard) return IsCleared(previous, StageDifficulty.Hard);
            return IsCleared(previous, StageDifficulty.Easy)
                || IsCleared(previous, StageDifficulty.Normal)
                || IsCleared(previous, StageDifficulty.Hard);
        }

        /// <summary>The stage can be opened on at least one difficulty (its map marker isn't locked).</summary>
        public static bool IsAnyDifficultyUnlocked(string stageId)
        {
            foreach (StageDifficulty difficulty in Difficulties)
            {
                if (IsUnlocked(stageId, difficulty)) return true;
            }
            return false;
        }

        /// <summary>What a locked difficulty needs, for the dossier: "CLEAR STAGE 02 ON HARD".</summary>
        public static string GetUnlockRequirement(string stageId, StageDifficulty difficulty)
        {
            int number = GetStageNumber(stageId);
            if (number <= 1) return string.Empty;

            string previous = $"STAGE {number - 1:00}";
            return difficulty == StageDifficulty.Hard ? $"CLEAR {previous} ON HARD" : $"CLEAR {previous} FIRST";
        }

        /// <summary>Forget every clear and star (TRASH TD → Reset Player Progress).</summary>
        public static void ResetAll()
        {
            for (int number = 1; number <= MaxTrackedStageNumber; number++)
            {
                string stageId = GetStageId(number);
                foreach (StageDifficulty difficulty in Difficulties) PlayerPrefs.DeleteKey(Key(stageId, difficulty));
            }
            PlayerPrefs.Save();
        }

        private static string Key(string stageId, StageDifficulty difficulty) => KeyPrefix + stageId + "." + difficulty;
    }
}
