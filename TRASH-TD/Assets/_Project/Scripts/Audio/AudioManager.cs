using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TrashTD.Core.GameLoop;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Audio
{
    /// <summary>
    /// Shared (non-unit) sounds. Each maps to a file under Assets/_Project/Audio/Resources/SFX/ (see SfxPaths and
    /// Audio/SFX_GUIDE.md). Per-unit sounds go through UnitSfx instead.
    /// </summary>
    public enum SfxId
    {
        // UI
        UiHover,
        UiClick,
        UiBack,
        UiDenied,
        UiConfirm,
        UiCardShuffle,
        UiCardDeal,
        UiCardFlip,
        UiReroll,
        UiCardDiscard,
        TutorialVoice,  // Coalesce's talking blip, per typed character (a synth blip plays until a file exists)

        // Gameplay flow
        GameplayDeploy,
        GameplayRetreat,
        GameplayWaveStart,
        GameplayWaveCleared,
        GameplayEnemyDeath,
        GameplayOperatorDeath,
        GameplayLifeLost,
        GameplayVictory,
        GameplayDefeat,
        GameplayPause,
        GameplayResume,
        GameplayCardUpgrade,

        // Combat fallbacks and shared combat events
        GameplayRangedPhysicalAttack,
        GameplayMagicAttack,
        GameplayMeleeAttack,
        CombatStun,
        CombatMiss,

        // Special tiles (any stage)
        TileTrapTick,
        TileHoleEnter,
        TileHoleExit,

        // Stage environments
        Stage2ItemFall,
        Stage2ItemLandGlass,
        Stage2ItemLandMetal,
        Stage2ItemLandTools,
        Stage4HeatUp,
        Stage4Ignite,
        Stage4BurnTick,
        Stage4Extinguish,
        Stage5CarPass,
        Stage5CarHit,
        Stage6AcidTick
    }

    /// <summary>
    /// Loads project audio by convention and persists across scene changes.
    /// Put clips under Assets/_Project/Audio/Resources/Music or SFX.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        private const string MainMenuScene = "MainMenu";
        private const string GameplayScene = "GameplayTest";
        private const string MusicResourcesFolder = "Music/";
        private const string SfxResourcesFolder = "SFX/";
        private const string MusicVolumeKey = "TrashTD.MusicVolume";
        private const string SfxVolumeKey = "TrashTD.SfxVolume";
        private const float DefaultMusicVolume = 0.5f;
        private const float DefaultSfxVolume = 0.8f;

        // One-shots get their own source each, so a random pitch on one sound doesn't bend the others playing.
        private const int SfxVoiceCount = 24;
        // The same shared sound won't restart sooner than this (several burn/acid ticks in one frame play once).
        private const float MinSharedRepeatSeconds = 0.05f;
        private const float AmbienceFadeSpeed = 1.5f;
        private const float DefaultAmbienceLevel = 0.45f;
        private const float StopFadeSeconds = 0.04f;
        // Every stage's ambience is SFX/Stages/StageNN/Ambience (see PlayStageAmbience).
        private const string StageAmbienceClipName = "Ambience";

        /// <summary>
        /// File for each shared sound: "{folder}/{name}" under Resources/SFX. Variants ({name}_2, {name}_3, ...)
        /// are picked at random, as with unit sounds. This table is the list of shared file names.
        /// </summary>
        public static readonly IReadOnlyDictionary<SfxId, string> SfxPaths = new Dictionary<SfxId, string>
        {
            { SfxId.UiHover, "UI/ButtonHover" },
            { SfxId.UiClick, "UI/ButtonClick" },
            { SfxId.UiBack, "UI/ButtonBack" },
            { SfxId.UiDenied, "UI/ButtonDenied" },
            { SfxId.UiConfirm, "UI/ButtonConfirm" },
            { SfxId.UiCardShuffle, "UI/CardShuffle" },
            { SfxId.UiCardDeal, "UI/CardDeal" },
            { SfxId.UiCardFlip, "UI/CardFlip" },
            { SfxId.UiReroll, "UI/Reroll" },
            { SfxId.UiCardDiscard, "UI/CardDiscard" },
            { SfxId.TutorialVoice, "UI/TutorialVoice" },

            { SfxId.GameplayDeploy, "Gameplay/Deploy" },
            { SfxId.GameplayRetreat, "Gameplay/Retreat" },
            { SfxId.GameplayWaveStart, "Gameplay/WaveStart" },
            { SfxId.GameplayWaveCleared, "Gameplay/WaveCleared" },
            { SfxId.GameplayEnemyDeath, "Gameplay/EnemyDeath" },
            { SfxId.GameplayOperatorDeath, "Gameplay/OperatorDeath" },
            { SfxId.GameplayLifeLost, "Gameplay/LifeLost" },
            { SfxId.GameplayVictory, "Gameplay/Victory" },
            { SfxId.GameplayDefeat, "Gameplay/Defeat" },
            { SfxId.GameplayPause, "Gameplay/Pause" },
            { SfxId.GameplayResume, "Gameplay/Resume" },
            { SfxId.GameplayCardUpgrade, "Gameplay/CardUpgrade" },

            { SfxId.GameplayRangedPhysicalAttack, "Combat/RangedPhysicalAttack" },
            { SfxId.GameplayMagicAttack, "Combat/MagicAttack" },
            { SfxId.GameplayMeleeAttack, "Combat/MeleeAttack" },
            { SfxId.CombatStun, "Combat/Stun" },
            { SfxId.CombatMiss, "Combat/Miss" },
            
            { SfxId.TileTrapTick, "Tiles/TrapTick" },
            { SfxId.TileHoleEnter, "Tiles/HoleEnter" },
            { SfxId.TileHoleExit, "Tiles/HoleExit" },

            { SfxId.Stage2ItemFall, "Stages/Stage02/ItemFall" },
            { SfxId.Stage2ItemLandGlass, "Stages/Stage02/ItemLandGlass" },
            { SfxId.Stage2ItemLandMetal, "Stages/Stage02/ItemLandMetal" },
            { SfxId.Stage2ItemLandTools, "Stages/Stage02/ItemLandTools" },
            { SfxId.Stage4HeatUp, "Stages/Stage04/HeatUp" },
            { SfxId.Stage4Ignite, "Stages/Stage04/Ignite" },
            { SfxId.Stage4BurnTick, "Stages/Stage04/BurnTick" },
            { SfxId.Stage4Extinguish, "Stages/Stage04/Extinguish" },
            { SfxId.Stage5CarPass, "Stages/Stage05/CarPass" },
            { SfxId.Stage5CarHit, "Stages/Stage05/CarHit" },
            { SfxId.Stage6AcidTick, "Stages/Stage06/AcidTick" }
        };

        public static AudioManager Instance { get; private set; }

        [SerializeField, Range(0f, 1f)] private float musicVolume = DefaultMusicVolume;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = DefaultSfxVolume;

        private AudioSource musicSource;
        // Each voice plays one sound at a time, so a sound can be stopped on its own (see StopSfx).
        private readonly List<AudioSource> sfxVoices = new List<AudioSource>();
        private float[] voiceLevels;     // per-sound volume before the player's SFX volume
        private bool[] voiceStopping;    // fading out after StopSfx
        private int nextVoice;
        private SfxVolumeSettings volumeSettings;
        private readonly Dictionary<SfxId, float> sharedLastPlayed = new Dictionary<SfxId, float>();
        private AudioSource ambienceSource;
        private float ambienceLevel;
        private AudioClip mainMenuMusic;
        private AudioClip gameplayMusic;
        private EnemyManager enemyManager;
        private OperatorManager operatorManager;
        private GameManager gameManager;
        private WaveManager waveManager;
        private RarityUpgradeSystem upgradeSystem;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateInstance()
        {
            if (Instance != null) return;

            GameObject audioObject = new GameObject("AudioManager");
            DontDestroyOnLoad(audioObject);
            audioObject.AddComponent<AudioManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.loop = true;
            musicSource.playOnAwake = false;
            musicSource.spatialBlend = 0f;
            for (int i = 0; i < SfxVoiceCount; i++)
            {
                AudioSource voice = gameObject.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.spatialBlend = 0f;
                sfxVoices.Add(voice);
            }
            voiceLevels = new float[SfxVoiceCount];
            voiceStopping = new bool[SfxVoiceCount];
            volumeSettings = Resources.Load<SfxVolumeSettings>(SfxVolumeSettings.ResourceName);
            ambienceSource = gameObject.AddComponent<AudioSource>();
            ambienceSource.loop = true;
            ambienceSource.playOnAwake = false;
            ambienceSource.spatialBlend = 0f;
            ambienceSource.volume = 0f;

            musicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume);
            sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, DefaultSfxVolume);
            ApplyVolumes();

            mainMenuMusic = Resources.Load<AudioClip>(MusicResourcesFolder + "MainMenuMusic");
            gameplayMusic = Resources.Load<AudioClip>(MusicResourcesFolder + "GameplayMusic");
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void Start()
        {
            HandleSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        // Fades run on unscaled time, so they behave the same at any game speed or while paused.
        private void Update()
        {
            UpdateStoppingVoices();

            if (!ambienceSource.isPlaying) return;

            float target = sfxVolume * ambienceLevel;
            ambienceSource.volume = Mathf.MoveTowards(ambienceSource.volume, target, AmbienceFadeSpeed * Time.unscaledDeltaTime);
            if (ambienceLevel <= 0f && ambienceSource.volume <= 0f) ambienceSource.Stop();
        }

        private void UpdateStoppingVoices()
        {
            for (int i = 0; i < sfxVoices.Count; i++)
            {
                if (!voiceStopping[i]) continue;

                AudioSource voice = sfxVoices[i];
                float fullVolume = sfxVolume * voiceLevels[i];
                voice.volume = Mathf.MoveTowards(voice.volume, 0f, fullVolume / StopFadeSeconds * Time.unscaledDeltaTime);
                if (voice.volume <= 0f || !voice.isPlaying)
                {
                    voice.Stop();
                    voiceStopping[i] = false;
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                UnsubscribeFromGameplayEvents();
                Instance = null;
            }
        }

        /// <summary>The shared clip for this id (a random variant if there are several), or null if none exists yet.</summary>
        public AudioClip GetClip(SfxId id)
        {
            if (!SfxPaths.TryGetValue(id, out string path)) return null;

            int slash = path.LastIndexOf('/');
            return UnitSfx.GetRandomClip(SfxResourcesFolder + path.Substring(0, slash), path.Substring(slash + 1));
        }

        public void PlayMainMenuMusic()
        {
            PlayMusic(mainMenuMusic);
        }

        public void PlayGameplayMusic()
        {
            PlayMusic(gameplayMusic);
        }

        /// <summary>
        /// Plays the stage's own track, Music/Stages/Stage02 for "STAGE_02" (the sandbox passes the map it shows),
        /// or GameplayMusic if that stage has none yet. The same track keeps playing across retries.
        /// </summary>
        public void PlayStageMusic(string stageId)
        {
            string stageFolder = ToStageFolderName(stageId);
            AudioClip stageMusic = stageFolder != null
                ? Resources.Load<AudioClip>(MusicResourcesFolder + "Stages/" + stageFolder)
                : null;

            PlayMusic(stageMusic != null ? stageMusic : gameplayMusic);
        }

        /// <summary>
        /// Loops the stage's ambience, SFX/Stages/Stage02/Ambience for "STAGE_02" (a random one of Ambience,
        /// Ambience_2, ... if there are several), or fades ambience out if the stage has none. Keeps playing across retries.
        /// </summary>
        public void PlayStageAmbience(string stageId, float level = DefaultAmbienceLevel)
        {
            string stageFolder = ToStageFolderName(stageId);
            AudioClip clip = stageFolder != null
                ? UnitSfx.GetRandomClip(SfxResourcesFolder + "Stages/" + stageFolder, StageAmbienceClipName)
                : null;
            PlayAmbience(clip, level);
        }

        /// <summary>"STAGE_02" -> "Stage02", the folder/file name used for that stage's music and SFX.</summary>
        public static string ToStageFolderName(string stageId)
        {
            return string.IsNullOrEmpty(stageId) ? null : stageId.Replace("STAGE_", "Stage");
        }

        /// <summary>Plays a shared sound. Returns the voice playing it (for StopSfx), or null if it has no file or was throttled.</summary>
        public AudioSource PlaySfx(SfxId id, float volume = 1f, float pitch = 1f)
        {
            float now = Time.unscaledTime;
            if (sharedLastPlayed.TryGetValue(id, out float last) && now - last < MinSharedRepeatSeconds) return null;
            sharedLastPlayed[id] = now;

            return PlaySfx(GetClip(id), volume, pitch);
        }

        /// <summary>Plays the sound if its file exists yet, otherwise the fallback (keeps placeholders audible).</summary>
        public AudioSource PlaySfxOrFallback(SfxId id, SfxId fallback, float volume = 1f, float pitch = 1f)
        {
            return PlaySfx(GetClip(id) != null ? id : fallback, volume, pitch);
        }

        /// <summary>
        /// Plays a clip on its own voice, scaled by its row in SfxVolumeSettings (if any).
        /// Returns the voice playing it (for StopSfx), or null if nothing played.
        /// </summary>
        public AudioSource PlaySfx(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (clip == null || sfxVoices.Count == 0) return null;

            int index = GetFreeVoiceIndex();
            AudioSource voice = sfxVoices[index];
            float clipVolume = volumeSettings != null ? volumeSettings.GetVolume(clip) : 1f;
            voiceLevels[index] = Mathf.Clamp01(volume) * clipVolume;
            voiceStopping[index] = false;

            voice.Stop();
            voice.clip = clip;
            voice.pitch = pitch;
            voice.volume = sfxVolume * voiceLevels[index];
            voice.Play();
            return voice;
        }

        /// <summary>
        /// Quickly fades out a sound started by PlaySfx (about 0.04 s, so it doesn't click). Does nothing if that voice
        /// has finished or moved on to another clip.
        /// </summary>
        public void StopSfx(AudioSource voice, AudioClip clip)
        {
            if (voice == null || clip == null || !voice.isPlaying || voice.clip != clip) return;

            int index = sfxVoices.IndexOf(voice);
            if (index >= 0) voiceStopping[index] = true;
        }

        /// <summary>Starts a looping ambience (fades in), replacing any current one. A null clip fades ambience out.</summary>
        public void PlayAmbience(AudioClip clip, float level = DefaultAmbienceLevel)
        {
            if (clip == null)
            {
                StopAmbience();
                return;
            }

            if (ambienceSource.clip != clip || !ambienceSource.isPlaying)
            {
                ambienceSource.clip = clip;
                ambienceSource.volume = 0f;
                ambienceSource.Play();
            }
            ambienceLevel = Mathf.Clamp01(level);
        }

        /// <summary>Fades the stage ambience out.</summary>
        public void StopAmbience()
        {
            ambienceLevel = 0f;
        }

        public float MusicVolume => musicVolume;
        public float SfxVolume => sfxVolume;

        public void SetMusicVolume(float volume)
        {
            musicVolume = Mathf.Clamp01(volume);
            if (musicSource != null) musicSource.volume = musicVolume;
            PlayerPrefs.SetFloat(MusicVolumeKey, musicVolume);
            PlayerPrefs.Save();
        }

        public void SetSfxVolume(float volume)
        {
            sfxVolume = Mathf.Clamp01(volume);
            ApplyVolumes();
            PlayerPrefs.SetFloat(SfxVolumeKey, sfxVolume);
            PlayerPrefs.Save();
        }

        private void ApplyVolumes()
        {
            if (musicSource != null) musicSource.volume = musicVolume;
            for (int i = 0; i < sfxVoices.Count; i++)
                if (sfxVoices[i] != null && !voiceStopping[i]) sfxVoices[i].volume = sfxVolume * voiceLevels[i];
            if (ambienceSource != null) ambienceSource.volume = sfxVolume * ambienceLevel;
        }

        private int GetFreeVoiceIndex()
        {
            for (int i = 0; i < sfxVoices.Count; i++)
            {
                int index = (nextVoice + i) % sfxVoices.Count;
                if (!sfxVoices[index].isPlaying)
                {
                    nextVoice = (index + 1) % sfxVoices.Count;
                    return index;
                }
            }

            // All busy: take over the voice closest to finishing its sound.
            int stolen = 0;
            float mostDone = -1f;
            for (int i = 0; i < sfxVoices.Count; i++)
            {
                AudioSource voice = sfxVoices[i];
                float done = voice.clip != null && voice.clip.length > 0f ? voice.time / voice.clip.length : 1f;
                if (done > mostDone)
                {
                    mostDone = done;
                    stolen = i;
                }
            }
            return stolen;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            UnsubscribeFromGameplayEvents();
            StopAmbience();

            if (scene.name == MainMenuScene)
            {
                PlayMainMenuMusic();
                return;
            }

            if (scene.name != GameplayScene) return;

            PlayGameplayMusic();
            SubscribeToGameplayEvents();
        }

        private void PlayMusic(AudioClip clip)
        {
            if (musicSource == null || clip == null || musicSource.clip == clip) return;

            musicSource.clip = clip;
            musicSource.Play();
        }

        private void SubscribeToGameplayEvents()
        {
            enemyManager = FindAnyObjectByType<EnemyManager>();
            operatorManager = FindAnyObjectByType<OperatorManager>();
            gameManager = FindAnyObjectByType<GameManager>();
            waveManager = FindAnyObjectByType<WaveManager>();
            upgradeSystem = FindAnyObjectByType<RarityUpgradeSystem>();

            if (enemyManager != null) enemyManager.OnEnemyDied += HandleEnemyDied;
            if (operatorManager != null)
            {
                operatorManager.OnOperatorDeployed += HandleOperatorDeployed;
                operatorManager.OnOperatorRetreated += HandleOperatorRetreated;
            }
            if (gameManager != null)
            {
                gameManager.OnStageVictory += HandleStageVictory;
                gameManager.OnStageDefeat += HandleStageDefeat;
            }
            if (waveManager != null) waveManager.OnSingleWaveFinished += HandleWaveCleared;
            if (upgradeSystem != null) upgradeSystem.OnOperatorUpgraded += HandleOperatorUpgraded;
        }

        private void UnsubscribeFromGameplayEvents()
        {
            if (enemyManager != null) enemyManager.OnEnemyDied -= HandleEnemyDied;
            if (operatorManager != null)
            {
                operatorManager.OnOperatorDeployed -= HandleOperatorDeployed;
                operatorManager.OnOperatorRetreated -= HandleOperatorRetreated;
            }
            if (gameManager != null)
            {
                gameManager.OnStageVictory -= HandleStageVictory;
                gameManager.OnStageDefeat -= HandleStageDefeat;
            }
            if (waveManager != null) waveManager.OnSingleWaveFinished -= HandleWaveCleared;
            if (upgradeSystem != null) upgradeSystem.OnOperatorUpgraded -= HandleOperatorUpgraded;

            enemyManager = null;
            operatorManager = null;
            gameManager = null;
            waveManager = null;
            upgradeSystem = null;
        }

        private void HandleEnemyDied(EnemyBase enemy)
        {
            if (enemy == null || !UnitSfx.Play(enemy.Data, UnitSfxEvent.Death, 0.8f))
                PlaySfx(SfxId.GameplayEnemyDeath, 0.75f);
        }

        private void HandleOperatorDeployed(OperatorBase op)
        {
            if (op == null || !UnitSfx.Play(op.Data, UnitSfxEvent.Deploy))
                PlaySfx(SfxId.GameplayDeploy);
        }

        // Deaths also raise OnOperatorRetreated; they already played a death sound, so only real retreats play this.
        private void HandleOperatorRetreated(OperatorBase op)
        {
            if (op != null && op.CurrentHP <= 0) return;
            PlaySfx(SfxId.GameplayRetreat);
        }

        private void HandleStageVictory(int stars) => PlaySfx(SfxId.GameplayVictory);
        private void HandleStageDefeat() => PlaySfx(SfxId.GameplayDefeat);
        private void HandleWaveCleared() => PlaySfx(SfxId.GameplayWaveCleared);

        private void HandleOperatorUpgraded(OperatorData data, OperatorRarity oldRarity, OperatorRarity newRarity) =>
            PlaySfx(SfxId.GameplayCardUpgrade);
    }
}
