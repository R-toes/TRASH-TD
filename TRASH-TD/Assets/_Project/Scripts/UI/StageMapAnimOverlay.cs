using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TrashTD.UI
{
    /// <summary>
    /// Animates the 12-frame radar/sonar pulse overlay (Resources/StageMap_Anim_Overlay.png) stretched over
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

        /// <summary>
        /// Loads the 12 sliced frames from Resources/StageMap_Anim_Overlay, the same way in the Editor and in builds.
        /// The Image is hidden while there are no frames: an Image without a sprite draws a solid white rectangle,
        /// which used to cover the whole stage map in builds.
        /// </summary>
        public void EnsureFramesLoaded()
        {
            if (frames == null || frames.Length < 12 || frames[0] == null)
            {
                var loaded = Resources.LoadAll<Sprite>(ResourcePath);
                if (loaded != null && loaded.Length > 0)
                {
                    var list = new List<Sprite>(loaded);
                    list.Sort((a, b) => ExtractTrailingIndex(a.name).CompareTo(ExtractTrailingIndex(b.name)));
                    frames = list.ToArray();
                }
                else
                {
                    Debug.LogWarning($"[StageMapAnimOverlay] No frames at Resources/{ResourcePath}; the radar overlay is hidden.");
                }
            }

            if (targetImage == null) targetImage = GetComponent<Image>();
            if (targetImage != null) targetImage.enabled = frames != null && frames.Length > 0 && frames[0] != null;
        }

        private const string ResourcePath = "StageMap_Anim_Overlay";

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
