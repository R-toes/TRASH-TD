using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Data;
using TrashTD.Operators;
using TrashTD.Systems;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Oculoid Carrier enemy (Tank archetype).
    /// Features:
    /// - High DEF, low RES.
    /// - Immune to Stage 6 acid rain and acid/trap tiles.
    /// - When blocked, reduces the block count of the blocking operator by 1.
    /// - When killed, spawns three Oculite Spawns that continue along the route to the exit.
    /// </summary>
    public class OculoidCarrierEnemy : EnemyBase
    {
        [Header("Carrier Abilities")]
        [SerializeField] private int blockCountReduction = 1;
        [SerializeField] private EnemyData oculiteSpawnData;

        public int BlockCountReduction => blockCountReduction;
        public override bool IsImmuneToAcidRain => true;
        public override bool IsImmuneToAcidTiles => true;

        private OperatorBase debuffedBlocker;
        private SlipperyEffect activeSlipperyEffect;
        private bool hasSpawnedChildren;

        public SlipperyEffect ActiveSlipperyEffect => activeSlipperyEffect;

        public void SetOculiteSpawnData(EnemyData data)
        {
            oculiteSpawnData = data;
        }

        public override void OnBlocked(OperatorBase blocker)
        {
            base.OnBlocked(blocker);

            if (blocker != null && activeSlipperyEffect == null)
            {
                debuffedBlocker = blocker;
                activeSlipperyEffect = SlipperyEffect.Apply(blocker, blockCountReduction);
            }
        }

        public override void OnUnblocked()
        {
            TriggerSlipperyFading();
            base.OnUnblocked();
        }

        public override void ForceKill()
        {
            hasSpawnedChildren = true;
            TriggerSlipperyFading();
            base.ForceKill();
        }

        protected override void Die()
        {
            TriggerSlipperyFading();
            SpawnOculiteChildren();
            base.Die();
        }

        private void OnDisable()
        {
            TriggerSlipperyFading();
        }

        private void TriggerSlipperyFading()
        {
            if (activeSlipperyEffect != null)
            {
                activeSlipperyEffect.StartFading(10f);
                activeSlipperyEffect = null;
            }
            debuffedBlocker = null;
        }

        private void SpawnOculiteChildren()
        {
            if (hasSpawnedChildren) return;
            hasSpawnedChildren = true;

            EnemyData childData = oculiteSpawnData;
            if (childData == null)
            {
                childData = Resources.Load<EnemyData>("Enemies/Enemy_Grunt_OculiteSpawn");
            }
            if (childData == null)
            {
                childData = Resources.Load<EnemyData>("Enemy_Grunt_OculiteSpawn");
            }
#if UNITY_EDITOR
            if (childData == null)
            {
                string[] guids = UnityEditor.AssetDatabase.FindAssets("t:EnemyData OculiteSpawn");
                if (guids.Length > 0)
                {
                    string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                    childData = UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyData>(p);
                }
            }
#endif

            if (childData == null)
            {
                Debug.LogWarning("[OculoidCarrier] No OculiteSpawn EnemyData assigned or found to spawn on death!");
                return;
            }

            // Construct remaining path starting from carrier's current position
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

            // Spawn 3 Oculite Spawns with slight spatial offsets
            Vector3 offsetA = new Vector3(-0.18f, 0.08f, 0f);
            Vector3 offsetB = new Vector3(0.18f, -0.08f, 0f);
            Vector3 offsetC = new Vector3(0f, 0.14f, 0f);

            var pathA = new List<Vector3>(remainingPath);
            pathA[0] = transform.position + offsetA;

            var pathB = new List<Vector3>(remainingPath);
            pathB[0] = transform.position + offsetB;

            var pathC = new List<Vector3>(remainingPath);
            pathC[0] = transform.position + offsetC;

            EnemyManager em = EnemyManager.Instance ?? FindAnyObjectByType<EnemyManager>();
            if (em != null)
            {
                em.SpawnEnemy(childData, pathA, difficultyLevel);
                em.SpawnEnemy(childData, pathB, difficultyLevel);
                em.SpawnEnemy(childData, pathC, difficultyLevel);
            }
        }
    }
}
