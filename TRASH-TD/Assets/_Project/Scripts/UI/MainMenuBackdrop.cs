using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TrashTD.Data;
using TrashTD.Enemies;

namespace TrashTD.UI
{
    /// <summary>
    /// Title-screen backdrop, built in code by MainMenuController. Slowly pans across each stage's map art,
    /// cross-fading from stage to stage, while the stages' enemies parade along a hazard-striped floor.
    /// Only uses art referenced by the stage assets in Resources/Stages, so it works in builds. Unscaled time.
    /// </summary>
    public class MainMenuBackdrop : MonoBehaviour
    {
        // Stage slides
        private const float SlideDuration = 11f;
        private const float CrossfadeDuration = 1.6f;
        private const float PanMargin = 160f;     // extra art beyond the screen so there is room to pan
        private const float MaxPanTravel = 220f;  // canvas units travelled over one slide
        private const float SheetFrameDuration = 0.12f; // matches StageMapPreview
        private static readonly Color MapTint = new Color(0.40f, 0.46f, 0.42f, 1f);
        private static readonly Color ShadeColor = new Color(0.010f, 0.025f, 0.018f, 1f);

        // Enemy parade: kept low so it never reaches the menu column
        private const float FloorHeight = 16f;
        private const float ParadeScale = 3f;
        private const float MaxParadeHeight = 108f;
        private const float ParadeAirHeight = 64f;
        private const int MaxWalkers = 5;

        private sealed class Slide
        {
            public RectTransform root;
            public CanvasGroup group;
            public readonly List<Image> layers = new List<Image>();
            public Image animatedLayer;
            public Sprite[] animatedFrames;
            public Vector2 artSize;
            public Vector2 from; // pan endpoints as fractions of the overflow, -1..1 per axis
            public Vector2 to;
            public float age;
        }

        private sealed class ParadeEnemy
        {
            public Sprite[] frames;
            public float frameRate;
            public float speed;
            public bool flying;
        }

        private sealed class Walker
        {
            public RectTransform rt;
            public Image image;
            public RectTransform shadow;
            public Image shadowImage;
            public ParadeEnemy enemy;
            public float x;
            public float age;
            public float phase;
        }

        private readonly List<(StageData stage, int number)> stages = new List<(StageData, int)>();
        private readonly List<ParadeEnemy> paradeEnemies = new List<ParadeEnemy>();
        private readonly List<Walker> walkers = new List<Walker>();
        private readonly Slide[] slides = new Slide[2];
        private RectTransform rect;
        private RectTransform slidesRoot;
        private RectTransform paradeLayer;
        private Text caption;
        private int currentSlide;
        private int stageCursor;
        private float slideTime;
        private float spawnTimer = 1f;

        private static Sprite horizontalFadeSprite;
        private static Sprite verticalFadeSprite;
        private static readonly Dictionary<string, Sprite> WholeTextureSprites = new Dictionary<string, Sprite>();

        /// <summary>
        /// Builds the layers and starts the show. <paramref name="stageList"/> is in stage order (index i = Stage i+1);
        /// entries without map art are skipped. <paramref name="captionText"/> (optional) shows the current stage.
        /// </summary>
        public void Initialize(IList<StageData> stageList, Text captionText)
        {
            rect = (RectTransform)transform;
            caption = captionText;
            BuildLayers();

            var seen = new HashSet<EnemyData>();
            for (int i = 0; i < stageList.Count; i++)
            {
                StageData stage = stageList[i];
                if (stage == null) continue;
                if (stage.mapVisualSprite != null) stages.Add((stage, i + 1));
                CollectEnemies(stage, seen);
            }

            if (stages.Count > 0)
            {
                stageCursor = Random.Range(0, stages.Count);
                ShowSlide(stages[stageCursor], true);
            }
            else if (caption != null)
            {
                caption.text = string.Empty;
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            UpdateSlides(dt);
            UpdateParade(dt);
        }

        // ─────────────────────────────────────────────────────
        //  Layers
        // ─────────────────────────────────────────────────────

        private void BuildLayers()
        {
            slidesRoot = CreateRect("Slides", transform);
            StretchFill(slidesRoot);
            for (int i = 0; i < slides.Length; i++) slides[i] = CreateSlide(i);

            // Darken the art so the menu reads on top; heavier on the left (menu column) and bottom (parade)
            Image shade = CreateImage("Shade", transform, new Color(ShadeColor.r, ShadeColor.g, ShadeColor.b, 0.55f));
            StretchFill(shade.rectTransform);

            Image leftFade = CreateImage("LeftFade", transform, new Color(ShadeColor.r, ShadeColor.g, ShadeColor.b, 0.94f));
            leftFade.sprite = GetFadeSprite(true);
            leftFade.rectTransform.anchorMin = Vector2.zero;
            leftFade.rectTransform.anchorMax = new Vector2(0.72f, 1f);
            leftFade.rectTransform.offsetMin = Vector2.zero;
            leftFade.rectTransform.offsetMax = Vector2.zero;

            Image bottomFade = CreateImage("BottomFade", transform, new Color(ShadeColor.r, ShadeColor.g, ShadeColor.b, 0.85f));
            bottomFade.sprite = GetFadeSprite(false);
            bottomFade.rectTransform.anchorMin = Vector2.zero;
            bottomFade.rectTransform.anchorMax = new Vector2(1f, 0f);
            bottomFade.rectTransform.pivot = new Vector2(0.5f, 0f);
            bottomFade.rectTransform.offsetMin = Vector2.zero;
            bottomFade.rectTransform.offsetMax = new Vector2(0f, 260f);

            paradeLayer = CreateRect("Parade", transform);
            StretchFill(paradeLayer);

            // Hazard-striped floor the parade walks on
            Image floor = CreateImage("Floor", transform, new Color(1f, 1f, 1f, 0.85f));
            Sprite stripe = LoadWholeTextureSprite("UI/decor/hazard_strip");
            if (stripe != null)
            {
                floor.sprite = stripe;
                floor.type = Image.Type.Tiled;
            }
            else
            {
                floor.color = new Color(0.95f, 0.75f, 0.15f, 0.85f);
            }
            floor.rectTransform.anchorMin = Vector2.zero;
            floor.rectTransform.anchorMax = new Vector2(1f, 0f);
            floor.rectTransform.pivot = new Vector2(0.5f, 0f);
            floor.rectTransform.offsetMin = Vector2.zero;
            floor.rectTransform.offsetMax = new Vector2(0f, FloorHeight);

            Image floorEdge = CreateImage("FloorEdge", transform, new Color(0.01f, 0.015f, 0.012f, 1f));
            floorEdge.rectTransform.anchorMin = new Vector2(0f, 0f);
            floorEdge.rectTransform.anchorMax = new Vector2(1f, 0f);
            floorEdge.rectTransform.pivot = new Vector2(0.5f, 0f);
            floorEdge.rectTransform.offsetMin = new Vector2(0f, FloorHeight);
            floorEdge.rectTransform.offsetMax = new Vector2(0f, FloorHeight + 3f);
        }

        private Slide CreateSlide(int index)
        {
            RectTransform root = CreateRect("Slide" + index, slidesRoot);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);

            var slide = new Slide { root = root, group = root.gameObject.AddComponent<CanvasGroup>() };
            slide.group.blocksRaycasts = false;
            slide.group.interactable = false;

            root.gameObject.SetActive(false);
            return slide;
        }

        // ─────────────────────────────────────────────────────
        //  Stage slides
        // ─────────────────────────────────────────────────────

        private void ShowSlide((StageData stage, int number) entry, bool instant)
        {
            currentSlide = 1 - currentSlide;
            Slide slide = slides[currentSlide];
            StageData stage = entry.stage;
            Sprite map = stage.mapVisualSprite;

            // Same layers, in gameplay draw order, as StageMapPreview. Stage 3's ground has transparent gaps
            // that its animated background sheet and upper background fill.
            int used = 0;
            slide.animatedLayer = null;
            slide.animatedFrames = null;
            if (stage.backgroundVisualSprite != null)
            {
                slide.animatedFrames = StageMapPreview.GetSheetFrames(stage.backgroundVisualSprite, map);
                slide.animatedLayer = SetLayer(slide, used++,
                    slide.animatedFrames != null ? slide.animatedFrames[0] : stage.backgroundVisualSprite);
                if (slide.animatedFrames == null) slide.animatedLayer = null;
            }
            SetLayer(slide, used++, map);
            if (stage.upperBackgroundVisualSprites != null)
            {
                foreach (Sprite upper in stage.upperBackgroundVisualSprites)
                {
                    if (upper != null) SetLayer(slide, used++, upper);
                }
            }
            if (stage.foregroundVisualSprite != null) SetLayer(slide, used++, stage.foregroundVisualSprite);
            for (int i = used; i < slide.layers.Count; i++) slide.layers[i].gameObject.SetActive(false);

            // Pick a pan of about MaxPanTravel at the current screen size, stored as fractions of the overflow
            // so LayoutSlide can re-fit it to whatever size the screen has each frame
            slide.artSize = map.rect.size;
            Vector2 range = GetPanRange(slide.artSize, ViewSize());
            Vector2 from = new Vector2(Random.Range(-range.x, range.x), Random.Range(-range.y, range.y));
            Vector2 direction = Random.insideUnitCircle.normalized;
            if (direction == Vector2.zero) direction = Vector2.right;
            Vector2 to = ClampToRange(from + direction * MaxPanTravel, range);
            if ((to - from).sqrMagnitude < MaxPanTravel * MaxPanTravel * 0.25f)
            {
                to = ClampToRange(from - direction * MaxPanTravel, range);
            }
            slide.from = new Vector2(from.x / range.x, from.y / range.y);
            slide.to = new Vector2(to.x / range.x, to.y / range.y);

            slide.age = 0f;
            slide.root.SetAsLastSibling();
            slide.root.gameObject.SetActive(true);
            slide.group.alpha = instant ? 1f : 0f;
            slideTime = instant ? CrossfadeDuration : 0f;
            LayoutSlide(slide, ViewSize());

            if (caption != null)
            {
                string mapName = string.IsNullOrEmpty(stage.mapName) ? "UNKNOWN" : stage.mapName.ToUpperInvariant();
                caption.text = $"CAM {entry.number:00}  //  {mapName}";
            }
        }

        private Image SetLayer(Slide slide, int index, Sprite sprite)
        {
            while (slide.layers.Count <= index)
            {
                Image layer = CreateImage("Layer" + slide.layers.Count, slide.root, MapTint);
                StretchFill(layer.rectTransform);
                slide.layers.Add(layer);
            }

            Image image = slide.layers[index];
            UsePointFilter(sprite);
            image.sprite = sprite;
            image.gameObject.SetActive(true);
            return image;
        }

        private void UpdateSlides(float dt)
        {
            if (stages.Count == 0) return;

            slideTime += dt;
            Slide current = slides[currentSlide];
            Slide previous = slides[1 - currentSlide];

            current.group.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(slideTime / CrossfadeDuration));
            if (current.group.alpha >= 1f && previous.root.gameObject.activeSelf)
            {
                previous.root.gameObject.SetActive(false);
            }

            Vector2 view = ViewSize();
            foreach (Slide slide in slides)
            {
                if (!slide.root.gameObject.activeSelf) continue;

                slide.age += dt;
                LayoutSlide(slide, view);

                if (slide.animatedLayer != null)
                {
                    int frame = Mathf.FloorToInt(slide.age / SheetFrameDuration) % slide.animatedFrames.Length;
                    slide.animatedLayer.sprite = slide.animatedFrames[frame];
                }
            }

            if (slideTime >= SlideDuration && stages.Count > 1)
            {
                stageCursor = (stageCursor + 1) % stages.Count;
                ShowSlide(stages[stageCursor], false);
            }
        }

        /// <summary>
        /// Sizes the slide to cover the current screen with room to pan and places it along its pan.
        /// Done every frame, so the art never leaves a screen edge uncovered, whatever the resolution.
        /// </summary>
        private static void LayoutSlide(Slide slide, Vector2 view)
        {
            Vector2 size = GetSlideSize(slide.artSize, view);
            Vector2 range = (size - view) * 0.5f;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(slide.age / (SlideDuration + CrossfadeDuration)));
            slide.root.sizeDelta = size;
            slide.root.anchoredPosition = Vector2.Scale(Vector2.Lerp(slide.from, slide.to, k), range);
        }

        /// <summary>Whole-number scale of the art that covers the screen plus PanMargin on each axis.</summary>
        private static Vector2 GetSlideSize(Vector2 art, Vector2 view)
        {
            float scale = Mathf.Ceil(Mathf.Max((view.x + PanMargin) / art.x, (view.y + PanMargin) / art.y));
            return art * scale;
        }

        private static Vector2 GetPanRange(Vector2 art, Vector2 view)
        {
            return (GetSlideSize(art, view) - view) * 0.5f;
        }

        private static Vector2 ClampToRange(Vector2 point, Vector2 range)
        {
            return new Vector2(Mathf.Clamp(point.x, -range.x, range.x), Mathf.Clamp(point.y, -range.y, range.y));
        }

        // ─────────────────────────────────────────────────────
        //  Enemy parade
        // ─────────────────────────────────────────────────────

        private void CollectEnemies(StageData stage, HashSet<EnemyData> seen)
        {
            foreach (WaveData[] waves in new[] { stage.wavesEasy, stage.wavesNormal, stage.wavesHard })
            {
                if (waves == null) continue;
                foreach (WaveData wave in waves)
                {
                    if (wave?.entries == null) continue;
                    foreach (WaveEntry entry in wave.entries)
                    {
                        EnemyData data = entry?.enemyData;
                        if (data == null || !seen.Add(data)) continue;
                        if (!TryGetEnemyAnimation(data, out Sprite[] frames, out float frameRate)) continue;

                        foreach (Sprite frame in frames) UsePointFilter(frame);
                        paradeEnemies.Add(new ParadeEnemy
                        {
                            frames = frames,
                            frameRate = frameRate,
                            speed = Mathf.Clamp(50f + data.moveSpeed * 40f, 45f, 170f),
                            flying = data.movementType == EnemyMovementType.Air
                        });
                    }
                }
            }
        }

        /// <summary>
        /// The enemy's walk cycle as gameplay plays it: <see cref="EnemyData.animationFrames"/>, otherwise the
        /// prefab's sheet animation (only when the prefab's sprite isn't overridden, as in EnemyManager).
        /// Enemies without at least two frames (e.g. placeholder art) are left out of the parade.
        /// </summary>
        private static bool TryGetEnemyAnimation(EnemyData data, out Sprite[] frames, out float frameRate)
        {
            frames = CollectFrames(data.animationFrames);
            frameRate = data.animationFrameRate > 0f ? data.animationFrameRate : 8f;
            if (frames.Length >= 2) return true;

            if (data.enemyPrefab != null)
            {
                IEnemySpriteFrames prefabAnimation = data.enemyPrefab.GetComponentInChildren<IEnemySpriteFrames>(true);
                SpriteRenderer renderer = data.enemyPrefab.GetComponentInChildren<SpriteRenderer>(true);
                bool spriteOverridden = data.sprite != null && renderer != null && renderer.sprite != data.sprite;
                if (prefabAnimation != null && !spriteOverridden)
                {
                    frames = CollectFrames(prefabAnimation.Frames);
                    frameRate = prefabAnimation.FrameDuration > 0f ? 1f / prefabAnimation.FrameDuration : 4f;
                    if (frames.Length >= 2) return true;
                }
            }

            return false;
        }

        private static Sprite[] CollectFrames(Sprite[] source)
        {
            var frames = new List<Sprite>();
            if (source != null)
            {
                foreach (Sprite frame in source)
                {
                    if (frame != null) frames.Add(frame);
                }
            }
            return frames.ToArray();
        }

        private void UpdateParade(float dt)
        {
            if (paradeEnemies.Count == 0) return;

            Vector2 view = ViewSize();

            spawnTimer -= dt;
            if (spawnTimer <= 0f)
            {
                SpawnPack();
                spawnTimer = Random.Range(3.5f, 6.5f);
            }

            foreach (Walker w in walkers)
            {
                if (!w.rt.gameObject.activeSelf) continue;

                w.age += dt;
                w.x += w.enemy.speed * dt;

                Sprite[] frames = w.enemy.frames;
                w.image.sprite = frames[Mathf.FloorToInt(w.age * w.enemy.frameRate + w.phase) % frames.Length];

                float y = w.enemy.flying
                    ? ParadeAirHeight + Mathf.Sin(w.age * 3f + w.phase) * 6f
                    : FloorHeight + 2f;
                w.rt.anchoredPosition = new Vector2(Mathf.Round(w.x), Mathf.Round(y));
                w.shadow.anchoredPosition = new Vector2(Mathf.Round(w.x), FloorHeight + 3f);

                if (w.x - w.rt.sizeDelta.x * 0.5f > view.x + 20f)
                {
                    w.rt.gameObject.SetActive(false);
                    w.shadow.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>Enemies arrive in small packs of one type, like the waves do.</summary>
        private void SpawnPack()
        {
            ParadeEnemy enemy = paradeEnemies[Random.Range(0, paradeEnemies.Count)];
            Vector2 frameSize = enemy.frames[0].rect.size;
            float scale = Mathf.Max(1f, Mathf.Floor(Mathf.Min(ParadeScale, MaxParadeHeight / frameSize.y)));
            Vector2 size = frameSize * scale;
            int count = Random.Range(1, 3);
            float spacing = size.x * 1.1f + Random.Range(10f, 40f);

            for (int i = 0; i < count; i++)
            {
                Walker w = GetFreeWalker();
                if (w == null) return;

                w.enemy = enemy;
                w.age = 0f;
                w.phase = Random.Range(0f, 10f);
                w.x = -size.x * 0.5f - i * spacing;
                w.image.sprite = enemy.frames[0];
                w.rt.sizeDelta = size;

                // Ground units cast a solid shadow; flyers a smaller, fainter one below them
                w.shadow.sizeDelta = new Vector2(size.x * (enemy.flying ? 0.45f : 0.7f), 6f);
                w.shadowImage.color = new Color(0f, 0f, 0f, enemy.flying ? 0.22f : 0.4f);

                w.rt.gameObject.SetActive(true);
                w.shadow.gameObject.SetActive(true);
                w.rt.anchoredPosition = new Vector2(w.x, -999f);
            }
        }

        private Walker GetFreeWalker()
        {
            int active = 0;
            foreach (Walker w in walkers)
            {
                if (!w.rt.gameObject.activeSelf) return w;
                active++;
            }

            if (active >= MaxWalkers) return null;

            Image shadow = CreateImage("WalkerShadow", paradeLayer, Color.black);
            shadow.rectTransform.anchorMin = shadow.rectTransform.anchorMax = Vector2.zero;
            shadow.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            Image image = CreateImage("Walker", paradeLayer, Color.white);
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = Vector2.zero;
            image.rectTransform.pivot = new Vector2(0.5f, 0f);

            var walker = new Walker { rt = image.rectTransform, image = image, shadow = shadow.rectTransform, shadowImage = shadow };
            walkers.Add(walker);
            return walker;
        }

        // ─────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────

        private Vector2 ViewSize()
        {
            Vector2 size = rect != null ? rect.rect.size : Vector2.zero;
            return size.x < 1f || size.y < 1f ? new Vector2(1920f, 1080f) : size;
        }

        private static void UsePointFilter(Sprite sprite)
        {
            if (sprite != null && sprite.texture != null) sprite.texture.filterMode = FilterMode.Point;
        }

        /// <summary>Soft alpha ramp (opaque at the left / bottom edge), tinted by the Image colour.</summary>
        private static Sprite GetFadeSprite(bool horizontal)
        {
            Sprite cached = horizontal ? horizontalFadeSprite : verticalFadeSprite;
            if (cached != null) return cached;

            const int steps = 64;
            var tex = new Texture2D(horizontal ? steps : 1, horizontal ? 1 : steps, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            for (int i = 0; i < steps; i++)
            {
                float t = i / (steps - 1f);
                var c = new Color(1f, 1f, 1f, Mathf.Pow(1f - t, 1.6f));
                if (horizontal) tex.SetPixel(i, 0, c);
                else tex.SetPixel(0, i, c);
            }
            tex.Apply();

            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            if (horizontal) horizontalFadeSprite = sprite;
            else verticalFadeSprite = sprite;
            return sprite;
        }

        /// <summary>Whole texture as a full-rect sprite, so Tiled images don't fight the importer's slicing.</summary>
        internal static Sprite LoadWholeTextureSprite(string path)
        {
            if (WholeTextureSprites.TryGetValue(path, out Sprite cached) && cached != null) return cached;

            var tex = Resources.Load<Texture2D>(path);
            if (tex == null) return null;
            tex.filterMode = FilterMode.Point;
            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            WholeTextureSprites[path] = sprite;
            return sprite;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void StretchFill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
