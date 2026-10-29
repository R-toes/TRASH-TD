using System;
using UnityEngine;

namespace TrashTD.Data
{
    /// <summary>What a tutorial step does. Talking steps freeze the game; task steps wait for the player.</summary>
    public enum TutorialAction
    {
        Say,              // Coalesce talks (game frozen); a click shows the next step
        RevealPath,       // Say, and the enemy route lights up from spawn to exit
        Draft,            // Task: deal `cards` and wait for the player to pick `operatorData` (null = any card)
        Deploy,           // Task: wait until `operatorData` is deployed
        StartWave,        // Task: wait for START WAVE
        WaitForEnemy,     // The wave runs (no bubble) until `enemyData` appears, then a moment longer
        WaitForWaveEnd,   // The wave runs (no bubble) until it is cleared
        GiftCards,        // Say, and `count` copies of `operatorData` go into the deck
        Retreat,          // Task: wait until `operatorData` is retreated
        Finish,           // Say; a click ends the tutorial and shows the stage's results screen
        OpenTutorialStage,// Map intro only. Task: open STAGE 00 and press PLAY
        DefeatChoice      // Defeat only. TRY AGAIN (restart the tutorial) or SKIP TUTORIAL (to the stage map)
    }

    /// <summary>What Coalesce points at while a step is shown.</summary>
    public enum TutorialHighlight
    {
        None,
        Lives,
        DeckCard,        // The deck slot holding `operatorData`
        StartWave,
        DraftCard,       // The offered card showing `operatorData`
        PathTiles,       // Every tile ground operators can stand on
        HighGroundTiles,
        Operator,        // The tile of the deployed `operatorData`
        SpawnAndExit,
        StageNode        // Map intro: the marker of `stageNumber` (0 = the tutorial)
    }

    /// <summary>The little pose Coalesce strikes as a line starts. Leans and tilts go toward the speech bubble.</summary>
    public enum TutorialEmote
    {
        Nod,     // A small nod: plain explaining
        Happy,   // One hop: praise, greetings
        Curious, // A head tilt held for a moment: questions, new ideas
        Worried, // Shrinks back and shivers: threats, warnings
        Excited, // Two bouncy hops: big moments
        Point    // Leans in toward the screen: "look here", instructions
    }

    /// <summary>
    /// When a step plays; steps whose condition is false are skipped. Life conditions read the lives left when the
    /// step is reached (the tutorial has 3: all = NoLivesLost, 2 = SomeLivesLost, 1 = OneLifeLeft).
    /// </summary>
    public enum TutorialCondition
    {
        Always,
        FirstVisit,       // The tutorial hadn't been finished or skipped when this run started
        ReturningPlayer,  // It had: a replay from STAGE 00
        NoLivesLost,
        SomeLivesLost,    // At least one life lost, more than one left
        OneLifeLeft,
        OperatorOnField,  // `operatorData` is deployed
        OperatorInDeck,   // Not deployed, but a card of it is in the deck (e.g. recalled earlier)
        OperatorGone      // Neither: it was knocked out
    }

    [Serializable]
    public class TutorialStep
    {
        public TutorialAction action;

        [Tooltip("Coalesce's line. For task steps this is the instruction shown while the player acts.")]
        [TextArea(2, 6)] public string text;

        [Tooltip("Extra pointer. Draft, Deploy, StartWave and Retreat steps already point at what they wait for.")]
        public TutorialHighlight highlight;

        [Tooltip("Operator to pick, deploy, retreat, gift or point at")]
        public OperatorData operatorData;

        [Tooltip("WaitForEnemy: the enemy whose first spawn ends the wait")]
        public EnemyData enemyData;

        [Tooltip("Draft: the hand dealt, left to right")]
        public OperatorData[] cards;

        [Tooltip("GiftCards: how many copies")]
        [Min(1)] public int count = 1;

        [Tooltip("StageNode highlight: which map marker (0 = the tutorial)")]
        [Min(0)] public int stageNumber;

        [Tooltip("Coalesce's pose as the line starts (Coalesce only shows up for lines with text)")]
        public TutorialEmote emote;
        [Tooltip("Play this step only when the condition holds (e.g. lines for first-timers vs returning players)")]
        public TutorialCondition condition;
    }

    /// <summary>
    /// Coalesce's tutorial, at Resources/Tutorial/TutorialScript so builds can load it; edit the lines and steps
    /// freely. <see cref="mapIntro"/> plays on the stage map until the tutorial is done (MapTutorialGuide);
    /// <see cref="steps"/> play on STAGE_TUTORIAL itself (TutorialDirector), for first-timers and returning players;
    /// <see cref="onDefeat"/> plays over the defeat screen if the tutorial is lost.
    /// </summary>
    [CreateAssetMenu(fileName = "TutorialScript", menuName = "TRASH TD/Tutorial Script")]
    public class TutorialScript : ScriptableObject
    {
        [Tooltip("Name on the speech bubble")]
        public string speakerName = "COALESCE";

        [Tooltip("Operator whose sprite talks (its portrait sheet is animated while text types)")]
        public OperatorData speaker;

        [Tooltip("Stage map, first visit: Say steps, ending with OpenTutorialStage")]
        public TutorialStep[] mapIntro;

        [Tooltip("The tutorial stage")]
        public TutorialStep[] steps;

        [Tooltip("Over the defeat screen: Say steps, ending with DefeatChoice")]
        public TutorialStep[] onDefeat;
        [Tooltip("When a hostile costs a life mid-wave (and lives remain): Say steps; the first one whose condition holds plays")]
        public TutorialStep[] onLifeLost;
        [Tooltip("StartWave steps: shown instead of the step's line while no operator is on the field (empty = built-in line)")]
        [TextArea(2, 6)] public string emptyFieldWarning;
    }
}
