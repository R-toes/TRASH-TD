using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Audio
{
    /// <summary>Sound events a unit can have its own clip for.</summary>
    public enum UnitSfxEvent
    {
        /// <summary>Operator placed on the field.</summary>
        Deploy,
        /// <summary>Attack starts: swing, shot, cast, or a Medic's heal.</summary>
        Attack,
        /// <summary>The attack lands: projectile hit, explosion.</summary>
        Impact,
        /// <summary>
        /// The unit's special ability triggers (shield pop, knockback, stun, retaliation, reactive heal).
        /// Units with several triggers also use named clips (Ability_Dodge, ...) via UnitSfx.PlayAbility.
        /// </summary>
        Ability,
        /// <summary>The unit dies.</summary>
        Death
    }

    /// <summary>
    /// Per-unit sound effects, found by convention so adding a sound only means dropping a file in place:
    ///   Assets/_Project/Audio/Resources/SFX/Operators/{Class}/{OperatorName}/{Event}.wav
    ///   Assets/_Project/Audio/Resources/SFX/Enemies/{Archetype}/{EnemyName}/{Event}.wav
    /// {OperatorName}/{EnemyName} is the unit's display name with spaces and symbols removed ("Chi Paw" -> ChiPaw);
    /// {Event} is a UnitSfxEvent name. Variants (Attack_2, Attack_3, ...) are picked at random. Any audio format
    /// Unity imports works. Missing files are fine: callers fall back to the shared generic sounds.
    /// See Assets/_Project/Audio/SFX_GUIDE.md.
    /// </summary>
    public static class UnitSfx
    {
        public const string OperatorsFolder = "SFX/Operators";
        public const string EnemiesFolder = "SFX/Enemies";

        // The same unit's same sound won't restart sooner than this, so crowds and AoE hits don't stack it.
        private const float MinRepeatSeconds = 0.08f;

        private static readonly Dictionary<string, Dictionary<string, List<AudioClip>>> FolderCache =
            new Dictionary<string, Dictionary<string, List<AudioClip>>>();
        private static readonly Dictionary<string, float> LastPlayed = new Dictionary<string, float>();

        // Fresh lookups every play session, so newly added files are found even without a domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            FolderCache.Clear();
            LastPlayed.Clear();
        }

        public static string GetFolder(OperatorData data)
        {
            return data == null ? null : $"{OperatorsFolder}/{data.operatorClass}/{ToFolderName(data.operatorName)}";
        }

        public static string GetFolder(EnemyData data)
        {
            return data == null ? null : $"{EnemiesFolder}/{data.archetype}/{ToFolderName(data.enemyName)}";
        }

        /// <summary>Plays the operator's clip for the event. Returns false if it has none (throttled repeats count as played).</summary>
        public static bool Play(OperatorData data, UnitSfxEvent sfxEvent, float volume = 1f)
        {
            return PlayFrom(GetFolder(data), sfxEvent.ToString(), volume);
        }

        /// <summary>Plays the enemy's clip for the event. Returns false if it has none (throttled repeats count as played).</summary>
        public static bool Play(EnemyData data, UnitSfxEvent sfxEvent, float volume = 1f)
        {
            return PlayFrom(GetFolder(data), sfxEvent.ToString(), volume);
        }

        /// <summary>
        /// Plays one of the operator's named ability clips ("Dodge" -> Ability_Dodge), falling back to its plain
        /// Ability clip, so a unit with a single Ability file uses it for every trigger.
        /// </summary>
        public static bool PlayAbility(OperatorData data, string abilityName, float volume = 1f)
        {
            string folder = GetFolder(data);
            if (!string.IsNullOrEmpty(abilityName) && PlayFrom(folder, AbilityClipName(abilityName), volume)) return true;
            return PlayFrom(folder, nameof(UnitSfxEvent.Ability), volume);
        }

        public static string AbilityClipName(string abilityName) => $"{nameof(UnitSfxEvent.Ability)}_{abilityName}";

        public static bool HasClip(OperatorData data, UnitSfxEvent sfxEvent) => GetClips(GetFolder(data), sfxEvent.ToString()) != null;
        public static bool HasClip(EnemyData data, UnitSfxEvent sfxEvent) => GetClips(GetFolder(data), sfxEvent.ToString()) != null;

        /// <summary>
        /// Shared (non-unit) sounds use the same lookup: "SFX/Gameplay" + "Victory" finds Victory, Victory_2, ...
        /// Returns a random variant, or null if the folder has none.
        /// </summary>
        public static AudioClip GetRandomClip(string folder, string clipName)
        {
            List<AudioClip> clips = GetClips(folder, clipName);
            return clips == null ? null : clips[Random.Range(0, clips.Count)];
        }

        /// <summary>"Chi Paw" -> "ChiPaw", "Stag-ger" -> "Stagger".</summary>
        public static string ToFolderName(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return "Unnamed";

            var builder = new StringBuilder(displayName.Length);
            foreach (char c in displayName)
                if (char.IsLetterOrDigit(c)) builder.Append(c);
            return builder.Length > 0 ? builder.ToString() : "Unnamed";
        }

        private static bool PlayFrom(string folder, string eventName, float volume)
        {
            List<AudioClip> clips = GetClips(folder, eventName);
            if (clips == null) return false;

            // A throttled repeat still counts as handled, so callers don't fall back to the generic sound.
            string key = folder + "/" + eventName;
            float now = Time.unscaledTime;
            if (LastPlayed.TryGetValue(key, out float last) && now - last < MinRepeatSeconds) return true;
            LastPlayed[key] = now;

            AudioClip clip = clips[Random.Range(0, clips.Count)];
            AudioManager.Instance?.PlaySfx(clip, volume, Random.Range(0.96f, 1.04f));
            return true;
        }

        private static List<AudioClip> GetClips(string folder, string eventName)
        {
            if (folder == null) return null;

            if (!FolderCache.TryGetValue(folder, out Dictionary<string, List<AudioClip>> byEvent))
            {
                byEvent = new Dictionary<string, List<AudioClip>>();
                foreach (AudioClip clip in Resources.LoadAll<AudioClip>(folder))
                {
                    string clipEvent = StripVariant(clip.name);
                    if (!byEvent.TryGetValue(clipEvent, out List<AudioClip> list))
                        byEvent[clipEvent] = list = new List<AudioClip>();
                    list.Add(clip);
                }
                FolderCache[folder] = byEvent;
            }

            return byEvent.TryGetValue(eventName, out List<AudioClip> clips) ? clips : null;
        }

        /// <summary>"Attack_2" -> "Attack", "Ability_Dodge_2" -> "Ability_Dodge"; names without a numeric suffix are unchanged.</summary>
        private static string StripVariant(string clipName)
        {
            int underscore = clipName.LastIndexOf('_');
            if (underscore <= 0 || underscore == clipName.Length - 1) return clipName;

            for (int i = underscore + 1; i < clipName.Length; i++)
                if (!char.IsDigit(clipName[i])) return clipName;
            return clipName.Substring(0, underscore);
        }
    }
}
