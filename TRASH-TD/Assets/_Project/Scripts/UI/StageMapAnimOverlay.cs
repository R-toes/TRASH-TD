using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TrashTD.UI
{
    /// <summary>
    /// Animates the 12-frame radar/sonar pulse overlay ("StageMap_Anim_Overlay") stretched over
    /// the stage selection map ("Stage Selector Map.png").
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class StageMapAnimOverlay : MonoBehaviour
    {
        [Header("Animation Settings")]
        [Tooltip("The 12 frames of the StageMap_Anim_Overlay strip.")]
        [SerializeField] private Sprite[] frames;

        [Tooltip("Duration of each frame in seconds (0.0833s = 12 FPS, 1.0s full loop).")]
        [Min(0.01f)]
        [SerializeField] private float frameDuration = 0.0833f;

        [Tooltip("Whether to loop the animation continuously.")]
        [SerializeField] private bool loop = true;

        [Tooltip("Whether animation uses unscaled time so it continues during UI pauses.")]
        [SerializeField] private bool unscaledTime = true;

        private Image targetImage;
        private float timer;
        private int currentFrameIndex;

        public Sprite[] Frames
        {
            get => frames;
            set
            {
                frames = value;
                if (frames != null && frames.Length > 0 && targetImage != null)
                {
                    targetImage.sprite = frames[0];
                }
            }
        }

        public float FrameDuration
        {
            get => frameDuration;
            set => frameDuration = Mathf.Max(0.01f, value);
        }

        private void Awake()
        {
            targetImage = GetComponent<Image>();
            EnsureFramesLoaded();
        }

        private void Start()
        {
            EnsureFramesLoaded();
            if (targetImage != null && frames != null && frames.Length > 0)
            {
                targetImage.sprite = frames[currentFrameIndex];
            }
        }

        private void OnEnable()
        {
            timer = 0f;
            currentFrameIndex = 0;
            if (targetImage != null && frames != null && frames.Length > 0)
            {
                targetImage.sprite = frames[0];
            }
        }

        private void Update()
        {
            if (frames == null || frames.Length == 0) return;

            // If an active Animator is already driving the sprite, let it take precedence
            if (TryGetComponent<Animator>(out var anim) && anim != null && anim.runtimeAnimatorController != null && anim.enabled)
            {
                return;
            }

            float dt = unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            timer += dt;

            if (timer >= frameDuration)
            {
                int step = Mathf.FloorToInt(timer / frameDuration);
                timer %= frameDuration;

                if (loop)
                {
                    currentFrameIndex = (currentFrameIndex + step) % frames.Length;
                }
                else
                {
                    currentFrameIndex = Mathf.Min(currentFrameIndex + step, frames.Length - 1);
                }

                if (targetImage != null && currentFrameIndex >= 0 && currentFrameIndex < frames.Length)
                {
                    targetImage.sprite = frames[currentFrameIndex];
                }
            }
        }

        public void EnsureFramesLoaded()
        {
            if (frames != null && frames.Length >= 12 && frames[0] != null) return;

            // 1. In Unity Editor: load all sub-sprites directly from the asset database
#if UNITY_EDITOR
            var editorAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/StageMap_Anim_Overlay.png");
            if (editorAssets != null && editorAssets.Length > 0)
            {
                var spriteList = new List<Sprite>();
                foreach (var asset in editorAssets)
                {
                    if (asset is Sprite s) spriteList.Add(s);
                }

                if (spriteList.Count > 0)
                {
                    spriteList.Sort((a, b) => ExtractTrailingIndex(a.name).CompareTo(ExtractTrailingIndex(b.name)));
                    frames = spriteList.ToArray();
                    return;
                }
            }
#endif

            // 2. Try loading via Resources
            var loaded = Resources.LoadAll<Sprite>("StageMap_Anim_Overlay");
            if (loaded != null && loaded.Length > 0)
            {
                var list = new List<Sprite>(loaded);
                list.Sort((a, b) => ExtractTrailingIndex(a.name).CompareTo(ExtractTrailingIndex(b.name)));
                frames = list.ToArray();
                return;
            }

            // 3. Runtime fallback: load PNG from disk and slice 12 horizontal frames (each 480x270)
            try
            {
                string relativePath = "Assets/_Project/Art/Levels/StageMap_Anim_Overlay.png";
                string fullPath = System.IO.Path.Combine(Application.dataPath, "..", relativePath);
                if (System.IO.File.Exists(fullPath))
                {
                    byte[] bytes = System.IO.File.ReadAllBytes(fullPath);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tex.filterMode = FilterMode.Point;
                    if (tex.LoadImage(bytes))
                    {
                        tex.filterMode = FilterMode.Point;
                        const int count = 12;
                        float w = tex.width / (float)count; // 480
                        float h = tex.height; // 270
                        frames = new Sprite[count];
                        for (int i = 0; i < count; i++)
                        {
                            frames[i] = Sprite.Create(tex, new Rect(i * w, 0f, w, h), new Vector2(0.5f, 0.5f), 100f);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[StageMapAnimOverlay] Failed to load strip frames from disk: {ex.Message}");
            }
        }

        private static int ExtractTrailingIndex(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            int lastUnderscore = name.LastIndexOf('_');
            if (lastUnderscore >= 0 && int.TryParse(name.Substring(lastUnderscore + 1), out int result))
            {
                return result;
            }
            return 0;
        }
    }
}
