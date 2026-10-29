using System.Collections.Generic;
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
        private static readonly string[] ArtSearchFolders =
        {
            ArtFolder + "/Operators",
            ArtFolder + "/Enemies",
            ArtFolder + "/Tiles",
            ArtFolder + "/Placeholders",
            ArtFolder,
        };
        private const string OperatorDataFolder = "Assets/_Project/Data/Operators";
        private const string EnemyDataFolder = "Assets/_Project/Data/Enemies";
        private const string StageDataFolder = "Assets/Resources/Stages";
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
            GenerateStage2();
            GenerateStage3();
            GenerateStage6();

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
            bootstrapper.stageData = AssetDatabase.LoadAssetAtPath<StageData>($"{StageDataFolder}/STAGE_01.asset");
            bootstrapper.difficulty = StageDifficulty.Normal;

            // Assign Operators Pool
            bootstrapper.operatorPool = new System.Collections.Generic.List<OperatorData>
            {
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Guard_Scrapper.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Guard_QiFu.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Guard_Echosquire.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Specialist_Drawgoo.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Guard_Shadeslice.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Defender_Bulkhead.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Defender_Mossmo.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Sniper_Deadeye.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Sniper_Pompoth.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Sniper_Proxishot.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Sniper_Basurocket.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Caster_Pyrolite.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Caster_Chillpath.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Medic_Coalesce.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Medic_Bubblets.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Medic_Progeny.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Guard_Stagger.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Defender_Thornchin.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Guard_Ignisaur.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Caster_Resicochet.asset"),
                AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Specialist_Framiliar.asset")
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

        /// <summary>
        /// Waves are authored on the stage asset (see Wave Planner). Generated waves are only a first
        /// draft, so existing waves are kept unless the user explicitly confirms overwriting them.
        /// </summary>
        private static bool ShouldGenerateWaves(StageData stage)
        {
            bool hasWaves = (stage.wavesEasy != null && stage.wavesEasy.Length > 0) ||
                            (stage.wavesNormal != null && stage.wavesNormal.Length > 0) ||
                            (stage.wavesHard != null && stage.wavesHard.Length > 0);
            if (!hasWaves) return true;
            if (Application.isBatchMode) return false;

            return !EditorUtility.DisplayDialog(
                "Keep existing waves?",
                $"{stage.name} already has waves. Regenerating replaces ALL of its Easy/Normal/Hard waves " +
                "with generated defaults and discards any hand-tuned changes.\n\n" +
                "The map layout and paths are regenerated either way.",
                "Keep existing waves",
                "Overwrite waves");
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

        // Resolves a bare art filename to its path in the Art folder or one of its category subfolders.
        private static string ResolveArtPath(string filename)
        {
            foreach (string folder in ArtSearchFolders)
            {
                string path = $"{folder}/{filename}";
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets)))
                {
                    return path;
                }
            }

            return $"{ArtFolder}/{filename}";
        }

        private static Sprite LoadSprite(string filename)
        {
            string path = ResolveArtPath(filename);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Sprite LoadSprite(string filename, string spriteName)
        {
            string path = ResolveArtPath(filename);
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite sprite && sprite.name == spriteName)
                {
                    return sprite;
                }
            }

            return null;
        }

        private static Sprite[] LoadSpriteFrames(string filename, string spriteNamePrefix, int frameCount)
        {
            Sprite[] frames = new Sprite[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                frames[i] = LoadSprite(filename, $"{spriteNamePrefix}_{i}");
                if (frames[i] == null)
                    Debug.LogError($"InitialAssetGenerator: Missing animation frame '{spriteNamePrefix}_{i}' in {filename}.");
            }

            return frames;
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
            if (prefabName == "Prefab_OP_Bulkhead") go.AddComponent<BulkheadOperator>();
            else if (prefabName == "Prefab_OP_Deadeye") go.AddComponent<DeadeyeOperator>();
            else if (prefabName == "Prefab_OP_Pompoth") go.AddComponent<PompothOperator>();
            else if (prefabName == "Prefab_OP_Pyrolite") go.AddComponent<PyroliteOperator>();
            else if (prefabName == "Prefab_OP_Chillpath") go.AddComponent<ChillpathOperator>();
            else if (prefabName == "Prefab_OP_Coalesce") go.AddComponent<CoalesceOperator>();
            else if (prefabName == "Prefab_OP_Scrapper") go.AddComponent<ScrapperOperator>();
            else if (prefabName == "Prefab_OP_Proxishot") go.AddComponent<ProxishotOperator>();
            else
            {
                switch (opClass)
                {
                    case OperatorClass.Guard: go.AddComponent<GuardOperator>(); break;
                    case OperatorClass.Defender: go.AddComponent<DefenderOperator>(); break;
                    case OperatorClass.Sniper: go.AddComponent<SniperOperator>(); break;
                    case OperatorClass.Caster: go.AddComponent<CasterOperator>(); break;
                    case OperatorClass.Medic: go.AddComponent<MedicOperator>(); break;
                    case OperatorClass.Specialist: go.AddComponent<SpecialistOperator>(); break;
                }
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

        private static GameObject CreateOrGetRobozombiePrefab(string prefabName, Sprite[] frames)
        {
            string path = $"{EnemyPrefabFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var go = new GameObject(prefabName);
            go.transform.localScale = new Vector3(1.2f, 1.2f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = frames != null && frames.Length > 0 ? frames[0] : null;
            sr.sortingOrder = 4;

            go.AddComponent<RobozombieEnemy>();
            var anim = go.AddComponent<RobozombieSpriteAnimation>();
            if (frames != null && frames.Length > 0)
            {
                var framesField = typeof(RobozombieSpriteAnimation).GetField("frames", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (framesField != null)
                {
                    framesField.SetValue(anim, frames);
                }
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateOrGetGooelistPrefab(string prefabName, Sprite[] frames)
        {
            string path = $"{EnemyPrefabFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var go = new GameObject(prefabName);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = frames != null && frames.Length > 0 ? frames[0] : null;
            sr.sortingOrder = 4;

            var gooelistComp = go.AddComponent<TrashTD.Enemies.GooelistEnemy>();
            gooelistComp.SelfDefReductionPercent = 0.75f;
            gooelistComp.OperatorDefReductionPercent = 0.50f;
            var anim = go.AddComponent<TrashTD.Enemies.GooelistSpriteAnimation>();
            if (frames != null && frames.Length > 0)
            {
                var framesField = typeof(TrashTD.Enemies.GooelistSpriteAnimation).GetField("frames", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (framesField != null)
                {
                    framesField.SetValue(anim, frames);
                }
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateOrGetSpoilcupinePrefab(string prefabName, Sprite[] frames)
        {
            string path = $"{EnemyPrefabFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var go = new GameObject(prefabName);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = frames != null && frames.Length > 0 ? frames[0] : null;
            sr.sortingOrder = 4;

            go.AddComponent<TrashTD.Enemies.SpoilcupineEnemy>();
            var anim = go.AddComponent<TrashTD.Enemies.SpoilcupineSpriteAnimation>();
            if (frames != null && frames.Length > 0)
            {
                var framesField = typeof(TrashTD.Enemies.SpoilcupineSpriteAnimation).GetField("frames", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (framesField != null)
                {
                    framesField.SetValue(anim, frames);
                }
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static void GenerateOperators()
        {
            // 1. Guard (Scrapper)
            var scrapperFrame0 = LoadSprite("Scrapper-Sheet.png", "Scrapper-Sheet_0") ?? LoadSprite("Scrapper-Sheet.png", "Scrapper_0") ?? LoadSprite("tex_op_guard.png");
            var scrapperFrame1 = LoadSprite("Scrapper-Sheet.png", "Scrapper-Sheet_1") ?? LoadSprite("Scrapper-Sheet.png", "Scrapper_1");
            var scrapperFrames = scrapperFrame1 != null ? new[] { scrapperFrame0, scrapperFrame1 } : new[] { scrapperFrame0 };
            var guardPrefab = CreateScrapperPrefab(scrapperFrames);
            var scrapper = CreateOperator("OP_Guard_Scrapper", "Scrapper", OperatorClass.Guard, OperatorPosition.Melee, OperatorRarity.Star1,
                hp: 140, atk: 85, def: 20, res: 0, blockCount: 1, range: 2, interval: 1.1f,
                new[] { new Vector2Int(1, 0), new Vector2Int(2, 0) },
                scrapperFrame0, guardPrefab);
            scrapper.skillDescription = "When attacking, has a 20% chance to Stun the target for 2 seconds.";
            EditorUtility.SetDirty(scrapper);

            var echosquireFrameOne = LoadSprite("Echosquire-Sheet.png", "Echosquire-Sheet_0");
            var echosquireFrameTwo = LoadSprite("Echosquire-Sheet.png", "Echosquire-Sheet_1");
            if (echosquireFrameOne == null || echosquireFrameTwo == null)
            {
                Debug.LogError("Echosquire-Sheet.png must contain the imported Echosquire-Sheet_0 and Echosquire-Sheet_1 sprites.");
            }
            else
            {
                var echosquirePrefab = CreateEchosquirePrefab(echosquireFrameOne, echosquireFrameTwo);
                var echosquire = CreateOperator(
                    "OP_Guard_Echosquire",
                    "Echosquire",
                    OperatorClass.Guard,
                    OperatorPosition.Melee,
                    OperatorRarity.Star2,
                    hp: 130,
                    atk: 55,
                    def: 18,
                    res: 0,
                    blockCount: 2,
                    range: 1,
                    interval: 1.2f,
                    new[] { new Vector2Int(1, 0) },
                    echosquireFrameOne,
                    echosquirePrefab);
                echosquire.roleTags = new[] { "Lifesteal", "Survival" };
                echosquire.skillDescription =
                    "Passive - Vampiric Guard: Restores 20% of physical damage dealt as HP. Cannot be healed by other operators.";
                EditorUtility.SetDirty(echosquire);
            }

            var qiFuFrames = new Sprite[8];
            bool hasAllQiFuFrames = true;
            for (int i = 0; i < qiFuFrames.Length; i++)
            {
                qiFuFrames[i] = LoadSprite("Qi_Fu_Sheet.png", $"Qi_Fu_Sheet_{i}");
                hasAllQiFuFrames &= qiFuFrames[i] != null;
            }

            if (!hasAllQiFuFrames)
            {
                Debug.LogError("Qi_Fu_Sheet.png must contain the imported Qi_Fu_Sheet_0 through Qi_Fu_Sheet_7 sprites.");
            }
            else
            {
                var qiFuPrefab = CreateQiFuPrefab(qiFuFrames);
                var qiFu = CreateOperator(
                    "OP_Guard_QiFu",
                    "Chi Paw",
                    OperatorClass.Guard,
                    OperatorPosition.Melee,
                    OperatorRarity.Star1,
                    hp: 140,
                    atk: 65,
                    def: 20,
                    res: 0,
                    blockCount: 2,
                    range: 1,
                    interval: 1.1f,
                    new[] { new Vector2Int(1, 0) },
                    qiFuFrames[0],
                    qiFuPrefab);
                qiFu.damageType = DamageType.Physical;
                qiFu.roleTags = new[] { "Chi", "Knockback" };
                qiFu.skillDescription = "Every fourth landed hit channels a green Chi Paw that pushes the enemy back one block.";
                EditorUtility.SetDirty(qiFu);
            }

            // 1d. Guard (Stag-ger)
            var staggerFrame0 = LoadSprite("Stag-ger-Sheet.png", "Stag-ger-Sheet_0");
            var staggerFrame1 = LoadSprite("Stag-ger-Sheet.png", "Stag-ger-Sheet_1");
            var staggerFrame2 = LoadSprite("Stag-ger-Sheet.png", "Stag-ger-Sheet_2");
            var staggerFrame3 = LoadSprite("Stag-ger-Sheet.png", "Stag-ger-Sheet_3");
            var staggerFrame4 = LoadSprite("Stag-ger-Sheet.png", "Stag-ger-Sheet_4");
            var staggerFrame5 = LoadSprite("Stag-ger-Sheet.png", "Stag-ger-Sheet_5");
            var staggerFrame6 = LoadSprite("Stag-ger-Sheet.png", "Stag-ger-Sheet_6");
            var staggerFrame7 = LoadSprite("Stag-ger-Sheet.png", "Stag-ger-Sheet_7");
            if (staggerFrame0 != null && staggerFrame1 != null && staggerFrame2 != null && staggerFrame3 != null)
            {
                var staggerFrames = (staggerFrame4 != null && staggerFrame5 != null && staggerFrame6 != null && staggerFrame7 != null)
                    ? new[] { staggerFrame0, staggerFrame1, staggerFrame2, staggerFrame3, staggerFrame4, staggerFrame5, staggerFrame6, staggerFrame7 }
                    : new[] { staggerFrame0, staggerFrame1, staggerFrame2, staggerFrame3 };
                var staggerPrefab = CreateStaggerPrefab(staggerFrames);
                var stagger = CreateOperator(
                    "OP_Guard_Stagger",
                    "Stag-ger",
                    OperatorClass.Guard,
                    OperatorPosition.Melee,
                    OperatorRarity.Star2,
                    hp: 145,
                    atk: 75,
                    def: 22,
                    res: 0,
                    blockCount: 2,
                    range: 1,
                    interval: 1.2f,
                    new[] { new Vector2Int(1, 0) },
                    staggerFrame0,
                    staggerPrefab);
                stagger.damageType = DamageType.Physical;
                stagger.roleTags = new[] { "Multi-Target", "Brawler" };
                stagger.skillDescription =
                    "Passive - Six-Limb Slugger: When attacking, strikes all blocked enemies on his tile and in range ahead simultaneously (including enemies blocked by allies). If no enemies are blocked, attacks a single target in range. Has a 20% to dodge any attack, atk then increase by 50% in the next attack if the dodge is successful";
                EditorUtility.SetDirty(stagger);
            }

            // 1e. Guard (Ignisaur)
            var ignisaurFrame0 = LoadSprite("Ignisaur-Sheet.png", "Ignisaur-Sheet_0") ?? LoadSprite("Ignisaur-Sheet.png", "Ignisaur_0");
            var ignisaurFrame1 = LoadSprite("Ignisaur-Sheet.png", "Ignisaur-Sheet_1") ?? LoadSprite("Ignisaur-Sheet.png", "Ignisaur_1");
            var ignisaurFrame2 = LoadSprite("Ignisaur-Sheet.png", "Ignisaur-Sheet_2") ?? LoadSprite("Ignisaur-Sheet.png", "Ignisaur_2");
            var ignisaurFrame3 = LoadSprite("Ignisaur-Sheet.png", "Ignisaur-Sheet_3") ?? LoadSprite("Ignisaur-Sheet.png", "Ignisaur_3");
            if (ignisaurFrame0 != null && ignisaurFrame1 != null && ignisaurFrame2 != null && ignisaurFrame3 != null)
            {
                var ignisaurFrames = new[] { ignisaurFrame0, ignisaurFrame1, ignisaurFrame2, ignisaurFrame3 };
                var ignisaurPrefab = CreateIgnisaurPrefab(ignisaurFrames);
                var ignisaur = CreateOperator(
                    "OP_Guard_Ignisaur",
                    "Ignisaur",
                    OperatorClass.Guard,
                    OperatorPosition.Melee,
                    OperatorRarity.Star2,
                    hp: 115,
                    atk: 60,
                    def: 18,
                    res: 10,
                    blockCount: 1,
                    range: 2,
                    interval: 1.2f,
                    new[]
                    {
                        new Vector2Int(1, 0),
                        new Vector2Int(1, 1),
                        new Vector2Int(1, -1),
                        new Vector2Int(2, 0)
                    },
                    ignisaurFrame0,
                    ignisaurPrefab);
                ignisaur.damageType = DamageType.Arts;
                ignisaur.roleTags = new[] { "Arts", "Melee", "Anti-Air" };
                ignisaur.skillDescription =
                    "Passive - Protean: Deals Physical damage with a melee slash to enemies on his tile, and Arts damage with ranged attacks to all forward and surrounding tiles in range. Can attack aerial/drone enemies. Reduces attack interval when not blocking, and increases ATK by 25% when blocking.";
                EditorUtility.SetDirty(ignisaur);
            }

            // 2. Defender (Bulkhead)
            var defenderSprite = LoadSprite("tex_op_defender.png");
            var defenderPrefab = CreateOrGetOperatorPrefab("Prefab_OP_Bulkhead", defenderSprite, OperatorClass.Defender);
            var bulkhead = CreateOperator("OP_Defender_Bulkhead", "Bulkhead", OperatorClass.Defender, OperatorPosition.Melee, OperatorRarity.Star1,
                hp: 280, atk: 35, def: 45, res: 10, blockCount: 3, range: 1, interval: 1.4f,
                new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) },
                defenderSprite, defenderPrefab);
            bulkhead.skillDescription = "When blocking 3 enemies, increases ATK by 20% but decreases DEF by 20%.";
            EditorUtility.SetDirty(bulkhead);

            // 2b. Defender (Mossmo)
            var mossmoFrameOne = LoadSprite("Mossmo-Sheet.png", "Mossmo-Sheet_0");
            var mossmoFrameTwo = LoadSprite("Mossmo-Sheet.png", "Mossmo-Sheet_1");
            if (mossmoFrameOne != null && mossmoFrameTwo != null)
            {
                var mossmoPrefab = CreateMossmoPrefab(mossmoFrameOne, mossmoFrameTwo);
                var mossmo = CreateOperator("OP_Defender_Mossmo", "Mossmo", OperatorClass.Defender, OperatorPosition.Melee, OperatorRarity.Star3,
                    hp: 160, atk: 10, def: 30, res: 5, blockCount: 3, range: 1, interval: 1.4f,
                    new[]
                    {
                        new Vector2Int(0, 0),
                        new Vector2Int(1, 0), new Vector2Int(-1, 0),
                        new Vector2Int(0, 1), new Vector2Int(0, -1)
                    },
                    mossmoFrameOne, mossmoPrefab);
                mossmo.roleTags = new[] { "Defense", "Support" };
                mossmo.skillDescription =
                    "Passive - Symbiotic Spores: Mossmo does not attack. Every time he takes damage, he heals up to 2 operators in adjacent tiles (including himself).";
                EditorUtility.SetDirty(mossmo);
            }

            // 2c. Defender (Thornchin)
            var thornchinFrame0 = LoadSprite("Thornchin-Sheet.png", "Thornchin-Sheet_0");
            var thornchinFrame1 = LoadSprite("Thornchin-Sheet.png", "Thornchin-Sheet_1");
            var thornchinFrame2 = LoadSprite("Thornchin-Sheet.png", "Thornchin-Sheet_2");
            var thornchinFrame3 = LoadSprite("Thornchin-Sheet.png", "Thornchin-Sheet_3");
            if (thornchinFrame0 != null && thornchinFrame1 != null && thornchinFrame2 != null && thornchinFrame3 != null)
            {
                var thornchinFrames = new[] { thornchinFrame0, thornchinFrame1, thornchinFrame2, thornchinFrame3 };
                var thornchinPrefab = CreateThornchinPrefab(thornchinFrames);
                var thornchin = CreateOperator("OP_Defender_Thornchin", "Thornchin", OperatorClass.Defender, OperatorPosition.Melee, OperatorRarity.Star2,
                    hp: 150, atk: 40, def: 36, res: 5, blockCount: 3, range: 0, interval: 1.4f,
                    new[] { new Vector2Int(0, 0) },
                    thornchinFrame0, thornchinPrefab);
                thornchin.roleTags = new[] { "Defense", "Retaliation" };
                thornchin.skillDescription =
                    "Passive - Bristling Spines: Whenever Thornchin is attacked, it retaliates against the attacker with sharp urchin spines, dealing Physical damage equal to Thornchin's ATK.";
                EditorUtility.SetDirty(thornchin);
            }

            // 3. Sniper (Deadeye)
            var sniperSprite = LoadSprite("Deadeye-Sheet.png", "Deadeye-Sheet_0") ?? LoadSprite("tex_op_sniper.png");
            var sniperPrefab = CreateOrGetOperatorPrefab("Prefab_OP_Deadeye", sniperSprite, OperatorClass.Sniper);
            var deadeye = CreateOperator("OP_Sniper_Deadeye", "Deadeye", OperatorClass.Sniper, OperatorPosition.Ranged, OperatorRarity.Star1,
                hp: 85, atk: 90, def: 8, res: 0, blockCount: 0, range: 3, interval: 1.0f,
                new[]
                {
                    new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(3, 0),
                    new Vector2Int(1, 1), new Vector2Int(2, 1),
                    new Vector2Int(1, -1), new Vector2Int(2, -1)
                },
                sniperSprite, sniperPrefab);
            deadeye.skillDescription = "Increases damage by 20% but also increases attack interval by 20% against flying enemies.";
            EditorUtility.SetDirty(deadeye);

            // 3a. Sniper (Pompoth)
            var pompothFrame0 = LoadSprite("Pompoth-Sheet.png", "Pompoth-Sheet_0");
            if (pompothFrame0 != null)
            {
                var pompothPrefab = CreateOrGetOperatorPrefab("Prefab_OP_Pompoth", pompothFrame0, OperatorClass.Sniper);
                var pompoth = CreateOperator("OP_Sniper_Pompoth", "Pompoth", OperatorClass.Sniper, OperatorPosition.Ranged, OperatorRarity.Star1,
                    hp: 85, atk: 95, def: 8, res: 0, blockCount: 0, range: 3, interval: 0.6f,
                    new[]
                    {
                        new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(3, 0),
                        new Vector2Int(1, 1), new Vector2Int(2, 1),
                        new Vector2Int(1, -1), new Vector2Int(2, -1),
                        new Vector2Int(3, -1), new Vector2Int(3, 1)
                    },
                    pompothFrame0, pompothPrefab);
                pompoth.skillDescription = "Rapid-fire sniper. Fires faster with lower damage per shot but equal DPS. Passive: attack interval shortens by 10% per enemy in range, with no upper limit.";
                EditorUtility.SetDirty(pompoth);
            }

            // 3b. Sniper (Basurocket)
            var basuFrame0 = LoadSprite("Basurocket-sheet.png", "Basurocket-sheet_0");
            var basuFrame1 = LoadSprite("Basurocket-sheet.png", "Basurocket-sheet_1");
            var basuFrame2 = LoadSprite("Basurocket-sheet.png", "Basurocket-sheet_2");
            var basuFrame3 = LoadSprite("Basurocket-sheet.png", "Basurocket-sheet_3");
            if (basuFrame0 != null && basuFrame1 != null && basuFrame2 != null && basuFrame3 != null)
            {
                var basuFrames = new[] { basuFrame0, basuFrame1, basuFrame2, basuFrame3 };
                var basuPrefab = CreateBasurocketPrefab(basuFrames);
                var basurocket = CreateOperator("OP_Sniper_Basurocket", "Basurocket", OperatorClass.Sniper, OperatorPosition.Ranged, OperatorRarity.Star3,
                    hp: 50, atk: 175, def: 5, res: 0, blockCount: 0, range: 7, interval: 2.8f,
                    new[]
                    {
                        new Vector2Int(2, 0), new Vector2Int(3, 0), new Vector2Int(4, 0),
                        new Vector2Int(5, 0), new Vector2Int(6, 0), new Vector2Int(7, 0)
                    },
                    basuFrame0, basuPrefab);
                basurocket.roleTags = new[] { "Long Range", "AoE", "Explosive", "DPS" };
                basurocket.skillDescription =
                    "Long-range explosive sniper. Fires up to 7 tiles forward, cannot attack the adjacent tile, and deals high physical area damage on impact.";
                EditorUtility.SetDirty(basurocket);
            }

            // 3c. Sniper (Proxyshot / Proxishot)
            var proxishot = AssetDatabase.LoadAssetAtPath<OperatorData>($"{OperatorDataFolder}/OP_Sniper_Proxishot.asset");
            if (proxishot != null)
            {
                proxishot.attackInterval = 2.3f;
                EditorUtility.SetDirty(proxishot);
            }

            // 4. Caster (Pyrolite)
            var casterSprite = LoadSprite("Pyrolite-Sheet.png", "Pyrolite-Sheet_0") ?? LoadSprite("tex_op_caster.png");
            var casterPrefab = CreateOrGetOperatorPrefab("Prefab_OP_Pyrolite", casterSprite, OperatorClass.Caster);
            var caster = CreateOperator("OP_Caster_Pyrolite", "Pyrolite", OperatorClass.Caster, OperatorPosition.Ranged, OperatorRarity.Star1,
                hp: 95, atk: 80, def: 10, res: 20, blockCount: 0, range: 2, interval: 1.6f,
                new[]
                {
                    new Vector2Int(1, 0), new Vector2Int(2, 0),
                    new Vector2Int(0, 1), new Vector2Int(1, 1),
                    new Vector2Int(0, -1), new Vector2Int(1, -1)
                },
                casterSprite, casterPrefab);
            caster.damageType = DamageType.Arts;
            caster.skillDescription = "Increases damage by 3% per enemy in range (stacks up to 5 times). Deals AoE Arts damage.";
            EditorUtility.SetDirty(caster);

            // 4b. Caster (Resicochet)
            var resicochetFrame0 = LoadSprite("Resicochet-Sheet.png", "Resicochet_0");
            var resicochetFrame1 = LoadSprite("Resicochet-Sheet.png", "Resicochet_1");
            var resicochetFrame2 = LoadSprite("Resicochet-Sheet.png", "Resicochet_2");
            var resicochetFrame3 = LoadSprite("Resicochet-Sheet.png", "Resicochet_3");
            var resicochetFrame4 = LoadSprite("Resicochet-Sheet.png", "Resicochet_4");
            var resicochetFrame5 = LoadSprite("Resicochet-Sheet.png", "Resicochet_5");
            var resicochetFrame6 = LoadSprite("Resicochet-Sheet.png", "Resicochet_6");
            var resicochetFrame7 = LoadSprite("Resicochet-Sheet.png", "Resicochet_7");
            var resicochetFrame8 = LoadSprite("Resicochet-Sheet.png", "Resicochet_8");
            var resicochetFrame9 = LoadSprite("Resicochet-Sheet.png", "Resicochet_9");
            var resicochetFrame10 = LoadSprite("Resicochet-Sheet.png", "Resicochet_10");
            var resicochetFrame11 = LoadSprite("Resicochet-Sheet.png", "Resicochet_11");

            var resicochetC1 = new[] { resicochetFrame0, resicochetFrame1, resicochetFrame2, resicochetFrame3 };
            var resicochetC2 = new[] { resicochetFrame4, resicochetFrame5, resicochetFrame6, resicochetFrame7 };
            var resicochetC3 = new[] { resicochetFrame8, resicochetFrame9, resicochetFrame10, resicochetFrame11 };

            var resicochetPrefab = CreateResicochetPrefab(resicochetC1, resicochetC2, resicochetC3);
            var resicochet = CreateOperator("OP_Caster_Resicochet", "Resicochet", OperatorClass.Caster, OperatorPosition.Ranged, OperatorRarity.Star3,
                hp: 90, atk: 150, def: 40, res: 0, blockCount: 0, range: 2, interval: 1.5f,
                new[]
                {
                    new Vector2Int(1, 0), new Vector2Int(2, 0),
                    new Vector2Int(0, 1), new Vector2Int(1, 1),
                    new Vector2Int(0, -1), new Vector2Int(1, -1)
                },
                resicochetFrame0, resicochetPrefab);
            resicochet.damageType = DamageType.Arts;
            resicochet.roleTags = new[] { "Arts", "Bounce", "Burst" };
            resicochet.skillDescription = "Stores a charge every 5 seconds (up to 3). Attacks fire 1 projectile per charge that bounces to up to 4 targets, dealing less damage each bounce.";
            EditorUtility.SetDirty(resicochet);

            // 5. Medic (Coalesce)
            var coalesceFrame0 = LoadSprite("Coalesce-Sheet.png", "Coalesce-Sheet_0");
            var coalesceFrame1 = LoadSprite("Coalesce-Sheet.png", "Coalesce-Sheet_1");
            var coalesceFrame2 = LoadSprite("Coalesce-Sheet.png", "Coalesce-Sheet_2");
            var coalesceFrame3 = LoadSprite("Coalesce-Sheet.png", "Coalesce-Sheet_3");
            var coalesceFrames = (coalesceFrame0 != null && coalesceFrame1 != null && coalesceFrame2 != null && coalesceFrame3 != null)
                ? new[] { coalesceFrame0, coalesceFrame1, coalesceFrame2, coalesceFrame3 }
                : (coalesceFrame0 != null ? new[] { coalesceFrame0 } : null);
            var coalescePrefab = CreateCoalescePrefab(coalesceFrames);
            var coalesce = CreateOperator("OP_Medic_Coalesce", "Coalesce", OperatorClass.Medic, OperatorPosition.Ranged, OperatorRarity.Star1,
                hp: 90, atk: 55, def: 12, res: 15, blockCount: 0, range: 2, interval: 1.8f,
                new[]
                {
                    new Vector2Int(0, 0),
                    new Vector2Int(1, 0), new Vector2Int(-1, 0),
                    new Vector2Int(0, 1), new Vector2Int(0, -1),
                    new Vector2Int(1, 1), new Vector2Int(-1, -1)
                },
                coalesceFrame0 ?? LoadSprite("tex_op_medic.png"), coalescePrefab);
            coalesce.skillDescription = "Healing an operator with HP below 30% has a 20% chance to trigger an extra healing.";
            EditorUtility.SetDirty(coalesce);

            // 5b. Medic (Bubblets)
            var bubbletsFrameOne = LoadSprite("Bubblets-Sheet.png", "Bubblets-Sheet_0");
            var bubbletsFrameTwo = LoadSprite("Bubblets-Sheet.png", "Bubblets-Sheet_1");
            if (bubbletsFrameOne != null && bubbletsFrameTwo != null)
            {
                var bubbletsPrefab = CreateBubbletsPrefab(bubbletsFrameOne, bubbletsFrameTwo);
                var bubblets = CreateOperator("OP_Medic_Bubblets", "Bubblets", OperatorClass.Medic, OperatorPosition.Ranged, OperatorRarity.Star3,
                    hp: 85, atk: 45, def: 10, res: 10, blockCount: 0, range: 1, interval: 2.0f,
                    new[]
                    {
                        new Vector2Int(0, 0),
                        new Vector2Int(1, 0), new Vector2Int(-1, 0),
                        new Vector2Int(0, 1), new Vector2Int(0, -1),
                        new Vector2Int(1, 1), new Vector2Int(-1, 1),
                        new Vector2Int(1, -1), new Vector2Int(-1, -1)
                    },
                    bubbletsFrameOne, bubbletsPrefab);
                bubblets.damageType = DamageType.Arts;
                bubblets.roleTags = new[] { "Shield", "Support" };
                bubblets.skillDescription =
                    "Passive - Protective Bubble: Grants an allied operator in range a bubble shield that negates 1 instance of damage. When popped, the bubble heals the operator and deals Arts damage equal to 30% of Bubblets' ATK to enemies on that tile or directly in front.";
                EditorUtility.SetDirty(bubblets);
            }

            // 5c. Medic (Progeny)
            var progenyFrame0 = LoadSprite("Progeny-Sheet.png", "Progeny-Sheet_0");
            var progenyFrame1 = LoadSprite("Progeny-Sheet.png", "Progeny-Sheet_1");
            var progenyFrame2 = LoadSprite("Progeny-Sheet.png", "Progeny-Sheet_2");
            var progenyFrame3 = LoadSprite("Progeny-Sheet.png", "Progeny-Sheet_3");
            if (progenyFrame0 != null && progenyFrame1 != null && progenyFrame2 != null && progenyFrame3 != null)
            {
                var progenyFrames = new[] { progenyFrame0, progenyFrame1, progenyFrame2, progenyFrame3 };
                var progenyPrefab = CreateProgenyPrefab(progenyFrames);
                var progeny = CreateOperator("OP_Medic_Progeny", "Progeny", OperatorClass.Medic, OperatorPosition.Ranged, OperatorRarity.Star2,
                    hp: 95, atk: 35, def: 12, res: 12, blockCount: 0, range: 1, interval: 1.8f,
                    new[]
                    {
                        new Vector2Int(0, 0),
                        new Vector2Int(1, 0), new Vector2Int(-1, 0),
                        new Vector2Int(0, 1), new Vector2Int(0, -1)
                    },
                    progenyFrame0, progenyPrefab);
                progeny.roleTags = new[] { "Chain Heal", "Support" };
                progeny.skillDescription =
                    "Passive - Chain Heal: Restores HP to an allied operator in range. The heal bounces to a second operator in the surrounding tiles of the healed operator (healing reduced by 50% when it bounces).";
                EditorUtility.SetDirty(progeny);
            }

            // 6. Specialist (Framiliar)
            var framiliarFrame0 = LoadSprite("Framiliar-Sheet.png", "Framiliar-Sheet_0");
            var framiliarFrame1 = LoadSprite("Framiliar-Sheet.png", "Framiliar-Sheet_1");
            var framiliarFrame2 = LoadSprite("Framiliar-Sheet.png", "Framiliar-Sheet_2");
            var framiliarFrame3 = LoadSprite("Framiliar-Sheet.png", "Framiliar-Sheet_3");
            var framiliarFrames = (framiliarFrame0 != null && framiliarFrame1 != null && framiliarFrame2 != null && framiliarFrame3 != null)
                ? new[] { framiliarFrame0, framiliarFrame1, framiliarFrame2, framiliarFrame3 }
                : (framiliarFrame0 != null ? new[] { framiliarFrame0 } : null);

            var framiliarPrefab = CreateFramiliarPrefab(framiliarFrames);
            var framiliar = CreateOperator("OP_Specialist_Framiliar", "Framiliar", OperatorClass.Specialist, OperatorPosition.Any, OperatorRarity.Star2,
                hp: 100, atk: 50, def: 10, res: 0, blockCount: 1, range: 1, interval: 1.2f,
                new[]
                {
                    new Vector2Int(1, 0), new Vector2Int(0, 0)
                },
                framiliarFrame0, framiliarPrefab);
            framiliar.damageType = DamageType.Physical;
            framiliar.roleTags = new[] { "Specialist", "Mimic", "Deck" };
            framiliar.skillDescription = "Copies any operator in your deck, taking on their form in grayscale. When retreated in the same preparation phase, returns as Framiliar; if retreated in the next prep phase, returns as the copied operator. Specialists have no retreat cooldown.";
            EditorUtility.SetDirty(framiliar);
        }

        private static GameObject CreateFramiliarPrefab(Sprite[] frames)
        {
            const string prefabName = "Prefab_OP_Framiliar";
            string path = $"{OperatorPrefabFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                var existingOp = existing.GetComponent<TrashTD.Operators.FramiliarOperator>();
                if (existingOp != null && frames != null)
                {
                    existingOp.ConfigureAnimationFrames(frames);
                }
                var existingAnim = existing.GetComponent<TrashTD.Operators.OperatorSpriteAnimation>();
                if (existingAnim != null && frames != null && frames.Length > 0)
                {
                    existingAnim.Configure(frames);
                }
                var existingSr = existing.GetComponent<SpriteRenderer>();
                if (existingSr != null && frames != null && frames.Length > 0 && frames[0] != null)
                {
                    existingSr.sprite = frames[0];
                }
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var go = new GameObject(prefabName);
            var spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = frames != null && frames.Length > 0 ? frames[0] : null;
            spriteRenderer.sortingOrder = 5;
            var op = go.AddComponent<TrashTD.Operators.FramiliarOperator>();
            if (frames != null && frames.Length > 0)
            {
                op.ConfigureAnimationFrames(frames);
            }
            var animation = go.AddComponent<TrashTD.Operators.OperatorSpriteAnimation>();
            if (frames != null && frames.Length > 0)
            {
                animation.Configure(frames);
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateResicochetPrefab(Sprite[] c1, Sprite[] c2, Sprite[] c3)
        {
            const string prefabName = "Prefab_OP_Resicochet";
            string path = $"{OperatorPrefabFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                var existingOp = existing.GetComponent<TrashTD.Operators.ResicochetOperator>();
                if (existingOp != null)
                {
                    existingOp.ConfigureAnimationFrames(c1, c2, c3);
                }
                var existingAnim = existing.GetComponent<TrashTD.Operators.OperatorSpriteAnimation>();
                if (existingAnim != null && c1 != null && c1.Length > 0)
                {
                    existingAnim.Configure(c1);
                }
                var existingSr = existing.GetComponent<SpriteRenderer>();
                if (existingSr != null && c1 != null && c1.Length > 0 && c1[0] != null)
                {
                    existingSr.sprite = c1[0];
                }
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var go = new GameObject(prefabName);
            var spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = c1 != null && c1.Length > 0 ? c1[0] : null;
            spriteRenderer.sortingOrder = 5;
            var op = go.AddComponent<TrashTD.Operators.ResicochetOperator>();
            op.ConfigureAnimationFrames(c1, c2, c3);
            var animation = go.AddComponent<TrashTD.Operators.OperatorSpriteAnimation>();
            if (c1 != null && c1.Length > 0)
            {
                animation.Configure(c1);
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateBubbletsPrefab(Sprite firstFrame, Sprite secondFrame)
        {
            const string prefabName = "Prefab_OP_Bubblets";
            string path = $"{OperatorPrefabFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var go = new GameObject(prefabName);
            var spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = firstFrame;
            spriteRenderer.sortingOrder = 5;
            go.AddComponent<BubbletsOperator>();
            var animation = go.AddComponent<OperatorSpriteAnimation>();
            animation.Configure(new[] { firstFrame, secondFrame });

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateMossmoPrefab(Sprite firstFrame, Sprite secondFrame)
        {
            const string prefabName = "Prefab_OP_Mossmo";
            string path = $"{OperatorPrefabFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var go = new GameObject(prefabName);
            var spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = firstFrame;
            spriteRenderer.sortingOrder = 5;
            go.AddComponent<MossmoOperator>();
            var animation = go.AddComponent<OperatorSpriteAnimation>();
            animation.Configure(new[] { firstFrame, secondFrame });

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateEchosquirePrefab(Sprite firstFrame, Sprite secondFrame)
        {
            const string prefabName = "Prefab_OP_Echosquire";
            string path = $"{OperatorPrefabFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var go = new GameObject(prefabName);
            var spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = firstFrame;
            spriteRenderer.sortingOrder = 5;
            go.AddComponent<EchosquireOperator>();
            var animation = go.AddComponent<OperatorSpriteAnimation>();
            animation.Configure(new[] { firstFrame, secondFrame });

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateQiFuPrefab(Sprite[] frames)
        {
            const string prefabName = "Prefab_OP_QiFu";
            string path = $"{OperatorPrefabFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var go = new GameObject(prefabName);
            var spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = frames[0];
            spriteRenderer.sortingOrder = 5;
            go.AddComponent<QiFuOperator>();
            var animation = go.AddComponent<OperatorSpriteAnimation>();
            animation.Configure(frames);

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateBasurocketPrefab(Sprite[] frames)
        {
            const string prefabName = "Prefab_OP_Sniper_Basurocket";
            string path = $"{OperatorPrefabFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                var existingAnim = existing.GetComponent<OperatorSpriteAnimation>();
                if (existingAnim == null)
                {
                    existingAnim = existing.AddComponent<OperatorSpriteAnimation>();
                }
                existingAnim.Configure(frames);
                var existingSr = existing.GetComponent<SpriteRenderer>();
                if (existingSr != null && frames != null && frames.Length > 0 && frames[0] != null)
                {
                    existingSr.sprite = frames[0];
                }
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var go = new GameObject(prefabName);
            var spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = frames != null && frames.Length > 0 ? frames[0] : null;
            spriteRenderer.sortingOrder = 5;
            go.AddComponent<BasurocketOperator>();
            var animation = go.AddComponent<OperatorSpriteAnimation>();
            animation.Configure(frames);

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateProgenyPrefab(Sprite[] frames)
        {
            const string prefabName = "Prefab_OP_Progeny";
            string path = $"{OperatorPrefabFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                var existingAnim = existing.GetComponent<OperatorSpriteAnimation>();
                if (existingAnim == null)
                {
                    existingAnim = existing.AddComponent<OperatorSpriteAnimation>();
                }
                existingAnim.Configure(frames);
                var existingSr = existing.GetComponent<SpriteRenderer>();
                if (existingSr != null && frames != null && frames.Length > 0 && frames[0] != null)
                {
                    existingSr.sprite = frames[0];
                }
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var go = new GameObject(prefabName);
            var spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = frames != null && frames.Length > 0 ? frames[0] : null;
            spriteRenderer.sortingOrder = 5;
            go.AddComponent<ProgenyOperator>();
            var animation = go.AddComponent<OperatorSpriteAnimation>();
            animation.Configure(frames);

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateStaggerPrefab(Sprite[] frames)
        {
            const string prefabName = "Prefab_OP_Stagger";
            string path = $"{OperatorPrefabFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                var existingAnim = existing.GetComponent<OperatorSpriteAnimation>();
                if (existingAnim == null)
                {
                    existingAnim = existing.AddComponent<OperatorSpriteAnimation>();
                }
                existingAnim.Configure(frames);
                var existingSr = existing.GetComponent<SpriteRenderer>();
                if (existingSr != null && frames != null && frames.Length > 0 && frames[0] != null)
                {
                    existingSr.sprite = frames[0];
                }
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var go = new GameObject(prefabName);
            var spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = frames != null && frames.Length > 0 ? frames[0] : null;
            spriteRenderer.sortingOrder = 5;
            go.AddComponent<StaggerOperator>();
            var animation = go.AddComponent<OperatorSpriteAnimation>();
            animation.Configure(frames);

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateThornchinPrefab(Sprite[] frames)
        {
            const string prefabName = "Prefab_OP_Defender_Thornchin";
            string path = $"{OperatorPrefabFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                var existingAnim = existing.GetComponent<OperatorSpriteAnimation>();
                if (existingAnim == null)
                {
                    existingAnim = existing.AddComponent<OperatorSpriteAnimation>();
                }
                existingAnim.Configure(frames);
                var existingSr = existing.GetComponent<SpriteRenderer>();
                if (existingSr != null && frames != null && frames.Length > 0 && frames[0] != null)
                {
                    existingSr.sprite = frames[0];
                }
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var go = new GameObject(prefabName);
            var spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = frames != null && frames.Length > 0 ? frames[0] : null;
            spriteRenderer.sortingOrder = 5;
            go.AddComponent<ThornchinOperator>();
            var animation = go.AddComponent<OperatorSpriteAnimation>();
            animation.Configure(frames);

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateIgnisaurPrefab(Sprite[] frames)
        {
            const string prefabName = "Prefab_OP_Guard_Ignisaur";
            string path = $"{OperatorPrefabFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                var existingAnim = existing.GetComponent<OperatorSpriteAnimation>();
                if (existingAnim == null)
                {
                    existingAnim = existing.AddComponent<OperatorSpriteAnimation>();
                }
                existingAnim.Configure(frames);
                var existingSr = existing.GetComponent<SpriteRenderer>();
                if (existingSr != null && frames != null && frames.Length > 0 && frames[0] != null)
                {
                    existingSr.sprite = frames[0];
                }
                if (existing.GetComponent<IgnisaurOperator>() == null)
                {
                    existing.AddComponent<IgnisaurOperator>();
                }
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var go = new GameObject(prefabName);
            var spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = frames != null && frames.Length > 0 ? frames[0] : null;
            spriteRenderer.sortingOrder = 5;
            go.AddComponent<IgnisaurOperator>();
            var animation = go.AddComponent<OperatorSpriteAnimation>();
            animation.Configure(frames);

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateScrapperPrefab(Sprite[] frames)
        {
            const string prefabName = "Prefab_OP_Scrapper";
            string path = $"{OperatorPrefabFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                var existingAnim = existing.GetComponent<OperatorSpriteAnimation>();
                if (existingAnim == null)
                {
                    existingAnim = existing.AddComponent<OperatorSpriteAnimation>();
                }
                existingAnim.Configure(frames);
                var existingSr = existing.GetComponent<SpriteRenderer>();
                if (existingSr != null && frames != null && frames.Length > 0 && frames[0] != null)
                {
                    existingSr.sprite = frames[0];
                }
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var go = new GameObject(prefabName);
            var spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = frames != null && frames.Length > 0 ? frames[0] : null;
            spriteRenderer.sortingOrder = 5;
            go.AddComponent<ScrapperOperator>();
            var animation = go.AddComponent<OperatorSpriteAnimation>();
            animation.Configure(frames);

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateCoalescePrefab(Sprite[] frames)
        {
            const string prefabName = "Prefab_OP_Coalesce";
            string path = $"{OperatorPrefabFolder}/{prefabName}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                var existingAnim = existing.GetComponent<OperatorSpriteAnimation>();
                if (existingAnim == null)
                {
                    existingAnim = existing.AddComponent<OperatorSpriteAnimation>();
                }
                existingAnim.Configure(frames);
                var existingSr = existing.GetComponent<SpriteRenderer>();
                if (existingSr != null && frames != null && frames.Length > 0 && frames[0] != null)
                {
                    existingSr.sprite = frames[0];
                }
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var go = new GameObject(prefabName);
            var spriteRenderer = go.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = frames != null && frames.Length > 0 ? frames[0] : null;
            spriteRenderer.sortingOrder = 5;
            go.AddComponent<CoalesceOperator>();
            var animation = go.AddComponent<OperatorSpriteAnimation>();
            animation.Configure(frames);

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static OperatorData CreateOperator(string assetName, string displayName, OperatorClass opClass,
            OperatorPosition pos, OperatorRarity rarity, int hp, int atk, int def, int res, int blockCount,
            int range, float interval, Vector2Int[] rangePattern, Sprite portrait, GameObject prefab)
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

            var robozombieFrames = LoadSpriteFrames("Robozombie Grunt Sheet.png", "Robozombie Grunt Sheet", 8);
            bool hasAllRobozombieFrames = true;
            for (int i = 0; i < robozombieFrames.Length; i++)
                hasAllRobozombieFrames &= robozombieFrames[i] != null;

            if (hasAllRobozombieFrames)
            {
                var robozombiePrefab = CreateOrGetRobozombiePrefab("Prefab_Enemy_Robozombie", robozombieFrames);
                var robozombie = CreateEnemy("Enemy_Grunt_Robozombie", "Robozombie", EnemyArchetype.Grunt,
                    EnemyMovementType.Ground, hp: 200, atk: 25, def: 10, res: 0, speed: 0.75f,
                    isUnblockable: false, robozombieFrames[0], robozombiePrefab);
                robozombie.visualOffset = new Vector3(0f, 0.2f, 0f);
                EditorUtility.SetDirty(robozombie);
            }

            var spoilcupineFrames = LoadSpriteFrames("Spoilcupine Sheet.png", "Spoilcupine Sheet", 8);
            bool hasAllSpoilcupineFrames = true;
            for (int i = 0; i < spoilcupineFrames.Length; i++)
                hasAllSpoilcupineFrames &= spoilcupineFrames[i] != null;

            if (hasAllSpoilcupineFrames)
            {
                var spoilcupinePrefab = CreateOrGetSpoilcupinePrefab("Prefab_Enemy_Spoilcupine", spoilcupineFrames);
                var spoilcupine = CreateEnemy("Enemy_Grunt_Spoilcupine", "Spoilcupine", EnemyArchetype.Grunt,
                    EnemyMovementType.Ground, hp: 320, atk: 25, def: 5, res: 0, speed: 1.0f,
                    isUnblockable: false, spoilcupineFrames[0], spoilcupinePrefab);
                spoilcupine.damageType = DamageType.Physical;
                spoilcupine.animationFrames = spoilcupineFrames;
                spoilcupine.animationFrameRate = 8f;
                EditorUtility.SetDirty(spoilcupine);
            }

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

            var sandHulkFrames = LoadSpriteFrames("Sand Hulk Sheet.png", "Sand Hulk Sheet", 8);
            bool hasAllSandHulkFrames = true;
            for (int i = 0; i < sandHulkFrames.Length; i++)
                hasAllSandHulkFrames &= sandHulkFrames[i] != null;

            if (hasAllSandHulkFrames)
            {
                var sandHulk = CreateEnemy("Enemy_Tank_SandHulk", "Sand Hulk", EnemyArchetype.Tank,
                    EnemyMovementType.Ground, hp: 750, atk: 45, def: 35, res: 5, speed: 0.45f,
                    isUnblockable: false, sandHulkFrames[0], tankPrefab);
                sandHulk.damageType = DamageType.Physical;
                sandHulk.operatorAttackMissChance = 0.2f;
                sandHulk.frontAreaAttackTiles = 2;
                sandHulk.animationFrames = sandHulkFrames;
                sandHulk.animationFrameRate = 8f;
                sandHulk.visualScale = 2f;
                sandHulk.visualOffset = new Vector3(0.15f, 0.2f, 0f);
                EditorUtility.SetDirty(sandHulk);
            }

            var gooelistFrames = LoadSpriteFrames("Gooelist Sheet.png", "Gooelist Sheet", 8);
            bool hasAllGooelistFrames = true;
            for (int i = 0; i < gooelistFrames.Length; i++)
                hasAllGooelistFrames &= gooelistFrames[i] != null;

            if (hasAllGooelistFrames)
            {
                var gooelistPrefab = CreateOrGetGooelistPrefab("Prefab_Enemy_Gooelist", gooelistFrames);
                var gooelist = CreateEnemy("Enemy_Tank_Gooelist", "Gooelist", EnemyArchetype.Tank,
                    EnemyMovementType.Ground, hp: 650, atk: 45, def: 120, res: 0, speed: 0.9f,
                    isUnblockable: false, gooelistFrames[0], gooelistPrefab);
                gooelist.damageType = DamageType.Physical;
                gooelist.animationFrames = gooelistFrames;
                gooelist.animationFrameRate = 8f;
                EditorUtility.SetDirty(gooelist);
            }

            var smogCasterFrames = LoadSpriteFrames("smog caster sheet.png", "smog caster sheet", 8);
            bool hasAllSmogCasterFrames = true;
            for (int i = 0; i < smogCasterFrames.Length; i++)
                hasAllSmogCasterFrames &= smogCasterFrames[i] != null;

            var casterSprite = hasAllSmogCasterFrames
                ? smogCasterFrames[0]
                : LoadSprite("tex_enemy_caster.png");
            var casterPrefab = CreateOrGetEnemyPrefab("Prefab_Enemy_Caster", casterSprite, EnemyArchetype.Caster);
            var smogCaster = CreateEnemy("Enemy_Caster_Smog", "Smog Caster", EnemyArchetype.Caster, EnemyMovementType.Ground,
                hp: 110, atk: 35, def: 10, res: 15, speed: 0.85f, isUnblockable: false,
                casterSprite, casterPrefab);
            if (hasAllSmogCasterFrames)
            {
                smogCaster.animationFrames = smogCasterFrames;
                smogCaster.animationFrameRate = 8f;
            }
            EditorUtility.SetDirty(smogCaster);

            var sandPonchoSprite = LoadSprite("Sand Poncho Enemy Sheet.png", "Sand Poncho Enemy Sheet_0");
            var sandPoncho = CreateEnemy("Enemy_Caster_SandPoncho", "Sand Poncho", EnemyArchetype.Caster,
                EnemyMovementType.Ground, hp: 110, atk: 35, def: 10, res: 15, speed: 0.65f,
                isUnblockable: false, sandPonchoSprite, casterPrefab);
            sandPoncho.damageType = DamageType.Arts;
            sandPoncho.operatorAttackMissChance = 0.2f;
            sandPoncho.attackProjectileColor = new Color(0.95f, 0.72f, 0.34f);
            sandPoncho.usesSandProjectileVisual = true;
            sandPoncho.animationFrames = LoadSpriteFrames(
                "Sand Poncho Enemy Sheet.png",
                "Sand Poncho Enemy Sheet",
                8);
            EditorUtility.SetDirty(sandPoncho);

            var acidFlyerFrames = LoadSpriteFrames("acid flyer sheet.png", "acid flyer sheet", 8);
            bool hasAllAcidFlyerFrames = true;
            for (int i = 0; i < acidFlyerFrames.Length; i++)
                hasAllAcidFlyerFrames &= acidFlyerFrames[i] != null;

            var flyerSprite = hasAllAcidFlyerFrames
                ? acidFlyerFrames[0]
                : LoadSprite("tex_enemy_flyer.png");
            var flyerPrefab = CreateOrGetEnemyPrefab("Prefab_Enemy_Flyer", flyerSprite, EnemyArchetype.Flyer);
            var acidFlyer = CreateEnemy("Enemy_Flyer_Acid", "Acid Flyer", EnemyArchetype.Flyer, EnemyMovementType.Air,
                hp: 80, atk: 25, def: 5, res: 10, speed: 1.25f, isUnblockable: true,
                flyerSprite, flyerPrefab);
            acidFlyer.visualOffset = new Vector3(0f, 0.75f, 0f);
            if (hasAllAcidFlyerFrames)
            {
                acidFlyer.animationFrames = acidFlyerFrames;
                acidFlyer.animationFrameRate = 8f;
            }
            EditorUtility.SetDirty(acidFlyer);
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
            string path = $"{StageDataFolder}/STAGE_01.asset";
            var stage = AssetDatabase.LoadAssetAtPath<StageData>(path);
            if (stage == null)
            {
                stage = ScriptableObject.CreateInstance<StageData>();
                AssetDatabase.CreateAsset(stage, path);
            }

            stage.stageId = "STAGE_01";
            stage.mapName = "Landfill Outskirts";
            stage.gridWidth = 12;
            stage.gridHeight = 6;

            var levelSprites = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/level1 complete.png");
            if (levelSprites == null || levelSprites.Length == 0)
                levelSprites = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/Level1 complete.png");

            if (levelSprites != null)
            {
                foreach (var obj in levelSprites)
                {
                    if (obj is Sprite s)
                    {
                        stage.mapVisualSprite = s;
                        break;
                    }
                }
            }
            stage.visualTilePixelSize = 32;
            stage.visualTileOffset = Vector2.zero;

            var fgSprites = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/level 1 bottom.png");
            if (fgSprites != null)
            {
                foreach (var obj in fgSprites)
                {
                    if (obj is Sprite s)
                    {
                        stage.foregroundVisualSprite = s;
                        break;
                    }
                }
            }

            stage.squadSizeLimit = 8;

            // Lives: 3 on Easy/Normal, 1 on Hard
            stage.lifePointsEasy = 3;
            stage.lifePointsNormal = 3;
            stage.lifePointsHard = 1;

            string[] layoutRows =
            {
                "HHHHHHHHHHHH",
                "SLLHLLLHHHHH",
                "HHLHLHLHLLLH",
                "HHLHLHLLLHLE",
                "HHLLLHHHHHHH",
                "HHHHHHHHHHHH"
            };
            stage.tileLayout = new TileType[12 * 6];
            var spawnPoints = new List<Vector2Int>();
            var exitPoints = new List<Vector2Int>();
            for (int row = 0; row < layoutRows.Length; row++)
            {
                for (int x = 0; x < 12; x++)
                {
                    int y = layoutRows.Length - 1 - row;
                    int index = y * 12 + x;
                    switch (layoutRows[row][x])
                    {
                        case 'H':
                            stage.tileLayout[index] = TileType.HighGround;
                            break;
                        case 'S':
                            stage.tileLayout[index] = TileType.SpawnPoint;
                            spawnPoints.Add(new Vector2Int(x, y));
                            break;
                        case 'E':
                            stage.tileLayout[index] = TileType.ExitPoint;
                            exitPoints.Add(new Vector2Int(x, y));
                            break;
                        default:
                            stage.tileLayout[index] = TileType.LowGround;
                            break;
                    }
                }
            }
            stage.spawnPoints = spawnPoints.ToArray();
            stage.exitPoints = exitPoints.ToArray();

            var grunt = AssetDatabase.LoadAssetAtPath<EnemyData>($"{EnemyDataFolder}/Enemy_Grunt_Sludge.asset");
            var rusher = AssetDatabase.LoadAssetAtPath<EnemyData>($"{EnemyDataFolder}/Enemy_Rusher_Toxic.asset");
            var tank = AssetDatabase.LoadAssetAtPath<EnemyData>($"{EnemyDataFolder}/Enemy_Tank_Pollution.asset");

            if (grunt != null && ShouldGenerateWaves(stage))
            {
                stage.wavesEasy = new[]
                {
                    new WaveData
                    {
                        waveName = "Wave 1: Scouts",
                        preWaveDelay = 2.0f,
                        entries = new[]
                        {
                            new WaveEntry { enemyData = grunt, count = 3, spawnInterval = 3.0f, startDelay = 0f, spawnPointIndex = 0 }
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

                stage.wavesEasy = ExtendWaveCampaign(stage.wavesEasy, 10, 0, grunt, rusher, tank);
                stage.wavesNormal = ExtendWaveCampaign(stage.wavesNormal, 20, 1, grunt, rusher, tank);
                stage.wavesHard = ExtendWaveCampaign(stage.wavesHard, 30, 2, grunt, rusher, tank);
            }

            EditorUtility.SetDirty(stage);
        }

        // On editor load, only create stage assets that are missing. Existing stages are never
        // regenerated automatically (that used to overwrite hand-edited waves on every recompile);
        // use the TRASH TD > Generate Stage menu items to regenerate one on purpose.
        [InitializeOnLoadMethod]
        private static void AutoEnsureStages()
        {
            EditorApplication.delayCall += () =>
            {
                CreateStageIfMissing("STAGE_02.asset", GenerateStage2);
                CreateStageIfMissing("STAGE_03.asset", GenerateStage3);
                CreateStageIfMissing("STAGE_05.asset", GenerateStage5);
                CreateStageIfMissing("STAGE_06.asset", GenerateStage6);
            };
        }

        private static void CreateStageIfMissing(string fileName, System.Action generate)
        {
            if (AssetDatabase.LoadAssetAtPath<StageData>($"{StageDataFolder}/{fileName}") == null)
                generate();
        }

        [MenuItem("TRASH TD/Generate Stage 2 (Scrapyard Junction)")]
        public static void GenerateStage2()
        {
            string path = $"{StageDataFolder}/STAGE_02.asset";
            var stage = AssetDatabase.LoadAssetAtPath<StageData>(path);
            if (stage == null)
            {
                stage = ScriptableObject.CreateInstance<StageData>();
                AssetDatabase.CreateAsset(stage, path);
            }

            stage.stageId = "STAGE_02";
            stage.mapName = "Scrapyard Junction";
            stage.shortDescription = "Secure the scrapyard junction against converging waves.";
            stage.gridWidth = 11;
            stage.gridHeight = 7;

            var levelSprites = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/level2 complete.png");
            if (levelSprites == null || levelSprites.Length == 0)
                levelSprites = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/Level2 complete.png");

            if (levelSprites != null)
            {
                foreach (var obj in levelSprites)
                {
                    if (obj is Sprite s)
                    {
                        stage.mapVisualSprite = s;
                        break;
                    }
                }
            }
            stage.visualTilePixelSize = 32;
            stage.visualTileOffset = Vector2.zero;

            var fgSprites = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/level2 bottom.png");
            if (fgSprites == null || fgSprites.Length == 0)
                fgSprites = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/Level2 bottom.png");

            if (fgSprites != null)
            {
                foreach (var obj in fgSprites)
                {
                    if (obj is Sprite s)
                    {
                        stage.foregroundVisualSprite = s;
                        break;
                    }
                }
            }
            stage.squadSizeLimit = 8;

            stage.lifePointsEasy = 3;
            stage.lifePointsNormal = 3;
            stage.lifePointsHard = 1;

            string[] layoutRows =
            {
                "BBBBBBBBBBS",
                "BBBBLLLLLLL",
                "BHHHLHHHHHH",
                "ELLLLLLLLLE",
                "HHHHHHLHHHB",
                "LLLLLLLBBBB",
                "SBBBBBBBBBB"
            };
            stage.tileLayout = new TileType[11 * 7];
            var spawnPoints = new List<Vector2Int>();
            var exitPoints = new List<Vector2Int>();
            for (int row = 0; row < layoutRows.Length; row++)
            {
                for (int x = 0; x < 11; x++)
                {
                    int y = layoutRows.Length - 1 - row;
                    int index = y * 11 + x;
                    switch (layoutRows[row][x])
                    {
                        case 'H':
                            stage.tileLayout[index] = TileType.HighGround;
                            break;
                        case 'S':
                            stage.tileLayout[index] = TileType.SpawnPoint;
                            spawnPoints.Add(new Vector2Int(x, y));
                            break;
                        case 'E':
                            stage.tileLayout[index] = TileType.ExitPoint;
                            exitPoints.Add(new Vector2Int(x, y));
                            break;
                        case 'B':
                            stage.tileLayout[index] = TileType.Blocked;
                            break;
                        default:
                            stage.tileLayout[index] = TileType.LowGround;
                            break;
                    }
                }
            }
            stage.spawnPoints = new[]
            {
                new Vector2Int(10, 6), // Spawn 0: Top-Right (Yellow arrow)
                new Vector2Int(0, 0)   // Spawn 1: Bottom-Left (Blue arrow)
            };
            stage.exitPoints = new[]
            {
                new Vector2Int(0, 3),  // Exit 0: Left exit (Blue arrow destination)
                new Vector2Int(10, 3)  // Exit 1: Right exit (Yellow arrow destination)
            };

            // Symmetrical paths crossing at Scrapyard Junction:
            // Path 0: Top-Right spawn (10, 6) -> Row 5 westward -> down col 4 -> Row 3 eastward -> Right exit (10, 3) [Yellow arrow]
            var path0Waypoints = new[]
            {
                new Vector2Int(10, 6),
                new Vector2Int(10, 5),
                new Vector2Int(9, 5),
                new Vector2Int(8, 5),
                new Vector2Int(7, 5),
                new Vector2Int(6, 5),
                new Vector2Int(5, 5),
                new Vector2Int(4, 5),
                new Vector2Int(4, 4),
                new Vector2Int(4, 3),
                new Vector2Int(5, 3),
                new Vector2Int(6, 3),
                new Vector2Int(7, 3),
                new Vector2Int(8, 3),
                new Vector2Int(9, 3),
                new Vector2Int(10, 3)
            };

            // Path 1: Bottom-Left spawn (0, 0) -> Row 1 eastward -> up col 6 -> Row 3 westward -> Left exit (0, 3) [Blue arrow]
            var path1Waypoints = new[]
            {
                new Vector2Int(0, 0),
                new Vector2Int(0, 1),
                new Vector2Int(1, 1),
                new Vector2Int(2, 1),
                new Vector2Int(3, 1),
                new Vector2Int(4, 1),
                new Vector2Int(5, 1),
                new Vector2Int(6, 1),
                new Vector2Int(6, 2),
                new Vector2Int(6, 3),
                new Vector2Int(5, 3),
                new Vector2Int(4, 3),
                new Vector2Int(3, 3),
                new Vector2Int(2, 3),
                new Vector2Int(1, 3),
                new Vector2Int(0, 3)
            };

            stage.enemyPaths = new[]
            {
                new PathData
                {
                    spawnPointIndex = 0,
                    exitPointIndex = 1,
                    waypoints = path0Waypoints
                },
                new PathData
                {
                    spawnPointIndex = 1,
                    exitPointIndex = 0,
                    waypoints = path1Waypoints
                }
            };

            var grunt = AssetDatabase.LoadAssetAtPath<EnemyData>($"{EnemyDataFolder}/Enemy_Grunt_Sludge.asset");
            var rusher = AssetDatabase.LoadAssetAtPath<EnemyData>($"{EnemyDataFolder}/Enemy_Rusher_Toxic.asset");
            var tank = AssetDatabase.LoadAssetAtPath<EnemyData>($"{EnemyDataFolder}/Enemy_Tank_Pollution.asset");

            if (ShouldGenerateWaves(stage))
            {
                stage.wavesEasy = BuildStage2Waves(10, 0, grunt, rusher, tank);
                stage.wavesNormal = BuildStage2Waves(20, 1, grunt, rusher, tank);
                stage.wavesHard = BuildStage2Waves(30, 2, grunt, rusher, tank);
            }

            EditorUtility.SetDirty(stage);
            AssetDatabase.SaveAssets();

            Debug.Log("<color=green>TRASH TD: Stage 2 (Scrapyard Junction) successfully generated with dual-lane enemy waves!</color>");
        }

        [MenuItem("TRASH TD/Generate Stage 3 (Railyard Crossing)")]
        public static void GenerateStage3()
        {
            string path = $"{StageDataFolder}/STAGE_03.asset";
            var stage = AssetDatabase.LoadAssetAtPath<StageData>(path);
            if (stage == null)
            {
                stage = ScriptableObject.CreateInstance<StageData>();
                AssetDatabase.CreateAsset(stage, path);
            }

            stage.stageId = "STAGE_03";
            stage.mapName = "Railyard Crossing";
            stage.shortDescription = "Defend the crossing while avoiding the toxic traps.";
            stage.gridWidth = 14;
            stage.gridHeight = 7;

            var backgroundSprites = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/Level3-Backgroundt.png");
            stage.backgroundVisualSprite = null;
            if (backgroundSprites != null)
            {
                foreach (var obj in backgroundSprites)
                {
                    if (obj is Sprite sprite)
                    {
                        stage.backgroundVisualSprite = sprite;
                        break;
                    }
                }
            }

            var mainGroundSprites = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/Level3-MainGroundt.png");
            stage.mapVisualSprite = null;
            if (mainGroundSprites != null)
            {
                foreach (var obj in mainGroundSprites)
                {
                    if (obj is Sprite sprite)
                    {
                        stage.mapVisualSprite = sprite;
                        break;
                    }
                }
            }

            var upperBackgroundSprites = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/Level3-UpperBackgroundt.png");
            var upperBackgroundLayers = new List<Sprite>();
            if (upperBackgroundSprites != null)
            {
                foreach (var obj in upperBackgroundSprites)
                {
                    if (obj is Sprite sprite)
                    {
                        upperBackgroundLayers.Add(sprite);
                    }
                }
            }
            stage.upperBackgroundVisualSprites = upperBackgroundLayers.ToArray();

            stage.visualTilePixelSize = 32;
            stage.visualTileOffset = Vector2.zero;
            stage.foregroundVisualSprite = null;
            stage.squadSizeLimit = 8;
            stage.lifePointsEasy = 3;
            stage.lifePointsNormal = 3;
            stage.lifePointsHard = 1;

            string[] layoutRows =
            {
                "BBBBBBBBBBBBBB",
                "BHHHHHBBHHHHHB",
                "SLLHLLLLLLLLHH",
                "BBLLLBBBBBHLLH",
                "BBBBBBBBBBBBLH",
                "SLLTLLLLLLLLLH",
                "HHHTHHBBHHHHLE"
            };
            stage.tileLayout = new TileType[stage.gridWidth * stage.gridHeight];
            var spawnPoints = new List<Vector2Int>();
            var exitPoints = new List<Vector2Int>();
            for (int row = 0; row < layoutRows.Length; row++)
            {
                for (int x = 0; x < stage.gridWidth; x++)
                {
                    int y = layoutRows.Length - 1 - row;
                    int index = y * stage.gridWidth + x;
                    switch (layoutRows[row][x])
                    {
                        case 'H':
                            stage.tileLayout[index] = TileType.HighGround;
                            break;
                        case 'S':
                            stage.tileLayout[index] = TileType.SpawnPoint;
                            spawnPoints.Add(new Vector2Int(x, y));
                            break;
                        case 'E':
                            stage.tileLayout[index] = TileType.ExitPoint;
                            exitPoints.Add(new Vector2Int(x, y));
                            break;
                        case 'T':
                            stage.tileLayout[index] = TileType.Trap;
                            break;
                        case 'B':
                            stage.tileLayout[index] = TileType.Blocked;
                            break;
                        default:
                            stage.tileLayout[index] = TileType.LowGround;
                            break;
                    }
                }
            }

            stage.spawnPoints = spawnPoints.ToArray();
            stage.exitPoints = exitPoints.ToArray();
            stage.enemyPaths = new[]
            {
                new PathData
                {
                    spawnPointIndex = 0,
                    exitPointIndex = 0,
                    waypoints = new[]
                    {
                        new Vector2Int(0, 4), new Vector2Int(1, 4), new Vector2Int(2, 4),
                        new Vector2Int(2, 3), new Vector2Int(3, 3), new Vector2Int(4, 3),
                        new Vector2Int(4, 4), new Vector2Int(5, 4), new Vector2Int(6, 4),
                        new Vector2Int(7, 4), new Vector2Int(8, 4), new Vector2Int(9, 4),
                        new Vector2Int(10, 4), new Vector2Int(11, 4), new Vector2Int(11, 3),
                        new Vector2Int(12, 3), new Vector2Int(12, 2), new Vector2Int(12, 1),
                        new Vector2Int(12, 0), new Vector2Int(13, 0)
                    }
                },
                new PathData
                {
                    spawnPointIndex = 1,
                    exitPointIndex = 0,
                    waypoints = new[]
                    {
                        new Vector2Int(0, 1), new Vector2Int(1, 1),
                        new Vector2Int(3, 1), new Vector2Int(4, 1), new Vector2Int(5, 1),
                        new Vector2Int(6, 1), new Vector2Int(7, 1), new Vector2Int(8, 1),
                        new Vector2Int(9, 1), new Vector2Int(10, 1), new Vector2Int(11, 1),
                        new Vector2Int(12, 1), new Vector2Int(12, 0), new Vector2Int(13, 0)
                    }
                }
            };

            var grunt = AssetDatabase.LoadAssetAtPath<EnemyData>($"{EnemyDataFolder}/Enemy_Grunt_Sludge.asset");
            var rusher = AssetDatabase.LoadAssetAtPath<EnemyData>($"{EnemyDataFolder}/Enemy_Rusher_Toxic.asset");
            var tank = AssetDatabase.LoadAssetAtPath<EnemyData>($"{EnemyDataFolder}/Enemy_Tank_Pollution.asset");
            if (ShouldGenerateWaves(stage))
            {
                stage.wavesEasy = BuildStage3Waves(0, grunt, rusher, tank);
                stage.wavesNormal = BuildStage3Waves(1, grunt, rusher, tank);
                stage.wavesHard = BuildStage3Waves(2, grunt, rusher, tank);
            }

            EditorUtility.SetDirty(stage);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("TRASH TD/Generate Stage 5 (Boulevard)")]
        public static void GenerateStage5()
        {
            string path = $"{StageDataFolder}/STAGE_05.asset";
            var stage = AssetDatabase.LoadAssetAtPath<StageData>(path);
            if (stage == null)
            {
                stage = ScriptableObject.CreateInstance<StageData>();
                AssetDatabase.CreateAsset(stage, path);
            }

            stage.stageId = "STAGE_05";
            stage.mapName = "Boulevard";
            stage.shortDescription = "Navigate the pipes and hold the boulevard against toxic pollutants.";
            stage.gridWidth = 13;
            stage.gridHeight = 8;

            var levelSprites = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/Level 5 complete.png");
            if (levelSprites == null || levelSprites.Length == 0)
                levelSprites = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/level 5 complete.png");

            if (levelSprites != null)
            {
                foreach (var obj in levelSprites)
                {
                    if (obj is Sprite s)
                    {
                        stage.mapVisualSprite = s;
                        break;
                    }
                }
            }
            stage.visualTilePixelSize = 32;
            stage.visualTileOffset = new Vector2(0, -1);

            var fgSprites = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/Level 5 bottom.png");
            if (fgSprites == null || fgSprites.Length == 0)
                fgSprites = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/level 5 bottom.png");

            if (fgSprites != null)
            {
                foreach (var obj in fgSprites)
                {
                    if (obj is Sprite s)
                    {
                        stage.foregroundVisualSprite = s;
                        break;
                    }
                }
            }

            var vehicleAssets = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/Level 5 vehicles.png");
            if (vehicleAssets == null || vehicleAssets.Length == 0)
                vehicleAssets = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/level 5 vehicles.png");

            var vList = new List<Sprite>();
            if (vehicleAssets != null)
            {
                foreach (var obj in vehicleAssets)
                {
                    if (obj is Sprite s)
                    {
                        vList.Add(s);
                    }
                }
            }
            vList.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            stage.trafficVehicleSprites = vList.ToArray();

            stage.backgroundVisualSprite = null;
            stage.upperBackgroundVisualSprites = null;
            stage.squadSizeLimit = 8;
            stage.lifePointsEasy = 3;
            stage.lifePointsNormal = 3;
            stage.lifePointsHard = 1;

            string[] layoutRows =
            {
                "BHHSSHHHHHHHH",
                "HHHLLHHHOHHHE",
                "SLLLLLLLLLLLL",
                "SLLLLLLLLLLLL",
                "BHHLLHHHHHHHH",
                "BHHLLHHHHHHHH",
                "SLLLLLLLLLLLS",
                "BBBBBOBBBBBBB"
            };

            stage.tileLayout = new TileType[stage.gridWidth * stage.gridHeight];
            var spawnPoints = new List<Vector2Int>();
            var exitPoints = new List<Vector2Int>();
            for (int row = 0; row < layoutRows.Length; row++)
            {
                for (int x = 0; x < stage.gridWidth; x++)
                {
                    int y = layoutRows.Length - 1 - row;
                    int index = y * stage.gridWidth + x;
                    switch (layoutRows[row][x])
                    {
                        case 'H':
                            stage.tileLayout[index] = TileType.HighGround;
                            break;
                        case 'S':
                            stage.tileLayout[index] = TileType.SpawnPoint;
                            spawnPoints.Add(new Vector2Int(x, y));
                            break;
                        case 'E':
                            stage.tileLayout[index] = TileType.ExitPoint;
                            exitPoints.Add(new Vector2Int(x, y));
                            break;
                        case 'B':
                            stage.tileLayout[index] = TileType.Blocked;
                            break;
                        case 'O':
                            stage.tileLayout[index] = TileType.Hole;
                            break;
                        default:
                            stage.tileLayout[index] = TileType.LowGround;
                            break;
                    }
                }
            }

            stage.spawnPoints = new[]
            {
                new Vector2Int(3, 7),
                new Vector2Int(4, 7),
                new Vector2Int(0, 5),
                new Vector2Int(0, 4),
                new Vector2Int(0, 1),
                new Vector2Int(12, 1)
            };
            stage.exitPoints = new[]
            {
                new Vector2Int(12, 6)
            };

            stage.enemyPaths = new[]
            {
                new PathData
                {
                    spawnPointIndex = 0,
                    exitPointIndex = 0,
                    waypoints = new[]
                    {
                        new Vector2Int(3, 7), new Vector2Int(3, 6), new Vector2Int(3, 5),
                        new Vector2Int(3, 4), new Vector2Int(3, 3), new Vector2Int(3, 2),
                        new Vector2Int(3, 1), new Vector2Int(4, 1), new Vector2Int(5, 1),
                        new Vector2Int(5, 0),
                        new Vector2Int(8, 6),
                        new Vector2Int(8, 5), new Vector2Int(9, 5), new Vector2Int(10, 5),
                        new Vector2Int(11, 5), new Vector2Int(12, 5), new Vector2Int(12, 6)
                    }
                },
                new PathData
                {
                    spawnPointIndex = 1,
                    exitPointIndex = 0,
                    waypoints = new[]
                    {
                        new Vector2Int(4, 7), new Vector2Int(4, 6), new Vector2Int(4, 5),
                        new Vector2Int(4, 4), new Vector2Int(4, 3), new Vector2Int(4, 2),
                        new Vector2Int(4, 1), new Vector2Int(5, 1),
                        new Vector2Int(5, 0),
                        new Vector2Int(8, 6),
                        new Vector2Int(8, 5), new Vector2Int(8, 4), new Vector2Int(9, 4),
                        new Vector2Int(10, 4), new Vector2Int(11, 4), new Vector2Int(12, 4),
                        new Vector2Int(12, 5), new Vector2Int(12, 6)
                    }
                },
                new PathData
                {
                    spawnPointIndex = 2,
                    exitPointIndex = 0,
                    waypoints = new[]
                    {
                        new Vector2Int(0, 5), new Vector2Int(1, 5), new Vector2Int(2, 5),
                        new Vector2Int(3, 5), new Vector2Int(4, 5), new Vector2Int(5, 5),
                        new Vector2Int(6, 5), new Vector2Int(7, 5), new Vector2Int(8, 5),
                        new Vector2Int(9, 5), new Vector2Int(10, 5), new Vector2Int(11, 5),
                        new Vector2Int(12, 5), new Vector2Int(12, 6)
                    }
                },
                new PathData
                {
                    spawnPointIndex = 3,
                    exitPointIndex = 0,
                    waypoints = new[]
                    {
                        new Vector2Int(0, 4), new Vector2Int(1, 4), new Vector2Int(2, 4),
                        new Vector2Int(3, 4), new Vector2Int(4, 4), new Vector2Int(5, 4),
                        new Vector2Int(6, 4), new Vector2Int(7, 4), new Vector2Int(8, 4),
                        new Vector2Int(9, 4), new Vector2Int(10, 4), new Vector2Int(11, 4),
                        new Vector2Int(12, 4), new Vector2Int(12, 5), new Vector2Int(12, 6)
                    }
                },
                new PathData
                {
                    spawnPointIndex = 4,
                    exitPointIndex = 0,
                    waypoints = new[]
                    {
                        new Vector2Int(0, 1), new Vector2Int(1, 1),
                        new Vector2Int(3, 1), new Vector2Int(3, 2), new Vector2Int(3, 3),
                        new Vector2Int(3, 4), new Vector2Int(3, 5), new Vector2Int(4, 5),
                        new Vector2Int(5, 5), new Vector2Int(6, 5), new Vector2Int(7, 5),
                        new Vector2Int(8, 5), new Vector2Int(9, 5), new Vector2Int(10, 5),
                        new Vector2Int(11, 5), new Vector2Int(12, 5), new Vector2Int(12, 6)
                    }
                },
                new PathData
                {
                    spawnPointIndex = 5,
                    exitPointIndex = 0,
                    waypoints = new[]
                    {
                        new Vector2Int(12, 1), new Vector2Int(11, 1), new Vector2Int(10, 1),
                        new Vector2Int(9, 1), new Vector2Int(8, 1), new Vector2Int(7, 1),
                        new Vector2Int(6, 1), new Vector2Int(5, 1), new Vector2Int(4, 1),
                        new Vector2Int(4, 2), new Vector2Int(4, 3), new Vector2Int(4, 4),
                        new Vector2Int(5, 4), new Vector2Int(6, 4), new Vector2Int(7, 4),
                        new Vector2Int(8, 4), new Vector2Int(9, 4), new Vector2Int(10, 4),
                        new Vector2Int(11, 4), new Vector2Int(12, 4), new Vector2Int(12, 5),
                        new Vector2Int(12, 6)
                    }
                }
            };

            var grunt = AssetDatabase.LoadAssetAtPath<EnemyData>($"{EnemyDataFolder}/Enemy_Grunt_Sludge.asset");
            var rusher = AssetDatabase.LoadAssetAtPath<EnemyData>($"{EnemyDataFolder}/Enemy_Rusher_Toxic.asset");
            var tank = AssetDatabase.LoadAssetAtPath<EnemyData>($"{EnemyDataFolder}/Enemy_Tank_Pollution.asset");

            if (ShouldGenerateWaves(stage))
            {
                stage.wavesEasy = BuildStage5Waves(10, 0, grunt, rusher, tank, stage.spawnPoints.Length);
                stage.wavesNormal = BuildStage5Waves(20, 1, grunt, rusher, tank, stage.spawnPoints.Length);
                stage.wavesHard = BuildStage5Waves(30, 2, grunt, rusher, tank, stage.spawnPoints.Length);
            }

            EditorUtility.SetDirty(stage);
            AssetDatabase.SaveAssets();

            Debug.Log("<color=green>TRASH TD: Stage 5 (Boulevard) successfully generated!</color>");
        }

        private static WaveData[] BuildStage5Waves(int targetWaveCount, int difficultyTier,
            EnemyData grunt, EnemyData rusher, EnemyData tank, int spawnCount)
        {
            var waves = new List<WaveData>();

            for (int w = 1; w <= targetWaveCount; w++)
            {
                int waveNumber = w;
                int threat = waveNumber + difficultyTier * 2;

                float preWaveDelay = Mathf.Max(1.5f, 4.0f - difficultyTier * 0.5f - waveNumber * 0.05f);
                float spawnInterval = Mathf.Max(0.7f, 2.2f - difficultyTier * 0.2f - waveNumber * 0.035f);

                var entries = new List<WaveEntry>();
                bool isTankWave = threat >= 8 && waveNumber % 5 == 0 && tank != null;
                bool isRusherWave = !isTankWave && threat >= 4 && waveNumber % 3 == 0 && rusher != null;

                int primaryLane = (waveNumber - 1) % spawnCount;
                int secondaryLane = (primaryLane + 2) % spawnCount;
                int holeLane = 5;

                if (isTankWave)
                {
                    entries.Add(new WaveEntry
                    {
                        enemyData = tank,
                        count = 1,
                        spawnInterval = 0f,
                        startDelay = 0f,
                        spawnPointIndex = primaryLane
                    });

                    int escortCount = 1 + threat / 4;
                    if (escortCount > 0 && grunt != null)
                    {
                        entries.Add(new WaveEntry
                        {
                            enemyData = grunt,
                            count = escortCount,
                            spawnInterval = spawnInterval,
                            startDelay = 1.5f,
                            spawnPointIndex = secondaryLane
                        });
                    }

                    if (rusher != null)
                    {
                        entries.Add(new WaveEntry
                        {
                            enemyData = rusher,
                            count = 1 + difficultyTier,
                            spawnInterval = 1.0f,
                            startDelay = 2.5f,
                            spawnPointIndex = holeLane
                        });
                    }
                }
                else if (isRusherWave)
                {
                    int rushCount = 2 + threat / 3;
                    entries.Add(new WaveEntry
                    {
                        enemyData = rusher,
                        count = rushCount,
                        spawnInterval = Mathf.Max(0.6f, spawnInterval * 0.6f),
                        startDelay = 0f,
                        spawnPointIndex = primaryLane
                    });

                    entries.Add(new WaveEntry
                    {
                        enemyData = rusher,
                        count = Mathf.Max(1, rushCount / 2),
                        spawnInterval = 0.8f,
                        startDelay = 1.2f,
                        spawnPointIndex = holeLane
                    });
                }
                else
                {
                    int gruntCount = 2 + threat / 2;
                    entries.Add(new WaveEntry
                    {
                        enemyData = grunt != null ? grunt : rusher,
                        count = gruntCount,
                        spawnInterval = spawnInterval,
                        startDelay = 0f,
                        spawnPointIndex = primaryLane
                    });

                    if (waveNumber >= 2)
                    {
                        entries.Add(new WaveEntry
                        {
                            enemyData = grunt != null ? grunt : rusher,
                            count = Mathf.Max(1, gruntCount - 1),
                            spawnInterval = spawnInterval,
                            startDelay = 1.2f,
                            spawnPointIndex = (primaryLane == holeLane) ? secondaryLane : holeLane
                        });
                    }
                }

                waves.Add(new WaveData
                {
                    waveName = $"Wave {waveNumber}",
                    preWaveDelay = preWaveDelay,
                    entries = entries.ToArray()
                });
            }

            return waves.ToArray();
        }

        [MenuItem("TRASH TD/Generate Stage 6 (Meltdown)")]
        public static void GenerateStage6()
        {
            string path = $"{StageDataFolder}/STAGE_06.asset";
            var stage = AssetDatabase.LoadAssetAtPath<StageData>(path);
            if (stage == null)
            {
                stage = ScriptableObject.CreateInstance<StageData>();
                AssetDatabase.CreateAsset(stage, path);
            }

            stage.stageId = "STAGE_06";
            stage.mapName = "Meltdown";
            stage.shortDescription = "Hold the line across the meltdown while avoiding the toxic hazard.";
            stage.gridWidth = 12;
            stage.gridHeight = 5;

            var levelSprites = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/level 6 complete.png");
            if (levelSprites == null || levelSprites.Length == 0)
                levelSprites = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/Level 6 complete.png");

            if (levelSprites != null)
            {
                foreach (var obj in levelSprites)
                {
                    if (obj is Sprite s)
                    {
                        stage.mapVisualSprite = s;
                        break;
                    }
                }
            }
            stage.visualTilePixelSize = 32;
            stage.visualTileOffset = new Vector2(0, -1);

            var fgSprites = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/level 6 bottom.png");
            if (fgSprites == null || fgSprites.Length == 0)
                fgSprites = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Levels/Level 6 bottom.png");

            if (fgSprites != null)
            {
                foreach (var obj in fgSprites)
                {
                    if (obj is Sprite s)
                    {
                        stage.foregroundVisualSprite = s;
                        break;
                    }
                }
            }
            stage.backgroundVisualSprite = null;
            stage.upperBackgroundVisualSprites = null;
            stage.squadSizeLimit = 8;
            stage.lifePointsEasy = 3;
            stage.lifePointsNormal = 3;
            stage.lifePointsHard = 1;

            string[] layoutRows =
            {
                "BHHHLLLLLLLS",
                "BHTTTHHHBHBB",
                "ELTBTLLLLLLS",
                "BHTTTHBHHHBB",
                "BHHHLLLLLLLS"
            };

            stage.tileLayout = new TileType[stage.gridWidth * stage.gridHeight];
            var spawnPoints = new List<Vector2Int>();
            var exitPoints = new List<Vector2Int>();
            for (int row = 0; row < layoutRows.Length; row++)
            {
                for (int x = 0; x < stage.gridWidth; x++)
                {
                    int y = layoutRows.Length - 1 - row;
                    int index = y * stage.gridWidth + x;
                    switch (layoutRows[row][x])
                    {
                        case 'H':
                            stage.tileLayout[index] = TileType.HighGround;
                            break;
                        case 'S':
                            stage.tileLayout[index] = TileType.SpawnPoint;
                            spawnPoints.Add(new Vector2Int(x, y));
                            break;
                        case 'E':
                            stage.tileLayout[index] = TileType.ExitPoint;
                            exitPoints.Add(new Vector2Int(x, y));
                            break;
                        case 'T':
                            stage.tileLayout[index] = TileType.Trap;
                            break;
                        case 'B':
                            stage.tileLayout[index] = TileType.Blocked;
                            break;
                        default:
                            stage.tileLayout[index] = TileType.LowGround;
                            break;
                    }
                }
            }

            stage.spawnPoints = new[]
            {
                new Vector2Int(11, 4),
                new Vector2Int(11, 2),
                new Vector2Int(11, 0)
            };
            stage.exitPoints = new[]
            {
                new Vector2Int(0, 2)
            };

            stage.enemyPaths = new[]
            {
                new PathData
                {
                    spawnPointIndex = 0,
                    exitPointIndex = 0,
                    waypoints = new[]
                    {
                        new Vector2Int(11, 4), new Vector2Int(10, 4), new Vector2Int(9, 4),
                        new Vector2Int(8, 4), new Vector2Int(7, 4), new Vector2Int(6, 4),
                        new Vector2Int(5, 4), new Vector2Int(4, 4), new Vector2Int(4, 3),
                        new Vector2Int(4, 2), new Vector2Int(4, 1), new Vector2Int(3, 1),
                        new Vector2Int(2, 1), new Vector2Int(2, 2), new Vector2Int(1, 2),
                        new Vector2Int(0, 2)
                    }
                },
                new PathData
                {
                    spawnPointIndex = 1,
                    exitPointIndex = 0,
                    waypoints = new[]
                    {
                        new Vector2Int(11, 2), new Vector2Int(10, 2), new Vector2Int(9, 2),
                        new Vector2Int(8, 2), new Vector2Int(7, 2), new Vector2Int(6, 2),
                        new Vector2Int(5, 2), new Vector2Int(4, 2), new Vector2Int(4, 3),
                        new Vector2Int(3, 3), new Vector2Int(2, 3), new Vector2Int(2, 2),
                        new Vector2Int(1, 2), new Vector2Int(0, 2)
                    }
                },
                new PathData
                {
                    spawnPointIndex = 2,
                    exitPointIndex = 0,
                    waypoints = new[]
                    {
                        new Vector2Int(11, 0), new Vector2Int(10, 0), new Vector2Int(9, 0),
                        new Vector2Int(8, 0), new Vector2Int(7, 0), new Vector2Int(6, 0),
                        new Vector2Int(5, 0), new Vector2Int(4, 0), new Vector2Int(4, 1),
                        new Vector2Int(4, 2), new Vector2Int(4, 3), new Vector2Int(3, 3),
                        new Vector2Int(2, 3), new Vector2Int(2, 2), new Vector2Int(1, 2),
                        new Vector2Int(0, 2)
                    }
                }
            };

            var grunt = AssetDatabase.LoadAssetAtPath<EnemyData>($"{EnemyDataFolder}/Enemy_Grunt_Sludge.asset");
            var rusher = AssetDatabase.LoadAssetAtPath<EnemyData>($"{EnemyDataFolder}/Enemy_Rusher_Toxic.asset");
            var tank = AssetDatabase.LoadAssetAtPath<EnemyData>($"{EnemyDataFolder}/Enemy_Tank_Pollution.asset");

            var stage1 = AssetDatabase.LoadAssetAtPath<StageData>($"{StageDataFolder}/STAGE_01.asset");
            bool generateWaves = ShouldGenerateWaves(stage);
            if (generateWaves && stage1 != null && stage1.wavesEasy != null && stage1.wavesEasy.Length > 0)
            {
                stage.wavesEasy = DistributeWavesToLanes(stage1.wavesEasy);
                stage.wavesNormal = DistributeWavesToLanes(stage1.wavesNormal);
                stage.wavesHard = DistributeWavesToLanes(stage1.wavesHard);
            }
            else if (generateWaves)
            {
                var baseEasy = new[]
                {
                    new WaveData
                    {
                        waveName = "Wave 1: Scouts",
                        preWaveDelay = 2.0f,
                        entries = new[] { new WaveEntry { enemyData = grunt, count = 3, spawnInterval = 3.0f } }
                    }
                };
                var baseNormal = new[]
                {
                    new WaveData
                    {
                        waveName = "Wave 1: Sludge Scouts",
                        preWaveDelay = 2.0f,
                        entries = new[] { new WaveEntry { enemyData = grunt, count = 3, spawnInterval = 2.5f } }
                    },
                    new WaveData
                    {
                        waveName = "Wave 2: Toxic Surge",
                        preWaveDelay = 4.0f,
                        entries = new[]
                        {
                            new WaveEntry { enemyData = grunt, count = 4, spawnInterval = 2.0f },
                            new WaveEntry { enemyData = rusher, count = 2, spawnInterval = 1.5f, startDelay = 3f }
                        }
                    }
                };
                var baseHard = new[]
                {
                    new WaveData
                    {
                        waveName = "Wave 1: Advance Swarm",
                        preWaveDelay = 1.5f,
                        entries = new[]
                        {
                            new WaveEntry { enemyData = grunt, count = 5, spawnInterval = 1.8f },
                            new WaveEntry { enemyData = rusher, count = 3, spawnInterval = 1.2f, startDelay = 2f }
                        }
                    },
                    new WaveData
                    {
                        waveName = "Wave 2: Heavy Incursion",
                        preWaveDelay = 3.0f,
                        entries = new[]
                        {
                            new WaveEntry { enemyData = tank, count = 1, spawnInterval = 0f },
                            new WaveEntry { enemyData = rusher, count = 4, spawnInterval = 1.2f, startDelay = 2f }
                        }
                    }
                };

                stage.wavesEasy = DistributeWavesToLanes(ExtendWaveCampaign(baseEasy, 10, 0, grunt, rusher, tank));
                stage.wavesNormal = DistributeWavesToLanes(ExtendWaveCampaign(baseNormal, 20, 1, grunt, rusher, tank));
                stage.wavesHard = DistributeWavesToLanes(ExtendWaveCampaign(baseHard, 30, 2, grunt, rusher, tank));
            }

            EditorUtility.SetDirty(stage);
            AssetDatabase.SaveAssets();

            Debug.Log("<color=green>TRASH TD: Stage 6 (Meltdown) successfully generated!</color>");
        }

        private static WaveData[] DistributeWavesToLanes(WaveData[] sourceWaves)
        {
            if (sourceWaves == null) return new WaveData[0];
            var result = new WaveData[sourceWaves.Length];
            for (int i = 0; i < sourceWaves.Length; i++)
            {
                var src = sourceWaves[i];
                var entries = new List<WaveEntry>();
                if (src.entries != null)
                {
                    foreach (var srcEntry in src.entries)
                    {
                        if (srcEntry == null || srcEntry.enemyData == null) continue;
                        int distributedCount = Mathf.Max(1, Mathf.CeilToInt(srcEntry.count * 0.35f));
                        for (int lane = 0; lane < 3; lane++)
                        {
                            entries.Add(new WaveEntry
                            {
                                enemyData = srcEntry.enemyData,
                                count = distributedCount,
                                spawnInterval = srcEntry.spawnInterval,
                                startDelay = srcEntry.startDelay + lane * 0.4f,
                                spawnPointIndex = lane
                            });
                        }
                    }
                }
                result[i] = new WaveData
                {
                    waveName = src.waveName,
                    preWaveDelay = src.preWaveDelay,
                    entries = entries.ToArray()
                };
            }
            return result;
        }

        private static WaveData[] BuildStage3Waves(int difficultyTier, EnemyData grunt, EnemyData rusher, EnemyData tank)
        {
            int multiplier = difficultyTier + 1;
            return new[]
            {
                new WaveData
                {
                    waveName = "Wave 1: Crossing Patrol",
                    preWaveDelay = 2f,
                    entries = new[]
                    {
                        new WaveEntry { enemyData = grunt, count = 2 * multiplier, spawnInterval = 2f, spawnPointIndex = 0 }
                    }
                },
                new WaveData
                {
                    waveName = "Wave 2: Toxic Rush",
                    preWaveDelay = 3f,
                    entries = new[]
                    {
                        new WaveEntry { enemyData = rusher != null ? rusher : grunt, count = multiplier, spawnInterval = 1.5f, spawnPointIndex = 1 },
                        new WaveEntry { enemyData = grunt, count = 2 * multiplier, spawnInterval = 2f, startDelay = 1f, spawnPointIndex = 0 }
                    }
                },
                new WaveData
                {
                    waveName = "Wave 3: Heavy Crossing",
                    preWaveDelay = 3f,
                    entries = new[]
                    {
                        new WaveEntry { enemyData = tank != null ? tank : grunt, count = 1, spawnInterval = 0f, spawnPointIndex = 0 },
                        new WaveEntry { enemyData = grunt, count = 2 * multiplier, spawnInterval = 1.5f, startDelay = 1f, spawnPointIndex = 1 }
                    }
                }
            };
        }

        private static WaveData[] BuildStage2Waves(int targetWaveCount, int difficultyTier,
            EnemyData grunt, EnemyData rusher, EnemyData tank)
        {
            var waves = new List<WaveData>();

            for (int w = 1; w <= targetWaveCount; w++)
            {
                int waveNumber = w;
                int threat = waveNumber + difficultyTier * 2;

                float preWaveDelay = Mathf.Max(1.5f, 4.0f - difficultyTier * 0.5f - waveNumber * 0.05f);
                float spawnInterval = Mathf.Max(0.7f, 2.2f - difficultyTier * 0.2f - waveNumber * 0.035f);

                var entries = new List<WaveEntry>();
                string waveCategory;

                bool isTankWave = threat >= 8 && waveNumber % 5 == 0 && tank != null;
                bool isRusherWave = !isTankWave && threat >= 4 && waveNumber % 3 == 0 && rusher != null;

                // Alternate primary lane between spawn 0 (Top-Right) and spawn 1 (Bottom-Left)
                int primaryLane = (waveNumber % 2 == 1) ? 0 : 1;
                int secondaryLane = (primaryLane == 0) ? 1 : 0;

                float primaryDelay = 0f;
                float secondaryDelay = (waveNumber == 1) ? 2.0f : 1.5f;

                if (isTankWave)
                {
                    bool doubleTank = threat >= 16;
                    waveCategory = doubleTank ? "Heavy Dual Incursion" : "Heavy Pincer Incursion";

                    if (!doubleTank)
                    {
                        entries.Add(new WaveEntry
                        {
                            enemyData = tank,
                            count = 1,
                            spawnInterval = 0f,
                            startDelay = primaryDelay,
                            spawnPointIndex = primaryLane
                        });

                        int escortCount = 1 + threat / 4;
                        if (escortCount > 0 && grunt != null)
                        {
                            entries.Add(new WaveEntry
                            {
                                enemyData = grunt,
                                count = escortCount,
                                spawnInterval = spawnInterval,
                                startDelay = primaryDelay + 1.2f,
                                spawnPointIndex = primaryLane
                            });
                        }

                        EnemyData flankEnemy = (rusher != null) ? rusher : grunt;
                        int flankCount = 2 + threat / 3;
                        entries.Add(new WaveEntry
                        {
                            enemyData = flankEnemy,
                            count = flankCount,
                            spawnInterval = spawnInterval * 0.85f,
                            startDelay = secondaryDelay,
                            spawnPointIndex = secondaryLane
                        });
                    }
                    else
                    {
                        entries.Add(new WaveEntry
                        {
                            enemyData = tank,
                            count = 1,
                            spawnInterval = 0f,
                            startDelay = primaryDelay,
                            spawnPointIndex = primaryLane
                        });
                        entries.Add(new WaveEntry
                        {
                            enemyData = tank,
                            count = 1,
                            spawnInterval = 0f,
                            startDelay = secondaryDelay,
                            spawnPointIndex = secondaryLane
                        });

                        int supportCount = 2 + threat / 4;
                        if (grunt != null)
                        {
                            entries.Add(new WaveEntry
                            {
                                enemyData = grunt,
                                count = supportCount,
                                spawnInterval = spawnInterval,
                                startDelay = primaryDelay + 1.2f,
                                spawnPointIndex = primaryLane
                            });
                        }
                        if (rusher != null)
                        {
                            entries.Add(new WaveEntry
                            {
                                enemyData = rusher,
                                count = supportCount,
                                spawnInterval = spawnInterval * 0.8f,
                                startDelay = secondaryDelay + 1.2f,
                                spawnPointIndex = secondaryLane
                            });
                        }
                    }
                }
                else if (isRusherWave)
                {
                    waveCategory = "Converging Toxic Surge";
                    int rusherPrimary = 2 + threat / 4;
                    int rusherSecondary = 1 + threat / 4;

                    entries.Add(new WaveEntry
                    {
                        enemyData = rusher,
                        count = rusherPrimary,
                        spawnInterval = spawnInterval * 0.8f,
                        startDelay = primaryDelay,
                        spawnPointIndex = primaryLane
                    });
                    entries.Add(new WaveEntry
                    {
                        enemyData = rusher,
                        count = rusherSecondary,
                        spawnInterval = spawnInterval * 0.8f,
                        startDelay = secondaryDelay,
                        spawnPointIndex = secondaryLane
                    });

                    if (threat >= 6 && grunt != null)
                    {
                        entries.Add(new WaveEntry
                        {
                            enemyData = grunt,
                            count = 2 + threat / 5,
                            spawnInterval = spawnInterval,
                            startDelay = primaryDelay + 1.8f,
                            spawnPointIndex = primaryLane
                        });
                    }
                }
                else
                {
                    waveCategory = (waveNumber == 1) ? "Converging Scouts" : "Crossfire Swarm";
                    int countPrimary = (waveNumber == 1) ? 2 : (2 + threat / 4);
                    int countSecondary = (waveNumber == 1) ? 2 : (1 + threat / 4);

                    entries.Add(new WaveEntry
                    {
                        enemyData = grunt != null ? grunt : rusher,
                        count = countPrimary,
                        spawnInterval = spawnInterval,
                        startDelay = primaryDelay,
                        spawnPointIndex = primaryLane
                    });
                    entries.Add(new WaveEntry
                    {
                        enemyData = grunt != null ? grunt : rusher,
                        count = countSecondary,
                        spawnInterval = spawnInterval,
                        startDelay = secondaryDelay,
                        spawnPointIndex = secondaryLane
                    });

                    if (threat >= 7 && rusher != null)
                    {
                        entries.Add(new WaveEntry
                        {
                            enemyData = rusher,
                            count = 1 + threat / 6,
                            spawnInterval = spawnInterval * 0.85f,
                            startDelay = secondaryDelay + 1.5f,
                            spawnPointIndex = secondaryLane
                        });
                    }
                }

                waves.Add(new WaveData
                {
                    waveName = $"Wave {waveNumber}: {waveCategory}",
                    preWaveDelay = preWaveDelay,
                    entries = entries.ToArray()
                });
            }

            return waves.ToArray();
        }

        private static WaveData[] ExtendWaveCampaign(WaveData[] openingWaves, int targetWaveCount, int difficultyTier,
            EnemyData grunt, EnemyData rusher, EnemyData tank)
        {
            var waves = new List<WaveData>(openingWaves ?? new WaveData[0]);

            while (waves.Count < targetWaveCount)
            {
                int waveNumber = waves.Count + 1;
                int threat = waveNumber + difficultyTier * 2;
                EnemyData waveEnemy = threat >= 9 && waveNumber % 5 == 0 && tank != null
                    ? tank
                    : threat >= 5 && waveNumber % 3 == 0 && rusher != null
                        ? rusher
                        : grunt;
                int enemyCount = 2 + threat / 3;
                string enemyGroup = waveEnemy == tank ? "Heavy Incursion" : waveEnemy == rusher ? "Toxic Surge" : "Debris Swarm";

                waves.Add(new WaveData
                {
                    waveName = $"Wave {waveNumber}: {enemyGroup}",
                    preWaveDelay = Mathf.Max(1f, 4f - difficultyTier * 0.75f - waveNumber * 0.08f),
                    entries = new[]
                    {
                        new WaveEntry
                        {
                            enemyData = waveEnemy,
                            count = enemyCount,
                            spawnInterval = Mathf.Max(0.7f, 2.4f - difficultyTier * 0.25f - waveNumber * 0.04f),
                            startDelay = 0f,
                            spawnPointIndex = 0
                        }
                    }
                });
            }

            return waves.ToArray();
        }
    }
}
