using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Operators
{
    /// <summary>
    /// Mlem's animations: a 16-frame idle (32x32), an 8-frame digesting loop (32x32) and the 12-frame tongue attack
    /// (80x40) with a matching 12-frame effect overlay.
    /// <para>
    /// The art faces left; the operator flips it when it faces right. Attack frames are 80x40 with Mlem's 32x32 body in
    /// the right 32 px. Every pivot is on the body's centre (idle and digest 0.5, 0.5; attack and its fx 0.8, 0.4), the
    /// same convention as the other operators, so his feet stay in the same place in every animation and he sits on the
    /// tile like the rest of the squad.
    /// </para>
    /// <para>
    /// Attack frames (0-indexed here, F1-F12 in the animation notes), 90 ms each, 1.08 s in all: F1 wind-up, F2 jaw opens,
    /// F3-F4 tongue lashes out, F5 GRAB (the tongue clenches: frame 4), F6-F7 reel in, F8 CHOMP (frame 7), F9-F12 chew,
    /// gulp, burp, recover. The gameplay hooks are events fired as those frames are reached:
    /// <see cref="GrabFrameReached"/>, <see cref="ChompFrameReached"/> and <see cref="AttackFinished"/>. A whiff
    /// (<see cref="EndAfterRetract"/>) skips the chewing frames. The attack plays at a fixed speed (it is not scaled
    /// by attack speed).
    /// </para>
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class MlemSpriteAnimation : MonoBehaviour, IOperatorSpriteFrames
    {
        public const int AttackFrameCount = 12;
        public const int DigestFrameCount = 8;
        public const int IdleFrameCount = 16;
        public const float DefaultIdleFrameDuration = 0.1f;
        public const float DefaultDigestFrameDuration = 0.12f;
        public const float DefaultAttackFrameDuration = 0.09f;

        public const float PixelsPerUnit = 32f;

        /// <summary>Height of the sprite pivot (his body centre) above the bottom edge of the frame, in pixels.</summary>
        public const float PivotHeightPixels = 16f;

        /// <summary>
        /// Where the clenched tongue tip is on attack frames 5 to 8 (the grab, the two reel-in frames and the mouth), in
        /// pixels from his feet at the frame's pivot column: negative x is in front of him in the left-facing art,
        /// positive y is up. Enemies the tongue has caught follow these points.
        /// </summary>
        public static readonly Vector2[] GripPixels =
        {
            new Vector2(-59f, 15.5f),
            new Vector2(-42f, 22.5f),
            new Vector2(-26f, 18.5f),
            new Vector2(-13f, 16f)
        };

        /// <summary>How big a caught enemy is at each grip point (it shrinks as it is reeled in).</summary>
        public static readonly float[] GripScales = { 1f, 0.85f, 0.65f, 0.4f };

        /// <summary>Spread of the extra enemies caught by one tongue (2nd upgrade), in pixels, mirrored like the art.</summary>
        public static readonly Vector2[] ClusterPixels =
        {
            new Vector2(0f, 0f), new Vector2(-4f, 3f), new Vector2(4f, -3f)
        };

        [SerializeField] private Sprite[] frames;
        [SerializeField] private Sprite[] digestFrames;
        [SerializeField] private Sprite[] attackFrames;
        [SerializeField] private Sprite[] attackFxFrames;
        [SerializeField, Min(0.01f)] private float idleFrameDuration = DefaultIdleFrameDuration;
        [SerializeField, Min(0.01f)] private float digestFrameDuration = DefaultDigestFrameDuration;
        [SerializeField, Min(0.01f)] private float attackFrameDuration = DefaultAttackFrameDuration;
        [Tooltip("Attack frame on which the tongue catches its target")]
        [SerializeField] private int grabFrame = 4;
        [Tooltip("Attack frame on which his mouth closes and the catch is swallowed")]
        [SerializeField] private int chompFrame = 7;

        public Sprite[] Frames => frames;
        public Sprite[] DigestFrames => digestFrames;
        public Sprite[] AttackFrames => attackFrames;
        public Sprite[] AttackFxFrames => attackFxFrames;
        public float FrameDuration => idleFrameDuration;
        public float AttackFrameDuration => attackFrameDuration;
        public int GrabFrame => grabFrame;
        public int ChompFrame => chompFrame;
        public bool IsAttacking => attacking;
        public bool IsDigesting => digesting;
        public int CurrentAttackFrame => attackFrame;
        public bool HasAttackAnimation => attackFrames != null && attackFrames.Length > chompFrame;

        /// <summary>How long the tongue takes to drag its catch from the grab to the chomp.</summary>
        public float GrabToChompSeconds => Mathf.Max(0.01f, (chompFrame - grabFrame) * attackFrameDuration);

        /// <summary>The attack reached the grab frame: the tongue catches its target.</summary>
        public event Action GrabFrameReached;

        /// <summary>The attack reached the chomp frame: the catch is swallowed.</summary>
        public event Action ChompFrameReached;

        /// <summary>The attack ended (after the chewing, or straight after the retract on a whiff).</summary>
        public event Action AttackFinished;

        private SpriteRenderer spriteRenderer;
        private SpriteRenderer fxRenderer;
        private int idleFrame;
        private float idleTimer;
        private int digestFrame;
        private bool digesting;
        private bool stopDigestAtBoundary;
        private bool attacking;
        private bool endAfterRetract;
        private int attackFrame;
        private float attackTimer;

        public void Configure(Sprite[] idle, Sprite[] digest, Sprite[] attack, Sprite[] attackFx)
        {
            frames = idle;
            digestFrames = digest;
            attackFrames = attack;
            attackFxFrames = attackFx;
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            idleFrame = 0;
            idleTimer = 0f;
            CancelAttack();
        }

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            EnsureFramesLoaded();
            if (spriteRenderer != null && frames != null && frames.Length > 0 && frames[0] != null)
            {
                spriteRenderer.sprite = frames[0];
            }
        }

        /// <summary>Loads any sheet the prefab didn't assign (editor only; the prefab holds the frames in a build).</summary>
        public void EnsureFramesLoaded()
        {
#if UNITY_EDITOR
            if (frames == null || frames.Length == 0) frames = LoadSheet("Mlem_Sheet", IdleFrameCount);
            if (digestFrames == null || digestFrames.Length == 0) digestFrames = LoadSheet("Mlem_Digest_Sheet", DigestFrameCount);
            if (attackFrames == null || attackFrames.Length == 0) attackFrames = LoadSheet("Mlem_Attack_Sheet", AttackFrameCount);
            if (attackFxFrames == null || attackFxFrames.Length == 0) attackFxFrames = LoadSheet("Mlem_Attack_FX_Sheet", AttackFrameCount);
#endif
        }

#if UNITY_EDITOR
        private static Sprite[] LoadSheet(string sheet, int count)
        {
            UnityEngine.Object[] assets =
                UnityEditor.AssetDatabase.LoadAllAssetsAtPath($"Assets/_Project/Art/Operators/Mlem/{sheet}.png");
            if (assets == null || assets.Length == 0) return null;

            var list = new List<Sprite>();
            for (int i = 0; i < count; i++)
            {
                string spriteName = $"{sheet}_{i}";
                foreach (UnityEngine.Object asset in assets)
                {
                    if (asset is Sprite sprite && sprite.name == spriteName)
                    {
                        list.Add(sprite);
                        break;
                    }
                }
            }
            return list.Count == count ? list.ToArray() : null;
        }
#endif

        // ----------------------------------------------------------------------------------------------
        // Control
        // ----------------------------------------------------------------------------------------------

        /// <summary>Starts the tongue attack from frame 0.</summary>
        public void PlayAttack()
        {
            if (!HasAttackAnimation) return;

            attacking = true;
            endAfterRetract = false;
            attackFrame = 0;
            attackTimer = 0f;
            ShowAttackFrame();
        }

        /// <summary>The tongue came back empty: play the retract, then go straight to idle without chewing.</summary>
        public void EndAfterRetract()
        {
            endAfterRetract = true;
        }

        /// <summary>Stops the attack and returns to the idle (or digesting) loop.</summary>
        public void CancelAttack()
        {
            stopDigestAtBoundary = false;
            attacking = false;
            endAfterRetract = false;
            attackFrame = 0;
            attackTimer = 0f;
            HideFx();
            ShowLoopFrame();
        }

        /// <summary>Switches the loop between idling and digesting (the swollen belly).</summary>
        public void SetDigesting(bool value)
        {
            if (value)
            {
                stopDigestAtBoundary = false;
                if (digesting) return;
                digesting = true;
                digestFrame = 0;
                idleTimer = 0f;
                if (!attacking) ShowLoopFrame();
                return;
            }

            if (!digesting) return;

            // The munching loop finishes the frame it is on, then he goes back to idling
            if (attacking) digesting = false;
            else stopDigestAtBoundary = true;
        }

        // ----------------------------------------------------------------------------------------------
        // Playback
        // ----------------------------------------------------------------------------------------------

        private void Update()
        {
            if (spriteRenderer == null) return;

            if (attacking)
            {
                UpdateAttack();
                return;
            }

            Sprite[] loop = CurrentLoop;
            if (loop == null || loop.Length == 0) return;

            float duration = digesting ? digestFrameDuration : idleFrameDuration;
            idleTimer += Time.deltaTime;
            if (idleTimer < duration) return;

            int steps = Mathf.FloorToInt(idleTimer / duration);
            idleTimer -= steps * duration;
            if (digesting && stopDigestAtBoundary)
            {
                digesting = false;
                stopDigestAtBoundary = false;
                idleFrame = 0;
            }
            else if (digesting) digestFrame = (digestFrame + steps) % loop.Length;
            else idleFrame = (idleFrame + steps) % loop.Length;
            ShowLoopFrame();
        }

        private void LateUpdate()
        {
            // The fx overlay mirrors the body (the operator flips the body's renderer when it faces right)
            if (fxRenderer != null && fxRenderer.enabled && spriteRenderer != null)
            {
                fxRenderer.flipX = spriteRenderer.flipX;
            }
        }

        private void UpdateAttack()
        {
            attackTimer += Time.deltaTime;
            while (attacking && attackTimer >= attackFrameDuration)
            {
                attackTimer -= attackFrameDuration;
                attackFrame++;

                bool finished = attackFrame >= attackFrames.Length || (endAfterRetract && attackFrame >= chompFrame);
                if (finished)
                {
                    FinishAttack();
                    return;
                }

                ShowAttackFrame();
                if (attackFrame == chompFrame) ChompFrameReached?.Invoke();
            }
        }

        private void FinishAttack()
        {
            attacking = false;
            endAfterRetract = false;
            attackFrame = 0;
            attackTimer = 0f;
            HideFx();
            ShowLoopFrame();
            AttackFinished?.Invoke();
        }

        private Sprite[] CurrentLoop => digesting && digestFrames != null && digestFrames.Length > 0 ? digestFrames : frames;

        private void ShowLoopFrame()
        {
            if (spriteRenderer == null) return;
            Sprite[] loop = CurrentLoop;
            if (loop == null || loop.Length == 0) return;

            int index = digesting && loop == digestFrames ? digestFrame : idleFrame;
            spriteRenderer.sprite = loop[Mathf.Clamp(index, 0, loop.Length - 1)];
        }

        private void ShowAttackFrame()
        {
            if (spriteRenderer == null || attackFrames == null) return;

            int index = Mathf.Clamp(attackFrame, 0, attackFrames.Length - 1);
            spriteRenderer.sprite = attackFrames[index];
            ShowFx(index);
            if (attackFrame == grabFrame) GrabFrameReached?.Invoke();
        }

        private void ShowFx(int index)
        {
            if (attackFxFrames == null || index >= attackFxFrames.Length || attackFxFrames[index] == null)
            {
                HideFx();
                return;
            }

            if (fxRenderer == null)
            {
                var fxObject = new GameObject("MlemAttackFx");
                fxObject.transform.SetParent(transform, false);
                fxRenderer = fxObject.AddComponent<SpriteRenderer>();
                fxRenderer.sharedMaterial = spriteRenderer.sharedMaterial;
                fxRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
                fxRenderer.sortingOrder = spriteRenderer.sortingOrder + 1;
            }

            fxRenderer.sortingOrder = spriteRenderer.sortingOrder + 1;
            fxRenderer.flipX = spriteRenderer.flipX;
            fxRenderer.sprite = attackFxFrames[index];
            fxRenderer.enabled = true;
        }

        private void HideFx()
        {
            if (fxRenderer != null) fxRenderer.enabled = false;
        }
    }
}
