using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Data;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Flyer enemy — follows the shared ground route but remains an air target.
    /// Countered by ranged operators.
    ///
    /// Movement type is set to Air via EnemyData.movementType. Every flyer draws in the air layer with a
    /// hovering ground shadow; a plain flyer (Acid Flyer) shoots with the shared ranged-caster behaviour,
    /// preferring ranged operators.
    /// </summary>
    public class FlyerEnemy : EnemyBase
    {
        // Air units draw above ground units (enemies 4, operators 5) and foreground artwork (10).
        private const int AirSortingOrder = 12;
        // The ground shadow draws over operators/ground enemies so the flyer visibly passes above them.
        private const int ShadowSortingOrder = 6;
        private const float ShadowDrop = 0.34f;

        private static Sprite shadowSprite;

        private SpriteRenderer shadowRenderer;
        private float hoverPhase;

        /// <summary>
        /// True for flyers that run their own attack logic (AirCombatEnemy); they only need the
        /// base movement/status update, not this class's default projectile attack.
        /// </summary>
        protected virtual bool HandlesOwnAttacks => false;

        public override void Initialize(EnemyData enemyData, int difficultyLevel)
        {
            base.Initialize(enemyData, difficultyLevel);
            hoverPhase = Random.value * Mathf.PI * 2f;
            CreateAirborneVisuals();
        }

        protected override void Update()
        {
            if (HandlesOwnAttacks)
            {
                // Flyers are never blocked, so the base update only animates, applies status and moves.
                base.Update();
            }
            else
            {
                UpdateRangedBehavior(prioritizeRangedOperators: true);
            }

            if (!IsDead) UpdateHoverShadow();
        }

        /// <summary>
        /// Flyers ignore blocking — they fly over ground operators.
        /// </summary>
        public override void OnBlocked(Operators.OperatorBase blocker)
        {
            // Flyers cannot be blocked — they fly over everything
        }

        protected static float CellSize
        {
            get
            {
                GridManager grid = GridManagerCache;
                return grid != null ? grid.CellSize : 1f;
            }
        }

        private static GridManager cachedGrid;
        protected static GridManager GridManagerCache
        {
            get
            {
                if (cachedGrid == null) cachedGrid = FindAnyObjectByType<GridManager>();
                return cachedGrid;
            }
        }

        // ========================
        // Airborne visuals
        // ========================

        private void CreateAirborneVisuals()
        {
            SpriteRenderer renderer = GetComponent<SpriteRenderer>();
            if (renderer == null) return;

            // A flyer with a visualOffset draws through its "EnemyVisual" child (the root renderer is hidden),
            // so the air sorting order and tint go on every sprite but the shadow.
            foreach (SpriteRenderer sprite in GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sprite.gameObject.name == "AirShadow") continue;
                sprite.sortingOrder = AirSortingOrder;
                sprite.color = GetAirColor();
            }

            if (shadowRenderer != null) return;

            GameObject shadow = new GameObject("AirShadow");
            shadow.transform.SetParent(transform, false);
            shadow.transform.localPosition = new Vector3(0f, -ShadowDrop, 0.1f);
            shadowRenderer = shadow.AddComponent<SpriteRenderer>();
            shadowRenderer.sprite = GetShadowSprite();
            shadowRenderer.color = new Color(0f, 0f, 0f, 0.4f);
            shadowRenderer.sortingLayerID = renderer.sortingLayerID;
            shadowRenderer.sortingOrder = ShadowSortingOrder;
            UpdateHoverShadow();
        }

        /// <summary>Shadow breathes as if the flyer bobs up and down; shrinks/fades when "higher".</summary>
        private void UpdateHoverShadow()
        {
            if (shadowRenderer == null) return;

            hoverPhase += Time.deltaTime * 3f;
            float bob = (Mathf.Sin(hoverPhase) + 1f) * 0.5f; // 0 = low, 1 = high
            float size = CellSize * Mathf.Lerp(0.62f, 0.5f, bob);
            Vector3 parentScale = transform.lossyScale;
            shadowRenderer.transform.localScale = new Vector3(
                size / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
                size * 0.32f / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
                1f);
            Color color = shadowRenderer.color;
            color.a = Mathf.Lerp(0.45f, 0.28f, bob);
            shadowRenderer.color = color;
        }

        /// <summary>Soft-edged 1x1-unit ellipse generated once and shared by all flyers.</summary>
        private static Sprite GetShadowSprite()
        {
            if (shadowSprite != null) return shadowSprite;

            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center) / center;
                    float dy = (y - center) / center;
                    float alpha = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.SmoothStep(0f, 1f, alpha * 1.6f) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            shadowSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return shadowSprite;
        }

        protected virtual Color GetAirColor()
        {
            return Color.white;
        }
    }
}
