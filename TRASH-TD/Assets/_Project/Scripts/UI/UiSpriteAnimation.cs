using UnityEngine;
using UnityEngine.UI;

namespace TrashTD.UI
{
    /// <summary>
    /// Loops a set of sprites on a UI Image at a fixed frame time. Runs on unscaled time, so menu animations keep
    /// playing while the game is paused. Assign <see cref="Frames"/> and <see cref="FrameDuration"/> after adding it.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class UiSpriteAnimation : MonoBehaviour
    {
        private Image image;
        private Sprite[] frames;
        private float frameDuration = 0.1f;
        private float timer;
        private int frame;

        public Sprite[] Frames
        {
            get => frames;
            set
            {
                frames = value;
                frame = 0;
                timer = 0f;
                Show();
            }
        }

        public float FrameDuration
        {
            get => frameDuration;
            set => frameDuration = Mathf.Max(0.01f, value);
        }

        private void Awake()
        {
            image = GetComponent<Image>();
        }

        private void OnEnable()
        {
            frame = 0;
            timer = 0f;
            Show();
        }

        private void Update()
        {
            if (frames == null || frames.Length < 2) return;

            timer += Time.unscaledDeltaTime;
            if (timer < frameDuration) return;

            int steps = Mathf.FloorToInt(timer / frameDuration);
            timer -= steps * frameDuration;
            frame = (frame + steps) % frames.Length;
            Show();
        }

        private void Show()
        {
            if (image == null) image = GetComponent<Image>();
            if (image != null && frames != null && frames.Length > 0 && frame < frames.Length && frames[frame] != null)
                image.sprite = frames[frame];
        }
    }
}
