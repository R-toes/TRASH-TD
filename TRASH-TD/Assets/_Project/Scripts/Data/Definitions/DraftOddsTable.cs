using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Data
{
    /// <summary>Chance of each draft rarity, in percent (the three add up to 100).</summary>
    public readonly struct DraftOdds
    {
        public readonly float Star1;
        public readonly float Star2;
        public readonly float Star3;

        public DraftOdds(float star1, float star2, float star3)
        {
            Star1 = star1;
            Star2 = star2;
            Star3 = star3;
        }

        /// <summary>Scales relative weights to percentages. All-zero (or negative) weights give 100% 1★.</summary>
        public static DraftOdds FromWeights(float star1, float star2, float star3)
        {
            star1 = Mathf.Max(0f, star1);
            star2 = Mathf.Max(0f, star2);
            star3 = Mathf.Max(0f, star3);
            float total = star1 + star2 + star3;
            if (total <= 0f) return new DraftOdds(100f, 0f, 0f);
            return new DraftOdds(star1 * 100f / total, star2 * 100f / total, star3 * 100f / total);
        }

        public float Get(OperatorRarity rarity)
        {
            return rarity switch
            {
                OperatorRarity.Star1 => Star1,
                OperatorRarity.Star2 => Star2,
                OperatorRarity.Star3 => Star3,
                _ => 0f
            };
        }
    }

    /// <summary>
    /// Card draft rarity odds that step up as the stage goes on. Each step applies once its number of waves has
    /// been cleared and lasts until the next step: with the default table, the drafts before waves 1–5 use the
    /// "0" row, the drafts before waves 6–10 use the "5" row, and so on. The odds go by waves cleared, not by
    /// difficulty, so only Hard (30 waves) reaches the last rows. The stage's first draft is still 1★ only
    /// (CardDraftSystem.IsOpeningDraft). The asset lives at Assets/_Project/Data/Resources/DraftOddsTable.asset;
    /// without it the built-in default table below is used.
    /// </summary>
    [CreateAssetMenu(fileName = ResourceName, menuName = "TRASH TD/Draft Odds Table")]
    public sealed class DraftOddsTable : ScriptableObject
    {
        public const string ResourceName = "DraftOddsTable";

        [Serializable]
        public struct Step
        {
            [Tooltip("The step applies from the draft after this many waves are cleared (0 = from the start).")]
            public int afterWavesCleared;
            [Tooltip("Relative weights (normalised, so they don't have to add up to 100).")]
            public float star1;
            public float star2;
            public float star3;

            public Step(int afterWavesCleared, float star1, float star2, float star3)
            {
                this.afterWavesCleared = afterWavesCleared;
                this.star1 = star1;
                this.star2 = star2;
                this.star3 = star3;
            }

            public DraftOdds Odds => DraftOdds.FromWeights(star1, star2, star3);
        }

        [Tooltip("One row per step. Order doesn't matter; the row with the highest 'after waves cleared' not above the waves cleared so far is used.")]
        [SerializeField] private List<Step> steps = CreateDefaultSteps();

        private static DraftOddsTable builtInDefault;

        public IReadOnlyList<Step> Steps => steps;

        /// <summary>Team-approved table (2026-11-04): 1★ drops 8 points every 5 waves, ending at 40/35/25.</summary>
        public static List<Step> CreateDefaultSteps()
        {
            return new List<Step>
            {
                new Step(0, 80f, 17f, 3f),
                new Step(5, 72f, 21f, 7f),
                new Step(10, 64f, 24f, 12f),
                new Step(15, 56f, 28f, 16f),
                new Step(20, 48f, 31f, 21f),
                new Step(25, 40f, 35f, 25f)
            };
        }

        /// <summary>The asset from Resources, or the built-in default table if there is none.</summary>
        public static DraftOddsTable Load()
        {
            DraftOddsTable table = Resources.Load<DraftOddsTable>(ResourceName);
            return table != null ? table : GetBuiltInDefault();
        }

        public static DraftOddsTable GetBuiltInDefault()
        {
            if (builtInDefault == null)
            {
                builtInDefault = CreateInstance<DraftOddsTable>();
                builtInDefault.name = ResourceName + " (built-in)";
                builtInDefault.hideFlags = HideFlags.HideAndDontSave;
            }
            return builtInDefault;
        }

        /// <summary>Index into <see cref="Steps"/> of the step in force after this many cleared waves, or -1 if none.</summary>
        public int GetStepIndex(int wavesCleared)
        {
            int best = -1;
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].afterWavesCleared > wavesCleared) continue;
                if (best < 0 || steps[i].afterWavesCleared > steps[best].afterWavesCleared) best = i;
            }
            return best;
        }

        /// <summary>Odds in force after this many cleared waves. Before the first step (or with no steps): 100% 1★.</summary>
        public DraftOdds GetOdds(int wavesCleared)
        {
            int index = GetStepIndex(wavesCleared);
            return index >= 0 ? steps[index].Odds : new DraftOdds(100f, 0f, 0f);
        }

        /// <summary>True if this exact number of cleared waves starts a new step (the first draft that uses it).</summary>
        public bool IsStepStart(int wavesCleared)
        {
            if (wavesCleared <= 0) return false;
            int index = GetStepIndex(wavesCleared);
            return index >= 0 && steps[index].afterWavesCleared == wavesCleared;
        }

        /// <summary>Waves cleared at which the next step starts, if there is a later one.</summary>
        public bool TryGetNextStep(int wavesCleared, out int afterWavesCleared)
        {
            afterWavesCleared = int.MaxValue;
            for (int i = 0; i < steps.Count; i++)
            {
                int at = steps[i].afterWavesCleared;
                if (at > wavesCleared && at < afterWavesCleared) afterWavesCleared = at;
            }
            return afterWavesCleared != int.MaxValue;
        }
    }
}
