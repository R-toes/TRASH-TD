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
        public static bool Play(OperatorData data, UnitSfxEvent sfxEvent, float volume = 1f, bool ignoreThrottle = false)
        {
            return PlayFrom(GetFolder(data), sfxEvent.ToString(), volume, out _, ignoreThrottle);
        }

        /// <summary>
        /// As Play, also giving the voice now playing the clip (null if it was throttled), so the caller can stop it
        /// early with AudioManager.StopSfx.
        /// </summary>
        public static bool Play(OperatorData data, UnitSfxEvent sfxEvent, out AudioSource voice, float volume = 1f, bool ignoreThrottle = false)
        {
            return Play(data, sfxEvent, out voice, out _, volume, ignoreThrottle);
        }

        public static bool Play(OperatorData data, UnitSfxEvent sfxEvent, out AudioSource voice, out AudioClip clip, float volume = 1f, bool ignoreThrottle = false)
        {
            return PlayFrom(GetFolder(data), sfxEvent.ToString(), volume, out voice, out clip, ignoreThrottle);
        }

        /// <summary>Plays the enemy's clip for the event. Returns false if it has none (throttled repeats count as played).</summary>
        public static bool Play(EnemyData data, UnitSfxEvent sfxEvent, float volume = 1f, bool ignoreThrottle = false)
        {
            return PlayFrom(GetFolder(data), sfxEvent.ToString(), volume, out _, ignoreThrottle);
        }

        /// <summary>As Play, also giving the voice now playing the clip (null if it was throttled).</summary>
        public static bool Play(EnemyData data, UnitSfxEvent sfxEvent, out AudioSource voice, float volume = 1f, bool ignoreThrottle = false)
        {
            return PlayFrom(GetFolder(data), sfxEvent.ToString(), volume, out voice, ignoreThrottle);
        }

        /// <summary>
        /// Plays a named attack clip (e.g. "Charge1" -> Attack_Charge1, Charge1, or "Charge 1"), falling back to the plain
        /// Attack clip, so a unit with a single Attack file uses it for every charge level.
        /// </summary>
        public static bool PlayAttack(OperatorData data, string attackVariant, out AudioSource voice, float volume = 1f, bool ignoreThrottle = false)
        {
            return PlayAttack(data, attackVariant, out voice, out _, volume, ignoreThrottle);
        }

        public static bool PlayAttack(OperatorData data, string attackVariant, out AudioSource voice, out AudioClip clip, float volume = 1f, bool ignoreThrottle = false)
        {
            voice = null;
            clip = null;
            string folder = GetFolder(data);
            if (!string.IsNullOrEmpty(attackVariant))
            {
                if (PlayFrom(folder, AttackClipName(attackVariant), volume, out voice, out clip, ignoreThrottle)) return true;
                if (PlayFrom(folder, attackVariant, volume, out voice, out clip, ignoreThrottle)) return true;
                if (attackVariant.StartsWith("Charge") && attackVariant.Length > 6)
                {
                    string spaced = "Charge " + attackVariant.Substring(6);
                    if (PlayFrom(folder, spaced, volume, out voice, out clip, ignoreThrottle)) return true;
                }
            }
            return PlayFrom(folder, nameof(UnitSfxEvent.Attack), volume, out voice, out clip, ignoreThrottle);
        }

        public static bool PlayAttack(OperatorData data, string attackVariant, float volume = 1f, bool ignoreThrottle = false)
        {
            return PlayAttack(data, attackVariant, out _, volume, ignoreThrottle);
        }

        public static string AttackClipName(string variantName) => $"{nameof(UnitSfxEvent.Attack)}_{variantName}";

        /// <summary>
        /// Plays one of the operator's named ability clips ("Dodge" -> Ability_Dodge or Dodge), falling back to its plain
        /// Ability clip, so a unit with a single Ability file uses it for every trigger.
        /// </summary>
        public static bool PlayAbility(OperatorData data, string abilityName, float volume = 1f, bool ignoreThrottle = false)
        {
            string folder = GetFolder(data);
            if (!string.IsNullOrEmpty(abilityName))
            {
                if (PlayFrom(folder, AbilityClipName(abilityName), volume, out _, ignoreThrottle)) return true;
                if (PlayFrom(folder, abilityName, volume, out _, ignoreThrottle)) return true;
            }
            return PlayFrom(folder, nameof(UnitSfxEvent.Ability), volume, out _, ignoreThrottle);
        }

        /// <summary>Plays a sound directly from an audio folder by event or file name (e.g. "SFX/Operators/Medic/Panacea", "Impact").</summary>
        public static bool PlayFromFolder(string folder, string eventName, float volume = 1f, bool ignoreThrottle = false)
        {
            return PlayFrom(folder, eventName, volume, out _, out _, ignoreThrottle);
        }

        public static bool PlayFromFolder(string folder, string eventName, out AudioSource voice, float volume = 1f, bool ignoreThrottle = false)
        {
            return PlayFrom(folder, eventName, volume, out voice, out _, ignoreThrottle);
        }

        public static bool PlayFromFolder(string folder, string eventName, out AudioSource voice, out AudioClip clip, float volume = 1f, bool ignoreThrottle = false)
        {
            return PlayFrom(folder, eventName, volume, out voice, out clip, ignoreThrottle);
        }

        /// <summary>
        /// Plays a random clip matching any of the candidate event names in the folder.
        /// Pools all matching clips so multiple event names (e.g. "Charge 1", "Charge 2", "Charge 3") can be randomized together.
        /// </summary>
        public static bool PlayRandomFromFolder(string folder, string[] eventNames, out AudioSource voice, out AudioClip clip, float volume = 1f, bool ignoreThrottle = false)
        {
            voice = null;
            clip = null;
            if (folder == null || eventNames == null || eventNames.Length == 0) return false;

            var pool = new List<AudioClip>();
            for (int i = 0; i < eventNames.Length; i++)
            {
                List<AudioClip> clips = GetClips(folder, eventNames[i]);
                if (clips != null)
                {
                    for (int j = 0; j < clips.Count; j++)
                    {
                        if (!pool.Contains(clips[j])) pool.Add(clips[j]);
                    }
                }
            }

            if (pool.Count == 0) return false;

            string key = folder + "/pooled_" + string.Join("_", eventNames);
            float now = Time.unscaledTime;
            if (!ignoreThrottle && LastPlayed.TryGetValue(key, out float last) && now - last < MinRepeatSeconds) return true;
            LastPlayed[key] = now;

            clip = pool[Random.Range(0, pool.Count)];
            voice = AudioManager.Instance?.PlaySfx(clip, volume, Random.Range(0.96f, 1.04f));
            return true;
        }

        public static bool PlayRandomFromFolder(string folder, string[] eventNames, out AudioSource voice, float volume = 1f, bool ignoreThrottle = false)
        {
            return PlayRandomFromFolder(folder, eventNames, out voice, out _, volume, ignoreThrottle);
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

        private static bool PlayFrom(string folder, string eventName, float volume, out AudioSource voice, bool ignoreThrottle = false)
        {
            return PlayFrom(folder, eventName, volume, out voice, out _, ignoreThrottle);
        }

        private static bool PlayFrom(string folder, string eventName, float volume, out AudioSource voice, out AudioClip clip, bool ignoreThrottle = false)
        {
            voice = null;
            clip = null;
            List<AudioClip> clips = GetClips(folder, eventName);
            if (clips == null || clips.Count == 0) return false;

            // A throttled repeat still counts as handled, so callers don't fall back to the generic sound.
            string key = folder + "/" + eventName;
            float now = Time.unscaledTime;
            if (!ignoreThrottle && LastPlayed.TryGetValue(key, out float last) && now - last < MinRepeatSeconds) return true;
            LastPlayed[key] = now;

            clip = clips[Random.Range(0, clips.Count)];
            voice = AudioManager.Instance?.PlaySfx(clip, volume, Random.Range(0.96f, 1.04f));
            return true;
        }

        private static List<AudioClip> GetClips(string folder, string eventName)
        {
            if (folder == null) return null;

            if (!FolderCache.TryGetValue(folder, out Dictionary<string, List<AudioClip>> byEvent))
            {
                byEvent = new Dictionary<string, List<AudioClip>>(System.StringComparer.OrdinalIgnoreCase);
                foreach (AudioClip clip in Resources.LoadAll<AudioClip>(folder))
                {
                    string clipEvent = StripVariant(clip.name);
                    AddClipToMap(byEvent, clipEvent, clip);

                    // Also index with spaces stripped so "Charge 1" matches "Charge1" and vice-versa
                    string noSpaces = clipEvent.Replace(" ", "");
                    if (!string.Equals(noSpaces, clipEvent, System.StringComparison.OrdinalIgnoreCase))
                    {
                        AddClipToMap(byEvent, noSpaces, clip);
                    }
                }
                FolderCache[folder] = byEvent;
            }

            if (byEvent.TryGetValue(eventName, out List<AudioClip> clips)) return clips;
            string cleanEvent = eventName.Replace(" ", "");
            return byEvent.TryGetValue(cleanEvent, out clips) ? clips : null;
        }

        private static void AddClipToMap(Dictionary<string, List<AudioClip>> map, string key, AudioClip clip)
        {
            if (!map.TryGetValue(key, out List<AudioClip> list))
                map[key] = list = new List<AudioClip>();
            list.Add(clip);
        }

        /// <summary>"Attack_2" -> "Attack", "Charge 1_2" -> "Charge 1", "Attack (2)" -> "Attack", "Ability2" -> "Ability"; names without a numeric suffix are unchanged.</summary>
        public static string StripVariant(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return "";

            // 1. Underscore or hyphen syntax: "Ability_2", "Ability-2", "Attack_03"
            int sep = Mathf.Max(clipName.LastIndexOf('_'), clipName.LastIndexOf('-'));
            if (sep > 0 && sep < clipName.Length - 1)
            {
                bool allDigits = true;
                for (int i = sep + 1; i < clipName.Length; i++)
                {
                    if (!char.IsDigit(clipName[i])) { allDigits = false; break; }
                }
                if (allDigits) return clipName.Substring(0, sep);
            }

            // 2. Parentheses syntax: "Attack (2)", "Ability (3)"
            if (clipName.EndsWith(")") && clipName.Contains("("))
            {
                int openParen = clipName.LastIndexOf('(');
                if (openParen > 0)
                {
                    string inside = clipName.Substring(openParen + 1, clipName.Length - openParen - 2).Trim();
                    bool allDigits = inside.Length > 0;
                    for (int i = 0; i < inside.Length; i++)
                    {
                        if (!char.IsDigit(inside[i])) { allDigits = false; break; }
                    }
                    if (allDigits) return clipName.Substring(0, openParen).TrimEnd();
                }
            }

            // 3. Trailing space + digits: "Ability 2", "Attack 3" (except for "Charge 1", "Charge 2", "Charge 3")
            int lastSpace = clipName.LastIndexOf(' ');
            if (lastSpace > 0 && lastSpace < clipName.Length - 1)
            {
                string baseName = clipName.Substring(0, lastSpace);
                if (!baseName.Equals("Charge", System.StringComparison.OrdinalIgnoreCase))
                {
                    bool allDigits = true;
                    for (int i = lastSpace + 1; i < clipName.Length; i++)
                    {
                        if (!char.IsDigit(clipName[i])) { allDigits = false; break; }
                    }
                    if (allDigits) return baseName;
                }
            }

            // 4. Direct trailing digits on known event names: "Ability2" -> "Ability", "Deploy2" -> "Deploy", "Attack2" -> "Attack"
            for (int i = clipName.Length - 1; i >= 1; i--)
            {
                if (!char.IsDigit(clipName[i]))
                {
                    if (i < clipName.Length - 1)
                    {
                        string prefix = clipName.Substring(0, i + 1);
                        if (prefix.Equals("Ability", System.StringComparison.OrdinalIgnoreCase) ||
                            prefix.Equals("Deploy", System.StringComparison.OrdinalIgnoreCase) ||
                            prefix.Equals("Attack", System.StringComparison.OrdinalIgnoreCase) ||
                            prefix.Equals("Impact", System.StringComparison.OrdinalIgnoreCase) ||
                            prefix.Equals("Death", System.StringComparison.OrdinalIgnoreCase) ||
                            prefix.Equals("Heal", System.StringComparison.OrdinalIgnoreCase))
                        {
                            return prefix;
                        }
                    }
                    break;
                }
            }

            return clipName;
        }
    }
}
