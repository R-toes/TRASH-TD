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
        OpenTutorialStage // Map intro only. Task: open STAGE 00 and press PLAY
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
    }

    /// <summary>
    /// Coalesce's tutorial, at Resources/Tutorial/TutorialScript so builds can load it; edit the lines and steps
    /// freely. <see cref="mapIntro"/> plays on the stage map until the tutorial is done (MapTutorialGuide);
    /// <see cref="steps"/> play on STAGE_TUTORIAL itself (TutorialDirector), for first-timers and returning players.
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
    }
}
