using System.Collections.Generic;
using UnityEngine;
using TrashTD.Audio;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Core.GameLoop
{
    /// <summary>
    /// Controls a vehicle driving along the boulevard in Stage 5.
    /// Deals true damage based on distance travelled to ground enemies and deployed operators.
    /// Damage counter resets per target type (enemy vs ally counters are separate).
    /// When hitting an operator, the vehicle decelerates on impact (squash + slowdown) then recovers speed.
    /// When damage ramps back up to 80+ again after hitting and stunning an operator, it can stun again.
    /// </summary>
    public class Stage5TrafficCar : MonoBehaviour
    {
        public const float DefaultSpeed = 5.0f;
        public const int BaseTrueDamage = 40;
        public const int DamageRampPerTwoTiles = 20;
        public const int StunRampDamageThreshold = 80;
        public const float ImpactSlowdownRatio = 0.3f;
        public const float ImpactSlowdownDuration = 0.7f;

        /// <summary>
        /// Calculates true damage based on distance travelled:
        /// Starts at 40 and ramps up by 20 every 2 tiles (40, 60, 80, 100, 120, 140, 160...).
        /// </summary>
        public static int CalculateDamage(float tilesTravelled)
        {
            float dist = Mathf.Max(0f, tilesTravelled);
            int steps = Mathf.FloorToInt((dist + 0.001f) / 2.0f);
            return BaseTrueDamage + (steps * DamageRampPerTwoTiles);
        }

        [Header("Car Properties")]
        public int LaneY { get; private set; }
        public float OriginalSpeed { get; private set; } = DefaultSpeed;
        private float currentSpeed = DefaultSpeed;
        public float Speed
        {
            get => currentSpeed;
            set
            {
                currentSpeed = value;
                if (hitSlowdownTimer <= 0f)
                {
                    OriginalSpeed = value;
                }
            }
        }

        private float hitSlowdownTimer = 0f;
        public float HitSlowdownTimer => hitSlowdownTimer;
        public bool IsSlowedDown => hitSlowdownTimer > 0f;

        private Vector3 baseScale = Vector3.one;
        private SpriteRenderer spriteRenderer;

        public float StartX { get; private set; }
        public float LastEnemyHitX { get; private set; }
        public float LastAllyHitX { get; private set; }

        public float FrontBumperOffsetX { get; set; } = 1.5f; // 96px width / 32 PPU = 3.0 units, half is 1.5 units
        public float RearBumperOffsetX { get; set; } = -1.5f;

        public float StunDuration { get; set; } = 5.0f;
        private bool hasHitFirstOperator = false;
        public bool HasHitFirstOperator => hasHitFirstOperator;

        private float exitX;
        public float ExitX => exitX;
        private float cellSize = 1.0f;

        private GridManager gridManager;
        private EnemyManager enemyManager;
        private OperatorManager operatorManager;

        private readonly HashSet<EnemyBase> hitEnemies = new HashSet<EnemyBase>();
        private readonly HashSet<OperatorBase> hitAllies = new HashSet<OperatorBase>();

        public IReadOnlyCollection<EnemyBase> HitEnemies => hitEnemies;
        public IReadOnlyCollection<OperatorBase> HitAllies => hitAllies;

        public void Initialize(
            int laneY,
            Sprite sprite,
            GridManager grid,
            EnemyManager enemies,
            OperatorManager operators,
            float speed = DefaultSpeed,
            int sortingOrder = 9)
        {
            LaneY = laneY;
            OriginalSpeed = speed;
            currentSpeed = speed;
            hitSlowdownTimer = 0f;
            baseScale = transform.localScale;

            gridManager = grid;
            enemyManager = enemies;
            operatorManager = operators;
            cellSize = gridManager != null ? gridManager.CellSize : 1.0f;

            // Configure SpriteRenderer
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
            {
                spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            }
            spriteRenderer.sprite = sprite;
            spriteRenderer.sortingOrder = sortingOrder;
            spriteRenderer.color = Color.white;

            // Lane center Y
            float laneCenterY = gridManager != null 
                ? gridManager.GridToWorldPosition(0, laneY).y 
                : laneY * cellSize + cellSize * 0.5f;

            // Align bottom of car with bottom of tile:
            // Bottom of tile is laneCenterY - cellSize * 0.5f.
            // Sprite height is 48px / 32 PPU = 1.5 units, pivot is at center (0.75 from bottom).
            float spriteHeightUnits = (sprite != null && sprite.pixelsPerUnit > 0)
                ? (sprite.rect.height / sprite.pixelsPerUnit)
                : 1.5f;
            float spriteWidthUnits = (sprite != null && sprite.pixelsPerUnit > 0)
                ? (sprite.rect.width / sprite.pixelsPerUnit)
                : 3.0f;

            FrontBumperOffsetX = spriteWidthUnits * 0.5f;
            RearBumperOffsetX = -spriteWidthUnits * 0.5f;

            float bottomTileY = laneCenterY - cellSize * 0.5f;
            float carCenterY = bottomTileY + (spriteHeightUnits * 0.5f);

            // Starting position: at red box (column 0)
            float cell0CenterX = gridManager != null
                ? gridManager.GridToWorldPosition(0, laneY).x
                : cellSize * 0.5f;

            StartX = cell0CenterX;
            LastEnemyHitX = StartX;
            LastAllyHitX = StartX;

            float initialCarCenterX = cell0CenterX - FrontBumperOffsetX;
            transform.position = new Vector3(initialCarCenterX, carCenterY, 0f);

            // Exit X: one tile after the last violet tile on the right (gridWidth - 1)
            int gridWidth = gridManager != null ? gridManager.Width : 13;
            float lastCellX = gridManager != null
                ? gridManager.GridToWorldPosition(gridWidth - 1, laneY).x
                : (gridWidth - 1) * cellSize + cellSize * 0.5f;
            exitX = lastCellX + cellSize;
        }

        private void Update()
        {
            if (GameManager.Instance != null && GameManager.Instance.IsPaused) return;

            Move(Time.deltaTime);
        }

        public void UpdateSpeedAndVisuals(float dt)
        {
            if (hitSlowdownTimer > 0f)
            {
                hitSlowdownTimer -= dt;
                if (hitSlowdownTimer <= 0f)
                {
                    hitSlowdownTimer = 0f;
                    currentSpeed = OriginalSpeed;
                }
                else
                {
                    float progress = 1f - (hitSlowdownTimer / ImpactSlowdownDuration);
                    float easeIn = progress * progress;
                    currentSpeed = Mathf.Lerp(OriginalSpeed * ImpactSlowdownRatio, OriginalSpeed, easeIn);
                }
            }
            else
            {
                currentSpeed = OriginalSpeed;
            }

            if (transform.localScale != baseScale)
            {
                transform.localScale = Vector3.MoveTowards(transform.localScale, baseScale, dt * 1.0f);
            }

            if (spriteRenderer != null && spriteRenderer.color != Color.white)
            {
                spriteRenderer.color = Color.Lerp(spriteRenderer.color, Color.white, dt * 8.0f);
            }
        }

        public void TriggerHitSlowdown()
        {
            hitSlowdownTimer = ImpactSlowdownDuration;
            currentSpeed = OriginalSpeed * ImpactSlowdownRatio;

            // Impact squash: compresses along X and expands along Y to visually register hitting a solid obstacle
            transform.localScale = new Vector3(baseScale.x * 0.88f, baseScale.y * 1.12f, baseScale.z);

            if (spriteRenderer != null)
            {
                spriteRenderer.color = new Color(1f, 0.75f, 0.75f);
            }
        }

        public void Move(float dt)
        {
            if (dt <= 0f) return;

            UpdateSpeedAndVisuals(dt);

            float moveDist = currentSpeed * dt;
            transform.position += new Vector3(moveDist, 0f, 0f);

            float frontX = transform.position.x + FrontBumperOffsetX;
            float rearX = transform.position.x + RearBumperOffsetX;

            // Process collisions on current step up to the front bumper
            CheckCollisions(frontX, rearX);

            // The car should be gone when it reached the last violet tile in the right
            if (frontX >= exitX)
            {
                if (Application.isPlaying)
                {
                    Destroy(gameObject);
                }
                else
                {
                    DestroyImmediate(gameObject);
                }
                return;
            }
        }

        public void CheckCollisions(float frontX, float rearX)
        {
            float laneCenterY = gridManager != null
                ? gridManager.GridToWorldPosition(0, LaneY).y
                : transform.position.y - 0.25f;

            // 1. Process ground enemies in lane
            if (enemyManager != null && enemyManager.ActiveEnemies != null)
            {
                var candidateEnemies = new List<EnemyBase>();
                var enemies = enemyManager.ActiveEnemies;
                for (int i = 0; i < enemies.Count; i++)
                {
                    var enemy = enemies[i];
                    if (enemy == null || enemy.IsDead || enemy.IsInsideHole) continue;
                    if (enemy.Data != null && enemy.Data.movementType != EnemyMovementType.Ground) continue;
                    if (hitEnemies.Contains(enemy)) continue;

                    Vector3 pos = enemy.transform.position;
                    if (Mathf.Abs(pos.y - laneCenterY) > 0.6f * cellSize) continue;

                    if (pos.x <= frontX && pos.x >= rearX - 0.3f)
                    {
                        candidateEnemies.Add(enemy);
                    }
                }

                if (candidateEnemies.Count > 1)
                {
                    candidateEnemies.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
                }

                for (int i = 0; i < candidateEnemies.Count; i++)
                {
                    var enemy = candidateEnemies[i];
                    hitEnemies.Add(enemy);

                    float targetX = enemy.transform.position.x;
                    float tilesTravelled = Mathf.Max(0f, (targetX - LastEnemyHitX) / cellSize);
                    int damage = CalculateDamage(tilesTravelled);

                    if (damage > 0)
                    {
                        AudioManager.Instance?.PlaySfx(SfxId.Stage5CarHit);
                        enemy.TakeTrueDamage(damage);
                    }

                    LastEnemyHitX = targetX;
                }
            }

            // 2. Process deployed operators in lane
            if (operatorManager != null && operatorManager.DeployedOperators != null)
            {
                var candidateOperators = new List<OperatorBase>();
                var operators = operatorManager.DeployedOperators;
                for (int i = 0; i < operators.Count; i++)
                {
                    var op = operators[i];
                    if (op == null || !op.IsDeployed || op.CurrentHP <= 0) continue;
                    if (hitAllies.Contains(op)) continue;

                    Vector3 pos = op.transform.position;
                    if (Mathf.Abs(pos.y - laneCenterY) > 0.6f * cellSize) continue;

                    if (pos.x <= frontX && pos.x >= rearX - 0.3f)
                    {
                        candidateOperators.Add(op);
                    }
                }

                if (candidateOperators.Count > 1)
                {
                    candidateOperators.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
                }

                for (int i = 0; i < candidateOperators.Count; i++)
                {
                    var op = candidateOperators[i];
                    hitAllies.Add(op);

                    float targetX = op.transform.position.x;
                    float tilesTravelled = Mathf.Max(0f, (targetX - LastAllyHitX) / cellSize);
                    int damage = CalculateDamage(tilesTravelled);

                    bool damageDealt = false;
                    if (damage > 0)
                    {
                        AudioManager.Instance?.PlaySfx(SfxId.Stage5CarHit);
                        damageDealt = op.TakeTrueDamage(damage, ignoreShieldNegation: true);
                    }

                    if (damageDealt)
                    {
                        LastAllyHitX = targetX;

                        TriggerHitSlowdown();

                        // First operator hit gets stunned if not killed, and when damage ramps
                        // back up to 80+ again after hitting and stunning an operator, it stuns again!
                        bool canStun = !hasHitFirstOperator || damage >= StunRampDamageThreshold;
                        if (canStun)
                        {
                            hasHitFirstOperator = true;
                            if (op != null && op.IsDeployed && op.CurrentHP > 0)
                            {
                                op.ApplyStun(StunDuration);
                            }
                        }
                    }
                }
            }
        }
    }
}
