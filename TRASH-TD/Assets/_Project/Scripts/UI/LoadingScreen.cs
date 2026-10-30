using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TrashTD.Data;

namespace TrashTD.UI
{
    /// <summary>
    /// Stage loading screen, styled as a squad uplink: the stage's map art drifts behind a dark tint inside
    /// viewfinder corners, with the stage code, name, difficulty and 3★ goal, a segmented progress bar driven
    /// by the async scene load, a terminal status line, and Coalesce with a tip.
    /// It survives the scene change (DontDestroyOnLoad) and fades out once the new scene has started, so the
    /// stage appears from behind it instead of cutting in. The bar fills over at least <see cref="MinShowTime"/>
    /// so the stage name can be read even when loading is instant.
    /// </summary>
    public sealed class LoadingScreen : MonoBehaviour
    {
        private const int CanvasSortingOrder = 32000; // Above every game canvas (CrtDisplay's own canvas is skipped)
        private const float FadeInTime = 0.3f;
        private const float FadeOutTime = 0.45f;
        private const float MinShowTime = 1.6f;
        private const float LockedHoldTime = 0.25f;
        private const float LeftMargin = 150f;
        private const float PlateSize = 300f;
        private const float PlateRightMargin = 150f;
        // The bar, status line and tip stop this far from the right edge, clear of Coalesce's plate
        private const float ProgressRightInset = PlateRightMargin + PlateSize + 40f;
        private const float BarHeight = 18f;
        private const int BarSegments = 59;
        private const string TutorialScriptPath = "Tutorial/TutorialScript";

        private static readonly Color Background = new Color(0.012f, 0.016f, 0.024f, 1f);
        private static readonly Color MapTint = new Color(0.55f, 0.80f, 0.65f, 0.30f);
        private static readonly Color Shade = new Color(0.01f, 0.02f, 0.015f, 0.55f);
        private static readonly Color Accent = new Color(0.18f, 0.92f, 0.50f, 1f);
        private static readonly Color AccentDim = new Color(0.22f, 0.91f, 0.50f, 0.45f);
        private static readonly Color TitleColor = new Color(0.32f, 1.00f, 0.64f, 1f);
        private static readonly Color TerminalText = new Color(0.45f, 0.78f, 0.56f, 1f);
        private static readonly Color TextDim = new Color(0.50f, 0.58f, 0.68f, 1f);
        private static readonly Color Plate = new Color(0.010f, 0.030f, 0.020f, 0.85f);
        private static readonly Color TrackColor = new Color(0.020f, 0.050f, 0.034f, 1f);
        private static readonly Color RecRed = new Color(1f, 0.27f, 0.22f, 1f);

        private static readonly string[] StatusLines =
        {
            "ESTABLISHING UPLINK",
            "SCANNING TERRAIN",
            "PLOTTING ENEMY ROUTES",
            "PRIMING RECLAIMERS",
            "SYNCING SQUAD DECK"
        };

        private static readonly string[] Tips =
        {
            "Press P before a wave to preview the enemy route.",
            "Most ranged operators deploy on High Ground. Melee operators hold Low Ground.",
            "After placing an operator, hold and drag from it to choose which way it faces.",
            "Placed someone on the wrong tile? Recall them and deploy again.",
            "Three copies of a card in your deck merge into a higher rarity. Recall one from the field to merge it.",
            "During a card draft, press M to minimize it and look at the map and your deck.",
            "Number keys 1-8 deploy that deck slot onto the tile under the cursor.",
            "Rerolls are limited. Save them for drafts that really need one.",
            "Taunting enemies pull your operators' fire. Plan around them.",
            "Clearing a stage on Hard opens Hard on the next one."
        };

        private static LoadingScreen active;

        private CanvasGroup group;
        private RectTransform mapArt;
        private Vector2 mapArtSize;
        private RectTransform barFill;
        private Text percentText;
        private Text statusText;
        private Image recDot;
        private RectTransform scanLine;
        private Image coalesceImage;
        private Sprite[] coalesceFrames;
        private float coalesceFrameTime = 0.25f;
        private float progress;
        private bool locked;

        public static bool IsLoading => active != null;

        /// <summary>Shows the loading screen and loads <paramref name="sceneName"/> behind it.</summary>
        public static void LoadStage(string sceneName, StageData stage, string stageTag, StageDifficulty difficulty, Color difficultyColor)
        {
            if (active != null) return;

            var go = new GameObject("LoadingScreen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(go);
            active = go.AddComponent<LoadingScreen>();
            active.Build(stage, stageTag, difficulty, difficultyColor);
            active.StartCoroutine(active.LoadRoutine(sceneName));
        }

        private void OnDestroy()
        {
            if (active == this) active = null;
        }

        // ─────────────────────────────────────────────────────
        //  Load flow
        // ─────────────────────────────────────────────────────

        private IEnumerator LoadRoutine(string sceneName)
        {
            yield return Fade(0f, 1f, FadeInTime);

            AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
            if (load == null)
            {
                Destroy(gameObject);
                yield break;
            }
            load.allowSceneActivation = false;

            // Unity reports 0-0.9 until activation is allowed. The bar follows the slower of the real load
            // and the minimum show time, easing so it never jumps.
            float shown = 0f;
            while (progress < 0.999f)
            {
                shown += Time.unscaledDeltaTime;
                float target = Mathf.Min(Mathf.Clamp01(load.progress / 0.9f), shown / MinShowTime);
                progress = Mathf.MoveTowards(progress, target, Time.unscaledDeltaTime * 1.5f);
                yield return null;
            }

            progress = 1f;
            locked = true;
            yield return new WaitForSecondsRealtime(LockedHoldTime);

            load.allowSceneActivation = true;
            while (!load.isDone) yield return null;

            // Let the new scene's Start build the stage before revealing it
            yield return null;
            yield return null;

            yield return Fade(1f, 0f, FadeOutTime);
            Destroy(gameObject);
        }

        private IEnumerator Fade(float from, float to, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                group.alpha = Mathf.Lerp(from, to, k * k * (3f - 2f * k));
                yield return null;
            }
            group.alpha = to;
        }

        private void Update()
        {
            float time = Time.unscaledTime;

            // Slow drift over the map art, like a camera panning the area
            if (mapArt != null)
            {
                float zoom = 1.08f + 0.03f * Mathf.Sin(time * 0.15f);
                mapArt.sizeDelta = mapArtSize * zoom;
                mapArt.anchoredPosition = new Vector2(Mathf.Sin(time * 0.11f) * 40f, Mathf.Cos(time * 0.09f) * 18f);
            }

            if (scanLine != null)
            {
                float y = Mathf.Repeat(time * 260f, 1300f) - 650f;
                scanLine.anchoredPosition = new Vector2(0f, -y);
            }

            if (recDot != null) recDot.enabled = Mathf.Repeat(time, 1f) < 0.6f;

            if (barFill != null) barFill.anchorMax = new Vector2(progress, 1f);
            if (percentText != null) percentText.text = Mathf.FloorToInt(progress * 100f).ToString("000") + "%";

            if (statusText != null)
            {
                bool cursor = Mathf.Repeat(time, 0.8f) < 0.45f;
                string line = locked
                    ? "SIGNAL LOCKED // DEPLOYING"
                    : StatusLines[Mathf.Min(StatusLines.Length - 1, Mathf.FloorToInt(progress * StatusLines.Length))] + "...";
                statusText.text = "> " + line + (cursor ? " _" : "");
                statusText.color = locked ? Accent : TerminalText;
            }

            if (coalesceImage != null && coalesceFrames != null && coalesceFrames.Length > 0)
            {
                int frame = Mathf.FloorToInt(time / coalesceFrameTime) % coalesceFrames.Length;
                coalesceImage.sprite = coalesceFrames[frame];
                coalesceImage.rectTransform.anchoredPosition = new Vector2(0f, Mathf.Round(Mathf.Sin(time * 2.2f) * 4f));
            }
        }

        // ─────────────────────────────────────────────────────
        //  Layout (1920 x 1080 reference)
        // ─────────────────────────────────────────────────────

        private void Build(StageData stage, string stageTag, StageDifficulty difficulty, Color difficultyColor)
        {
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform root = CreateRect("Root", transform);
            Stretch(root);
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = true; // Nothing underneath can be clicked while loading

            Image background = CreateImage(root, "Background", Background);
            Stretch(background.rectTransform);
            background.raycastTarget = true;

            BuildMapArt(root, stage);

            Image shade = CreateImage(root, "Shade", Shade);
            Stretch(shade.rectTransform);

            Image scan = CreateImage(root, "ScanLine", new Color(Accent.r, Accent.g, Accent.b, 0.05f));
            scanLine = scan.rectTransform;
            scanLine.anchorMin = new Vector2(0f, 0.5f);
            scanLine.anchorMax = new Vector2(1f, 0.5f);
            scanLine.sizeDelta = new Vector2(0f, 6f);

            BuildCorners(root);
            BuildHeader(root, stageTag);
            BuildStageInfo(root, stage, stageTag, difficulty, difficultyColor);
            BuildProgress(root);
            BuildCoalesce(root);
        }

        /// <summary>The stage's map art filling the screen (cover fit), tinted and darkened.</summary>
        private void BuildMapArt(RectTransform root, StageData stage)
        {
            Sprite art = stage != null ? stage.mapVisualSprite : null;
            if (art == null) return;

            Image image = CreateImage(root, "MapArt", MapTint);
            image.sprite = art;
            mapArt = image.rectTransform;
            mapArt.anchorMin = mapArt.anchorMax = new Vector2(0.5f, 0.5f);

            float aspect = art.rect.width / Mathf.Max(1f, art.rect.height);
            mapArtSize = aspect > 1920f / 1080f
                ? new Vector2(1080f * aspect, 1080f)
                : new Vector2(1920f, 1920f / aspect);
            mapArt.sizeDelta = mapArtSize;
        }

        /// <summary>Viewfinder brackets in the four corners.</summary>
        private static void BuildCorners(RectTransform root)
        {
            const float inset = 40f;
            const float length = 70f;
            const float thickness = 4f;

            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2(i % 2, i / 2);
                var direction = new Vector2(corner.x > 0.5f ? -1f : 1f, corner.y > 0.5f ? -1f : 1f);

                Image horizontal = CreateImage(root, "CornerH" + i, AccentDim);
                Place(horizontal.rectTransform, corner, corner, new Vector2(inset * direction.x, inset * direction.y), new Vector2(length, thickness));
                horizontal.rectTransform.pivot = new Vector2(corner.x, corner.y);

                Image vertical = CreateImage(root, "CornerV" + i, AccentDim);
                Place(vertical.rectTransform, corner, corner, new Vector2(inset * direction.x, inset * direction.y), new Vector2(thickness, length));
                vertical.rectTransform.pivot = new Vector2(corner.x, corner.y);
            }
        }

        private void BuildHeader(RectTransform root, string stageTag)
        {
            recDot = CreateImage(root, "RecDot", RecRed);
            Place(recDot.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(84f, -78f), new Vector2(14f, 14f));

            Text rec = CreateText(root, "RecLabel", "REC   UPLINK // SQUAD DEPLOYMENT", 20, TextAnchor.MiddleLeft, TerminalText);
            Place(rec.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(110f, -78f), new Vector2(900f, 30f));
            rec.rectTransform.pivot = new Vector2(0f, 0.5f);

            Text tag = CreateText(root, "HeaderTag", stageTag + " FEED", 20, TextAnchor.MiddleRight, TerminalText);
            Place(tag.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-84f, -78f), new Vector2(600f, 30f));
            tag.rectTransform.pivot = new Vector2(1f, 0.5f);
        }

        private static void BuildStageInfo(RectTransform root, StageData stage, string stageTag, StageDifficulty difficulty, Color difficultyColor)
        {
            string stageName = stage != null && !string.IsNullOrEmpty(stage.mapName) ? stage.mapName : "UNKNOWN SECTOR";

            RectTransform block = CreateRect("StageInfo", root);
            Place(block, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(LeftMargin, 60f), new Vector2(1100f, 380f));
            block.pivot = new Vector2(0f, 0.5f);

            Text entering = CreateText(block, "Entering", "NOW ENTERING  //  " + stageTag, 22, TextAnchor.UpperLeft, TerminalText);
            PlaceTop(entering.rectTransform, 0f, 32f);

            Image rule = CreateImage(block, "Rule", AccentDim);
            Place(rule.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -44f), new Vector2(460f, 3f));
            rule.rectTransform.pivot = new Vector2(0f, 1f);

            Text title = CreateText(block, "Title", stageName.ToUpperInvariant(), 64, TextAnchor.UpperLeft, TitleColor);
            title.horizontalOverflow = HorizontalWrapMode.Wrap;
            title.verticalOverflow = VerticalWrapMode.Overflow;
            PlaceTop(title.rectTransform, 62f, 150f);
            var glow = title.gameObject.AddComponent<Shadow>();
            glow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            glow.effectDistance = new Vector2(4f, -4f);

            // Difficulty chip
            RectTransform chip = CreateRect("DifficultyChip", block);
            Place(chip, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -226f), new Vector2(190f, 40f));
            chip.pivot = new Vector2(0f, 1f);
            Image chipBack = CreateImage(chip, "Back", new Color(difficultyColor.r * 0.18f, difficultyColor.g * 0.18f, difficultyColor.b * 0.18f, 0.95f));
            Stretch(chipBack.rectTransform);
            var chipOutline = chipBack.gameObject.AddComponent<Outline>();
            chipOutline.effectColor = difficultyColor;
            chipOutline.effectDistance = new Vector2(2f, -2f);
            Text chipText = CreateText(chip, "Label", "◆ " + difficulty.ToString().ToUpperInvariant(), 22, TextAnchor.MiddleCenter, difficultyColor);
            Stretch(chipText.rectTransform);

            string description = stage != null ? stage.shortDescription : string.Empty;
            if (!string.IsNullOrEmpty(description))
            {
                Text desc = CreateText(block, "Description", description, 22, TextAnchor.UpperLeft, TextDim);
                desc.horizontalOverflow = HorizontalWrapMode.Wrap;
                PlaceTop(desc.rectTransform, 286f, 60f);
            }

            string goal = stage != null ? stage.threeStarCondition : string.Empty;
            if (!string.IsNullOrEmpty(goal))
            {
                Text goalText = CreateText(block, "Goal", "★★★  " + goal.ToUpperInvariant(), 18, TextAnchor.UpperLeft, new Color(0.98f, 0.80f, 0.18f, 0.9f));
                PlaceTop(goalText.rectTransform, 350f, 30f);
            }
        }

        /// <summary>
        /// Status line and percentage over a segmented bar, with the tip underneath. All three stretch with the
        /// screen width but stop short of Coalesce's plate, and the tip wraps onto a second line instead.
        /// </summary>
        private void BuildProgress(RectTransform root)
        {
            RectTransform bar = CreateRect("Progress", root);
            PlaceBottomRow(bar, 150f, 150f + BarHeight, ProgressRightInset);

            Image track = CreateImage(bar, "Track", TrackColor);
            Stretch(track.rectTransform);
            var trackOutline = track.gameObject.AddComponent<Outline>();
            trackOutline.effectColor = AccentDim;
            trackOutline.effectDistance = new Vector2(2f, -2f);

            Image fill = CreateImage(bar, "Fill", Accent);
            barFill = fill.rectTransform;
            Stretch(barFill);
            barFill.anchorMax = new Vector2(0f, 1f);

            // Dark gaps cut the fill into pixel segments
            for (int i = 1; i < BarSegments; i++)
            {
                Image gap = CreateImage(bar, "Gap", TrackColor);
                float x = (float)i / BarSegments;
                gap.rectTransform.anchorMin = new Vector2(x, 0f);
                gap.rectTransform.anchorMax = new Vector2(x, 1f);
                gap.rectTransform.sizeDelta = new Vector2(4f, 0f);
            }

            statusText = CreateText(root, "Status", "> ", 22, TextAnchor.MiddleLeft, TerminalText);
            PlaceBottomRow(statusText.rectTransform, 180f, 212f, ProgressRightInset + 160f);

            percentText = CreateText(root, "Percent", "000%", 28, TextAnchor.MiddleRight, Accent);
            Place(percentText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-ProgressRightInset, 196f), new Vector2(160f, 36f));
            percentText.rectTransform.pivot = new Vector2(1f, 0.5f);

            Text tip = CreateText(root, "Tip", "TIP  //  " + Tips[Random.Range(0, Tips.Length)], 20, TextAnchor.UpperLeft, TextDim);
            tip.horizontalOverflow = HorizontalWrapMode.Wrap;
            PlaceBottomRow(tip.rectTransform, 64f, 124f, ProgressRightInset);
        }

        /// <summary>From <see cref="LeftMargin"/> to <paramref name="rightInset"/> from the right edge, between two heights from the bottom.</summary>
        private static void PlaceBottomRow(RectTransform rect, float bottom, float top, float rightInset)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(LeftMargin, bottom);
            rect.offsetMax = new Vector2(-rightInset, top);
        }

        /// <summary>Coalesce, the squad's guide, idling on a plate in the bottom-right corner.</summary>
        private void BuildCoalesce(RectTransform root)
        {
            var script = Resources.Load<TutorialScript>(TutorialScriptPath);
            if (script == null || script.speaker == null) return;

            coalesceFrames = UnitSpriteFrames.GetOperatorFrames(script.speaker, out coalesceFrameTime);
            coalesceFrameTime = Mathf.Max(0.06f, coalesceFrameTime);
            if (coalesceFrames == null || coalesceFrames.Length == 0) return;

            RectTransform plate = CreateRect("CoalescePlate", root);
            Place(plate, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-PlateRightMargin, 100f), new Vector2(PlateSize, PlateSize));
            plate.pivot = new Vector2(1f, 0f);

            Image back = CreateImage(plate, "Back", Plate);
            Stretch(back.rectTransform);
            var outline = back.gameObject.AddComponent<Outline>();
            outline.effectColor = AccentDim;
            outline.effectDistance = new Vector2(2f, -2f);

            RectTransform rig = CreateRect("Rig", plate);
            Place(rig, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 14f), new Vector2(224f, 224f));

            coalesceImage = CreateImage(rig, "Coalesce", Color.white);
            coalesceImage.sprite = coalesceFrames[0];
            coalesceImage.preserveAspect = true;
            Stretch(coalesceImage.rectTransform);

            // Shrinks to fit inside the plate rather than running past its edges
            Text speakerLabel = CreateText(plate, "Name", script.speakerName + " // ONLINE", 16, TextAnchor.MiddleCenter, Accent);
            speakerLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            speakerLabel.verticalOverflow = VerticalWrapMode.Truncate;
            speakerLabel.resizeTextForBestFit = true;
            speakerLabel.resizeTextMinSize = 10;
            speakerLabel.resizeTextMaxSize = 16;
            RectTransform labelRect = speakerLabel.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.offsetMin = new Vector2(14f, 10f);
            labelRect.offsetMax = new Vector2(-14f, 36f);
        }

        // ─────────────────────────────────────────────────────
        //  UI helpers
        // ─────────────────────────────────────────────────────

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>A plain coloured block. Images without a sprite draw solid colour, which is intended here.</summary>
        private static Image CreateImage(Transform parent, string name, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(Transform parent, string name, string value, int size, TextAnchor alignment, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = UIFontHelper.GetPixelFont();
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        /// <summary>Full width of the parent, <paramref name="top"/> px down from its top edge.</summary>
        private static void PlaceTop(RectTransform rect, float top, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -top);
            rect.sizeDelta = new Vector2(0f, height);
        }
    }
}
