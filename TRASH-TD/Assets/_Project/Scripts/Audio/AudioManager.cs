using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TrashTD.Enemies;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Audio
{
    public enum SfxId
    {
        UiHover,
        UiClick,
        UiBack,
        UiDenied,
        UiConfirm,
        GameplayDeploy,
        GameplayRetreat,
        GameplayWaveStart,
        GameplayEnemyDeath,
        GameplayLifeLost,
        GameplayPause,
        GameplayResume
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

        private static readonly Dictionary<SfxId, string> SfxNames = new Dictionary<SfxId, string>
        {
            { SfxId.UiHover, "UI_ButtonHover" },
            { SfxId.UiClick, "UI_ButtonClick" },
            { SfxId.UiBack, "UI_ButtonBack" },
            { SfxId.UiDenied, "UI_ButtonDenied" },
            { SfxId.UiConfirm, "UI_ButtonConfirm" },
            { SfxId.GameplayDeploy, "Gameplay_Deploy" },
            { SfxId.GameplayRetreat, "Gameplay_Retreat" },
            { SfxId.GameplayWaveStart, "Gameplay_WaveStart" },
            { SfxId.GameplayEnemyDeath, "Gameplay_EnemyDeath" },
            { SfxId.GameplayLifeLost, "Gameplay_LifeLost" },
            { SfxId.GameplayPause, "Gameplay_Pause" },
            { SfxId.GameplayResume, "Gameplay_Resume" }
        };

        public static AudioManager Instance { get; private set; }

        [SerializeField, Range(0f, 1f)] private float musicVolume = DefaultMusicVolume;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = DefaultSfxVolume;

        private AudioSource musicSource;
        private AudioSource sfxSource;
        private AudioClip mainMenuMusic;
        private AudioClip gameplayMusic;
        private EnemyManager enemyManager;
        private OperatorManager operatorManager;

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
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;
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

        private void OnDestroy()
        {
            if (Instance == this)
            {
                SceneManager.sceneLoaded -= HandleSceneLoaded;
                UnsubscribeFromGameplayEvents();
                Instance = null;
            }
        }

        public AudioClip GetClip(SfxId id)
        {
            string clipName;
            if (!SfxNames.TryGetValue(id, out clipName)) return null;
            return Resources.Load<AudioClip>(SfxResourcesFolder + clipName);
        }

        public void PlayMainMenuMusic()
        {
            PlayMusic(mainMenuMusic);
        }

        public void PlayGameplayMusic()
        {
            PlayMusic(gameplayMusic);
        }

        public void PlaySfx(SfxId id, float volume = 1f, float pitch = 1f)
        {
            PlaySfx(GetClip(id), volume, pitch);
        }

        public void PlaySfx(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (clip == null || sfxSource == null) return;

            sfxSource.pitch = pitch;
            sfxSource.PlayOneShot(clip, Mathf.Clamp01(volume));
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
            if (sfxSource != null) sfxSource.volume = sfxVolume;
            PlayerPrefs.SetFloat(SfxVolumeKey, sfxVolume);
            PlayerPrefs.Save();
        }

        private void ApplyVolumes()
        {
            if (musicSource != null) musicSource.volume = musicVolume;
            if (sfxSource != null) sfxSource.volume = sfxVolume;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            UnsubscribeFromGameplayEvents();

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
            enemyManager = FindFirstObjectByType<EnemyManager>();
            operatorManager = FindFirstObjectByType<OperatorManager>();

            if (enemyManager != null) enemyManager.OnEnemyDied += HandleEnemyDied;
            if (operatorManager != null)
            {
                operatorManager.OnOperatorDeployed += HandleOperatorDeployed;
                operatorManager.OnOperatorRetreated += HandleOperatorRetreated;
            }
        }

        private void UnsubscribeFromGameplayEvents()
        {
            if (enemyManager != null) enemyManager.OnEnemyDied -= HandleEnemyDied;
            if (operatorManager != null)
            {
                operatorManager.OnOperatorDeployed -= HandleOperatorDeployed;
                operatorManager.OnOperatorRetreated -= HandleOperatorRetreated;
            }

            enemyManager = null;
            operatorManager = null;
        }

        private void HandleEnemyDied(EnemyBase enemy) => PlaySfx(SfxId.GameplayEnemyDeath, 0.75f);
        private void HandleOperatorDeployed(OperatorBase op) => PlaySfx(SfxId.GameplayDeploy);
        private void HandleOperatorRetreated(OperatorBase op) => PlaySfx(SfxId.GameplayRetreat);
    }
}
