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
    /// When popped, deals Arts damage equal to Bubblets' ATK to enemies on that tile
    /// or directly in front (such as enemies blocked by the operator).
    /// </summary>
    public class BubbleShield : MonoBehaviour
    {
        private static Sprite sharedBubbleSprite;

        private OperatorBase recipient;
        private int bubbletATK;
        private BubbletsOperator source;
        private GameObject visualObject;
        private SpriteRenderer visualRenderer;
        private bool isActive;

        public bool IsActive => isActive;
        public int BubbletATK => bubbletATK;

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
            visualObject.transform.localPosition = new Vector3(0f, 0f, 0f);

            visualRenderer = visualObject.AddComponent<SpriteRenderer>();
            visualRenderer.sprite = GetBubbleSprite();
            visualRenderer.sortingOrder = 18; // Above operator sprite
            visualRenderer.color = new Color(1f, 1f, 1f, 0.9f);
        }

        private void Update()
        {
            if (!isActive || visualObject == null) return;

            // Subtle floating and pulsation animation fitting 32x32 px box
            float pulse = 1f + 0.03f * Mathf.Sin(Time.time * 3.5f);
            float bob = 0.02f * Mathf.Sin(Time.time * 2.5f);
            visualObject.transform.localScale = Vector3.one * pulse;
            visualObject.transform.localPosition = new Vector3(0f, bob, 0f);
        }

        /// <summary>
        /// Attempt to consume the shield when an incoming instance of damage occurs.
        /// Returns true if the shield negated the damage.
        /// </summary>
        public bool TryConsume(int rawATK, DamageType damageType)
        {
            if (!isActive || recipient == null) return false;

            isActive = false;

            // 1. Pop visual and sound
            PopVisual();

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

            return true;
        }

        private void PopVisual()
        {
            Vector3 pos = transform.position;

            // Cyan burst effect scaled to 32x32 px bounds (0.5 unit radius = 1 unit diameter)
            AoeBlastVisual.PlayCircle(pos, 0.5f, new Color(0.35f, 0.88f, 1f, 0.85f));

            // Floating indicator on recipient
            FloatingCombatNumber.ShowText(pos, "BLOCKED", new Color(0.4f, 0.9f, 1f));

            // Sound
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySfx(SfxId.GameplayMagicAttack);
            }
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

            GridManager gridManager = FindFirstObjectByType<GridManager>();
            GridCell currentCell = recipient.DeployedCell;
            GridCell frontCell = null;

            if (gridManager != null && currentCell != null)
            {
                Vector2Int frontOffset = recipient.Facing switch
                {
                    OperatorFacing.Right => new Vector2Int(1, 0),
                    OperatorFacing.Left => new Vector2Int(-1, 0),
                    OperatorFacing.Up => new Vector2Int(0, 1),
                    OperatorFacing.Down => new Vector2Int(0, -1),
                    _ => new Vector2Int(1, 0)
                };
                frontCell = gridManager.GetCell(currentCell.GridPosition + frontOffset);
            }

            var hitEnemies = new HashSet<EnemyBase>();

            // Enemies on current tile or front tile
            if (EnemyManager.Instance != null)
            {
                var checkCells = new List<GridCell>();
                if (currentCell != null) checkCells.Add(currentCell);
                if (frontCell != null) checkCells.Add(frontCell);

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

            // If front cell exists and has enemies, play splash on front cell too
            if (frontCell != null && hitEnemies.Count > 0)
            {
                AoeBlastVisual.PlayCircle(frontCell.WorldPosition, 0.8f, new Color(0.35f, 0.88f, 1f, 0.6f));
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

        private static Sprite GetBubbleSprite()
        {
            if (sharedBubbleSprite != null) return sharedBubbleSprite;

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
            sharedBubbleSprite = Sprite.Create(
                texture,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                32);

            return sharedBubbleSprite;
        }
    }
}
