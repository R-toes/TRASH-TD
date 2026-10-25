using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Robozombie — a Grunt enemy with standard Sludge Grunt stats,
    /// but with a 25% chance upon death to detonate like an Exploding Bug,
    /// dealing 3× ATK true damage to operators on the 4 adjacent tiles.
    /// </summary>
    public class RobozombieEnemy : GruntEnemy
    {
        private static readonly Vector2Int[] AdjacentOffsets =
        {
            new Vector2Int( 1,  0),
            new Vector2Int(-1,  0),
            new Vector2Int( 0,  1),
            new Vector2Int( 0, -1),
        };

        [SerializeField, Range(0f, 1f)]
        private float explosionChance = 0.25f;

        public float ExplosionChance
        {
            get => explosionChance;
            set => explosionChance = Mathf.Clamp01(value);
        }

        public bool DidExplode { get; private set; }

        protected override void Die()
        {
            if (Random.value <= explosionChance)
            {
                Explode();
            }

            base.Die();
        }

        public override void Initialize(EnemyData enemyData, int difficultyLevel)
        {
            base.Initialize(enemyData, difficultyLevel);

            if (data != null && data.visualOffset != Vector3.zero && transform.Find("EnemyVisual") == null)
            {
                SpriteRenderer sr = GetComponent<SpriteRenderer>();
                if (sr != null && sr.enabled)
                {
                    sr.enabled = false;
                    var visualObject = new GameObject("EnemyVisual");
                    visualObject.transform.SetParent(transform, false);
                    visualObject.transform.localPosition = data.visualOffset;

                    var visualRenderer = visualObject.AddComponent<SpriteRenderer>();
                    visualRenderer.sprite = sr.sprite;
                    visualRenderer.color = sr.color;
                    visualRenderer.sharedMaterial = sr.sharedMaterial;
                    visualRenderer.sortingLayerID = sr.sortingLayerID;
                    visualRenderer.sortingOrder = sr.sortingOrder;
                    visualRenderer.flipX = sr.flipX;
                    visualRenderer.flipY = sr.flipY;
                }
            }
        }

        private GridManager localGridManager;

        public new void SetGridManager(GridManager manager)
        {
            base.SetGridManager(manager);
            localGridManager = manager;
        }

        public void Explode()
        {
            DidExplode = true;

            // Visual feedback — orange/yellow burst using the existing projectile VFX system.
            var explosionPos = VisualPosition;
            Color flashColor = new Color(1f, 0.55f, 0.1f, 1f);

            // Expanding ring blast visual — radius 1.5 covers the full adjacent-tile area.
            AoeBlastVisual.PlayCircle(explosionPos, 1.5f, flashColor, AoeStyle.Explosion);

            // Flash a "BOOM!" text above the enemy.
            FloatingCombatNumber.ShowText(explosionPos, "BOOM!", flashColor);

            // Damage operators on the 4 adjacent tiles.
            if (OperatorManager.Instance == null || data == null)
                return;

            GridManager gridMgr = localGridManager != null ? localGridManager : FindAnyObjectByType<GridManager>();
            if (gridMgr == null)
                return;

            Vector2Int selfCell = gridMgr.WorldToGridPosition(transform.position);
            var deployed = OperatorManager.Instance.DeployedOperators;
            if (deployed == null)
                return;

            for (int i = 0; i < deployed.Count; i++)
            {
                OperatorBase op = deployed[i];
                if (op == null || !op.IsDeployed || op.DeployedCell == null)
                    continue;

                Vector2Int opCell = op.DeployedCell.GridPosition;
                Vector2Int delta = opCell - selfCell;

                // Hit if the operator is on one of the 4 adjacent tiles.
                bool isAdjacent = (delta.x == 0 && (delta.y == 1 || delta.y == -1)) ||
                                  (delta.y == 0 && (delta.x == 1 || delta.x == -1));

                if (isAdjacent)
                {
                    // True damage so the explosion bypasses operator DEF — 3× ATK for a meaningful burst.
                    op.TakeTrueDamage(currentATK * 3);
                }
            }
        }
    }
}
