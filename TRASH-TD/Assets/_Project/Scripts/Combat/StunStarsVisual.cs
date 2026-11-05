using System;
using System.Collections.Generic;
using UnityEngine;
using TrashTD.Operators;

namespace TrashTD.Combat
{
    /// <summary>
    /// Overhead spinning stars animation displayed above an operator or enemy while stunned.
    /// Uses the 32x16 6-frame sprite sheet located at Assets/_Project/Art/Status Effects/stun_stars.png.
    /// Replaces the "STUNNED!" text popup with an animated status indicator.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class StunStarsVisual : MonoBehaviour
    {
        public const float FrameDuration = 0.08f; // ~12.5 fps loop for snappy cartoon dizzy stars

        private static Sprite[] s_Frames;

        private SpriteRenderer spriteRenderer;
        private SpriteRenderer targetRenderer;
        private Transform targetTransform;
        private float frameTimer;
        private int currentFrame;
        private Sprite[] frames;

        public SpriteRenderer SpriteRenderer => spriteRenderer;
        public int CurrentFrame => currentFrame;
        public Sprite[] Frames => frames;

        public static Sprite[] GetFrames()
        {
            if (s_Frames != null && s_Frames.Length == 6 && s_Frames[0] != null)
            {
                return s_Frames;
            }

#if UNITY_EDITOR
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Status Effects/stun_stars.png");
            if (assets != null && assets.Length > 0)
            {
                var list = new List<Sprite>();
                foreach (var a in assets)
                {
                    if (a is Sprite s) list.Add(s);
                }
                if (list.Count >= 6)
                {
                    list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                    s_Frames = list.ToArray();
                    return s_Frames;
                }
            }
#endif

            // Fallback for runtime / build / tests
            string path = System.IO.Path.Combine(Application.dataPath, "_Project/Art/Status Effects/stun_stars.png");
            if (System.IO.File.Exists(path))
            {
                try
                {
                    byte[] raw = System.IO.File.ReadAllBytes(path);
                    var tex = new Texture2D(192, 16, TextureFormat.RGBA32, false);
                    tex.filterMode = FilterMode.Point;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.LoadImage(raw);
                    var loaded = new Sprite[6];
                    for (int i = 0; i < 6; i++)
                    {
                        loaded[i] = Sprite.Create(
                            tex,
                            new Rect(i * 32, 0, 32, 16),
                            new Vector2(0.5f, 0.5f),
                            32f);
                        loaded[i].name = $"stun stars_{i}";
                    }
                    s_Frames = loaded;
                    return s_Frames;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[StunStarsVisual] Failed loading fallback frames: {ex.Message}");
                }
            }

            return s_Frames ?? Array.Empty<Sprite>();
        }

        private void Awake()
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }
            frames = GetFrames();
        }

        public static StunStarsVisual Show(GameObject target, SpriteRenderer targetRenderer = null)
        {
            if (target == null) return null;

            var visual = target.GetComponentInChildren<StunStarsVisual>(true);
            if (visual == null)
            {
                var go = new GameObject("StunStarsVisual");
                go.transform.SetParent(target.transform, false);
                visual = go.AddComponent<StunStarsVisual>();
            }

            visual.Activate(target, targetRenderer);
            return visual;
        }

        public static void Hide(GameObject target)
        {
            if (target == null) return;
            var visual = target.GetComponentInChildren<StunStarsVisual>(true);
            if (visual != null)
            {
                visual.Deactivate();
            }
        }

        public static bool IsShowing(GameObject target)
        {
            if (target == null) return false;
            var visual = target.GetComponentInChildren<StunStarsVisual>(true);
            return visual != null && visual.gameObject.activeSelf && visual.enabled && visual.spriteRenderer != null && visual.spriteRenderer.enabled;
        }

        public void Activate(GameObject target, SpriteRenderer renderer = null)
        {
            targetTransform = target != null ? target.transform : transform.parent;
            targetRenderer = renderer != null ? renderer : FindTargetRenderer(target);

            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (frames == null || frames.Length == 0)
            {
                frames = GetFrames();
            }

            currentFrame = 0;
            frameTimer = 0f;

            if (frames != null && frames.Length > 0 && spriteRenderer != null)
            {
                spriteRenderer.sprite = frames[0];
            }

            if (spriteRenderer != null)
            {
                spriteRenderer.enabled = true;
                spriteRenderer.flipX = false;
                if (targetRenderer != null)
                {
                    spriteRenderer.sortingLayerID = targetRenderer.sortingLayerID;
                    spriteRenderer.sortingOrder = targetRenderer.sortingOrder + 15;
                }
                else
                {
                    spriteRenderer.sortingOrder = 25;
                }
            }

            gameObject.SetActive(true);
            enabled = true;

            UpdatePosition();
        }

        public void Deactivate()
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.enabled = false;
            }
            enabled = false;
            gameObject.SetActive(false);
        }

        private SpriteRenderer FindTargetRenderer(GameObject target)
        {
            if (target == null) return null;

            var renderers = target.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r != null && r.gameObject != gameObject && r.gameObject.name != "AirShadow" && !r.gameObject.name.StartsWith("StunStars"))
                {
                    return r;
                }
            }
            return target.GetComponent<SpriteRenderer>();
        }

        private void Update()
        {
            if (frames == null || frames.Length < 2) return;

            frameTimer += Time.deltaTime;
            if (frameTimer >= FrameDuration)
            {
                int steps = Mathf.FloorToInt(frameTimer / FrameDuration);
                frameTimer %= FrameDuration;
                currentFrame = (currentFrame + steps) % frames.Length;
                if (spriteRenderer != null)
                {
                    spriteRenderer.sprite = frames[currentFrame];
                }
            }
        }

        private void LateUpdate()
        {
            UpdatePosition();
        }

        public void UpdatePosition()
        {
            if (targetTransform == null)
            {
                targetTransform = transform.parent;
                if (targetTransform == null) return;
            }

            if (targetRenderer == null)
            {
                targetRenderer = FindTargetRenderer(targetTransform.gameObject);
            }

            float topY;
            float centerX;
            float centerZ;

            OperatorBase op = targetTransform != null ? targetTransform.GetComponent<OperatorBase>() : null;
            if (op != null)
            {
                topY = op.VisualCenter.y + op.BodyExtents.y;
                centerX = op.VisualCenter.x;
                centerZ = targetRenderer != null ? targetRenderer.transform.position.z - 0.05f : targetTransform.position.z - 0.05f;

                if (spriteRenderer != null && targetRenderer != null)
                {
                    spriteRenderer.sortingLayerID = targetRenderer.sortingLayerID;
                    spriteRenderer.sortingOrder = targetRenderer.sortingOrder + 15;
                }
            }
            else if (targetRenderer != null && targetRenderer.sprite != null)
            {
                topY = targetRenderer.bounds.max.y;
                centerX = targetRenderer.bounds.center.x;
                centerZ = targetRenderer.transform.position.z - 0.05f;

                if (spriteRenderer != null)
                {
                    spriteRenderer.sortingLayerID = targetRenderer.sortingLayerID;
                    spriteRenderer.sortingOrder = targetRenderer.sortingOrder + 15;
                }
            }
            else
            {
                topY = targetTransform.position.y + 0.5f;
                centerX = targetTransform.position.x;
                centerZ = targetTransform.position.z - 0.05f;
            }

            // Stun stars sprite is 16 pixels high (0.5 units). Pivot is center (0.5, 0.5).
            // Position pivot slightly above the top edge of the head/sprite (~0.18 units).
            transform.position = new Vector3(centerX, topY + 0.18f, centerZ);

            // Compensate for parent scale so stun stars always render at 1:1 pixel size (32x16 at 32 PPU)
            Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            float sx = parentScale.x != 0f ? 1f / Mathf.Abs(parentScale.x) : 1f;
            float sy = parentScale.y != 0f ? 1f / Mathf.Abs(parentScale.y) : 1f;
            transform.localScale = new Vector3(sx, sy, 1f);
        }
    }
}
