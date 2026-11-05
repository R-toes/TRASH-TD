using System.Collections.Generic;
using UnityEngine;
using TrashTD.Audio;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Bubble shield applied by Bubblets to an operator.
    /// Negates 1 instance of damage (regardless of value).
    /// When popped, plays a retro pixel pop animation on the actual operator's tile, heals recipient,
    /// and deals Arts damage equal to 30% of Bubblets' ATK to enemies on that actual tile
    /// and all surrounding tiles (3x3 grid area) as well as blocked enemies.
    /// Uses Bubblets-bubbles sprite at 40x40 scale centered directly on the operator.
    /// </summary>
    public class BubbleShield : MonoBehaviour
    {
        public const float BaseBubblePixelSize = 32f;
        public const float TargetBubblePixelSize = 40f;
        public const float BubbleScale = TargetBubblePixelSize / BaseBubblePixelSize; // 1.25f

        public const float PopAoePixelSize = 48f;
        public const float PopAoeRadius = (PopAoePixelSize * 0.5f) / BaseBubblePixelSize; // 0.75f units (24px radius = 48x48 visual)

        private static Sprite sharedBubbleSprite;

        private OperatorBase recipient;
        private int bubbletATK;
        private BubbletsOperator source;
        private GameObject visualObject;
        private SpriteRenderer visualRenderer;
        private bool isActive;

        public bool IsActive => isActive;
        public int BubbletATK => bubbletATK;
        public GameObject VisualObject => visualObject;
        public SpriteRenderer VisualRenderer => visualRenderer;

        public static bool HasActiveShield(OperatorBase op)
        {
            if (op == null) return false;
            var shield = op.GetComponent<BubbleShield>();
            return shield != null && shield.isActive;
        }

        public static BubbleShield Apply(OperatorBase target, int atk, BubbletsOperator caster)
        {
            if (target == null) return null;

            var existing = target.GetComponent<BubbleShield>();
            if (existing != null)
            {
                existing.Refresh(atk, caster);
                return existing;
            }

            var shield = target.gameObject.AddComponent<BubbleShield>();
            shield.Initialize(target, atk, caster);
            return shield;
        }

        public void Initialize(OperatorBase target, int atk, BubbletsOperator caster)
        {
            recipient = target;
            bubbletATK = atk;
            source = caster;
            isActive = true;

            CreateVisual();
        }

        public void Refresh(int atk, BubbletsOperator caster)
        {
            bubbletATK = atk;
            source = caster;
            isActive = true;

            if (visualObject == null)
            {
                CreateVisual();
            }
        }

        private void CreateVisual()
        {
            if (visualObject != null) return;

            visualObject = new GameObject("BubbleShieldVisual");
            visualObject.transform.SetParent(transform, false);
            visualObject.transform.localPosition = Vector3.zero;
            visualObject.transform.localScale = Vector3.one * BubbleScale;

            visualRenderer = visualObject.AddComponent<SpriteRenderer>();
            visualRenderer.sprite = GetBubbleSprite();
            visualRenderer.sortingOrder = 18; // Above operator sprite
            visualRenderer.color = new Color(1f, 1f, 1f, 0.95f);
        }

        private void Update()
        {
            if (!isActive || visualObject == null) return;

            // Subtle floating and pulsation animation fitting 40x40 scale centered on operator
            float pulse = 1f + 0.03f * Mathf.Sin(Time.time * 3.5f);
            float bob = 0.02f * Mathf.Sin(Time.time * 2.5f);
            visualObject.transform.localScale = Vector3.one * (BubbleScale * pulse);
            visualObject.transform.localPosition = new Vector3(0f, bob, 0f);
        }

        /// <summary>
        /// Pops the bubble shield. Can optionally suppress the "BLOCKED" text if incoming damage was not negated.
        /// Still triggers pop visual, sound, heal, and counter damage.
        /// </summary>
        public void Pop(bool showBlockedText = true)
        {
            if (!isActive || recipient == null) return;

            isActive = false;

            // 1. Pop visual and sound
            PopVisual(showBlockedText);

            // 2. Heal recipient when the bubble pops
            ApplyPopHeal();

            // 3. Retaliatory damage to enemies on this tile or in front
            ApplyCounterDamage();

            // 4. Clean up
            if (visualObject != null)
            {
                Destroy(visualObject);
            }
            Destroy(this);
        }

        /// <summary>
        /// Attempt to consume the shield when an incoming instance of damage occurs.
        /// Returns true if the shield negated the damage.
        /// </summary>
        public bool TryConsume(int rawATK, DamageType damageType)
        {
            if (!isActive || recipient == null) return false;

            Pop(true);
            return true;
        }

        private void PopVisual(bool showBlockedText = true)
        {
            Vector3 pos = transform.position;

            // 1. Play the crisp 48x48 retro pixel pop animation & pixel particles centered on the actual operator
            CombatFx.BubblePop(pos, PopAoeRadius);

            // 2. Play the circular AoE blast scaled to 48x48 px centered to the operator
            AoeBlastVisual.PlayCircle(pos, PopAoeRadius, new Color(0.35f, 0.88f, 1f, 0.8f), AoeStyle.Pulse);

            // Floating indicator on recipient
            if (showBlockedText)
            {
                FloatingCombatNumber.ShowText(pos, "BLOCKED", new Color(0.4f, 0.9f, 1f));
            }

            // Sound: the caster's Ability clip, else the shared magic sound.
            if (source != null) source.PlayShieldPopSound();
            else AudioManager.Instance?.PlaySfx(SfxId.GameplayMagicAttack);
        }

        private void ApplyPopHeal()
        {
            if (recipient == null || !recipient.IsDeployed) return;

            int healAmount = bubbletATK > 0 ? bubbletATK : (source != null ? source.CurrentATK : 45);
            recipient.Heal(healAmount);
        }

        private void ApplyCounterDamage()
        {
            if (recipient == null) return;

            GridManager gridManager = FindAnyObjectByType<GridManager>();
            GridCell currentCell = recipient.DeployedCell;
            if (currentCell == null && gridManager != null)
            {
                currentCell = gridManager.GetCell(gridManager.WorldToGridPosition(recipient.transform.position));
            }

            var checkCells = new List<GridCell>();

            // Collect the actual operator's tile and all 8 surrounding tiles (3x3 grid area)
            if (gridManager != null && currentCell != null)
            {
                Vector2Int centerPos = currentCell.GridPosition;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        GridCell cell = gridManager.GetCell(centerPos.x + dx, centerPos.y + dy);
                        if (cell != null && !checkCells.Contains(cell))
                        {
                            checkCells.Add(cell);
                        }
                    }
                }
            }
            else if (currentCell != null)
            {
                checkCells.Add(currentCell);
            }

            var hitEnemies = new HashSet<EnemyBase>();

            // Enemies on the actual tile or any surrounding tile
            if (EnemyManager.Instance != null && checkCells.Count > 0)
            {
                var inCells = EnemyManager.Instance.GetEnemiesInCells(
                    checkCells,
                    recipient != null && recipient.Data != null ? recipient.Data.position : OperatorPosition.Ranged);
                if (inCells != null)
                {
                    for (int i = 0; i < inCells.Count; i++)
                    {
                        var e = inCells[i];
                        if (e != null && !e.IsDead) hitEnemies.Add(e);
                    }
                }
            }
            else if (EnemyManager.Instance != null && checkCells.Count == 0)
            {
                // Fallback for tests when GridManager is uninitialized
                var active = EnemyManager.Instance.ActiveEnemies;
                if (active != null)
                {
                    Vector3 center = recipient.transform.position;
                    for (int i = 0; i < active.Count; i++)
                    {
                        var e = active[i];
                        if (e != null && !e.IsDead)
                        {
                            Vector3 diff = e.transform.position - center;
                            if (Mathf.Abs(diff.x) <= 1.55f && Mathf.Abs(diff.y) <= 1.55f)
                            {
                                hitEnemies.Add(e);
                            }
                        }
                    }
                }
            }

            // Enemies currently blocked by the recipient
            var blocked = recipient.BlockedEnemies;
            if (blocked != null)
            {
                for (int i = 0; i < blocked.Count; i++)
                {
                    var e = blocked[i];
                    if (e != null && !e.IsDead) hitEnemies.Add(e);
                }
            }

            int baseAtk = bubbletATK > 0 ? bubbletATK : (source != null ? source.CurrentATK : 45);
            int damageValue = Mathf.Max(1, Mathf.RoundToInt(baseAtk * 0.30f));

            foreach (var enemy in hitEnemies)
            {
                if (enemy == null || enemy.IsDead) continue;

                int calculated = DamageCalculator.CalculateDamage(damageValue, enemy.CurrentRES);
                enemy.TryTakeAttackDamage(calculated, DamageType.Arts, recipient);
            }
        }

        private void OnDestroy()
        {
            if (visualObject != null)
            {
                Destroy(visualObject);
            }
        }

        public static void ResetSharedBubbleSprite()
        {
            sharedBubbleSprite = null;
        }

        public static Sprite GetBubbleSprite()
        {
            if (sharedBubbleSprite != null) return sharedBubbleSprite;

#if UNITY_EDITOR
            sharedBubbleSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/Operators/Bubblets-bubbles.png");
            if (sharedBubbleSprite == null)
            {
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Operators/Bubblets-bubbles.png");
                if (assets != null)
                {
                    foreach (var a in assets)
                    {
                        if (a is Sprite s)
                        {
                            sharedBubbleSprite = s;
                            break;
                        }
                    }
                }
            }
#endif

            if (sharedBubbleSprite == null)
            {
                string path = System.IO.Path.Combine(Application.dataPath, "_Project/Art/Operators/Bubblets-bubbles.png");
                if (System.IO.File.Exists(path))
                {
                    try
                    {
                        byte[] raw = System.IO.File.ReadAllBytes(path);
                        var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                        texture.filterMode = FilterMode.Point;
                        texture.LoadImage(raw);
                        sharedBubbleSprite = Sprite.Create(
                            texture,
                            new Rect(0, 0, texture.width, texture.height),
                            new Vector2(0.5f, 0.5f),
                            32f);
                        sharedBubbleSprite.name = "Bubblets-bubbles";
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[BubbleShield] Failed to load Bubblets-bubbles.png: {ex.Message}");
                    }
                }
            }

            if (sharedBubbleSprite == null)
            {
                sharedBubbleSprite = CreateProceduralBubbleSprite();
            }

            return sharedBubbleSprite;
        }

        private static Sprite CreateProceduralBubbleSprite()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;

            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = (size - 1) * 0.48f;
            float rimWidth = 2.0f;

            // Highlight position at upper-left
            Vector2 highlightCenter = center + new Vector2(-radius * 0.45f, radius * 0.45f);
            float highlightRadius = radius * 0.28f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 pos = new Vector2(x, y);
                    float dist = Vector2.Distance(pos, center);

                    if (dist > radius)
                    {
                        texture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    // Base translucent body
                    Color col = new Color(0.45f, 0.85f, 1f, Mathf.Lerp(0.15f, 0.38f, dist / radius));

                    // Glowing rim
                    if (dist >= radius - rimWidth)
                    {
                        float rimAlpha = Mathf.InverseLerp(radius - rimWidth, radius, dist);
                        Color rimColor = new Color(0.75f, 0.95f, 1f, 0.88f);
                        col = Color.Lerp(col, rimColor, rimAlpha);
                    }

                    // Shine dot in top-left
                    float highlightDist = Vector2.Distance(pos, highlightCenter);
                    if (highlightDist < highlightRadius)
                    {
                        float shine = 1f - (highlightDist / highlightRadius);
                        col = Color.Lerp(col, new Color(1f, 1f, 1f, 0.95f), shine * 0.85f);
                    }

                    texture.SetPixel(x, y, col);
                }
            }

            texture.Apply();
            return Sprite.Create(
                texture,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                32);
        }
    }
}
