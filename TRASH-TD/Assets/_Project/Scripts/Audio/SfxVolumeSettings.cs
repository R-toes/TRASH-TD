using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Audio
{
    /// <summary>
    /// Per-clip volume for sound effects, edited in the Inspector. The asset lives at
    /// Assets/_Project/Audio/Resources/SfxVolumeSettings.asset; AudioManager applies it to every sound it plays
    /// (unit, shared and UI sounds). Clips not listed play at full volume. Edits apply live, even in Play mode.
    /// </summary>
    [CreateAssetMenu(fileName = ResourceName, menuName = "TRASH TD/Audio/SFX Volume Settings")]
    public sealed class SfxVolumeSettings : ScriptableObject
    {
        public const string ResourceName = "SfxVolumeSettings";

        [Serializable]
        public sealed class Entry
        {
            [Tooltip("The audio file to adjust (drag it here from the Project window).")]
            public AudioClip clip;

            [Tooltip("1 = as recorded. Unity can't play a clip louder than recorded; to boost one, raise it in an audio editor.")]
            [Range(0f, 1f)] public float volume = 1f;
        }

        [Tooltip("One row per clip you want quieter. Clips not listed play at full volume.")]
        [SerializeField] private List<Entry> clips = new List<Entry>();

        private Dictionary<AudioClip, Entry> lookup;

        /// <summary>Volume multiplier for this clip (1 if it isn't listed).</summary>
        public float GetVolume(AudioClip clip)
        {
            if (clip == null) return 1f;

            if (lookup == null)
            {
                lookup = new Dictionary<AudioClip, Entry>();
                foreach (Entry entry in clips)
                    if (entry != null && entry.clip != null) lookup[entry.clip] = entry;
            }

            return lookup.TryGetValue(clip, out Entry found) ? found.volume : 1f;
        }

        // Inspector edits (new rows, swapped clips) rebuild the lookup; slider changes are read live anyway.
        private void OnValidate() => lookup = null;
    }
}
