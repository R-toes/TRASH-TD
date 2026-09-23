using UnityEngine;
using System;

namespace TrashTD.Data
{
    /// <summary>
    /// Defines a single wave of enemies within a stage (GDD 1.5 wave format).
    /// </summary>
    [Serializable]
    public class WaveEntry
    {
        [Tooltip("Reference to the enemy data for this entry")]
        public EnemyData enemyData;

        [Tooltip("Number of enemies of this type to spawn")]
        public int count = 1;

        [Tooltip("Seconds between each spawn of this enemy type")]
        public float spawnInterval = 1.0f;

        [Tooltip("Delay before this entry starts spawning (relative to wave start)")]
        public float startDelay = 0f;

        [Tooltip("Which spawn point index to use (maps to StageData.spawnPoints)")]
        public int spawnPointIndex = 0;
    }

    /// <summary>
    /// A complete wave definition containing one or more enemy entries.
    /// </summary>
    [Serializable]
    public class WaveData
    {
        [Tooltip("Display name for this wave (e.g., 'Wave 1', 'Final Wave')")]
        public string waveName = "Wave";

        [Tooltip("Delay in seconds before this wave starts (after previous wave clears or timer)")]
        public float preWaveDelay = 5.0f;

        [Tooltip("All enemy entries in this wave")]
        public WaveEntry[] entries;
    }

    /// <summary>
    /// ScriptableObject defining a complete stage (GDD 1.11 template).
    /// Each stage has 3 difficulty variants (Easy/Normal/Hard) with separate life points.
    /// </summary>
    [CreateAssetMenu(fileName = "New Stage", menuName = "TRASH TD/Stage Data")]
    public class StageData : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Unique stage identifier")]
        public string stageId;

        [Tooltip("Display name for the map")]
        public string mapName;

        [Tooltip("Short description shown in the stage selector")]
        public string shortDescription;

        [Header("Map Layout")]
        [Tooltip("Width of the grid in tiles")]
        public int gridWidth = 10;

        [Tooltip("Height of the grid in tiles")]
        public int gridHeight = 8;

        [Tooltip("Grid layout — flattened row-major array of TileType. Length must equal gridWidth * gridHeight.")]
        public TileType[] tileLayout;

        [Tooltip("Spawn point positions on the grid (enemy entry points)")]
        public Vector2Int[] spawnPoints;

        [Tooltip("Exit point positions on the grid (objective — enemies leak here)")]
        public Vector2Int[] exitPoints;

        [Header("Path Data")]
        [Tooltip("Waypoint paths for enemies. Each path is an array of grid positions from spawn to exit.")]
        public PathData[] enemyPaths;

        [Header("Difficulty — Life Points (GDD 1.4.7: Easy=10, Normal=5, Hard=1)")]
        public int lifePointsEasy = 10;
        public int lifePointsNormal = 5;
        public int lifePointsHard = 1;

        [Header("Squad")]
        [Tooltip("Maximum number of operators that can be deployed simultaneously")]
        public int squadSizeLimit = 8;

        [Header("Waves")]
        [Tooltip("Wave list for Easy difficulty")]
        public WaveData[] wavesEasy;

        [Tooltip("Wave list for Normal difficulty")]
        public WaveData[] wavesNormal;

        [Tooltip("Wave list for Hard difficulty")]
        public WaveData[] wavesHard;

        [Header("Optional")]
        [Tooltip("Stage time limit in seconds (0 = no limit)")]
        public float timeLimit = 0f;

        [Tooltip("3-star rating conditions description")]
        public string threeStarCondition = "Clear with no life points lost";

        // TODO: narrative — awaiting design input (setting, factions, lore)
        // TODO: art & audio direction — awaiting design input (using placeholder visuals)
        [Tooltip("Tilemap prefab to instantiate for this stage's visuals")]
        public GameObject mapPrefab;

        /// <summary>
        /// Get life points for a specific difficulty.
        /// </summary>
        public int GetLifePoints(StageDifficulty difficulty)
        {
            return difficulty switch
            {
                StageDifficulty.Easy => lifePointsEasy,
                StageDifficulty.Normal => lifePointsNormal,
                StageDifficulty.Hard => lifePointsHard,
                _ => lifePointsNormal
            };
        }

        /// <summary>
        /// Get the wave list for a specific difficulty.
        /// </summary>
        public WaveData[] GetWaves(StageDifficulty difficulty)
        {
            return difficulty switch
            {
                StageDifficulty.Easy => wavesEasy,
                StageDifficulty.Normal => wavesNormal,
                StageDifficulty.Hard => wavesHard,
                _ => wavesNormal
            };
        }

        /// <summary>
        /// Get the TileType at a specific grid position.
        /// </summary>
        public TileType GetTile(int x, int y)
        {
            if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight)
                return TileType.Blocked;

            int index = y * gridWidth + x;
            if (tileLayout == null || index >= tileLayout.Length)
                return TileType.Blocked;

            return tileLayout[index];
        }
    }

    /// <summary>
    /// A single enemy path defined as an ordered list of waypoints.
    /// </summary>
    [Serializable]
    public class PathData
    {
        [Tooltip("Ordered list of grid positions forming the path")]
        public Vector2Int[] waypoints;

        [Tooltip("Which spawn point this path starts from")]
        public int spawnPointIndex = 0;

        [Tooltip("Which exit point this path leads to")]
        public int exitPointIndex = 0;
    }
}
