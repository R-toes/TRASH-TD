using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Operators
{
    /// <summary>
    /// Handles Scrapper's idle animation (32x32) and sledgehammer attack animation (64x64).
    /// The 64x64 attack sprite uses a pivot at (0.75, 0.25) so the bottom-right 32x32 quadrant
    /// aligns with the deployed tile and the hammer swing extends into the adjacent tile.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class ScrapperSpriteAnimation : MonoBehaviour, IOperatorSpriteFrames
    {
        public const int AttackFrameCount = 8;
        public const float DefaultIdleFrameDuration = 0.25f;
        public const float DefaultAttackFrameDuration = 0.055f;
        public const float SledgehammerPivotX = 0.75f;
        public const float SledgehammerPivotY = 0.25f;

        [SerializeField] private Sprite[] idleFrames;
        [SerializeField] private Sprite[] attackFrames;
        [SerializeField, Min(0.01f)] private float idleFrameDuration = DefaultIdleFrameDuration;
        [SerializeField, Min(0.01f)] private float attackFrameDuration = DefaultAttackFrameDuration;

        public Sprite[] Frames => idleFrames;
        public Sprite[] AttackFrames => attackFrames;
        public float FrameDuration => idleFrameDuration;
        public float AttackFrameDuration => attackFrameDuration;
        public bool IsAttacking => isAttacking;
        public int CurrentAttackFrame => attackFrame;
        public int CurrentIdleFrame => idleFrame;

        private SpriteRenderer spriteRenderer;
        private int idleFrame;
        private float idleTimer;
        private bool isAttacking;
        private int attackFrame;
        private float attackTimer;

        public void Configure(Sprite[] idle, Sprite[] attack)
        {
            idleFrames = idle;
            attackFrames = attack;
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            idleFrame = 0;
            idleTimer = 0f;
            CancelAttack();
        }

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            EnsureFramesLoaded();
            if (spriteRenderer != null && idleFrames != null && idleFrames.Length > 0 && idleFrames[0] != null)
            {
                spriteRenderer.sprite = idleFrames[0];
            }
        }

        public void EnsureFramesLoaded()
        {
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();

            // Load idle frames if not set
            if (idleFrames == null || idleFrames.Length == 0)
            {
#if UNITY_EDITOR
                var idleAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Operators/Scrapper/Scrapper-Sheet.png");
                if (idleAssets != null && idleAssets.Length > 0)
                {
                    var list = new List<Sprite>();
                    foreach (var a in idleAssets)
                    {
                        if (a is Sprite s) list.Add(s);
                    }
                    if (list.Count > 0) idleFrames = list.ToArray();
                }
#endif
            }

            // Load attack frames if not set
            if (attackFrames == null || attackFrames.Length == 0)
            {
#if UNITY_EDITOR
                var attackAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Operators/Scrapper/Scrapper Attack.png");
                if (attackAssets != null && attackAssets.Length > 0)
                {
                    var list = new List<Sprite>();
                    // Order by name (Scrapper Attack_0 to Scrapper Attack_7)
                    for (int i = 0; i < AttackFrameCount; i++)
                    {
                        string targetName = $"Scrapper Attack_{i}";
                        foreach (var a in attackAssets)
                        {
                            if (a is Sprite s && s.name == targetName)
                            {
                                list.Add(s);
                                break;
                            }
                        }
                    }
                    if (list.Count == AttackFrameCount)
                    {
                        attackFrames = list.ToArray();
                    }
                }
#endif
                // Slicing fallback for tests / runtime when AssetDatabase is unavailable or sprites not sliced
                if (attackFrames == null || attackFrames.Length < AttackFrameCount)
                {
                    SliceAttackFramesFallback();
                }
            }
        }

        private void SliceAttackFramesFallback()
        {
            Texture2D tex = null;
#if UNITY_EDITOR
            tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/Operators/Scrapper/Scrapper Attack.png");
#endif
            if (tex == null)
            {
                string path = System.IO.Path.Combine(Application.dataPath, "_Project/Art/Operators/Scrapper/Scrapper Attack.png");
                if (System.IO.File.Exists(path))
                {
                    try
                    {
                        byte[] raw = System.IO.File.ReadAllBytes(path);
                        tex = new Texture2D(512, 64, TextureFormat.RGBA32, false);
                        tex.filterMode = FilterMode.Point;
                        tex.LoadImage(raw);
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }

            if (tex != null)
            {
                var frames = new Sprite[AttackFrameCount];
                Vector2 pivot = new Vector2(SledgehammerPivotX, SledgehammerPivotY);
                for (int i = 0; i < AttackFrameCount; i++)
                {
                    frames[i] = Sprite.Create(tex, new Rect(i * 64, 0, 64, 64), pivot, 32f);
                    frames[i].name = $"Scrapper Attack_{i}";
                }
                attackFrames = frames;
            }
        }

        private void Update()
        {
            if (spriteRenderer == null) return;

            if (isAttacking)
            {
                if (attackFrames == null || attackFrames.Length == 0)
                {
                    isAttacking = false;
                    return;
                }

                attackTimer += Time.deltaTime;
                if (attackTimer >= attackFrameDuration)
                {
                    attackTimer -= attackFrameDuration;
                    attackFrame++;

                    if (attackFrame >= attackFrames.Length)
                    {
                        // Attack animation complete -> return to idle
                        isAttacking = false;
                        attackFrame = 0;
                        attackTimer = 0f;
                        DisplayCurrentIdleFrame();
                    }
                    else
                    {
                        spriteRenderer.sprite = attackFrames[attackFrame];
                    }
                }
            }
            else
            {
                if (idleFrames == null || idleFrames.Length == 0) return;

                idleTimer += Time.deltaTime;
                if (idleTimer >= idleFrameDuration)
                {
                    idleTimer -= idleFrameDuration;
                    idleFrame = (idleFrame + 1) % idleFrames.Length;
                    DisplayCurrentIdleFrame();
                }
            }
        }

        private void DisplayCurrentIdleFrame()
        {
            if (idleFrames != null && idleFrames.Length > 0 && idleFrame >= 0 && idleFrame < idleFrames.Length)
            {
                if (spriteRenderer != null)
                {
                    spriteRenderer.sprite = idleFrames[idleFrame];
                }
            }
        }

        public void PlayAttack()
        {
            if (attackFrames == null || attackFrames.Length == 0)
            {
                EnsureFramesLoaded();
            }

            if (attackFrames != null && attackFrames.Length > 0)
            {
                isAttacking = true;
                attackFrame = 0;
                attackTimer = 0f;
                if (spriteRenderer != null)
                {
                    spriteRenderer.sprite = attackFrames[0];
                }
            }
        }

        public void CancelAttack()
        {
            isAttacking = false;
            attackFrame = 0;
            attackTimer = 0f;
            DisplayCurrentIdleFrame();
        }
    }
}

