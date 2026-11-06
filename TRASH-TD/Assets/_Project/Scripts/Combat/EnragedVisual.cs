using System;
using System.Collections.Generic;
using UnityEngine;
using TrashTD.Enemies;
using TrashTD.Operators;

namespace TrashTD.Combat
{
    /// <summary>
    /// Overhead enraged animation displayed above an enemy while enraged (e.g. stalled by Mossmo).
    /// Uses the 32x16 8-frame sprite sheet located at Assets/_Project/Art/Status Effects/enraged.png.
    /// Replaces the "ENRAGED!" text popup with an animated status indicator.
    /// Mirrored when the enemy is facing right.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class EnragedVisual : MonoBehaviour
    {
        public const float FrameDuration = 0.08f; // ~12.5 fps loop for snappy cartoon enraged flame / sweat marks

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
            if (s_Frames != null && s_Frames.Length == 8 && s_Frames[0] != null)
            {
                return s_Frames;
            }

#if UNITY_EDITOR
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Status Effects/enraged.png");
            if (assets != null && assets.Length > 0)
            {
                var list = new List<Sprite>();
                foreach (var a in assets)
                {
                    if (a is Sprite s) list.Add(s);
                }
                if (list.Count >= 8)
                {
                    list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                    s_Frames = list.ToArray();
                    return s_Frames;
                }
            }
#endif

            // Fallback for runtime / build / tests
            string path = System.IO.Path.Combine(Application.dataPath, "_Project/Art/Status Effects/enraged.png");
            if (System.IO.File.Exists(path))
            {
                try
                {
                    byte[] raw = System.IO.File.ReadAllBytes(path);
                    var tex = new Texture2D(256, 16, TextureFormat.RGBA32, false);
                    tex.filterMode = FilterMode.Point;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.LoadImage(raw);
                    var loaded = new Sprite[8];
                    for (int i = 0; i < 8; i++)
                    {
                        loaded[i] = Sprite.Create(
                            tex,
                            new Rect(i * 32, 0, 32, 16),
                            new Vector2(0.5f, 0.5f),
                            32f);
                        loaded[i].name = $"enraged_{i}";
                    }
                    s_Frames = loaded;
                    return s_Frames;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[EnragedVisual] Failed loading fallback frames: {ex.Message}");
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

        public static EnragedVisual Show(GameObject target, SpriteRenderer targetRenderer = null)
        {
            if (target == null) return null;

            var visual = target.GetComponentInChildren<EnragedVisual>(true);
            if (visual == null)
            {
                var go = new GameObject("EnragedVisual");
                go.transform.SetParent(target.transform, false);
                visual = go.AddComponent<EnragedVisual>();
            }

            visual.Activate(target, targetRenderer);
            return visual;
        }

        public static void Hide(GameObject target)
        {
            if (target == null) return;
            var visual = target.GetComponentInChildren<EnragedVisual>(true);
            if (visual != null)
            {
                visual.Deactivate();
            }
        }

        public static bool IsShowing(GameObject target)
        {
            if (target == null) return false;
            var visual = target.GetComponentInChildren<EnragedVisual>(true);
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

            EnemyBase enemy = target.GetComponent<EnemyBase>();
            if (enemy != null)
            {
                SpriteRenderer primary = enemy.GetPrimaryVisualRenderer();
                if (primary != null && primary.enabled) return primary;
            }

            Transform visual = target.transform.Find("EnemyVisual");
            if (visual != null)
            {
                SpriteRenderer r = visual.GetComponent<SpriteRenderer>();
                if (r != null && r.enabled) return r;
            }

            var renderers = target.GetComponentsInChildren<SpriteRenderer>(false);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r != null && r.enabled && r.gameObject != gameObject && r.gameObject.name != "AirShadow" &&
                    !r.gameObject.name.StartsWith("StunStars") && !r.gameObject.name.StartsWith("EnragedVisual") &&
                    r.sprite != null)
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

            if (targetRenderer == null || !targetRenderer.enabled)
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

            EnemyBase enemy = targetTransform != null ? targetTransform.GetComponent<EnemyBase>() : null;
            bool isFacingLeft = enemy != null ? enemy.IsFacingLeft : (targetRenderer != null && targetRenderer.flipX);
            float facingDir = isFacingLeft ? -1f : 1f;

            // Check specific large 64x64 enemies
            bool isEradicator = false;
            bool isSandHulk = false;
            if (enemy != null && enemy.Data != null && !string.IsNullOrEmpty(enemy.Data.enemyName))
            {
                string enemyName = enemy.Data.enemyName;
                if (enemyName.IndexOf("Eradicator", StringComparison.OrdinalIgnoreCase) >= 0) isEradicator = true;
                if (enemyName.IndexOf("Sand Hulk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    enemyName.IndexOf("SandHulk", StringComparison.OrdinalIgnoreCase) >= 0) isSandHulk = true;
            }
            if (targetTransform != null)
            {
                string objName = targetTransform.name;
                if (objName.IndexOf("Eradicator", StringComparison.OrdinalIgnoreCase) >= 0) isEradicator = true;
                if (objName.IndexOf("Sand Hulk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    objName.IndexOf("SandHulk", StringComparison.OrdinalIgnoreCase) >= 0) isSandHulk = true;
            }
            if (targetRenderer != null && targetRenderer.sprite != null)
            {
                string spriteName = targetRenderer.sprite.name;
                if (spriteName.IndexOf("Eradicator", StringComparison.OrdinalIgnoreCase) >= 0) isEradicator = true;
                if (spriteName.IndexOf("Sand Hulk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    spriteName.IndexOf("SandHulk", StringComparison.OrdinalIgnoreCase) >= 0) isSandHulk = true;
            }

            bool isLargeEnemy = isEradicator || isSandHulk;
            if (!isLargeEnemy && targetRenderer != null)
            {
                if (targetRenderer.sprite != null && (targetRenderer.sprite.rect.width >= 48f || targetRenderer.sprite.rect.height >= 48f))
                {
                    isLargeEnemy = true;
                }
                else if (targetRenderer.bounds.size.y >= 1.4f || targetRenderer.bounds.size.x >= 1.4f)
                {
                    isLargeEnemy = true;
                }
            }

            // Offset tuning:
            // - Eradicator: up by 4 pixels (+4/32 units) from 0.18f
            // - Sand Hulk: down by 4 pixels (-4/32 units) from 0.18f
            // - Other 64x64 enemies: 0.18f baseline
            // - Standard (32x32) enemies: down by 1 pixel (-1/32 units), back by 2 pixels (-2/32 units)
            const float PixelSize = 1f / 32f;
            float frontOffset = isLargeEnemy ? 0.08f : (0.08f - 2f * PixelSize);
            float verticalOffset;
            if (isEradicator)
            {
                verticalOffset = 0.18f + 4f * PixelSize;
            }
            else if (isSandHulk)
            {
                verticalOffset = 0.18f - 4f * PixelSize;
            }
            else if (isLargeEnemy)
            {
                verticalOffset = 0.18f;
            }
            else
            {
                verticalOffset = 0.18f - 1f * PixelSize;
            }

            float targetX = centerX + facingDir * frontOffset;
            float targetY = topY + verticalOffset;

            transform.position = new Vector3(targetX, targetY, centerZ);

            if (spriteRenderer != null)
            {
                spriteRenderer.flipX = isFacingLeft;
            }

            // Compensate for parent scale so enraged visual always renders at 1:1 pixel size (32x16 at 32 PPU)
            Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
            float sx = parentScale.x != 0f ? 1f / Mathf.Abs(parentScale.x) : 1f;
            float sy = parentScale.y != 0f ? 1f / Mathf.Abs(parentScale.y) : 1f;
            transform.localScale = new Vector3(sx, sy, 1f);
        }
    }
}

