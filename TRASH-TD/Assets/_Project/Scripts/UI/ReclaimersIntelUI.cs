using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TrashTD.Audio;
using TrashTD.Data;

namespace TrashTD.UI
{
    /// <summary>
    /// Reclaimers' Intel: an almanac of every operator and enemy, opened from the main menu and the pause menu.
    /// A dossier picker (Operators / Enemies) leads to a gallery: filter tabs and a portrait grid on the left,
    /// and the selected unit's file on the right (animated sprite, chips, field notes, stats by tier, range, ability).
    /// Units come from <see cref="UnitIntelRegistry"/>, which updates itself when unit assets change, and all text
    /// comes from the unit data assets. Built in code in the green terminal style of AudioSettingsUI; runs on
    /// unscaled time so it works while the game is paused. The creating screen styles and places the open button
    /// and forwards Esc to <see cref="HandleBack"/>.
    /// </summary>
    public sealed class ReclaimersIntelUI : MonoBehaviour
    {
        // Terminal palette (matches AudioSettingsUI / MainMenuController)
        private static readonly Color AccentColor = new Color(0.18f, 0.82f, 0.45f, 1f);
        private static readonly Color AccentBright = new Color(0.32f, 1.00f, 0.64f, 1f);
        private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.72f);
        private static readonly Color CardColor = new Color(0.016f, 0.042f, 0.028f, 0.98f);
        private static readonly Color HeaderColor = new Color(0.030f, 0.090f, 0.055f, 1f);
        private static readonly Color TerminalButton = new Color(0.035f, 0.090f, 0.060f, 1f);
        private static readonly Color PanelColor = new Color(0.024f, 0.060f, 0.040f, 1f);
        private static readonly Color StageColor = new Color(0.010f, 0.026f, 0.018f, 1f);
        private static readonly Color LineColor = new Color(0.22f, 0.91f, 0.50f, 0.45f);
        private static readonly Color LabelColor = new Color(0.45f, 0.78f, 0.56f, 1f);
        private static readonly Color HintColor = new Color(0.45f, 0.78f, 0.56f, 0.7f);
        private static readonly Color BodyColor = new Color(0.78f, 0.92f, 0.82f, 1f);
        private static readonly Color SelectedLabel = new Color(0.02f, 0.10f, 0.05f, 1f);

        // Enemy dossier accent: hazard red / amber
        private static readonly Color HostileColor = new Color(0.95f, 0.33f, 0.36f, 1f);
        private static readonly Color HostileBright = new Color(1f, 0.55f, 0.50f, 1f);
        private static readonly Color HostileButton = new Color(0.10f, 0.035f, 0.035f, 1f);
        private static readonly Color HostilePanel = new Color(0.060f, 0.024f, 0.026f, 1f);
        private static readonly Color AmberColor = new Color(1f, 0.75f, 0.25f, 1f);

        // Range tiles (same colours as the draft range preview)
        private static readonly Color RangeSelfColor = new Color(0.15f, 0.85f, 1f, 1f);
        private static readonly Color RangeHitColor = new Color(0.15f, 1f, 0.28f, 1f);
        private static readonly Color RangeTalentColor = new Color(1f, 0.75f, 0.2f, 1f);
        private static readonly Color RangeEmptyColor = new Color(1f, 1f, 1f, 0.22f);
        private static readonly Color EnemySelfColor = new Color(1f, 0.42f, 0.38f, 1f);

        // Layout, in card pixels from the top-left. PressStart2P glyphs are as wide as the font size.
        private const float HeaderHeight = 64f;
        private const float Padding = 36f;
        private const float ChoiceWidth = 1120f;
        private const float ChoiceHeight = 660f;
        private const float OptionWidth = 500f;
        private const float OptionHeight = 452f;
        private const float GalleryWidth = 1760f;
        private const float GalleryHeight = 980f;
        private const float ListX = 32f;
        private const float ListWidth = 640f;
        private const float GridTop = 168f;
        private const int GridColumns = 4;
        private const float TileWidth = 140f;
        private const float TileHeight = 168f;
        private const float TileGap = 16f;
        private const float DetailX = 720f;
        private const float DetailWidth = GalleryWidth - DetailX - 32f;   // 1008
        private const float StageSize = 300f;
        private const float InfoX = DetailX + StageSize + 24f;
        private const float InfoWidth = GalleryWidth - 32f - InfoX;
        private const float DataTop = 420f;
        private const float DataHeight = 272f;
        private const float StatsWidth = 492f;
        private const float RangeX = DetailX + StatsWidth + 24f;
        private const float RangeWidth = DetailWidth - StatsWidth - 24f;
        private const float NotesTop = 708f;
        private const float ShowDuration = 0.18f;

        private static readonly OperatorClass[] ClassOrder =
        {
            OperatorClass.Guard, OperatorClass.Defender, OperatorClass.Sniper,
            OperatorClass.Caster, OperatorClass.Medic, OperatorClass.Specialist
        };

        private static readonly EnemyArchetype[] ArchetypeOrder =
        {
            EnemyArchetype.Grunt, EnemyArchetype.Rusher, EnemyArchetype.Tank,
            EnemyArchetype.Caster, EnemyArchetype.Flyer
        };

        private static readonly string[] DifficultyLabels = { "EASY", "NORMAL", "HARD" };

        private enum Dossier { Operators, Enemies }

        private sealed class IntelEntry
        {
            public OperatorData Operator;
            public EnemyData Enemy;
            public string FileId;
            public Sprite[] Frames;
            public float FrameDuration;
            public string Name => Operator != null ? Operator.operatorName : Enemy != null ? Enemy.enemyName : "UNKNOWN";
        }

        private sealed class StatRow
        {
            public Image Fill;
            public Text Value;
        }

        private sealed class FactBox
        {
            public RectTransform Root;
            public Text Label;
            public Text Value;
        }

        private sealed class TierButton
        {
            public Button Button;
            public Text Label;
            public Image Star;
        }

        public Button IntelButton { get; private set; }
        public bool IsOpen => panel != null && panel.activeSelf;

        private GameObject panel;
        private CanvasGroup panelGroup;
        private RectTransform choiceCard;
        private RectTransform galleryCard;
        private RectTransform activeCard;
        private float showTime = ShowDuration;

        // Choice screen
        private Button operatorsOption;
        private Button enemiesOption;
        private Text operatorsCount;
        private Text enemiesCount;
        private Image operatorsSprite;
        private Image enemiesSprite;
        private int choiceFocus = -1;

        // Gallery
        private Dossier dossier;
        private Text galleryTitle;
        private Image galleryHeaderLine;
        private RectTransform tabRow;
        private Text listCountText;
        private ScrollRect gridScroll;
        private RectTransform gridContent;
        private readonly List<Button> tabButtons = new List<Button>();
        private readonly List<Button> tileButtons = new List<Button>();
        private readonly List<IntelEntry> operatorEntries = new List<IntelEntry>();
        private readonly List<IntelEntry> enemyEntries = new List<IntelEntry>();
        private readonly List<IntelEntry> filtered = new List<IntelEntry>();
        private readonly Dictionary<EnemyData, List<string>> sightings = new Dictionary<EnemyData, List<string>>();
        private int filterIndex;
        private int selectedIndex = -1;
        private bool rosterLoaded;
        private readonly int[] operatorStatMax = new int[4];
        private readonly int[] enemyStatMax = new int[4];

        // Detail pane
        private Image stageBackground;
        private Image stageSprite;
        private Image stageFloor;
        private Text fileText;
        private Text nameText;
        private RectTransform chipRow;
        private RectTransform rarityRow;
        private Text statusText;
        private Text statsHeader;
        private readonly List<TierButton> tierButtons = new List<TierButton>();
        private readonly List<StatRow> statRows = new List<StatRow>();
        private readonly List<FactBox> factBoxes = new List<FactBox>();
        private Text rangeHeader;
        private RectTransform rangeGrid;
        private Text rangeReadout;
        private RectTransform rangeLegend;
        private ScrollRect notesScroll;
        private Text abilityHeader;
        private Text abilityText;
        private Text loreText;
        private Text sightedHeader;
        private Text sightedText;
        private int tierIndex;
        private Sprite[] playingFrames;
        private float playingFrameDuration = 0.25f;
        private float frameTimer;
        private int frameIndex;

        private static Sprite gridSprite;
        private static Sprite tileFrameSprite;
        private static Sprite hazardSprite;

        public static ReclaimersIntelUI Create(
            Transform canvasRoot,
            Transform buttonParent,
            Action<Button> styleButton,
            Action<RectTransform> positionButton)
        {
            if (canvasRoot == null || buttonParent == null) return null;

            GameObject root = new GameObject("ReclaimersIntelUI", typeof(RectTransform));
            root.transform.SetParent(canvasRoot, false);
            var intel = root.AddComponent<ReclaimersIntelUI>();
            intel.Build(buttonParent, styleButton, positionButton);
            return intel;
        }

        // ─────────────────────────────────────────────────────
        //  Open / close / back
        // ─────────────────────────────────────────────────────

        public void Open()
        {
            if (panel == null) return;
            LoadRoster();
            ShowChoice();
            panel.transform.SetAsLastSibling();
            panel.SetActive(true);
            EventSystem.current?.SetSelectedGameObject(null);
        }

        public void Close()
        {
            if (panel != null) panel.SetActive(false);
            playingFrames = null;
        }

        /// <summary>Esc: gallery → dossier picker → closed. Returns false when the panel was already closed.</summary>
        public bool HandleBack()
        {
            if (!IsOpen) return false;
            PlaySfx(SfxId.UiBack);
            if (galleryCard.gameObject.activeSelf) ShowChoice();
            else Close();
            return true;
        }

        private void ShowChoice()
        {
            galleryCard.gameObject.SetActive(false);
            choiceCard.gameObject.SetActive(true);
            playingFrames = null;
            SetChoiceFocus(-1);
            PopIn(choiceCard);
        }

        private void ShowGallery(Dossier which)
        {
            dossier = which;
            choiceCard.gameObject.SetActive(false);
            galleryCard.gameObject.SetActive(true);
            ApplyDossierTheme();
            BuildTabs();
            filterIndex = 0;
            ApplyFilter();
            PopIn(galleryCard);
        }

        private void PopIn(RectTransform card)
        {
            activeCard = card;
            showTime = 0f;
            panelGroup.alpha = 0f;
            card.localScale = Vector3.one * 0.94f;
        }

        private void Update()
        {
            if (!IsOpen) return;
            float dt = Time.unscaledDeltaTime;

            if (activeCard != null && showTime < ShowDuration)
            {
                showTime += dt;
                float k = Mathf.Clamp01(showTime / ShowDuration);
                float eased = 1f - (1f - k) * (1f - k) * (1f - k);
                panelGroup.alpha = eased;
                activeCard.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, eased);
            }

            AnimateDetailSprite(dt);
            HandleKeyboard();
        }

        private void HandleKeyboard()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (choiceCard.gameObject.activeSelf)
            {
                if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame) SetChoiceFocus(0);
                else if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame) SetChoiceFocus(1);
                else if (choiceFocus >= 0 && (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame))
                {
                    PlaySfx(SfxId.UiConfirm);
                    ShowGallery(choiceFocus == 0 ? Dossier.Operators : Dossier.Enemies);
                }
                return;
            }

            if (filtered.Count == 0) return;
            int step = 0;
            if (keyboard.leftArrowKey.wasPressedThisFrame) step = -1;
            else if (keyboard.rightArrowKey.wasPressedThisFrame) step = 1;
            else if (keyboard.upArrowKey.wasPressedThisFrame) step = -GridColumns;
            else if (keyboard.downArrowKey.wasPressedThisFrame) step = GridColumns;
            if (step == 0) return;

            int next = Mathf.Clamp((selectedIndex < 0 ? 0 : selectedIndex) + step, 0, filtered.Count - 1);
            if (next != selectedIndex)
            {
                PlaySfx(SfxId.UiHover);
                SelectEntry(next);
                ScrollToTile(next);
            }
        }

        // ─────────────────────────────────────────────────────
        //  Roster
        // ─────────────────────────────────────────────────────

        private void LoadRoster()
        {
            if (rosterLoaded) return;
            rosterLoaded = true;

            var operators = new List<OperatorData>();
            foreach (OperatorData op in UnitIntelRegistry.GetOperators())
            {
                if (op != null) operators.Add(op);
            }
            operators.Sort((a, b) =>
            {
                int result = Array.IndexOf(ClassOrder, a.operatorClass).CompareTo(Array.IndexOf(ClassOrder, b.operatorClass));
                if (result != 0) return result;
                result = ((int)a.baseRarity).CompareTo((int)b.baseRarity);
                return result != 0 ? result : string.Compare(a.operatorName, b.operatorName, StringComparison.OrdinalIgnoreCase);
            });

            var enemies = new List<EnemyData>();
            foreach (EnemyData enemy in UnitIntelRegistry.GetEnemies())
            {
                if (enemy != null) enemies.Add(enemy);
            }
            enemies.Sort((a, b) =>
            {
                int result = Array.IndexOf(ArchetypeOrder, a.archetype).CompareTo(Array.IndexOf(ArchetypeOrder, b.archetype));
                if (result != 0) return result;
                result = a.baseHP.CompareTo(b.baseHP);
                return result != 0 ? result : string.Compare(a.enemyName, b.enemyName, StringComparison.OrdinalIgnoreCase);
            });

            for (int i = 0; i < operators.Count; i++)
            {
                OperatorData op = operators[i];
                Sprite[] frames = UnitSpriteFrames.GetOperatorFrames(op, out float duration);
                operatorEntries.Add(new IntelEntry { Operator = op, FileId = "OP-" + (i + 1).ToString("00"), Frames = frames, FrameDuration = duration });

                OperatorRarity top = TopRarity(op);
                operatorStatMax[0] = Mathf.Max(operatorStatMax[0], op.GetScaledHP(top));
                operatorStatMax[1] = Mathf.Max(operatorStatMax[1], op.GetScaledATK(top));
                operatorStatMax[2] = Mathf.Max(operatorStatMax[2], op.GetScaledDEF(top));
                operatorStatMax[3] = Mathf.Max(operatorStatMax[3], op.GetScaledRES(top));
            }

            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyData enemy = enemies[i];
                Sprite[] frames = UnitSpriteFrames.GetEnemyFrames(enemy, out float frameRate);
                enemyEntries.Add(new IntelEntry { Enemy = enemy, FileId = "EN-" + (i + 1).ToString("00"), Frames = frames, FrameDuration = 1f / Mathf.Max(0.01f, frameRate) });

                enemyStatMax[0] = Mathf.Max(enemyStatMax[0], enemy.GetScaledHP(DifficultyLabels.Length));
                enemyStatMax[1] = Mathf.Max(enemyStatMax[1], enemy.GetScaledATK(DifficultyLabels.Length));
                enemyStatMax[2] = Mathf.Max(enemyStatMax[2], enemy.GetScaledDEF(DifficultyLabels.Length));
                enemyStatMax[3] = Mathf.Max(enemyStatMax[3], enemy.baseRES);
            }

            foreach (IntelEntry entry in operatorEntries) UsePointFilter(entry.Frames);
            foreach (IntelEntry entry in enemyEntries) UsePointFilter(entry.Frames);

            CollectSightings();

            operatorsCount.text = operatorEntries.Count + " RECLAIMERS ON FILE";
            enemiesCount.text = enemyEntries.Count + " HOSTILES CATALOGUED";
            SetPixelSprite(operatorsSprite, FindCover(operatorEntries, "Coalesce"), 256f);
            SetPixelSprite(enemiesSprite, FindCover(enemyEntries, "Toxic Rusher"), 256f);
        }

        /// <summary>Stages whose waves (any difficulty) include each enemy, from the stage assets in Resources.</summary>
        private void CollectSightings()
        {
            foreach (StageData stage in Resources.LoadAll<StageData>("Stages"))
            {
                if (stage == null || (stage.stageId != null && stage.stageId.ToUpperInvariant().Contains("SANDBOX"))) continue;
                if (stage.stageId == TrashTD.Core.GameLoop.TutorialDirector.TutorialStageId) continue; // Training drill, not a sighting
                string stageName = string.IsNullOrEmpty(stage.mapName) ? stage.stageId : stage.mapName;

                foreach (WaveData[] waves in new[] { stage.wavesEasy, stage.wavesNormal, stage.wavesHard })
                {
                    if (waves == null) continue;
                    foreach (WaveData wave in waves)
                    {
                        if (wave?.entries == null) continue;
                        foreach (WaveEntry waveEntry in wave.entries)
                        {
                            EnemyData enemy = waveEntry?.enemyData;
                            if (enemy == null) continue;
                            if (!sightings.TryGetValue(enemy, out List<string> stages))
                            {
                                stages = new List<string>();
                                sightings[enemy] = stages;
                            }
                            if (!stages.Contains(stageName)) stages.Add(stageName);
                        }
                    }
                }
            }
        }

        private static Sprite FindCover(List<IntelEntry> entries, string unitName)
        {
            foreach (IntelEntry entry in entries)
            {
                if (string.Equals(entry.Name, unitName, StringComparison.OrdinalIgnoreCase) && entry.Frames.Length > 0)
                    return entry.Frames[0];
            }
            return entries.Count > 0 && entries[0].Frames.Length > 0 ? entries[0].Frames[0] : null;
        }

        private static OperatorRarity TopRarity(OperatorData op)
        {
            // Operators upgrade up to two tiers above their base rarity (see OperatorUpgradeBadge).
            return (OperatorRarity)Mathf.Min((int)op.baseRarity + 2, (int)OperatorRarity.Star5);
        }

        // ─────────────────────────────────────────────────────
        //  Build
        // ─────────────────────────────────────────────────────

        private void Build(Transform buttonParent, Action<Button> styleButton, Action<RectTransform> positionButton)
        {
            IntelButton = CreateTerminalButton(buttonParent, "ReclaimersIntelButton", "INTEL", 16);
            styleButton?.Invoke(IntelButton);
            positionButton?.Invoke(IntelButton.GetComponent<RectTransform>());
            IntelButton.onClick.AddListener(Open);

            Transform canvasRoot = transform.parent;
            panel = new GameObject("ReclaimersIntelPanel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            panel.transform.SetParent(canvasRoot, false);
            SetStretch(panel.GetComponent<RectTransform>());
            panel.GetComponent<Image>().color = DimColor;
            panelGroup = panel.GetComponent<CanvasGroup>();

            BuildChoiceCard();
            BuildGalleryCard();
            panel.SetActive(false);
        }

        private RectTransform CreateCard(string name, float width, float height, string title, out Text titleText, out Image headerLine)
        {
            Image cardImage = CreateImage(panel.transform, name, CardColor);
            cardImage.raycastTarget = true;
            RectTransform card = cardImage.rectTransform;
            SetCentered(card, width, height);
            Outline outline = cardImage.gameObject.AddComponent<Outline>();
            outline.effectColor = LineColor;
            outline.effectDistance = new Vector2(2f, -2f);
            AddCornerBrackets(card, 26f, 3f, AccentBright, -7f);

            Image header = CreateImage(card, "Header", HeaderColor);
            PlaceTopLeft(header.rectTransform, 0f, 0f, width, HeaderHeight);
            headerLine = CreateImage(card, "HeaderLine", LineColor);
            PlaceTopLeft(headerLine.rectTransform, 0f, HeaderHeight, width, 2f);

            titleText = CreateText(header.transform, title, 20, TextAnchor.MiddleLeft, AccentBright);
            PlaceTopLeft(titleText.rectTransform, Padding - 8f, 0f, width * 0.6f, HeaderHeight);
            return card;
        }

        private void BuildChoiceCard()
        {
            choiceCard = CreateCard("IntelChoiceCard", ChoiceWidth, ChoiceHeight, "RECLAIMERS' INTEL", out _, out _);

            Button closeButton = CreateHeaderButton(choiceCard, "CloseButton", "CLOSE", ChoiceWidth - Padding - 140f, 140f);
            closeButton.onClick.AddListener(Close);

            Text subtitle = CreateText(choiceCard, "// SELECT A DOSSIER", 12, TextAnchor.MiddleLeft, AccentColor);
            PlaceTopLeft(subtitle.rectTransform, Padding, 84f, 300f, 24f);
            Image rule = CreateImage(choiceCard, "SubtitleRule", new Color(LineColor.r, LineColor.g, LineColor.b, 0.3f));
            PlaceTopLeft(rule.rectTransform, Padding + 260f, 95f, ChoiceWidth - Padding * 2f - 260f, 2f);

            float gap = ChoiceWidth - Padding * 2f - OptionWidth * 2f;
            operatorsOption = CreateDossierOption("OperatorsOption", Padding, "DOSSIER 01", "OPERATORS",
                AccentColor, AccentBright, PanelColor, false, out operatorsSprite, out operatorsCount);
            enemiesOption = CreateDossierOption("EnemiesOption", Padding + OptionWidth + gap, "DOSSIER 02", "ENEMIES",
                HostileColor, HostileBright, HostilePanel, true, out enemiesSprite, out enemiesCount);
            operatorsOption.onClick.AddListener(() => ShowGallery(Dossier.Operators));
            enemiesOption.onClick.AddListener(() => ShowGallery(Dossier.Enemies));
            operatorsOption.GetComponent<MenuButtonFeedback>().Hovered += () => SetChoiceFocus(0, false);
            enemiesOption.GetComponent<MenuButtonFeedback>().Hovered += () => SetChoiceFocus(1, false);

            Text hint = CreateText(choiceCard, "[ARROWS] SELECT    [ENTER] OPEN    [ESC] CLOSE", 10, TextAnchor.MiddleCenter, HintColor);
            PlaceTopLeft(hint.rectTransform, 0f, ChoiceHeight - 44f, ChoiceWidth, 24f);
        }

        private Button CreateDossierOption(string objectName, float x, string tag, string title, Color accent, Color bright,
            Color fill, bool hostile, out Image coverSprite, out Text countText)
        {
            Button option = CreateButtonObject(choiceCard, objectName, fill);
            RectTransform rect = option.GetComponent<RectTransform>();
            PlaceTopLeft(rect, x, 124f, OptionWidth, OptionHeight);
            Outline frame = option.gameObject.AddComponent<Outline>();
            frame.effectColor = new Color(accent.r, accent.g, accent.b, 0.55f);
            frame.effectDistance = new Vector2(2f, -2f);
            AddCornerBrackets(rect, 20f, 3f, bright, -6f);

            Text tagText = CreateText(rect, tag, 10, TextAnchor.MiddleLeft, new Color(accent.r, accent.g, accent.b, 0.85f));
            PlaceTopLeft(tagText.rectTransform, 20f, 14f, 200f, 20f);
            Text classified = CreateText(rect, hostile ? "THREAT FILE" : "PERSONNEL FILE", 10, TextAnchor.MiddleRight, new Color(accent.r, accent.g, accent.b, 0.6f));
            PlaceTopLeft(classified.rectTransform, OptionWidth - 220f, 14f, 200f, 20f);

            // Sprite on a gridded stage, still (no animation on the picker)
            float stageSize = 300f;
            CreateSpriteStage(rect, (OptionWidth - stageSize) * 0.5f, 44f, stageSize, accent, hostile, out _, out coverSprite, out _);

            Text titleText = CreateText(rect, title, 28, TextAnchor.MiddleCenter, bright);
            PlaceTopLeft(titleText.rectTransform, 0f, 356f, OptionWidth, 40f);
            countText = CreateText(rect, string.Empty, 11, TextAnchor.MiddleCenter, new Color(accent.r, accent.g, accent.b, 0.85f));
            PlaceTopLeft(countText.rectTransform, 0f, 400f, OptionWidth, 20f);

            if (hostile)
            {
                Image stripe = CreateHazardStripe(rect, "HazardStripe");
                PlaceTopLeft(stripe.rectTransform, 0f, OptionHeight - 14f, OptionWidth, 14f);
            }
            else
            {
                Image bar = CreateImage(rect, "AccentBar", new Color(accent.r, accent.g, accent.b, 0.8f));
                PlaceTopLeft(bar.rectTransform, 0f, OptionHeight - 6f, OptionWidth, 6f);
            }

            MenuButtonFeedback feedback = AttachFeedback(option, SfxId.UiConfirm, bright);
            feedback.hoverScale = 1.03f;
            feedback.glowOutline = frame;
            feedback.glowColor = new Color(bright.r, bright.g, bright.b, 1f);
            return option;
        }

        private void SetChoiceFocus(int index, bool playSound = true)
        {
            if (index == choiceFocus) return;
            choiceFocus = index;
            if (playSound && index >= 0) PlaySfx(SfxId.UiHover);
            SetGlow(operatorsOption, index == 0);
            SetGlow(enemiesOption, index == 1);
        }

        private static void SetGlow(Button button, bool on)
        {
            if (button == null) return;
            MenuButtonFeedback feedback = button.GetComponent<MenuButtonFeedback>();
            if (feedback != null) feedback.glowAlways = on;
        }

        private void BuildGalleryCard()
        {
            galleryCard = CreateCard("IntelGalleryCard", GalleryWidth, GalleryHeight, string.Empty, out galleryTitle, out galleryHeaderLine);

            Button closeButton = CreateHeaderButton(galleryCard, "CloseButton", "CLOSE", GalleryWidth - Padding - 140f, 140f);
            closeButton.onClick.AddListener(Close);
            Button backButton = CreateHeaderButton(galleryCard, "BackButton", "< BACK", GalleryWidth - Padding - 140f - 16f - 160f, 160f);
            backButton.onClick.AddListener(ShowChoice);

            // ── Left: filters + grid ──
            tabRow = CreateRect(galleryCard, "TabRow");
            PlaceTopLeft(tabRow, ListX, 84f, ListWidth, 40f);

            listCountText = CreateText(galleryCard, string.Empty, 10, TextAnchor.MiddleLeft, HintColor);
            PlaceTopLeft(listCountText.rectTransform, ListX, 136f, ListWidth, 20f);

            GameObject scrollObject = new GameObject("GridScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            scrollObject.transform.SetParent(galleryCard, false);
            RectTransform scrollRect = scrollObject.GetComponent<RectTransform>();
            PlaceTopLeft(scrollRect, ListX, GridTop, ListWidth, GalleryHeight - GridTop - 32f);
            scrollObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.18f);

            gridContent = CreateRect(scrollObject.transform, "GridContent");
            gridContent.anchorMin = new Vector2(0f, 1f);
            gridContent.anchorMax = new Vector2(1f, 1f);
            gridContent.pivot = new Vector2(0.5f, 1f);
            gridContent.sizeDelta = Vector2.zero;
            var grid = gridContent.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(TileWidth, TileHeight);
            grid.spacing = new Vector2(TileGap, TileGap);
            grid.padding = new RectOffset(16, 16, 16, 16);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = GridColumns;
            var fitter = gridContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            gridScroll = scrollObject.GetComponent<ScrollRect>();
            gridScroll.content = gridContent;
            gridScroll.viewport = scrollRect;
            gridScroll.horizontal = false;
            gridScroll.vertical = true;
            gridScroll.movementType = ScrollRect.MovementType.Clamped;
            gridScroll.scrollSensitivity = 40f;

            Image divider = CreateImage(galleryCard, "Divider", new Color(LineColor.r, LineColor.g, LineColor.b, 0.3f));
            PlaceTopLeft(divider.rectTransform, DetailX - 24f, 84f, 2f, GalleryHeight - 116f);

            BuildDetailPane();
        }

        private void BuildDetailPane()
        {
            // ── Sprite stage ──
            CreateSpriteStage(galleryCard, DetailX, 84f, StageSize, AccentColor, false, out stageBackground, out stageSprite, out stageFloor);

            // ── Identity column ──
            fileText = CreateText(galleryCard, string.Empty, 10, TextAnchor.MiddleLeft, HintColor);
            PlaceTopLeft(fileText.rectTransform, InfoX, 88f, InfoWidth, 20f);
            nameText = CreateText(galleryCard, string.Empty, 28, TextAnchor.MiddleLeft, AccentBright);
            PlaceTopLeft(nameText.rectTransform, InfoX, 112f, InfoWidth, 40f);
            chipRow = CreateRect(galleryCard, "ChipRow");
            PlaceTopLeft(chipRow, InfoX, 164f, InfoWidth, 32f);
            rarityRow = CreateRect(galleryCard, "RarityRow");
            PlaceTopLeft(rarityRow, InfoX, 210f, InfoWidth, 24f);
            statusText = CreateText(galleryCard, string.Empty, 10, TextAnchor.MiddleLeft, LabelColor);
            PlaceTopLeft(statusText.rectTransform, InfoX, 212f, InfoWidth, 20f);

            // ── Field notes (beside the sprite) ──
            Image notesBar = CreateImage(galleryCard, "FieldNotesBar", new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.6f));
            PlaceTopLeft(notesBar.rectTransform, InfoX, 248f, 4f, 148f);
            Text fieldNotesHeader = CreateText(galleryCard, "// FIELD NOTES", 12, TextAnchor.MiddleLeft, AccentColor);
            PlaceTopLeft(fieldNotesHeader.rectTransform, InfoX + 20f, 248f, InfoWidth - 20f, 20f);
            loreText = CreateText(galleryCard, string.Empty, 13, TextAnchor.UpperLeft, BodyColor);
            loreText.horizontalOverflow = HorizontalWrapMode.Wrap;
            loreText.verticalOverflow = VerticalWrapMode.Truncate;
            loreText.lineSpacing = 1.45f;
            loreText.resizeTextForBestFit = true;
            loreText.resizeTextMinSize = 10;
            loreText.resizeTextMaxSize = 13;
            PlaceTopLeft(loreText.rectTransform, InfoX + 20f, 274f, InfoWidth - 20f, 122f);

            // ── Combat data ──
            Image statsPanel = CreateImage(galleryCard, "StatsPanel", new Color(0f, 0f, 0f, 0.22f));
            PlaceTopLeft(statsPanel.rectTransform, DetailX, DataTop, StatsWidth, DataHeight);
            statsHeader = CreateSectionHeader(statsPanel.rectTransform, "COMBAT DATA", 16f, 14f, 200f);
            for (int i = 0; i < 3; i++) tierButtons.Add(CreateTierButton(statsPanel.rectTransform, i));

            string[] statNames = { "HP", "ATK", "DEF", "RES" };
            for (int i = 0; i < statNames.Length; i++) statRows.Add(CreateStatRow(statsPanel.rectTransform, statNames[i], 56f + i * 34f));

            for (int i = 0; i < 4; i++) factBoxes.Add(CreateFactBox(statsPanel.rectTransform, 0f, 200f, 0f));
            LayoutFacts(4);

            // ── Range ──
            Image rangePanel = CreateImage(galleryCard, "RangePanel", new Color(0f, 0f, 0f, 0.22f));
            PlaceTopLeft(rangePanel.rectTransform, RangeX, DataTop, RangeWidth, DataHeight);
            rangeHeader = CreateSectionHeader(rangePanel.rectTransform, "RANGE", 16f, 14f, 120f);
            rangeReadout = CreateText(rangePanel.rectTransform, string.Empty, 10, TextAnchor.MiddleRight, LabelColor);
            PlaceTopLeft(rangeReadout.rectTransform, 140f, 14f, RangeWidth - 156f, 24f);
            rangeGrid = CreateRect(rangePanel.rectTransform, "RangeGrid");
            rangeGrid.anchorMin = rangeGrid.anchorMax = new Vector2(0.5f, 1f);
            rangeGrid.pivot = new Vector2(0.5f, 0.5f);
            rangeGrid.anchoredPosition = new Vector2(0f, -144f);
            rangeGrid.sizeDelta = new Vector2(RangeWidth - 32f, 176f);
            rangeLegend = CreateRect(rangePanel.rectTransform, "RangeLegend");
            PlaceTopLeft(rangeLegend, 16f, DataHeight - 34f, RangeWidth - 32f, 20f);

            // ── Notes (scrolls if long) ──
            GameObject notesObject = new GameObject("NotesScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            notesObject.transform.SetParent(galleryCard, false);
            RectTransform notesRect = notesObject.GetComponent<RectTransform>();
            PlaceTopLeft(notesRect, DetailX, NotesTop, DetailWidth, GalleryHeight - NotesTop - 32f);
            notesObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.22f);

            RectTransform notesContent = CreateRect(notesObject.transform, "NotesContent");
            notesContent.anchorMin = new Vector2(0f, 1f);
            notesContent.anchorMax = new Vector2(1f, 1f);
            notesContent.pivot = new Vector2(0.5f, 1f);
            notesContent.sizeDelta = Vector2.zero;
            var layout = notesContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 16, 16);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            notesContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            notesScroll = notesObject.GetComponent<ScrollRect>();
            notesScroll.content = notesContent;
            notesScroll.viewport = notesRect;
            notesScroll.horizontal = false;
            notesScroll.movementType = ScrollRect.MovementType.Clamped;
            notesScroll.scrollSensitivity = 30f;

            abilityHeader = CreateNotesText(notesContent, "// ABILITY", 12, AccentColor);
            abilityText = CreateNotesText(notesContent, string.Empty, 11, BodyColor);
            sightedHeader = CreateNotesText(notesContent, "// SIGHTED AT", 12, AccentColor);
            sightedText = CreateNotesText(notesContent, string.Empty, 11, BodyColor);
        }

        private Text CreateNotesText(RectTransform parent, string content, int size, Color color)
        {
            Text text = CreateText(parent, content, size, TextAnchor.UpperLeft, color);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = 1.45f;
            return text;
        }

        private Text CreateSectionHeader(RectTransform parent, string label, float x, float y, float width)
        {
            Text header = CreateText(parent, "// " + label, 12, TextAnchor.MiddleLeft, AccentColor);
            PlaceTopLeft(header.rectTransform, x, y, width, 24f);
            return header;
        }

        private TierButton CreateTierButton(RectTransform parent, int index)
        {
            const float width = 88f;
            Button button = CreateButtonObject(parent, "TierButton_" + index, TerminalButton);
            PlaceTopLeft(button.GetComponent<RectTransform>(), StatsWidth - 16f - (3 - index) * (width + 6f) + 6f, 10f, width, 30f);

            Image star = CreateImage(button.transform, "Star", AmberColor);
            star.sprite = GameplayHUDUI.LoadIconSprite("icon_star");
            star.preserveAspect = true;
            PlaceTopLeft(star.rectTransform, 10f, 7f, 16f, 16f);

            Text label = CreateText(button.transform, string.Empty, 10, TextAnchor.MiddleCenter, LabelColor);
            SetStretch(label.rectTransform);
            int captured = index;
            button.onClick.AddListener(() => SetTier(captured));
            AttachFeedback(button, SfxId.UiClick, AccentBright);
            return new TierButton { Button = button, Label = label, Star = star };
        }

        private StatRow CreateStatRow(RectTransform parent, string label, float y)
        {
            Text name = CreateText(parent, label, 12, TextAnchor.MiddleLeft, LabelColor);
            PlaceTopLeft(name.rectTransform, 16f, y, 60f, 24f);

            const float trackX = 84f;
            float trackWidth = StatsWidth - trackX - 16f - 88f;
            Image track = CreateImage(parent, label + "Track", new Color(0.02f, 0.06f, 0.04f, 1f));
            PlaceTopLeft(track.rectTransform, trackX, y + 6f, trackWidth, 12f);
            Outline outline = track.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(LineColor.r, LineColor.g, LineColor.b, 0.35f);
            outline.effectDistance = new Vector2(1f, -1f);

            Image fill = CreateImage(track.rectTransform, "Fill", AccentColor);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;

            Text value = CreateText(parent, "0", 12, TextAnchor.MiddleRight, AccentBright);
            PlaceTopLeft(value.rectTransform, StatsWidth - 16f - 80f, y, 80f, 24f);
            return new StatRow { Fill = fill, Value = value };
        }

        private FactBox CreateFactBox(RectTransform parent, float x, float y, float width)
        {
            Image box = CreateImage(parent, "Fact", TerminalButton);
            PlaceTopLeft(box.rectTransform, x, y, width, 56f);
            Outline outline = box.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(LineColor.r, LineColor.g, LineColor.b, 0.25f);
            outline.effectDistance = new Vector2(1f, -1f);

            Text label = CreateText(box.rectTransform, string.Empty, 8, TextAnchor.MiddleCenter, HintColor);
            PlaceTopLeft(label.rectTransform, 0f, 8f, width, 16f);
            Text value = CreateText(box.rectTransform, string.Empty, 13, TextAnchor.MiddleCenter, AccentBright);
            PlaceTopLeft(value.rectTransform, 0f, 26f, width, 22f);
            return new FactBox { Root = box.rectTransform, Label = label, Value = value };
        }

        // Spreads the first `count` fact boxes evenly across the stats panel and hides the rest.
        private void LayoutFacts(int count)
        {
            float factWidth = (StatsWidth - 32f - (count - 1) * 8f) / count;
            for (int i = 0; i < factBoxes.Count; i++)
            {
                FactBox fact = factBoxes[i];
                bool shown = i < count;
                fact.Root.gameObject.SetActive(shown);
                if (!shown) continue;
                PlaceTopLeft(fact.Root, 16f + i * (factWidth + 8f), 200f, factWidth, 56f);
                PlaceTopLeft(fact.Label.rectTransform, 0f, 8f, factWidth, 16f);
                PlaceTopLeft(fact.Value.rectTransform, 0f, 26f, factWidth, 22f);
            }
        }

        // ─────────────────────────────────────────────────────
        //  Gallery: theme, tabs, grid
        // ─────────────────────────────────────────────────────

        private bool Hostile => dossier == Dossier.Enemies;
        private Color Accent => Hostile ? HostileColor : AccentColor;
        private Color Bright => Hostile ? HostileBright : AccentBright;
        private List<IntelEntry> Entries => Hostile ? enemyEntries : operatorEntries;

        private void ApplyDossierTheme()
        {
            galleryTitle.text = Hostile ? "RECLAIMERS' INTEL // ENEMIES" : "RECLAIMERS' INTEL // OPERATORS";
            galleryTitle.color = Bright;
            galleryHeaderLine.color = new Color(Accent.r, Accent.g, Accent.b, 0.55f);
            stageBackground.color = Hostile ? new Color(0.03f, 0.012f, 0.014f, 1f) : StageColor;
            stageFloor.color = new Color(Accent.r, Accent.g, Accent.b, 0.8f);
            nameText.color = Bright;
            statsHeader.text = Hostile ? "// THREAT DATA" : "// COMBAT DATA";
        }

        private void BuildTabs()
        {
            foreach (Button tab in tabButtons) Destroy(tab.gameObject);
            tabButtons.Clear();

            var labels = new List<string> { "ALL" };
            if (Hostile)
            {
                foreach (EnemyArchetype archetype in ArchetypeOrder) labels.Add(ArchetypeLabel(archetype));
            }
            else
            {
                foreach (OperatorClass operatorClass in ClassOrder) labels.Add(operatorClass.ToString().ToUpperInvariant());
            }

            const int fontSize = 9;
            const float tabGap = 6f;
            float textWidth = 0f;
            foreach (string label in labels) textWidth += label.Length * fontSize;
            float padding = Mathf.Max(8f, (ListWidth - textWidth - tabGap * (labels.Count - 1)) / (labels.Count * 2f));

            float x = 0f;
            for (int i = 0; i < labels.Count; i++)
            {
                float width = labels[i].Length * fontSize + padding * 2f;
                Button tab = CreateButtonObject(tabRow, "Tab_" + labels[i], Hostile ? HostileButton : TerminalButton);
                PlaceTopLeft(tab.GetComponent<RectTransform>(), x, 0f, width, 40f);
                Text text = CreateText(tab.transform, labels[i], fontSize, TextAnchor.MiddleCenter, LabelColor);
                SetStretch(text.rectTransform);
                int captured = i;
                tab.onClick.AddListener(() =>
                {
                    filterIndex = captured;
                    ApplyFilter();
                });
                AttachFeedback(tab, SfxId.UiClick, Bright);
                tabButtons.Add(tab);
                x += width + tabGap;
            }
        }

        private void ApplyFilter()
        {
            filtered.Clear();
            foreach (IntelEntry entry in Entries)
            {
                if (filterIndex == 0) filtered.Add(entry);
                else if (Hostile && entry.Enemy.archetype == ArchetypeOrder[filterIndex - 1]) filtered.Add(entry);
                else if (!Hostile && entry.Operator.operatorClass == ClassOrder[filterIndex - 1]) filtered.Add(entry);
            }

            for (int i = 0; i < tabButtons.Count; i++) SetToggleVisual(tabButtons[i], i == filterIndex, Accent, Hostile ? HostileButton : TerminalButton);
            listCountText.text = filtered.Count + (filtered.Count == 1 ? " ENTRY" : " ENTRIES") + "    [ARROWS] BROWSE";

            BuildTiles();
            gridScroll.verticalNormalizedPosition = 1f;
            selectedIndex = -1;
            if (filtered.Count > 0) SelectEntry(0);
            else ShowEmptyDetail();
        }

        private void BuildTiles()
        {
            foreach (Button tile in tileButtons) Destroy(tile.gameObject);
            tileButtons.Clear();

            for (int i = 0; i < filtered.Count; i++)
            {
                IntelEntry entry = filtered[i];
                Button tile = CreateButtonObject(gridContent, "Tile_" + entry.Name, Hostile ? HostilePanel : PanelColor);
                Outline outline = tile.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(Accent.r, Accent.g, Accent.b, 0.25f);
                outline.effectDistance = new Vector2(2f, -2f);

                Image portraitBack = CreateImage(tile.transform, "PortraitBack", new Color(0f, 0f, 0f, 0.3f));
                PlaceTopLeft(portraitBack.rectTransform, 10f, 10f, TileWidth - 20f, 112f);
                Image portrait = CreateImage(portraitBack.rectTransform, "Portrait", Color.white);
                portrait.rectTransform.anchorMin = portrait.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                SetPixelSprite(portrait, entry.Frames.Length > 0 ? entry.Frames[0] : null, 104f);

                if (entry.Operator != null)
                {
                    Image icon = CreateImage(tile.transform, "ClassIcon", Color.white);
                    icon.sprite = GameplayHUDUI.GetClassIconSprite(entry.Operator.operatorClass);
                    icon.enabled = icon.sprite != null;
                    icon.preserveAspect = true;
                    PlaceTopLeft(icon.rectTransform, 12f, 12f, 24f, 24f);

                    Text stars = CreateText(tile.transform, ((int)entry.Operator.baseRarity).ToString(), 10, TextAnchor.MiddleRight, AmberColor);
                    PlaceTopLeft(stars.rectTransform, TileWidth - 52f, 14f, 22f, 20f);
                    Image star = CreateImage(tile.transform, "Star", AmberColor);
                    star.sprite = GameplayHUDUI.LoadIconSprite("icon_star");
                    star.preserveAspect = true;
                    PlaceTopLeft(star.rectTransform, TileWidth - 28f, 15f, 16f, 16f);
                }
                else
                {
                    Text tag = CreateText(tile.transform, ArchetypeLabel(entry.Enemy.archetype), 8, TextAnchor.MiddleLeft, new Color(HostileBright.r, HostileBright.g, HostileBright.b, 0.85f));
                    PlaceTopLeft(tag.rectTransform, 14f, 14f, TileWidth - 28f, 16f);
                }

                Text name = CreateText(tile.transform, entry.Name.ToUpperInvariant(), 9, TextAnchor.MiddleCenter, LabelColor);
                name.horizontalOverflow = HorizontalWrapMode.Wrap;
                name.verticalOverflow = VerticalWrapMode.Truncate;
                name.lineSpacing = 1.2f;
                PlaceTopLeft(name.rectTransform, 6f, 126f, TileWidth - 12f, 38f);

                int captured = i;
                tile.onClick.AddListener(() => SelectEntry(captured));
                MenuButtonFeedback feedback = AttachFeedback(tile, SfxId.UiClick, Bright);
                feedback.glowOutline = outline;
                feedback.hoverScale = 1.05f;
                tileButtons.Add(tile);
            }
        }

        private void ScrollToTile(int index)
        {
            Canvas.ForceUpdateCanvases();
            float viewHeight = gridScroll.viewport.rect.height;
            float contentHeight = gridContent.rect.height;
            if (contentHeight <= viewHeight) return;

            int row = index / GridColumns;
            float top = 16f + row * (TileHeight + TileGap);
            float bottom = top + TileHeight;
            float scrollY = gridContent.anchoredPosition.y;
            if (top < scrollY) scrollY = top - 8f;
            else if (bottom > scrollY + viewHeight) scrollY = bottom - viewHeight + 8f;
            gridContent.anchoredPosition = new Vector2(gridContent.anchoredPosition.x, Mathf.Clamp(scrollY, 0f, contentHeight - viewHeight));
        }

        // ─────────────────────────────────────────────────────
        //  Detail
        // ─────────────────────────────────────────────────────

        private void SelectEntry(int index)
        {
            if (index < 0 || index >= filtered.Count) return;
            selectedIndex = index;
            for (int i = 0; i < tileButtons.Count; i++) SetGlow(tileButtons[i], i == index);

            IntelEntry entry = filtered[index];
            playingFrames = entry.Frames;
            playingFrameDuration = Mathf.Max(0.04f, entry.FrameDuration);
            frameIndex = 0;
            frameTimer = 0f;
            SetPixelSprite(stageSprite, playingFrames.Length > 0 ? playingFrames[0] : null, StageSize - 28f);

            fileText.text = "FILE " + entry.FileId + (Hostile ? "    // HOSTILE" : "    // RECLAIMER");
            nameText.text = entry.Name.ToUpperInvariant();
            BuildChips(entry);

            string lore = entry.Operator != null ? entry.Operator.intelLore : entry.Enemy.intelLore;
            loreText.text = string.IsNullOrWhiteSpace(lore) ? "No field notes recovered yet." : lore.Trim();

            string ability = entry.Operator != null ? entry.Operator.skillDescription : entry.Enemy.abilityDescription;
            abilityText.text = string.IsNullOrWhiteSpace(ability) ? "No special abilities on record." : ability.Trim();
            abilityHeader.text = entry.Operator != null ? "// SKILL" : "// ABILITY";

            bool showSightings = entry.Enemy != null;
            sightedHeader.gameObject.SetActive(showSightings);
            sightedText.gameObject.SetActive(showSightings);
            if (showSightings)
            {
                sightedText.text = sightings.TryGetValue(entry.Enemy, out List<string> stages) && stages.Count > 0
                    ? string.Join("\n", stages)
                    : "No confirmed sightings in the field yet.";
            }
            notesScroll.verticalNormalizedPosition = 1f;

            ConfigureTiers(entry);
            UpdateRange(entry);
        }

        private void ShowEmptyDetail()
        {
            playingFrames = null;
            SetPixelSprite(stageSprite, null, 0f);
            fileText.text = string.Empty;
            nameText.text = "NO ENTRIES";
            loreText.text = string.Empty;
            statusText.text = string.Empty;
            ClearChildren(chipRow);
            ClearChildren(rarityRow);
            ClearChildren(rangeGrid);
            ClearChildren(rangeLegend);
        }

        private void AnimateDetailSprite(float dt)
        {
            if (playingFrames == null || playingFrames.Length < 2 || !galleryCard.gameObject.activeSelf) return;

            frameTimer += dt;
            if (frameTimer < playingFrameDuration) return;
            int elapsed = Mathf.FloorToInt(frameTimer / playingFrameDuration);
            frameTimer %= playingFrameDuration;
            frameIndex = (frameIndex + elapsed) % playingFrames.Length;
            stageSprite.sprite = playingFrames[frameIndex];
        }

        private void BuildChips(IntelEntry entry)
        {
            ClearChildren(chipRow);
            float x = 0f;
            if (entry.Operator != null)
            {
                OperatorData op = entry.Operator;
                x = AddChip(x, op.operatorClass.ToString().ToUpperInvariant(), AccentBright, GameplayHUDUI.GetClassIconSprite(op.operatorClass));
                x = AddChip(x, PositionLabel(op.position), LabelColor, null);
                AddChip(x, DamageLabel(op.damageType), DamageColor(op.damageType), null);
            }
            else
            {
                EnemyData enemy = entry.Enemy;
                x = AddChip(x, ArchetypeLabel(enemy.archetype), HostileBright, GameplayHUDUI.GetEnemyClassIconSprite(enemy));
                // Flyers' archetype chip already reads AIR, so only show movement when it adds something
                string movement = enemy.movementType == EnemyMovementType.Air ? "AIR" : "GROUND";
                if (movement != ArchetypeLabel(enemy.archetype)) x = AddChip(x, movement, LabelColor, null);
                x = AddChip(x, DamageLabel(enemy.damageType), DamageColor(enemy.damageType), null);
                if (enemy.isUnblockable) AddChip(x, "UNBLOCKABLE", AmberColor, null);
            }
        }

        private float AddChip(float x, string label, Color color, Sprite icon)
        {
            const int fontSize = 10;
            float iconSpace = icon != null ? 26f : 0f;
            float width = label.Length * fontSize + 24f + iconSpace;

            Image chip = CreateImage(chipRow, "Chip_" + label, new Color(0.07f, 0.085f, 0.115f, 1f));
            PlaceTopLeft(chip.rectTransform, x, 0f, width, 32f);
            Outline outline = chip.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(color.r, color.g, color.b, 0.7f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            if (icon != null)
            {
                Image iconImage = CreateImage(chip.rectTransform, "Icon", Color.white);
                iconImage.sprite = icon;
                iconImage.preserveAspect = true;
                PlaceTopLeft(iconImage.rectTransform, 8f, 5f, 22f, 22f);
            }

            Text text = CreateText(chip.rectTransform, label, fontSize, TextAnchor.MiddleCenter, color);
            PlaceTopLeft(text.rectTransform, iconSpace, 0f, width - iconSpace, 32f);
            return x + width + 10f;
        }

        private void ConfigureTiers(IntelEntry entry)
        {
            int count;
            if (entry.Operator != null)
            {
                count = (int)TopRarity(entry.Operator) - (int)entry.Operator.baseRarity + 1;
                for (int i = 0; i < tierButtons.Count; i++)
                {
                    tierButtons[i].Star.enabled = true;
                    tierButtons[i].Label.text = "  " + ((int)entry.Operator.baseRarity + i);
                }
            }
            else
            {
                count = DifficultyLabels.Length;
                for (int i = 0; i < tierButtons.Count; i++)
                {
                    tierButtons[i].Star.enabled = false;
                    tierButtons[i].Label.text = DifficultyLabels[i];
                }
            }

            for (int i = 0; i < tierButtons.Count; i++) tierButtons[i].Button.gameObject.SetActive(i < count && count > 1);
            SetTier(0, false);
        }

        private void SetTier(int index, bool playSound = true)
        {
            if (selectedIndex < 0 || selectedIndex >= filtered.Count) return;
            tierIndex = index;
            for (int i = 0; i < tierButtons.Count; i++)
                SetToggleVisual(tierButtons[i].Button, i == index, Accent, Hostile ? HostileButton : TerminalButton);
            if (playSound) PlaySfx(SfxId.UiClick);
            RefreshStats(filtered[selectedIndex]);
        }

        private void RefreshStats(IntelEntry entry)
        {
            int[] values;
            int[] max;
            if (entry.Operator != null)
            {
                OperatorData op = entry.Operator;
                OperatorRarity rarity = (OperatorRarity)Mathf.Min((int)op.baseRarity + tierIndex, (int)OperatorRarity.Star5);
                values = new[] { op.GetScaledHP(rarity), op.GetScaledATK(rarity), op.GetScaledDEF(rarity), op.GetScaledRES(rarity) };
                max = operatorStatMax;

                LayoutFacts(3);
                SetFact(0, "BLOCK", op.blockCount.ToString());
                SetFact(1, "ATK SPD", FormatSeconds(op.attackInterval));
                SetFact(2, "REDEPLOY", FormatWaves(TrashTD.Systems.OperatorManager.GetRetreatCooldownRounds(op.operatorClass)));

                statusText.text = string.Empty;
                BuildRarityStars((int)rarity, tierIndex > 0);
            }
            else
            {
                EnemyData enemy = entry.Enemy;
                int level = tierIndex + 1;
                values = new[] { enemy.GetScaledHP(level), enemy.GetScaledATK(level), enemy.GetScaledDEF(level), enemy.baseRES };
                max = enemyStatMax;

                LayoutFacts(4);
                SetFact(0, "SPEED", enemy.moveSpeed.ToString("0.##"));
                SetFact(1, "ATK SPD", FormatSeconds(enemy.attackInterval));
                SetFact(2, "BLOCK WT", enemy.isUnblockable ? "-" : enemy.blockWeight.ToString());
                SetFact(3, "LIFE COST", enemy.lifePointCost.ToString());

                ClearChildren(rarityRow);
                bool sighted = sightings.TryGetValue(enemy, out List<string> stages) && stages.Count > 0;
                statusText.text = sighted ? "STATUS: ACTIVE THREAT" : "STATUS: UNCONFIRMED";
                statusText.color = sighted ? HostileBright : HintColor;
            }

            for (int i = 0; i < statRows.Count; i++)
            {
                float ratio = max[i] > 0 ? Mathf.Clamp01((float)values[i] / max[i]) : 0f;
                statRows[i].Fill.rectTransform.anchorMax = new Vector2(Mathf.Max(ratio, values[i] > 0 ? 0.02f : 0f), 1f);
                statRows[i].Fill.color = Accent;
                statRows[i].Value.text = values[i].ToString();
                statRows[i].Value.color = Bright;
            }
        }

        private void BuildRarityStars(int rarity, bool upgraded)
        {
            ClearChildren(rarityRow);
            Sprite star = GameplayHUDUI.LoadIconSprite("icon_star");
            for (int i = 0; i < rarity; i++)
            {
                Image image = CreateImage(rarityRow, "Star", AmberColor);
                image.sprite = star;
                image.preserveAspect = true;
                PlaceTopLeft(image.rectTransform, i * 26f, 0f, 22f, 22f);
            }

            if (upgraded)
            {
                Text note = CreateText(rarityRow, "UPGRADED", 9, TextAnchor.MiddleLeft, HintColor);
                PlaceTopLeft(note.rectTransform, rarity * 26f + 10f, 0f, 120f, 22f);
            }
        }

        private void SetFact(int index, string label, string value)
        {
            factBoxes[index].Label.text = label;
            factBoxes[index].Value.text = value;
            factBoxes[index].Value.color = Bright;
        }

        // ─────────────────────────────────────────────────────
        //  Range preview
        // ─────────────────────────────────────────────────────

        private void UpdateRange(IntelEntry entry)
        {
            ClearChildren(rangeGrid);
            ClearChildren(rangeLegend);

            var hitCells = new HashSet<Vector2Int>();
            var talentCells = new HashSet<Vector2Int>();
            Color selfColor;
            Color hitColor;
            Sprite selfIcon = null;

            if (entry.Operator != null)
            {
                OperatorData op = entry.Operator;
                if (op.rangePattern != null) foreach (Vector2Int cell in op.rangePattern) if (cell != Vector2Int.zero) hitCells.Add(cell);
                Vector2Int[] talent = op.GetTalentRangePattern();
                if (talent != null) foreach (Vector2Int cell in talent) if (cell != Vector2Int.zero && !hitCells.Contains(cell)) talentCells.Add(cell);

                selfColor = RangeSelfColor;
                hitColor = RangeHitColor;
                selfIcon = GameplayHUDUI.GetClassIconSprite(op.operatorClass);
                bool hitsSelf = op.rangePattern != null && Array.IndexOf(op.rangePattern, Vector2Int.zero) >= 0;
                rangeReadout.text = hitCells.Count == 0 ? (hitsSelf ? "OWN TILE" : "NO ATTACK") : hitCells.Count + " TILES, FACING RIGHT";

                float x = AddLegend(0f, selfColor, "SELF");
                x = AddLegend(x, hitColor, "ATTACK");
                if (talentCells.Count > 0) AddLegend(x, RangeTalentColor, "TALENT");
            }
            else
            {
                EnemyData enemy = entry.Enemy;
                float reach = enemy.attackRange > 0 ? enemy.attackRange
                    : enemy.archetype == EnemyArchetype.Caster ? 2.5f : 0f; // EnemyBase's default ranged reach
                if (reach > 0f)
                {
                    int r = Mathf.CeilToInt(reach);
                    for (int x = -r; x <= r; x++)
                        for (int y = -r; y <= r; y++)
                            if ((x != 0 || y != 0) && x * x + y * y <= reach * reach) hitCells.Add(new Vector2Int(x, y));
                    rangeReadout.text = "RANGED, " + reach.ToString("0.#") + " TILES";
                }
                else if (enemy.frontAreaAttackTiles > 0)
                {
                    for (int x = 1; x <= enemy.frontAreaAttackTiles; x++) hitCells.Add(new Vector2Int(x, 0));
                    rangeReadout.text = enemy.frontAreaAttackTiles + " TILES AHEAD";
                }
                else
                {
                    rangeReadout.text = enemy.movementType == EnemyMovementType.Air ? "MELEE, FROM THE AIR" : "MELEE, HITS ITS BLOCKER";
                }

                selfColor = EnemySelfColor;
                hitColor = AmberColor;
                float lx = AddLegend(0f, selfColor, "SELF");
                lx = AddLegend(lx, hitColor, "ATTACK");
                AddLegend(lx, new Color(1f, 1f, 1f, 0.5f), "PATH >");
            }

            int minX = 0, maxX = 0, minY = 0, maxY = 0;
            foreach (Vector2Int cell in hitCells) Expand(cell, ref minX, ref maxX, ref minY, ref maxY);
            foreach (Vector2Int cell in talentCells) Expand(cell, ref minX, ref maxX, ref minY, ref maxY);

            // Pad small patterns to at least 3x3 so the unit's own tile has context around it.
            while (maxX - minX + 1 < 3) { if (-minX <= maxX) minX--; else maxX++; }
            while (maxY - minY + 1 < 3) { if (-minY <= maxY) minY--; else maxY++; }

            int cols = maxX - minX + 1;
            int rows = maxY - minY + 1;
            Vector2 area = rangeGrid.sizeDelta;
            const float gap = 3f;
            float tileSize = Mathf.Floor(Mathf.Min(34f, (area.x - gap * (cols - 1)) / cols, (area.y - gap * (rows - 1)) / rows));
            float step = tileSize + gap;

            for (int gy = minY; gy <= maxY; gy++)
            {
                for (int gx = minX; gx <= maxX; gx++)
                {
                    var cell = new Vector2Int(gx, gy);
                    bool isSelf = cell == Vector2Int.zero;
                    bool hit = hitCells.Contains(cell);
                    bool talent = talentCells.Contains(cell);
                    Color color = isSelf ? selfColor : hit ? hitColor : talent ? RangeTalentColor : RangeEmptyColor;
                    var position = new Vector2(
                        Mathf.Round((gx - minX - (cols - 1) * 0.5f) * step),
                        Mathf.Round((gy - minY - (rows - 1) * 0.5f) * step));
                    CreateRangeTile(position, tileSize, color, isSelf || hit || talent, isSelf ? selfIcon : null);
                }
            }
        }

        private static void Expand(Vector2Int cell, ref int minX, ref int maxX, ref int minY, ref int maxY)
        {
            minX = Mathf.Min(minX, cell.x);
            maxX = Mathf.Max(maxX, cell.x);
            minY = Mathf.Min(minY, cell.y);
            maxY = Mathf.Max(maxY, cell.y);
        }

        private void CreateRangeTile(Vector2 position, float size, Color color, bool filled, Sprite icon)
        {
            Image frame = CreateImage(rangeGrid, "RangeTile", color);
            RectTransform rect = frame.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = position;

            Sprite tileSprite = GetTileFrameSprite();
            if (tileSprite != null)
            {
                frame.sprite = tileSprite;
                frame.type = Image.Type.Sliced;
            }
            else
            {
                frame.color = new Color(color.r, color.g, color.b, color.a * 0.5f);
            }

            float inset = Mathf.Min(3f, size * 0.2f);
            if (filled)
            {
                Image fill = CreateImage(rect, "Fill", new Color(color.r, color.g, color.b, 0.3f));
                SetStretch(fill.rectTransform);
                fill.rectTransform.offsetMin = new Vector2(inset, inset);
                fill.rectTransform.offsetMax = new Vector2(-inset, -inset);
            }

            if (icon != null)
            {
                Image iconImage = CreateImage(rect, "Icon", Color.white);
                iconImage.sprite = icon;
                iconImage.preserveAspect = true;
                SetStretch(iconImage.rectTransform);
                iconImage.rectTransform.offsetMin = new Vector2(inset + 1f, inset + 1f);
                iconImage.rectTransform.offsetMax = new Vector2(-inset - 1f, -inset - 1f);
            }
        }

        private float AddLegend(float x, Color color, string label)
        {
            Image swatch = CreateImage(rangeLegend, "Swatch", color);
            PlaceTopLeft(swatch.rectTransform, x, 4f, 12f, 12f);
            Text text = CreateText(rangeLegend, label, 9, TextAnchor.MiddleLeft, HintColor);
            float width = label.Length * 9f + 4f;
            PlaceTopLeft(text.rectTransform, x + 18f, 0f, width, 20f);
            return x + 18f + width + 18f;
        }

        // ─────────────────────────────────────────────────────
        //  Labels
        // ─────────────────────────────────────────────────────

        private static string ArchetypeLabel(EnemyArchetype archetype) =>
            archetype == EnemyArchetype.Flyer ? "AIR" : archetype.ToString().ToUpperInvariant();

        private static string PositionLabel(OperatorPosition position) => position switch
        {
            OperatorPosition.Melee => "MELEE",
            OperatorPosition.Ranged => "RANGED",
            _ => "ANY TILE"
        };

        private static string DamageLabel(DamageType type) => type switch
        {
            DamageType.Arts => "ARTS",
            DamageType.True => "TRUE DMG",
            _ => "PHYSICAL"
        };

        private static Color DamageColor(DamageType type) => type switch
        {
            DamageType.Arts => new Color(0.72f, 0.48f, 1f, 1f),
            DamageType.True => new Color(1f, 0.95f, 0.6f, 1f),
            _ => new Color(1f, 0.72f, 0.45f, 1f)
        };

        private static string FormatSeconds(float seconds) => seconds.ToString("0.##") + "S";

        private static string FormatWaves(int waves) => waves <= 0 ? "NONE" : waves == 1 ? "1 WAVE" : waves + " WAVES";

        // ─────────────────────────────────────────────────────
        //  Widgets
        // ─────────────────────────────────────────────────────

        /// <summary>A dark square with a faint pixel grid, a floor line and corner brackets, holding a pixel-scaled sprite.</summary>
        private static void CreateSpriteStage(RectTransform parent, float x, float y, float size, Color accent, bool hostile,
            out Image background, out Image sprite, out Image floor)
        {
            background = CreateImage(parent, "SpriteStage", hostile ? new Color(0.03f, 0.012f, 0.014f, 1f) : StageColor);
            PlaceTopLeft(background.rectTransform, x, y, size, size);

            Image grid = CreateImage(background.rectTransform, "Grid", new Color(accent.r, accent.g, accent.b, 0.08f));
            grid.sprite = GetGridSprite();
            grid.type = Image.Type.Tiled;
            grid.pixelsPerUnitMultiplier = 0.25f;
            SetStretch(grid.rectTransform);

            floor = CreateImage(background.rectTransform, "Floor", new Color(accent.r, accent.g, accent.b, 0.8f));
            PlaceTopLeft(floor.rectTransform, 0f, size - 4f, size, 4f);
            Image glow = CreateImage(background.rectTransform, "FloorGlow", new Color(accent.r, accent.g, accent.b, 0.08f));
            PlaceTopLeft(glow.rectTransform, 0f, size * 0.62f, size, size * 0.38f - 4f);

            AddCornerBrackets(background.rectTransform, 18f, 3f, new Color(accent.r, accent.g, accent.b, 0.9f), 4f);

            sprite = CreateImage(background.rectTransform, "Sprite", Color.white);
            sprite.rectTransform.anchorMin = sprite.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            sprite.rectTransform.anchoredPosition = Vector2.zero;
            sprite.preserveAspect = true;
        }

        /// <summary>Shows a sprite at the largest whole-number scale that fits <paramref name="maxSize"/> (pixel art stays crisp).</summary>
        private static void SetPixelSprite(Image image, Sprite sprite, float maxSize)
        {
            if (image == null) return;
            image.sprite = sprite;
            image.enabled = sprite != null;
            if (sprite == null) return;

            Vector2 pixels = sprite.rect.size;
            float scale = Mathf.Max(1f, Mathf.Floor(maxSize / Mathf.Max(pixels.x, pixels.y)));
            if (Mathf.Max(pixels.x, pixels.y) > maxSize) scale = maxSize / Mathf.Max(pixels.x, pixels.y);
            image.rectTransform.sizeDelta = pixels * scale;
        }

        private static void UsePointFilter(Sprite[] frames)
        {
            if (frames == null) return;
            foreach (Sprite frame in frames)
            {
                if (frame != null && frame.texture != null) frame.texture.filterMode = FilterMode.Point;
            }
        }

        private Button CreateHeaderButton(RectTransform card, string objectName, string label, float x, float width)
        {
            Button button = CreateTerminalButton(card, objectName, label, 12);
            PlaceTopLeft(button.GetComponent<RectTransform>(), x, 12f, width, 40f);
            AddCornerBrackets(button.GetComponent<RectTransform>(), 8f, 2f, LineColor);
            AttachFeedback(button, SfxId.UiBack, AccentBright);
            return button;
        }

        private static Button CreateButtonObject(Transform parent, string name, Color fill)
        {
            GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<Image>().color = fill;
            return buttonObject.GetComponent<Button>();
        }

        /// <summary>Flat terminal button: dark green fill, centred label (MenuButtonFeedback adds hover/glow).</summary>
        private static Button CreateTerminalButton(Transform parent, string name, string label, int fontSize)
        {
            Button button = CreateButtonObject(parent, name, TerminalButton);
            Text text = CreateText(button.transform, label, fontSize, TextAnchor.MiddleCenter, LabelColor);
            SetStretch(text.rectTransform);
            return button;
        }

        private static void SetToggleVisual(Button button, bool selected, Color accent, Color idle)
        {
            if (button == null) return;
            MenuButtonFeedback feedback = button.GetComponent<MenuButtonFeedback>();
            Color fill = selected ? accent : idle;
            if (feedback != null)
            {
                feedback.SetBaseColor(fill);
                feedback.glowAlways = selected;
            }
            else
            {
                button.GetComponent<Image>().color = fill;
            }

            foreach (Text label in button.GetComponentsInChildren<Text>())
                label.color = selected ? SelectedLabel : LabelColor;
            foreach (Image image in button.GetComponentsInChildren<Image>())
                if (image.gameObject != button.gameObject && image.name == "Star") image.color = selected ? SelectedLabel : AmberColor;
        }

        private static MenuButtonFeedback AttachFeedback(Button button, SfxId clickSound, Color glow)
        {
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            MenuButtonFeedback feedback = button.gameObject.AddComponent<MenuButtonFeedback>();
            feedback.hoverScale = 1.04f;
            feedback.respondToFocus = false;
            feedback.Hovered = () => AudioManager.Instance?.PlaySfx(SfxId.UiHover, 0.7f, UnityEngine.Random.Range(0.97f, 1.03f));
            feedback.Clicked = () => AudioManager.Instance?.PlaySfx(clickSound);

            Outline outline = button.GetComponent<Outline>();
            if (outline == null)
            {
                outline = button.gameObject.AddComponent<Outline>();
                outline.useGraphicAlpha = false;
                outline.effectDistance = new Vector2(2f, -2f);
                outline.effectColor = new Color(glow.r, glow.g, glow.b, 0f);
            }
            feedback.glowOutline = outline;
            feedback.glowColor = new Color(glow.r, glow.g, glow.b, 0.9f);
            return feedback;
        }

        private static void PlaySfx(SfxId id) => AudioManager.Instance?.PlaySfx(id);

        private static Image CreateHazardStripe(RectTransform parent, string name)
        {
            Image stripe = CreateImage(parent, name, Color.white);
            Sprite sprite = GetHazardSprite();
            if (sprite != null)
            {
                stripe.sprite = sprite;
                stripe.type = Image.Type.Tiled;
            }
            else
            {
                stripe.color = AmberColor;
            }
            return stripe;
        }

        private static Sprite GetHazardSprite()
        {
            if (hazardSprite != null) return hazardSprite;
            hazardSprite = Resources.Load<Sprite>("UI/decor/hazard_bar");
            if (hazardSprite == null)
            {
                Sprite[] all = Resources.LoadAll<Sprite>("UI/decor/hazard_bar");
                if (all != null && all.Length > 0) hazardSprite = all[0];
            }
            if (hazardSprite != null && hazardSprite.texture != null) hazardSprite.texture.filterMode = FilterMode.Point;
            return hazardSprite;
        }

        private static Sprite GetTileFrameSprite()
        {
            if (tileFrameSprite != null) return tileFrameSprite;
            Texture2D texture = Resources.Load<Texture2D>("UI/controls/checkbox_off");
            if (texture == null) return null;
            texture.filterMode = FilterMode.Point;
            tileFrameSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(3f, 3f, 3f, 3f));
            return tileFrameSprite;
        }

        private static Sprite GetGridSprite()
        {
            if (gridSprite != null) return gridSprite;
            const int size = 8;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = x == 0 || y == 0 ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            texture.SetPixels32(pixels);
            texture.Apply();
            gridSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            return gridSprite;
        }

        private static void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--) Destroy(parent.GetChild(i).gameObject);
        }

        private static Text CreateText(Transform parent, string content, int size, TextAnchor alignment, Color color)
        {
            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.text = content;
            text.font = UIFontHelper.GetPixelFont();
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            GameObject rectObject = new GameObject(name, typeof(RectTransform));
            rectObject.transform.SetParent(parent, false);
            return rectObject.GetComponent<RectTransform>();
        }

        /// <summary>Four L-shaped terminal corner brackets; a negative inset pushes them outside the rect.</summary>
        private static void AddCornerBrackets(RectTransform target, float length, float thickness, Color color, float inset = 0f)
        {
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2(i % 2, i / 2);
                var inward = new Vector2(corner.x < 0.5f ? 1f : -1f, corner.y < 0.5f ? 1f : -1f);
                CreateBracketBar(target, corner, inward * inset, new Vector2(length, thickness), color);
                CreateBracketBar(target, corner, inward * inset, new Vector2(thickness, length), color);
            }
        }

        private static void CreateBracketBar(RectTransform parent, Vector2 corner, Vector2 offset, Vector2 size, Color color)
        {
            Image bar = CreateImage(parent, "CornerBracket", color);
            RectTransform rect = bar.rectTransform;
            rect.anchorMin = corner;
            rect.anchorMax = corner;
            rect.pivot = corner;
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;
        }

        /// <summary>Places a rect by its top-left corner, in pixels from the top-left of its parent.</summary>
        private static void PlaceTopLeft(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetCentered(RectTransform rect, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetStretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
