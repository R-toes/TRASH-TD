using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Operators
{
    /// <summary>
    /// Handles Stag-ger's idle animation (32x32) and Six-Limb Slugger attack animation (48x32).
    /// The 48x32 attack sprite uses a pivot at (2/3, 0.5) so the right 32x32 quadrant
    /// aligns with the deployed tile and the attack extends forward into the adjacent tile.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class StaggerSpriteAnimation : MonoBehaviour, IOperatorSpriteFrames
    {
        public const int AttackFrameCount = 12;
        public const float DefaultIdleFrameDuration = 0.125f;
        public const float DefaultAttackFrameDuration = 0.05f;
        public const float SluggerPivotX = 2f / 3f;
        public const float SluggerPivotY = 0.5f;

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
                var idleAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Operators/Stag-ger/Stag-ger-Sheet.png");
                if (idleAssets != null && idleAssets.Length > 0)
                {
                    var list = new List<Sprite>();
                    for (int i = 0; i < 8; i++)
                    {
                        string targetName = $"Stag-ger-Sheet_{i}";
                        foreach (var a in idleAssets)
                        {
                            if (a is Sprite s && s.name == targetName)
                            {
                                list.Add(s);
                                break;
                            }
                        }
                    }
                    if (list.Count > 0) idleFrames = list.ToArray();
                }
#endif
            }

            // Load attack frames if not set
            if (attackFrames == null || attackFrames.Length == 0)
            {
#if UNITY_EDITOR
                var attackAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Operators/Stag-ger/Stag-ger Attack.png");
                if (attackAssets != null && attackAssets.Length > 0)
                {
                    var list = new List<Sprite>();
                    for (int i = 0; i < AttackFrameCount; i++)
                    {
                        string targetName = $"Stag-ger Attack_{i}";
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
                // Slicing fallback for tests / runtime when AssetDatabase is unavailable
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
            tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/Operators/Stag-ger/Stag-ger Attack.png");
#endif
            if (tex == null)
            {
                string path = System.IO.Path.Combine(Application.dataPath, "_Project/Art/Operators/Stag-ger/Stag-ger Attack.png");
                if (System.IO.File.Exists(path))
                {
                    try
                    {
                        byte[] raw = System.IO.File.ReadAllBytes(path);
                        tex = new Texture2D(576, 32, TextureFormat.RGBA32, false);
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
                Vector2 pivot = new Vector2(SluggerPivotX, SluggerPivotY);
                for (int i = 0; i < AttackFrameCount; i++)
                {
                    frames[i] = Sprite.Create(tex, new Rect(i * 48, 0, 48, 32), pivot, 32f);
                    frames[i].name = $"Stag-ger Attack_{i}";
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

