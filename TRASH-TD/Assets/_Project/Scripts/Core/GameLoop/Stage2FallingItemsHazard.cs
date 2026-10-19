using System;
using System.Collections;
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
    public enum Stage2ItemSet
    {
        WeakDotTrueDamage = 0,      // 0 - 7: give weak but dot true damage
        DirectTrueDamage = 1,       // 8 - 15: deals true damage
        DirectTrueDamageAndStun = 2 // 16 - 23: deals true damage and gives stun to the operator
    }

    /// <summary>
    /// Stage 2 Mechanic: Falling Items Hazard.
    /// Every 5 seconds, an item drops onto the map with:
    /// - 40% chance to target a random deployed operator
    /// - 40% chance to target a random deployable tile
    /// - 20% chance to target a random alive enemy
    ///
    /// Items are picked from the 24 sliced sprites of "Level 2 thrown-items":
    /// - 0..7: Weak DoT true damage (2 true damage / sec for 5s)
    /// - 8..15: Direct true damage (15 true damage)
    /// - 16..23: Direct true damage (15 true damage) + Stun (2.0s to operator)
    ///
    /// Falling animation drops from 3 tiles higher before landing in the middle of the 32x32 tile.
    /// Ability interactions:
    /// - Thornchin: does not retaliate
    /// - Bubblets: pops the bubble shield
    /// - Mossmo: gets cooldown (3s) and triggers reactive heal
    /// - Stag-ger: can still roll dodge (20%)
    /// - Proxyshot: still can activate camo
    /// </summary>
    public class Stage2FallingItemsHazard : MonoBehaviour
    {
        public const float DropIntervalSeconds = 10.0f;
        public const float FallHeightInTiles = 3.0f;
        public const float FallDurationSeconds = 0.40f;

        public const int DotDamagePerTick = 2;
        public const float DotIntervalSeconds = 1.0f;
        public const float DotDurationSeconds = 5.0f;

        public const int DirectTrueDamageAmount = 15;
        public const int StunItemTrueDamageAmount = 15;
        public const float StunDurationSeconds = 2.0f;

        private GridManager gridManager;
        private WaveManager waveManager;
        private OperatorManager operatorManager;
        private StageData stageData;
        private Sprite[] itemSprites;

        private float dropTimer;
        private bool isInitialized;

        public Sprite[] ItemSprites => itemSprites;
        public float DropTimer => dropTimer;
        public bool IsInitialized => isInitialized;

        public void Initialize(
            GridManager grid,
            WaveManager wave,
            OperatorManager operators,
            StageData stage,
            Sprite[] customSprites = null)
        {
            gridManager = grid != null ? grid : FindFirstObjectByType<GridManager>();
            waveManager = wave != null ? wave : FindFirstObjectByType<WaveManager>();
            operatorManager = operators != null ? operators : FindFirstObjectByType<OperatorManager>();
            stageData = stage;

            if (customSprites != null && customSprites.Length > 0)
            {
                itemSprites = customSprites;
            }
            else if (stageData != null && stageData.fallingItemSprites != null && stageData.fallingItemSprites.Length > 0)
            {
                itemSprites = stageData.fallingItemSprites;
            }
            else
            {
                LoadItemSprites();
            }

            dropTimer = 0f;
            isInitialized = true;
        }

        private void LoadItemSprites()
        {
#if UNITY_EDITOR
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/Level 2 thrown-items.png");
            if (assets != null && assets.Length > 0)
            {
                var list = new List<Sprite>();
                foreach (var obj in assets)
                {
                    if (obj is Sprite s) list.Add(s);
                }
                if (list.Count > 0)
                {
                    list.Sort((a, b) => ExtractTrailingIndex(a.name).CompareTo(ExtractTrailingIndex(b.name)));
                    itemSprites = list.ToArray();
                    return;
                }
            }
#endif
        }

        public static int ExtractTrailingIndex(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            int lastUnderscore = name.LastIndexOf('_');
            if (lastUnderscore >= 0 && int.TryParse(name.Substring(lastUnderscore + 1), out int index))
            {
                return index;
            }
            return 0;
        }

        public static Stage2ItemSet GetSetForIndex(int spriteIndex)
        {
            if (spriteIndex <= 7) return Stage2ItemSet.WeakDotTrueDamage;
            if (spriteIndex <= 15) return Stage2ItemSet.DirectTrueDamage;
            return Stage2ItemSet.DirectTrueDamageAndStun;
        }

        private void Update()
        {
            if (!isInitialized) return;

            GameManager gameManager = GameManager.Instance;
            bool isCombatPaused = gameManager != null && gameManager.IsPaused;
            bool isWaveActive = gameManager == null || gameManager.CurrentPhase == StagePhase.WaveActive;

            if (isCombatPaused || !isWaveActive) return;

            dropTimer += Time.deltaTime;
            if (dropTimer >= DropIntervalSeconds)
            {
                dropTimer -= DropIntervalSeconds;
                ExecuteDropRoll();
            }
        }

        public static (float allyChance, float enemyChance, float randomTileChance) GetDropProbabilities(int deployedOperatorCount)
        {
            // If there are only 1-3 operators deployed in the stage during wave phase: 60% random tile, 20% ally, 20% enemies
            if (deployedOperatorCount >= 1 && deployedOperatorCount <= 3)
            {
                return (0.20f, 0.20f, 0.60f);
            }

            if (deployedOperatorCount == 0)
            {
                return (0.00f, 0.20f, 0.80f);
            }

            // Default rate (4 or more operators deployed): 40% ally, 20% enemies, 40% random tile
            return (0.40f, 0.20f, 0.40f);
        }

        public void ExecuteDropRoll()
        {
            float roll = UnityEngine.Random.value;
            int spriteIndex = UnityEngine.Random.Range(0, itemSprites != null && itemSprites.Length > 0 ? itemSprites.Length : 24);

            int deployedCount = GetDeployedOperatorCount();
            var (allyChance, enemyChance, _) = GetDropProbabilities(deployedCount);

            if (roll < allyChance)
            {
                // Ally hit
                OperatorBase targetOp = GetRandomDeployedOperator();
                if (targetOp != null && targetOp.DeployedCell != null)
                {
                    SpawnFallingItem(targetOp.DeployedCell.WorldPosition, spriteIndex, targetOp, null);
                    return;
                }
            }
            else if (roll < allyChance + enemyChance)
            {
                // Enemy hit
                EnemyBase targetEnemy = GetRandomAliveEnemy();
                if (targetEnemy != null)
                {
                    SpawnFallingItem(targetEnemy.VisualPosition, spriteIndex, null, targetEnemy);
                    return;
                }
            }

            // Random deployable tile (or fallback)
            GridCell randomCell = GetRandomDeployableTile();
            if (randomCell != null)
            {
                SpawnFallingItem(randomCell.WorldPosition, spriteIndex, null, null);
            }
        }

        public void TriggerDrop(Vector3 targetWorldPosition, int spriteIndex, OperatorBase forcedOp = null, EnemyBase forcedEnemy = null)
        {
            SpawnFallingItem(targetWorldPosition, spriteIndex, forcedOp, forcedEnemy);
        }

        private void SpawnFallingItem(Vector3 targetPosition, int spriteIndex, OperatorBase targetedOperator, EnemyBase targetedEnemy)
        {
            Sprite spriteToUse = null;
            if (itemSprites != null && itemSprites.Length > 0)
            {
                int clampedIndex = Mathf.Clamp(spriteIndex, 0, itemSprites.Length - 1);
                spriteToUse = itemSprites[clampedIndex];
            }

            Stage2ItemSet itemSet = GetSetForIndex(spriteIndex);
            float cellSize = gridManager != null ? gridManager.CellSize : 1.0f;
            Vector3 startPosition = targetPosition + Vector3.up * (cellSize * FallHeightInTiles);

            GameObject itemObject = new GameObject($"Stage2FallingItem_{spriteIndex}");
            itemObject.transform.SetParent(transform, false);
            itemObject.transform.position = startPosition;

            SpriteRenderer sr = itemObject.AddComponent<SpriteRenderer>();
            sr.sprite = spriteToUse;
            sr.sortingOrder = 45; // Render cleanly above grid art/tiles, below unit overlays

            AudioManager.Instance?.PlaySfx(SfxId.Stage2ItemFall, 0.8f);
            StartCoroutine(FallingItemRoutine(itemObject, startPosition, targetPosition, itemSet, targetedOperator, targetedEnemy));
        }

        private IEnumerator FallingItemRoutine(
            GameObject itemObject,
            Vector3 startPos,
            Vector3 endPos,
            Stage2ItemSet set,
            OperatorBase targetedOperator,
            EnemyBase targetedEnemy)
        {
            float elapsed = 0f;
            while (elapsed < FallDurationSeconds)
            {
                if (itemObject == null) yield break;

                elapsed += Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / FallDurationSeconds);
                // Quadratic curve for gravity acceleration
                float t = progress * progress;

                itemObject.transform.position = Vector3.Lerp(startPos, endPos, t);
                itemObject.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, 180f, progress));

                yield return null;
            }

            if (itemObject == null) yield break;

            itemObject.transform.position = endPos;
            itemObject.transform.rotation = Quaternion.identity;

            // Impact!
            OnItemImpact(endPos, set, targetedOperator, targetedEnemy);

            // Item rests on ground for 0.4s, then fades out over 0.3s
            yield return new WaitForSeconds(0.4f);

            SpriteRenderer sr = itemObject != null ? itemObject.GetComponent<SpriteRenderer>() : null;
            if (sr != null)
            {
                float fadeElapsed = 0f;
                const float fadeDuration = 0.3f;
                Color startColor = sr.color;
                while (fadeElapsed < fadeDuration)
                {
                    if (itemObject == null) yield break;
                    fadeElapsed += Time.deltaTime;
                    float alpha = Mathf.Lerp(startColor.a, 0f, fadeElapsed / fadeDuration);
                    sr.color = new Color(startColor.r, startColor.g, startColor.b, alpha);
                    yield return null;
                }
            }

            if (itemObject != null)
            {
                Destroy(itemObject);
            }
        }

        public void OnItemImpact(Vector3 impactWorldPos, Stage2ItemSet set, OperatorBase targetedOperator, EnemyBase targetedEnemy)
        {
            // Impact sound per item type (glass / metal / tools); the generic melee hit until those files exist
            SfxId landSfx = set == Stage2ItemSet.WeakDotTrueDamage ? SfxId.Stage2ItemLandGlass
                : set == Stage2ItemSet.DirectTrueDamage ? SfxId.Stage2ItemLandMetal
                : SfxId.Stage2ItemLandTools;
            AudioManager.Instance?.PlaySfxOrFallback(landSfx, SfxId.GameplayMeleeAttack);

            // Impact visual ring/puff
            MeleeSwipeVisual.Play(impactWorldPos, impactWorldPos + Vector3.up * 0.1f, new Color(1f, 0.75f, 0.2f, 0.8f));

            // Determine affected units
            List<OperatorBase> operatorsToHit = new List<OperatorBase>();
            List<EnemyBase> enemiesToHit = new List<EnemyBase>();

            if (targetedOperator != null && targetedOperator.IsDeployed && targetedOperator.CurrentHP > 0)
            {
                operatorsToHit.Add(targetedOperator);
            }

            if (targetedEnemy != null && !targetedEnemy.IsDead && targetedEnemy.CurrentHP > 0)
            {
                enemiesToHit.Add(targetedEnemy);
            }

            // Also check for units located at the impact tile if no specific target was forced
            if (operatorsToHit.Count == 0 && enemiesToHit.Count == 0 && gridManager != null)
            {
                Vector2Int gridPos = gridManager.WorldToGridPosition(impactWorldPos);
                GridCell cell = gridManager.GetCell(gridPos);
                if (cell != null)
                {
                    if (cell.OccupantOperator != null)
                    {
                        OperatorBase op = cell.OccupantOperator.GetComponent<OperatorBase>();
                        if (op != null && op.IsDeployed && op.CurrentHP > 0)
                        {
                            operatorsToHit.Add(op);
                        }
                    }

                    if (EnemyManager.Instance != null)
                    {
                        var enemiesInCell = EnemyManager.Instance.GetEnemiesInCells(new[] { cell });
                        if (enemiesInCell != null)
                        {
                            for (int i = 0; i < enemiesInCell.Count; i++)
                            {
                                if (enemiesInCell[i] != null && !enemiesInCell[i].IsDead && enemiesInCell[i].CurrentHP > 0)
                                {
                                    enemiesToHit.Add(enemiesInCell[i]);
                                }
                            }
                        }
                    }
                }
            }

            // Apply effect to operators
            for (int i = 0; i < operatorsToHit.Count; i++)
            {
                ApplyEffectToOperator(operatorsToHit[i], set);
            }

            // Apply effect to enemies
            for (int i = 0; i < enemiesToHit.Count; i++)
            {
                ApplyEffectToEnemy(enemiesToHit[i], set);
            }
        }

        public void ApplyEffectToOperator(OperatorBase op, Stage2ItemSet set)
        {
            if (op == null || !op.IsDeployed || op.CurrentHP <= 0) return;

            switch (set)
            {
                case Stage2ItemSet.WeakDotTrueDamage:
                    // Set 0..7: weak but DoT true damage
                    // Check if Stag-ger dodges the incoming item
                    if (op is StaggerOperator staggerDot && staggerDot.TryRollDodge())
                    {
                        // Dodged completely!
                        return;
                    }

                    // Attach DoT effect
                    var dot = op.gameObject.GetComponent<Stage2ItemDotEffect>();
                    if (dot == null)
                    {
                        dot = op.gameObject.AddComponent<Stage2ItemDotEffect>();
                    }
                    dot.Initialize(op, DotDamagePerTick, DotDurationSeconds, DotIntervalSeconds, applyImmediateTick: true);
                    break;

                case Stage2ItemSet.DirectTrueDamage:
                    // Set 8..15: direct true damage
                    // Check if Stag-ger dodges the incoming item
                    if (op is StaggerOperator staggerDirect && staggerDirect.TryRollDodge())
                    {
                        return;
                    }

                    op.TakeTrueDamage(DirectTrueDamageAmount, ignoreShieldNegation: false, canDodge: false);
                    break;

                case Stage2ItemSet.DirectTrueDamageAndStun:
                    // Set 16..23: direct true damage and gives stun to the operator
                    // If Stag-ger dodges, he avoids both the damage and the stun!
                    if (op is StaggerOperator staggerStun && staggerStun.TryRollDodge())
                    {
                        return;
                    }

                    op.TakeTrueDamage(StunItemTrueDamageAmount, ignoreShieldNegation: false, canDodge: false);
                    op.ApplyStun(StunDurationSeconds);
                    break;
            }
        }

        public void ApplyEffectToEnemy(EnemyBase enemy, Stage2ItemSet set)
        {
            if (enemy == null || enemy.IsDead || enemy.CurrentHP <= 0) return;

            switch (set)
            {
                case Stage2ItemSet.WeakDotTrueDamage:
                    var dot = enemy.gameObject.GetComponent<Stage2ItemDotEffect>();
                    if (dot == null)
                    {
                        dot = enemy.gameObject.AddComponent<Stage2ItemDotEffect>();
                    }
                    dot.Initialize(enemy, DotDamagePerTick, DotDurationSeconds, DotIntervalSeconds);
                    break;

                case Stage2ItemSet.DirectTrueDamage:
                    enemy.TryTakeAttackDamage(DirectTrueDamageAmount, DamageType.True);
                    break;

                case Stage2ItemSet.DirectTrueDamageAndStun:
                    enemy.TryTakeAttackDamage(StunItemTrueDamageAmount, DamageType.True);
                    enemy.ApplyStun(StunDurationSeconds);
                    break;
            }
        }

        public int GetDeployedOperatorCount()
        {
            if (operatorManager == null) return 0;
            var deployed = operatorManager.DeployedOperators;
            if (deployed == null || deployed.Count == 0) return 0;

            int count = 0;
            for (int i = 0; i < deployed.Count; i++)
            {
                var op = deployed[i];
                if (op != null && op.IsDeployed && op.CurrentHP > 0 && op.DeployedCell != null)
                {
                    count++;
                }
            }
            return count;
        }

        public OperatorBase GetRandomDeployedOperator()
        {
            if (operatorManager == null) return null;
            var deployed = operatorManager.DeployedOperators;
            if (deployed == null || deployed.Count == 0) return null;

            List<OperatorBase> validOps = new List<OperatorBase>();
            for (int i = 0; i < deployed.Count; i++)
            {
                var op = deployed[i];
                if (op != null && op.IsDeployed && op.CurrentHP > 0 && op.DeployedCell != null)
                {
                    validOps.Add(op);
                }
            }

            if (validOps.Count == 0) return null;
            return validOps[UnityEngine.Random.Range(0, validOps.Count)];
        }

        public EnemyBase GetRandomAliveEnemy()
        {
            if (EnemyManager.Instance == null) return null;
            var active = EnemyManager.Instance.ActiveEnemies;
            if (active == null || active.Count == 0) return null;

            List<EnemyBase> validEnemies = new List<EnemyBase>();
            for (int i = 0; i < active.Count; i++)
            {
                var enemy = active[i];
                if (enemy != null && !enemy.IsDead && enemy.CurrentHP > 0)
                {
                    validEnemies.Add(enemy);
                }
            }

            if (validEnemies.Count == 0) return null;
            return validEnemies[UnityEngine.Random.Range(0, validEnemies.Count)];
        }

        public GridCell GetRandomDeployableTile()
        {
            if (gridManager == null) return null;
            List<GridCell> deployableCells = new List<GridCell>();

            for (int x = 0; x < gridManager.Width; x++)
            {
                for (int y = 0; y < gridManager.Height; y++)
                {
                    GridCell cell = gridManager.GetCell(x, y);
                    if (cell != null && (cell.CanDeploy(OperatorPosition.Melee) || cell.CanDeploy(OperatorPosition.Ranged) ||
                                         cell.TileType == TileType.LowGround || cell.TileType == TileType.HighGround ||
                                         cell.TileType == TileType.EnemyPath || cell.TileType == TileType.Trap))
                    {
                        deployableCells.Add(cell);
                    }
                }
            }

            if (deployableCells.Count == 0) return null;
            return deployableCells[UnityEngine.Random.Range(0, deployableCells.Count)];
        }
    }

    /// <summary>
    /// Attached to an operator or enemy to deal weak DoT true damage every second.
    /// </summary>
    public class Stage2ItemDotEffect : MonoBehaviour
    {
        private OperatorBase targetOperator;
        private EnemyBase targetEnemy;
        private int damagePerTick = 2;
        private float tickInterval = 1.0f;
        private float remainingDuration = 5.0f;
        private float timer;

        public float RemainingDuration => remainingDuration;
        public int DamagePerTick => damagePerTick;

        public void Initialize(OperatorBase op, int dmg, float duration, float interval, bool applyImmediateTick = false)
        {
            targetOperator = op;
            targetEnemy = null;
            damagePerTick = dmg;
            remainingDuration = duration;
            tickInterval = interval;
            timer = 0f;

            if (applyImmediateTick)
            {
                ApplyTick();
            }
        }

        public void Initialize(EnemyBase enemy, int dmg, float duration, float interval, bool applyImmediateTick = false)
        {
            targetEnemy = enemy;
            targetOperator = null;
            damagePerTick = dmg;
            remainingDuration = duration;
            tickInterval = interval;
            timer = 0f;

            if (applyImmediateTick)
            {
                ApplyTick();
            }
        }

        public void UpdateManual(float dt)
        {
            UpdateLogic(dt);
        }

        private void Update()
        {
            UpdateLogic(Time.deltaTime);
        }

        private void UpdateLogic(float dt)
        {
            if (targetOperator != null && (!targetOperator.IsDeployed || targetOperator.CurrentHP <= 0))
            {
                Destroy(this);
                return;
            }

            if (targetEnemy != null && (targetEnemy.IsDead || targetEnemy.CurrentHP <= 0))
            {
                Destroy(this);
                return;
            }

            remainingDuration -= dt;
            timer += dt;

            if (timer >= tickInterval)
            {
                timer -= tickInterval;
                ApplyTick();
            }

            if (remainingDuration <= 0f)
            {
                Destroy(this);
            }
        }

        private void ApplyTick()
        {
            if (targetOperator != null && targetOperator.IsDeployed && targetOperator.CurrentHP > 0)
            {
                targetOperator.TakeTrueDamage(damagePerTick, ignoreShieldNegation: false, canDodge: false);
            }
            else if (targetEnemy != null && !targetEnemy.IsDead && targetEnemy.CurrentHP > 0)
            {
                targetEnemy.TryTakeAttackDamage(damagePerTick, DamageType.True);
            }
        }
    }
}
