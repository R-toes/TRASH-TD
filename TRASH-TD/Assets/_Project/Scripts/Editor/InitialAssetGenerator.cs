using UnityEditor;
using UnityEngine;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;
using TrashTD.Core.GameLoop;

namespace TrashTD.Editor
{
    /// <summary>
    /// Editor utility to generate sample ScriptableObject data assets and prefabs for TRASH TD (GDD 1.3, 1.5, 1.11).
    /// Run from Unity menu: TRASH TD -> Generate Sample Data Assets
    /// </summary>
    public static class InitialAssetGenerator
    {
        private const string ArtFolder = "Assets/_Project/Art";
        private const string OperatorDataFolder = "Assets/_Project/Data/Operators";
        private const string EnemyDataFolder = "Assets/_Project/Data/Enemies";
        private const string StageDataFolder = "Assets/_Project/Data/Stages";
        private const string OperatorPrefabFolder = "Assets/_Project/Prefabs/Operators";
        private const string EnemyPrefabFolder = "Assets/_Project/Prefabs/Enemies";

        [MenuItem("TRASH TD/Generate Sample Data Assets")]
        public static void GenerateAllAssets()
        {
            EnsureFolder(OperatorDataFolder);
            EnsureFolder(EnemyDataFolder);
            EnsureFolder(StageDataFolder);
            EnsureFolder(OperatorPrefabFolder);
            EnsureFolder(EnemyPrefabFolder);

            AssetDatabase.Refresh();

            GenerateOperators();
            GenerateEnemies();
            GenerateStage();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("<color=green>TRASH TD: All sample Operator, Enemy, Stage assets, and Prefabs successfully generated with placeholder sprites!</color>");
        }

        [MenuItem("TRASH TD/Setup Test Gameplay Scene")]
        public static void SetupTestScene()
        {
            GenerateAllAssets();

            string scenePath = "Assets/_Project/Scenes/GameplayTest.unity";
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            var bootstrapperObj = new GameObject("StageBootstrapper");
            var bootstrapper = bootstrapperObj.AddComponent<StageBootstrapper>();

            // Assign Stage Data
            bootstrapper.stageData = AssetDatabase.LoadAssetAtPath<StageData>($"{StageDataFolder}/Stage_01_LandfillOutskirts.asset");
            bootstrapper.difficulty = StageDifficulty.Normal;

            // Assign Operators Pool
            bootstrapper.operatorPool = new System.Collections.Generic.List<OperatorData>
            {
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Guard_Scrapper.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Defender_Bulkhead.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Sniper_Deadeye.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Caster_Pyrolite.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Medic_NurseBot.asset")
            };

            // Assign Tile Sprites
            bootstrapper.lowGroundSprite = LoadSprite("tex_tile_lowground.png");
            bootstrapper.highGroundSprite = LoadSprite("tex_tile_highground.png");
            bootstrapper.blockedSprite = LoadSprite("tex_tile_blocked.png");
            bootstrapper.enemyPathSprite = LoadSprite("tex_tile_enemypath.png");
            bootstrapper.spawnPointSprite = LoadSprite("tex_tile_spawn.png");
            bootstrapper.exitPointSprite = LoadSprite("tex_tile_exit.png");

            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log($"<color=green>TRASH TD: Test Gameplay Scene successfully created and saved at: {scenePath}</color>");
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string[] parts = path.Split('/');
                string current = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                    {
                        AssetDatabase.CreateFolder(current, parts[i]);
                    }
                    current = next;
                }
            }
        }

        private static Sprite LoadSprite(string filename)
        {
            string path = $"{ArtFolder}/{filename}";
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static GameObject CreateOrGetOperatorPrefab(string prefabName, Sprite sprite, OperatorClass opClass)
        {
            string path = $"{OperatorPrefabFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var go = new GameObject(prefabName);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 5;

            // Add corresponding class component
            switch (opClass)
            {
                case OperatorClass.Guard: go.AddComponent<GuardOperator>(); break;
                case OperatorClass.Defender: go.AddComponent<DefenderOperator>(); break;
                case OperatorClass.Sniper: go.AddComponent<SniperOperator>(); break;
                case OperatorClass.Caster: go.AddComponent<CasterOperator>(); break;
                case OperatorClass.Medic: go.AddComponent<MedicOperator>(); break;
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateOrGetEnemyPrefab(string prefabName, Sprite sprite, EnemyArchetype archetype)
        {
            string path = $"{EnemyPrefabFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var go = new GameObject(prefabName);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 4;

            // Add corresponding archetype component
            switch (archetype)
            {
                case EnemyArchetype.Grunt: go.AddComponent<GruntEnemy>(); break;
                case EnemyArchetype.Rusher: go.AddComponent<RusherEnemy>(); break;
                case EnemyArchetype.Tank: go.AddComponent<TankEnemy>(); break;
                case EnemyArchetype.Caster: go.AddComponent<EnemyCaster>(); break;
                case EnemyArchetype.Flyer: go.AddComponent<FlyerEnemy>(); break;
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static void GenerateOperators()
        {
            // 1. Guard (Scrapper)
            var guardSprite = LoadSprite("tex_op_guard.png");
            var guardPrefab = CreateOrGetOperatorPrefab("Prefab_OP_Guard", guardSprite, OperatorClass.Guard);
            CreateOperator("OP_Guard_Scrapper", "Scrapper", OperatorClass.Guard, OperatorPosition.Melee, OperatorRarity.Star1,
                hp: 140, atk: 65, def: 20, res: 0, blockCount: 2, range: 1, interval: 1.1f, dp: 10,
                new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) },
                guardSprite, guardPrefab);

            // 2. Defender (Bulkhead)
            var defenderSprite = LoadSprite("tex_op_defender.png");
            var defenderPrefab = CreateOrGetOperatorPrefab("Prefab_OP_Defender", defenderSprite, OperatorClass.Defender);
            CreateOperator("OP_Defender_Bulkhead", "Bulkhead", OperatorClass.Defender, OperatorPosition.Melee, OperatorRarity.Star1,
                hp: 280, atk: 35, def: 45, res: 10, blockCount: 3, range: 1, interval: 1.4f, dp: 14,
                new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) },
                defenderSprite, defenderPrefab);

            // 3. Sniper (Deadeye)
            var sniperSprite = LoadSprite("tex_op_sniper.png");
            var sniperPrefab = CreateOrGetOperatorPrefab("Prefab_OP_Sniper", sniperSprite, OperatorClass.Sniper);
            CreateOperator("OP_Sniper_Deadeye", "Deadeye", OperatorClass.Sniper, OperatorPosition.Ranged, OperatorRarity.Star1,
                hp: 85, atk: 90, def: 8, res: 0, blockCount: 0, range: 3, interval: 1.0f, dp: 11,
                new[]
                {
                    new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(3, 0),
                    new Vector2Int(1, 1), new Vector2Int(2, 1),
                    new Vector2Int(1, -1), new Vector2Int(2, -1)
                },
                sniperSprite, sniperPrefab);

            // 4. Caster (Pyrolite)
            var casterSprite = LoadSprite("tex_op_caster.png");
            var casterPrefab = CreateOrGetOperatorPrefab("Prefab_OP_Caster", casterSprite, OperatorClass.Caster);
            var caster = CreateOperator("OP_Caster_Pyrolite", "Pyrolite", OperatorClass.Caster, OperatorPosition.Ranged, OperatorRarity.Star2,
                hp: 95, atk: 80, def: 10, res: 20, blockCount: 0, range: 2, interval: 1.6f, dp: 15,
                new[]
                {
                    new Vector2Int(1, 0), new Vector2Int(2, 0),
                    new Vector2Int(0, 1), new Vector2Int(1, 1),
                    new Vector2Int(0, -1), new Vector2Int(1, -1)
                },
                casterSprite, casterPrefab);
            caster.damageType = DamageType.Arts;
            EditorUtility.SetDirty(caster);

            // 5. Medic (NurseBot)
            var medicSprite = LoadSprite("tex_op_medic.png");
            var medicPrefab = CreateOrGetOperatorPrefab("Prefab_OP_Medic", medicSprite, OperatorClass.Medic);
            CreateOperator("OP_Medic_NurseBot", "NurseBot", OperatorClass.Medic, OperatorPosition.Ranged, OperatorRarity.Star1,
                hp: 90, atk: 55, def: 12, res: 15, blockCount: 0, range: 2, interval: 1.8f, dp: 12,
                new[]
                {
                    new Vector2Int(0, 0),
                    new Vector2Int(1, 0), new Vector2Int(-1, 0),
                    new Vector2Int(0, 1), new Vector2Int(0, -1),
                    new Vector2Int(1, 1), new Vector2Int(-1, -1)
                },
                medicSprite, medicPrefab);
        }

        private static OperatorData CreateOperator(string assetName, string displayName, OperatorClass opClass,
            OperatorPosition pos, OperatorRarity rarity, int hp, int atk, int def, int res, int blockCount,
            int range, float interval, int dp, Vector2Int[] rangePattern, Sprite portrait, GameObject prefab)
        {
            string path = $"{OperatorDataFolder}/{assetName}.asset";
            var data = AssetDatabase.LoadAssetAtPath<OperatorData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<OperatorData>();
                AssetDatabase.CreateAsset(data, path);
            }

            data.operatorName = displayName;
            data.operatorClass = opClass;
            data.position = pos;
            data.baseRarity = rarity;
            data.baseHP = hp;
            data.baseATK = atk;
            data.baseDEF = def;
            data.baseRES = res;
            data.blockCount = blockCount;
            data.attackRange = range;
            data.attackInterval = interval;
            data.dpCost = dp;
            data.rangePattern = rangePattern;
            data.portrait = portrait;
            data.operatorPrefab = prefab;

            EditorUtility.SetDirty(data);
            return data;
        }

        private static void GenerateEnemies()
        {
            var gruntSprite = LoadSprite("tex_enemy_grunt.png");
            var gruntPrefab = CreateOrGetEnemyPrefab("Prefab_Enemy_Grunt", gruntSprite, EnemyArchetype.Grunt);
            CreateEnemy("Enemy_Grunt_Sludge", "Sludge Grunt", EnemyArchetype.Grunt, EnemyMovementType.Ground,
                hp: 100, atk: 25, def: 10, res: 0, speed: 1.0f, isUnblockable: false,
                gruntSprite, gruntPrefab);

            var rusherSprite = LoadSprite("tex_enemy_rusher.png");
            var rusherPrefab = CreateOrGetEnemyPrefab("Prefab_Enemy_Rusher", rusherSprite, EnemyArchetype.Rusher);
            CreateEnemy("Enemy_Rusher_Toxic", "Toxic Rusher", EnemyArchetype.Rusher, EnemyMovementType.Ground,
                hp: 60, atk: 20, def: 5, res: 0, speed: 1.8f, isUnblockable: false,
                rusherSprite, rusherPrefab);

            var tankSprite = LoadSprite("tex_enemy_tank.png");
            var tankPrefab = CreateOrGetEnemyPrefab("Prefab_Enemy_Tank", tankSprite, EnemyArchetype.Tank);
            CreateEnemy("Enemy_Tank_Pollution", "Pollution Tank", EnemyArchetype.Tank, EnemyMovementType.Ground,
                hp: 350, atk: 45, def: 35, res: 5, speed: 0.6f, isUnblockable: false,
                tankSprite, tankPrefab);

            var casterSprite = LoadSprite("tex_enemy_caster.png");
            var casterPrefab = CreateOrGetEnemyPrefab("Prefab_Enemy_Caster", casterSprite, EnemyArchetype.Caster);
            CreateEnemy("Enemy_Caster_Smog", "Smog Caster", EnemyArchetype.Caster, EnemyMovementType.Ground,
                hp: 110, atk: 35, def: 10, res: 15, speed: 0.85f, isUnblockable: true,
                casterSprite, casterPrefab);

            var flyerSprite = LoadSprite("tex_enemy_flyer.png");
            var flyerPrefab = CreateOrGetEnemyPrefab("Prefab_Enemy_Flyer", flyerSprite, EnemyArchetype.Flyer);
            CreateEnemy("Enemy_Flyer_Acid", "Acid Flyer", EnemyArchetype.Flyer, EnemyMovementType.Air,
                hp: 80, atk: 25, def: 5, res: 10, speed: 1.25f, isUnblockable: true,
                flyerSprite, flyerPrefab);
        }

        private static EnemyData CreateEnemy(string assetName, string displayName, EnemyArchetype archetype,
            EnemyMovementType movementType, int hp, int atk, int def, int res, float speed, bool isUnblockable,
            Sprite sprite, GameObject prefab)
        {
            string path = $"{EnemyDataFolder}/{assetName}.asset";
            var data = AssetDatabase.LoadAssetAtPath<EnemyData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<EnemyData>();
                AssetDatabase.CreateAsset(data, path);
            }

            data.enemyName = displayName;
            data.archetype = archetype;
            data.movementType = movementType;
            data.baseHP = hp;
            data.baseATK = atk;
            data.baseDEF = def;
            data.baseRES = res;
            data.moveSpeed = speed;
            data.isUnblockable = isUnblockable;
            data.lifePointCost = 1;
            data.sprite = sprite;
            data.enemyPrefab = prefab;

            EditorUtility.SetDirty(data);
            return data;
        }

        private static void GenerateStage()
        {
            string path = $"{StageDataFolder}/Stage_01_LandfillOutskirts.asset";
            var stage = AssetDatabase.LoadAssetAtPath<StageData>(path);
            if (stage == null)
            {
                stage = ScriptableObject.CreateInstance<StageData>();
                AssetDatabase.CreateAsset(stage, path);
            }

            stage.stageId = "STAGE_01";
            stage.mapName = "Landfill Outskirts";
            stage.gridWidth = 8;
            stage.gridHeight = 6;
            stage.squadSizeLimit = 8;

            // Life Points per GDD 1.4.7: Easy=10, Normal=5, Hard=1
            stage.lifePointsEasy = 10;
            stage.lifePointsNormal = 5;
            stage.lifePointsHard = 1;

            // Spawn at (0, 3), Exit at (7, 3)
            stage.spawnPoints = new[] { new Vector2Int(0, 3) };
            stage.exitPoints = new[] { new Vector2Int(7, 3) };

            // 8x6 layout with a low-ground middle lane used by ground enemies,
            // surrounded by low-ground and high-ground deployment tiles.
            stage.tileLayout = new TileType[8 * 6];
            for (int y = 0; y < 6; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    int index = y * 8 + x;
                    if (x == 0 && y == 3) stage.tileLayout[index] = TileType.SpawnPoint;
                    else if (x == 7 && y == 3) stage.tileLayout[index] = TileType.ExitPoint;
                    else if (y == 3) stage.tileLayout[index] = TileType.LowGround;
                    else if (y == 2 || y == 4) stage.tileLayout[index] = TileType.LowGround;
                    else stage.tileLayout[index] = TileType.HighGround;
                }
            }

            var grunt = AssetDatabase.LoadAssetAtPath<EnemyData>($"{EnemyDataFolder}/Enemy_Grunt_Sludge.asset");
            var rusher = AssetDatabase.LoadAssetAtPath<EnemyData>($"{EnemyDataFolder}/Enemy_Rusher_Toxic.asset");
            var tank = AssetDatabase.LoadAssetAtPath<EnemyData>($"{EnemyDataFolder}/Enemy_Tank_Pollution.asset");

            if (grunt != null)
            {
                stage.wavesEasy = new[]
                {
                    new WaveData
                    {
                        waveName = "Wave 1: Scouts",
                        preWaveDelay = 2.0f,
                        entries = new[]
                        {
                            new WaveEntry { enemyData = grunt, count = 2, spawnInterval = 3.0f, startDelay = 0f, spawnPointIndex = 0 }
                        }
                    }
                };

                stage.wavesNormal = new[]
                {
                    new WaveData
                    {
                        waveName = "Wave 1: Sludge Scouts",
                        preWaveDelay = 2.0f,
                        entries = new[]
                        {
                            new WaveEntry { enemyData = grunt, count = 3, spawnInterval = 2.5f, startDelay = 0f, spawnPointIndex = 0 }
                        }
                    },
                    new WaveData
                    {
                        waveName = "Wave 2: Toxic Surge",
                        preWaveDelay = 4.0f,
                        entries = new[]
                        {
                            new WaveEntry { enemyData = grunt, count = 4, spawnInterval = 2.0f, startDelay = 0f, spawnPointIndex = 0 },
                            new WaveEntry { enemyData = rusher, count = 2, spawnInterval = 1.5f, startDelay = 3f, spawnPointIndex = 0 }
                        }
                    }
                };

                stage.wavesHard = new[]
                {
                    new WaveData
                    {
                        waveName = "Wave 1: Advance Swarm",
                        preWaveDelay = 1.5f,
                        entries = new[]
                        {
                            new WaveEntry { enemyData = grunt, count = 5, spawnInterval = 1.8f, startDelay = 0f, spawnPointIndex = 0 },
                            new WaveEntry { enemyData = rusher, count = 3, spawnInterval = 1.2f, startDelay = 2f, spawnPointIndex = 0 }
                        }
                    },
                    new WaveData
                    {
                        waveName = "Wave 2: Heavy Incursion",
                        preWaveDelay = 3.0f,
                        entries = new[]
                        {
                            new WaveEntry { enemyData = tank, count = 1, spawnInterval = 0f, startDelay = 0f, spawnPointIndex = 0 },
                            new WaveEntry { enemyData = rusher, count = 4, spawnInterval = 1.2f, startDelay = 2f, spawnPointIndex = 0 }
                        }
                    }
                };
            }

            EditorUtility.SetDirty(stage);
        }
    }
}
