using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Operators
{
    /// <summary>
    /// Panacea's sprite animation controller:
    /// - Idle: 8-frame loop ("Panacea Sheet.png") at 0.125s per frame (8 fps).
    /// - Attack (Throw): 12 frames (top row of "Panacea-Throw-Reload.png", 64x32).
    ///   The actual throw occurs on the 11th frame (index 10), launching the projectile from Panacea's hand.
    /// - Reload: 12 frames (bottom row of "Panacea-Throw-Reload.png", 64x32), played after throw finishes.
    /// - Idle Return: When both finish, returns to idle animation.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class PanaceaSpriteAnimation : MonoBehaviour, IOperatorSpriteFrames
    {
        public const int IdleFrameCount = 8;
        public const int ThrowFrameCount = 12;
        public const int ReloadFrameCount = 12;
        public const int ReleaseFrameIndex = 10; // 11th frame (0-indexed: 10)
        public const float DefaultIdleFrameDuration = 0.125f;
        public const float DefaultThrowFrameDuration = 0.06f;
        public const float DefaultReloadFrameDuration = 0.07f;

        public enum AnimState
        {
            Idle,
            Throw,
            Reload
        }

        [SerializeField] private Sprite[] idleFrames;
        [SerializeField] private Sprite[] frames; // fallback/alias for prefab compatibility
        [SerializeField] private Sprite[] throwFrames;
        [SerializeField] private Sprite[] reloadFrames;
        [SerializeField, Min(0.01f)] private float idleFrameDuration = DefaultIdleFrameDuration;

        public Sprite[] Frames => idleFrames != null && idleFrames.Length > 0 ? idleFrames : frames;
        public float FrameDuration => idleFrameDuration;
        public Sprite[] ThrowFrames => throwFrames;
        public Sprite[] ReloadFrames => reloadFrames;

        public AnimState CurrentState => currentState;
        public int CurrentFrameIndex => currentFrameIndex;
        public bool IsAttacking => currentState == AnimState.Throw || currentState == AnimState.Reload;
        public float ThrowAnimDuration => throwAnimDuration;
        public float ReloadAnimDuration => reloadAnimDuration;

        private SpriteRenderer spriteRenderer;
        private AnimState currentState = AnimState.Idle;
        private int currentFrameIndex = 0;
        private float frameTimer = 0f;
        private float throwAnimDuration = DefaultThrowFrameDuration;
        private float reloadAnimDuration = DefaultReloadFrameDuration;
        private Action onThrowCallback;

        public void Configure(Sprite[] idle, Sprite[] throwAnim, Sprite[] reloadAnim)
        {
            idleFrames = idle;
            throwFrames = throwAnim;
            reloadFrames = reloadAnim;
            CancelAnimation();
        }

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            EnsureFramesLoaded();
            if (spriteRenderer != null && Frames != null && Frames.Length > 0 && spriteRenderer.sprite == null)
            {
                spriteRenderer.sprite = Frames[0];
            }
        }

        public void EnsureFramesLoaded()
        {
            EnsureIdleFrames();
            EnsureThrowAndReloadFrames();
        }

        private void EnsureIdleFrames()
        {
            if (idleFrames != null && idleFrames.Length >= IdleFrameCount) return;
            if (frames != null && frames.Length >= IdleFrameCount)
            {
                idleFrames = frames;
                return;
            }

#if UNITY_EDITOR
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Operators/Panacea/Panacea Sheet.png");
            if (assets != null && assets.Length > 0)
            {
                var list = new List<Sprite>();
                foreach (var a in assets)
                {
                    if (a is Sprite s) list.Add(s);
                }
                list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                if (list.Count >= IdleFrameCount)
                {
                    idleFrames = list.GetRange(0, IdleFrameCount).ToArray();
                    return;
                }
            }
#endif

            string path = System.IO.Path.Combine(Application.dataPath, "_Project/Art/Operators/Panacea/Panacea Sheet.png");
            if (System.IO.File.Exists(path))
            {
                try
                {
                    byte[] bytes = System.IO.File.ReadAllBytes(path);
                    Texture2D tex = new Texture2D(256, 32, TextureFormat.RGBA32, false);
                    tex.filterMode = FilterMode.Point;
                    tex.LoadImage(bytes);

                    idleFrames = new Sprite[IdleFrameCount];
                    Vector2 pivot = new Vector2(0.5f, 0.5f);
                    float ppu = 32f;
                    for (int i = 0; i < IdleFrameCount; i++)
                    {
                        idleFrames[i] = Sprite.Create(tex, new Rect(i * 32, 0, 32, 32), pivot, ppu);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PanaceaSpriteAnimation] Could not load idle frames: {ex.Message}");
                }
            }
        }

        private void EnsureThrowAndReloadFrames()
        {
            if (throwFrames != null && throwFrames.Length >= ThrowFrameCount &&
                reloadFrames != null && reloadFrames.Length >= ReloadFrameCount) return;

#if UNITY_EDITOR
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Operators/Panacea/Panacea-Throw-Reload.png");
            if (assets != null && assets.Length > 0)
            {
                var allSprites = new List<Sprite>();
                foreach (var a in assets)
                {
                    if (a is Sprite s) allSprites.Add(s);
                }
                allSprites.Sort((a, b) =>
                {
                    int numA = ExtractTrailingNumber(a.name);
                    int numB = ExtractTrailingNumber(b.name);
                    return numA != -1 && numB != -1 ? numA.CompareTo(numB) : string.CompareOrdinal(a.name, b.name);
                });

                if (allSprites.Count >= ThrowFrameCount + ReloadFrameCount)
                {
                    throwFrames = allSprites.GetRange(0, ThrowFrameCount).ToArray();
                    reloadFrames = allSprites.GetRange(ThrowFrameCount, ReloadFrameCount).ToArray();
                    return;
                }
            }
#endif

            string path = System.IO.Path.Combine(Application.dataPath, "_Project/Art/Operators/Panacea/Panacea-Throw-Reload.png");
            if (System.IO.File.Exists(path))
            {
                try
                {
                    byte[] bytes = System.IO.File.ReadAllBytes(path);
                    Texture2D tex = new Texture2D(768, 64, TextureFormat.RGBA32, false);
                    tex.filterMode = FilterMode.Point;
                    tex.LoadImage(bytes);

                    throwFrames = new Sprite[ThrowFrameCount];
                    reloadFrames = new Sprite[ReloadFrameCount];
                    Vector2 pivot = new Vector2(0.5f, 0.5f);
                    float ppu = 32f;

                    for (int i = 0; i < ThrowFrameCount; i++)
                    {
                        // Top row: y = 32
                        throwFrames[i] = Sprite.Create(tex, new Rect(i * 64, 32, 64, 32), pivot, ppu);
                        // Bottom row: y = 0
                        reloadFrames[i] = Sprite.Create(tex, new Rect(i * 64, 0, 64, 32), pivot, ppu);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PanaceaSpriteAnimation] Could not load throw/reload frames: {ex.Message}");
                }
            }
        }

        private static int ExtractTrailingNumber(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            int idx = s.LastIndexOf('_');
            if (idx >= 0 && idx < s.Length - 1 && int.TryParse(s.Substring(idx + 1), out int result))
            {
                return result;
            }
            return -1;
        }

        public void PlayHealAnimation(float currentCooldown, Action onThrow = null)
        {
            EnsureFramesLoaded();

            float totalAnimTime = (ThrowFrameCount * DefaultThrowFrameDuration) + (ReloadFrameCount * DefaultReloadFrameDuration);

            // Scale down only if cooldown is shorter than total animation time
            if (currentCooldown < totalAnimTime && currentCooldown > 0.05f)
            {
                float scale = currentCooldown / totalAnimTime;
                throwAnimDuration = DefaultThrowFrameDuration * scale;
                reloadAnimDuration = DefaultReloadFrameDuration * scale;
            }
            else
            {
                throwAnimDuration = DefaultThrowFrameDuration;
                reloadAnimDuration = DefaultReloadFrameDuration;
            }

            currentState = AnimState.Throw;
            currentFrameIndex = 0;
            frameTimer = 0f;
            onThrowCallback = onThrow;

            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null && throwFrames != null && throwFrames.Length > 0)
            {
                spriteRenderer.sprite = throwFrames[0];
            }
        }

        public void CancelAnimation()
        {
            currentState = AnimState.Idle;
            currentFrameIndex = 0;
            frameTimer = 0f;
            onThrowCallback = null;

            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null && Frames != null && Frames.Length > 0)
            {
                spriteRenderer.sprite = Frames[0];
            }
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
                if (spriteRenderer == null) return;
            }

            frameTimer += dt;

            switch (currentState)
            {
                case AnimState.Throw:
                    while (frameTimer >= throwAnimDuration)
                    {
                        frameTimer -= throwAnimDuration;
                        currentFrameIndex++;

                        // 11th frame (index 10): projectile comes out of Panacea
                        if (currentFrameIndex == ReleaseFrameIndex)
                        {
                            onThrowCallback?.Invoke();
                            onThrowCallback = null;
                        }

                        if (currentFrameIndex >= ThrowFrameCount)
                        {
                            if (onThrowCallback != null)
                            {
                                onThrowCallback.Invoke();
                                onThrowCallback = null;
                            }

                            currentState = AnimState.Reload;
                            currentFrameIndex = 0;
                            if (reloadFrames != null && reloadFrames.Length > 0)
                            {
                                spriteRenderer.sprite = reloadFrames[0];
                            }
                            break;
                        }
                        else if (throwFrames != null && currentFrameIndex < throwFrames.Length)
                        {
                            spriteRenderer.sprite = throwFrames[currentFrameIndex];
                        }
                    }
                    break;

                case AnimState.Reload:
                    while (frameTimer >= reloadAnimDuration)
                    {
                        frameTimer -= reloadAnimDuration;
                        currentFrameIndex++;

                        if (currentFrameIndex >= ReloadFrameCount)
                        {
                            currentState = AnimState.Idle;
                            currentFrameIndex = 0;
                            if (Frames != null && Frames.Length > 0)
                            {
                                spriteRenderer.sprite = Frames[0];
                            }
                            break;
                        }
                        else if (reloadFrames != null && currentFrameIndex < reloadFrames.Length)
                        {
                            spriteRenderer.sprite = reloadFrames[currentFrameIndex];
                        }
                    }
                    break;

                case AnimState.Idle:
                default:
                    while (frameTimer >= idleFrameDuration)
                    {
                        frameTimer -= idleFrameDuration;
                        if (Frames != null && Frames.Length > 0)
                        {
                            currentFrameIndex = (currentFrameIndex + 1) % Frames.Length;
                            spriteRenderer.sprite = Frames[currentFrameIndex];
                        }
                    }
                    break;
            }
        }
    }
}
