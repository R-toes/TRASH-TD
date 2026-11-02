using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Data;
using TrashTD.Systems;

namespace TrashTD.Enemies
{
    /// <summary>
    /// O.F.O (Occu Flying Object) — Flying tanky enemy (64x64, 2x scale like Sand Hulk & Eradicator).
    /// Characteristics:
    /// - Airborne flyer (unblockable, hover shadow, acid immune).
    /// - Tanky with lower damage arts attacks (pinkish projectile).
    /// - Periodic summoning: pauses movement while deploying minions.
    /// - Spawns 2 Oculites sequentially with a visible delay so they emerge one by one.
    /// - 10% chance to spawn an additional Oculite.
    /// - 10% chance to spawn an Oculoid Carrier.
    /// - 10% chance to spawn an Occuworm.
    /// - Spawns 2 Oculites upon death.
    /// </summary>
    public class OfoEnemy : FlyerEnemy
    {
        public const float DefaultFirstTileWaitDuration = 15.0f;
        public const float DefaultSummonInterval = 6.0f;
        public const float DefaultSubSpawnInterval = 0.45f;
        public const int DefaultBaseSummonCount = 2;
        public const int DefaultDeathSpawnCount = 2;
        public const float BonusOcculiteChance = 0.10f;
        public const float BonusCarrierChance = 0.05f;
        public const float BonusOccuwormChance = 0.05f;

        // Pinkish projectile color matching the occuship sprite palette
        public static readonly Color PinkProjectileColor = new Color(0.95f, 0.20f, 0.55f, 1f);

        [Header("O.F.O Mechanics")]
        [SerializeField] private float firstTileWaitDuration = DefaultFirstTileWaitDuration;
        [SerializeField] private float summonInterval = DefaultSummonInterval;
        [SerializeField] private float subSpawnInterval = DefaultSubSpawnInterval;
        [SerializeField] private int baseSummonCount = DefaultBaseSummonCount;
        [SerializeField] private int deathSpawnCount = DefaultDeathSpawnCount;

        [Header("Minion Data")]
        [SerializeField] private EnemyData oculiteSpawnData;
        [SerializeField] private EnemyData oculoidCarrierData;
        [SerializeField] private EnemyData occuwormData;

        private float firstTileWaitTimer;
        private bool hasTriggeredFirstTileWait;
        private bool isWaitingOnFirstTile;
        private bool hasFinishedFirstTileWait;

        private float summonTimer;
        private bool isSummoning;
        private float subSpawnTimer;
        private readonly Queue<EnemyData> pendingSpawnQueue = new Queue<EnemyData>();
        private bool hasDeathSpawned;

        public override bool IsImmuneToAcidRain => true;
        public override bool IsImmuneToAcidTiles => true;

        public float FirstTileWaitDuration { get => firstTileWaitDuration; set => firstTileWaitDuration = value; }
        public float FirstTileWaitTimer => firstTileWaitTimer;
        public bool IsWaitingOnFirstTile => isWaitingOnFirstTile;
        public bool HasTriggeredFirstTileWait => hasTriggeredFirstTileWait;
        public bool HasFinishedFirstTileWait => hasFinishedFirstTileWait;
        public float SummonInterval { get => summonInterval; set => summonInterval = value; }
        public float SummonTimer => summonTimer;
        public float SubSpawnInterval { get => subSpawnInterval; set => subSpawnInterval = value; }
        public bool IsSummoning => isSummoning;
        public int PendingSpawnCount => pendingSpawnQueue.Count;

        public void TriggerFirstTileWaitManually()
        {
            isWaitingOnFirstTile = true;
            firstTileWaitTimer = firstTileWaitDuration;
            hasTriggeredFirstTileWait = true;
        }

        public void SetOculiteSpawnData(EnemyData data) => oculiteSpawnData = data;
        public void SetOculoidCarrierData(EnemyData data) => oculoidCarrierData = data;
        public void SetOccuwormData(EnemyData data) => occuwormData = data;

        public override void Initialize(EnemyData enemyData, int difficultyLevel)
        {
            base.Initialize(enemyData, difficultyLevel);
            firstTileWaitTimer = 0f;
            hasTriggeredFirstTileWait = false;
            isWaitingOnFirstTile = false;
            hasFinishedFirstTileWait = false;
            summonTimer = 0f;
            isSummoning = false;
            subSpawnTimer = 0f;
            pendingSpawnQueue.Clear();
            hasDeathSpawned = false;
        }

        protected override void Update()
        {
            if (isDead) return;

            float deltaTime = Time.deltaTime;

            if (!IsFrozen && !IsStunned)
            {
                // Upkeep first-tile wait timer (pauses on first tile outside spawn for 15s)
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

                // Upkeep summoning timer & sequential spawns
                if (isSummoning)
                {
                    subSpawnTimer -= deltaTime;
                    if (subSpawnTimer <= 0f)
                    {
                        if (pendingSpawnQueue.Count > 0)
                        {
                            SpawnNextQueuedMinion();
                            subSpawnTimer = subSpawnInterval;
                        }
                        else
                        {
                            // All minions spawned, resume moving!
                            isSummoning = false;
                        }
                    }
                }
                else
                {
                    summonTimer += deltaTime;
                    if (summonTimer >= summonInterval)
                    {
                        summonTimer = 0f;
                        BeginSummonSequence();
                    }
                }
            }

            // Standard flyer update (ranged arts attack, hover shadow)
            base.Update();
        }

        public void StepMovement() => MoveAlongPath();

        protected override void MoveAlongPath()
        {
            // O.F.O stops when spawning occulites, or while waiting 15s on the first tile
            if (isSummoning || isWaitingOnFirstTile)
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

        public void BeginSummonSequence(bool? forceBonusOcculite = null, bool? forceBonusCarrier = null, bool? forceBonusOccuworm = null)
        {
            if (isDead) return;

            pendingSpawnQueue.Clear();

            EnemyData oculite = ResolveOculiteData();
            if (oculite != null)
            {
                for (int i = 0; i < baseSummonCount; i++)
                {
                    pendingSpawnQueue.Enqueue(oculite);
                }

                bool addOcculite = forceBonusOcculite ?? (Random.value < BonusOcculiteChance);
                if (addOcculite)
                {
                    pendingSpawnQueue.Enqueue(oculite);
                }
            }

            bool addCarrier = forceBonusCarrier ?? (Random.value < BonusCarrierChance);
            if (addCarrier)
            {
                EnemyData carrier = ResolveCarrierData();
                if (carrier != null) pendingSpawnQueue.Enqueue(carrier);
            }

            bool addOccuworm = forceBonusOccuworm ?? (Random.value < BonusOccuwormChance);
            if (addOccuworm)
            {
                EnemyData worm = ResolveOccuwormData();
                if (worm != null) pendingSpawnQueue.Enqueue(worm);
            }

            if (pendingSpawnQueue.Count > 0)
            {
                isSummoning = true;
                // Spawn first minion immediately, then space remaining by subSpawnInterval
                SpawnNextQueuedMinion();
                subSpawnTimer = subSpawnInterval;
            }
        }

        private void SpawnNextQueuedMinion()
        {
            if (pendingSpawnQueue.Count == 0) return;
            EnemyData toSpawn = pendingSpawnQueue.Dequeue();
            if (toSpawn != null)
            {
                SpawnSingleMinion(toSpawn);
            }
        }

        private void SpawnSingleMinion(EnemyData minionData)
        {
            if (minionData == null) return;

            EnemyManager em = EnemyManager.Instance ?? FindAnyObjectByType<EnemyManager>();
            if (em == null) return;

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

            // Slight scatter on the ground path so units don't overlap completely
            Vector3 groundOffset = new Vector3(
                Random.Range(-0.15f, 0.15f),
                Random.Range(-0.08f, 0.08f),
                0f
            );

            var childPath = new List<Vector3>(remainingPath);
            childPath[0] = transform.position + groundOffset;

            em.SpawnEnemy(minionData, childPath, difficultyLevel);
        }

        public EnemyData ResolveOculiteData()
        {
            if (oculiteSpawnData != null) return oculiteSpawnData;
            oculiteSpawnData = Resources.Load<EnemyData>("Enemies/Enemy_Grunt_OculiteSpawn")
                            ?? Resources.Load<EnemyData>("Enemy_Grunt_OculiteSpawn");
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

        public EnemyData ResolveCarrierData()
        {
            if (oculoidCarrierData != null) return oculoidCarrierData;
            oculoidCarrierData = Resources.Load<EnemyData>("Enemies/Enemy_Tank_OculoidCarrier")
                              ?? Resources.Load<EnemyData>("Enemy_Tank_OculoidCarrier");
#if UNITY_EDITOR
            if (oculoidCarrierData == null)
            {
                string[] guids = UnityEditor.AssetDatabase.FindAssets("t:EnemyData OculoidCarrier");
                if (guids.Length > 0)
                {
                    string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    oculoidCarrierData = UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyData>(p);
                }
            }
#endif
            return oculoidCarrierData;
        }

        public EnemyData ResolveOccuwormData()
        {
            if (occuwormData != null) return occuwormData;
            occuwormData = Resources.Load<EnemyData>("Enemies/Enemy_Caster_Occulorm")
                        ?? Resources.Load<EnemyData>("Enemy_Caster_Occulorm");
#if UNITY_EDITOR
            if (occuwormData == null)
            {
                string[] guids = UnityEditor.AssetDatabase.FindAssets("t:EnemyData Occulorm");
                if (guids.Length > 0)
                {
                    string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    occuwormData = UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyData>(p);
                }
            }
#endif
            return occuwormData;
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
                EnemyData oculite = ResolveOculiteData();
                if (oculite != null)
                {
                    for (int i = 0; i < deathSpawnCount; i++)
                    {
                        SpawnSingleMinion(oculite);
                    }
                }
            }
            base.Die();
        }
    }
}
