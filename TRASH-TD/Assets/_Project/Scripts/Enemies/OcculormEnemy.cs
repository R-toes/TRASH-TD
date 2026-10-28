using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Data;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Occulorm — Enemy Caster archetype.
    /// Characteristics:
    /// - Arts ranged attack (normal caster range 2.5f, red projectile).
    /// - Immune to acid pools and acid rain.
    /// - When leaving the spawn point, pauses on the first tile outside the spawn point for 10 seconds.
    ///   While waiting, continues to attack operators in range and summon minions.
    /// - Summons 2 Oculite Spawns every 5 seconds.
    /// - Upon death, spawns 2 Oculite Spawns.
    /// </summary>
    public class OcculormEnemy : EnemyBase
    {
        public const float DefaultFirstTileWaitDuration = 10.0f;
        public const float DefaultSummonInterval = 5.0f;
        public const int DefaultSummonCount = 2;
        public const int DefaultDeathSpawnCount = 2;
        public static readonly Color RedProjectileColor = new Color(1f, 0.15f, 0.15f, 1f);

        [Header("Occulorm Mechanics")]
        [SerializeField] private float firstTileWaitDuration = DefaultFirstTileWaitDuration;
        [SerializeField] private float summonInterval = DefaultSummonInterval;
        [SerializeField] private int summonCount = DefaultSummonCount;
        [SerializeField] private int deathSpawnCount = DefaultDeathSpawnCount;
        [SerializeField] private EnemyData oculiteSpawnData;

        private float firstTileWaitTimer;
        private bool hasTriggeredFirstTileWait;
        private bool isWaitingOnFirstTile;
        private bool hasFinishedFirstTileWait;

        private float summonTimer;
        private bool hasDeathSpawned;

        public override bool IsImmuneToAcidRain => true;
        public override bool IsImmuneToAcidTiles => true;

        public float FirstTileWaitDuration
        {
            get => firstTileWaitDuration;
            set => firstTileWaitDuration = value;
        }

        public float FirstTileWaitTimer => firstTileWaitTimer;
        public bool IsWaitingOnFirstTile => isWaitingOnFirstTile;
        public bool HasTriggeredFirstTileWait => hasTriggeredFirstTileWait;
        public bool HasFinishedFirstTileWait => hasFinishedFirstTileWait;
        public float SummonInterval => summonInterval;
        public float SummonTimer => summonTimer;
        public int SummonCount => summonCount;
        public int DeathSpawnCount => deathSpawnCount;

        public void SetOculiteSpawnData(EnemyData data)
        {
            oculiteSpawnData = data;
        }

        public override void Initialize(EnemyData enemyData, int difficultyLevel)
        {
            base.Initialize(enemyData, difficultyLevel);
            firstTileWaitTimer = 0f;
            hasTriggeredFirstTileWait = false;
            isWaitingOnFirstTile = false;
            hasFinishedFirstTileWait = false;
            summonTimer = 0f;
            hasDeathSpawned = false;
        }

        public void TriggerFirstTileWaitManually()
        {
            isWaitingOnFirstTile = true;
            firstTileWaitTimer = firstTileWaitDuration;
            hasTriggeredFirstTileWait = true;
        }

        protected override void Update()
        {
            if (isDead) return;

            float deltaTime = Time.deltaTime;

            if (!IsFrozen && !IsStunned)
            {
                // Upkeep first-tile wait timer
                if (isWaitingOnFirstTile)
                {
                    firstTileWaitTimer -= deltaTime;
                    if (firstTileWaitTimer <= 0f)
                    {
                        firstTileWaitTimer = 0f;
                        isWaitingOnFirstTile = false;
                        hasFinishedFirstTileWait = true;
                    }
                }

                // Upkeep periodic summoning
                summonTimer += deltaTime;
                if (summonTimer >= summonInterval)
                {
                    summonTimer = 0f;
                    SpawnOculites(summonCount);
                }
            }

            // Standard ranged caster behavior (attacks operators in range, holds when blocked, moves when free)
            UpdateRangedBehavior(prioritizeRangedOperators: false);
        }

        protected override void MoveAlongPath()
        {
            if (isWaitingOnFirstTile)
            {
                return;
            }

            // Check if we reached the first tile outside the spawn point (path[1], or path[0] if path.Count == 1)
            if (!hasTriggeredFirstTileWait && !hasFinishedFirstTileWait && path != null && path.Count > 0)
            {
                int targetIndex = path.Count > 1 ? 1 : 0;
                Vector3 firstTilePos = path[targetIndex];

                if (currentPathIndex >= targetIndex && Vector3.Distance(transform.position, firstTilePos) <= 0.05f)
                {
                    transform.position = firstTilePos;
                    isWaitingOnFirstTile = true;
                    firstTileWaitTimer = firstTileWaitDuration;
                    hasTriggeredFirstTileWait = true;
                    return;
                }
            }

            base.MoveAlongPath();

            // Re-check after movement step
            if (!hasTriggeredFirstTileWait && !hasFinishedFirstTileWait && path != null && path.Count > 0)
            {
                int targetIndex = path.Count > 1 ? 1 : 0;
                Vector3 firstTilePos = path[targetIndex];

                if (currentPathIndex >= targetIndex && Vector3.Distance(transform.position, firstTilePos) <= 0.05f)
                {
                    transform.position = firstTilePos;
                    isWaitingOnFirstTile = true;
                    firstTileWaitTimer = firstTileWaitDuration;
                    hasTriggeredFirstTileWait = true;
                }
            }
        }

        public void SpawnOculites(int count)
        {
            if (count <= 0) return;

            EnemyData childData = ResolveOculiteData();
            if (childData == null)
            {
                Debug.LogWarning("[Occulorm] No OculiteSpawn EnemyData assigned or found to spawn!");
                return;
            }

            var remainingPath = new List<Vector3>();
            remainingPath.Add(transform.position);

            if (path != null)
            {
                for (int i = currentPathIndex; i < path.Count; i++)
                {
                    remainingPath.Add(path[i]);
                }
            }

            if (remainingPath.Count == 1)
            {
                remainingPath.Add(transform.position + Vector3.right * 0.1f);
            }

            EnemyManager em = EnemyManager.Instance ?? FindAnyObjectByType<EnemyManager>();
            if (em == null) return;

            Vector3[] offsets =
            {
                new Vector3(-0.18f, 0.08f, 0f),
                new Vector3(0.18f, -0.08f, 0f),
                new Vector3(0f, 0.14f, 0f),
                new Vector3(0f, -0.14f, 0f)
            };

            for (int i = 0; i < count; i++)
            {
                Vector3 offset = offsets[i % offsets.Length];
                var childPath = new List<Vector3>(remainingPath);
                childPath[0] = transform.position + offset;
                em.SpawnEnemy(childData, childPath, difficultyLevel);
            }
        }

        private EnemyData ResolveOculiteData()
        {
            if (oculiteSpawnData != null) return oculiteSpawnData;

            oculiteSpawnData = Resources.Load<EnemyData>("Enemies/Enemy_Grunt_OculiteSpawn");
            if (oculiteSpawnData == null)
            {
                oculiteSpawnData = Resources.Load<EnemyData>("Enemy_Grunt_OculiteSpawn");
            }
#if UNITY_EDITOR
            if (oculiteSpawnData == null)
            {
                string[] guids = UnityEditor.AssetDatabase.FindAssets("t:EnemyData OculiteSpawn");
                if (guids.Length > 0)
                {
                    string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    oculiteSpawnData = UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyData>(p);
                }
            }
#endif
            return oculiteSpawnData;
        }

        public override void ForceKill()
        {
            hasDeathSpawned = true;
            base.ForceKill();
        }

        protected override void Die()
        {
            if (!hasDeathSpawned)
            {
                hasDeathSpawned = true;
                SpawnOculites(deathSpawnCount);
            }
            base.Die();
        }
    }
}
