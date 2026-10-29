using System.Collections;
using UnityEngine;
using TrashTD.Core.GameLoop;
using TrashTD.Data;

namespace TrashTD.UI
{
    /// <summary>
    /// Coalesce on the stage map until the tutorial has been finished or skipped: introduces themself, the map and
    /// its markers (TutorialScript.mapIntro), then asks the player to open STAGE 00 and press PLAY. Until then
    /// only STAGE 00 can be opened (MainMenuController.SelectStage). SKIP TUTORIAL marks the tutorial done and
    /// frees the map. The intro plays once per session; later visits go straight to the STAGE 00 request.
    /// </summary>
    public sealed class MapTutorialGuide : MonoBehaviour
    {
        private const string ScriptResourcePath = "Tutorial/TutorialScript";

        // Domain reload is off: reset per play session
        private static bool introHeard;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetSession() => introHeard = false;

        private MainMenuController menu;
        private TutorialDialogueUI dialogue;
        private bool advanced;
        private bool closed;

        /// <summary>Only STAGE 00 can be opened while Coalesce is guiding the map.</summary>
        public bool RequiresTutorialStage => !closed;

        /// <summary>Coalesce is mid-line: the map shouldn't pan or zoom underneath.</summary>
        public bool IsTalking => !closed && dialogue != null && dialogue.IsBlockingGame;

        public static MapTutorialGuide Create(MainMenuController menu)
        {
            var script = Resources.Load<TutorialScript>(ScriptResourcePath);
            if (script == null || script.mapIntro == null || script.mapIntro.Length == 0) return null;

            var go = new GameObject("MapTutorialGuide");
            var guide = go.AddComponent<MapTutorialGuide>();
            guide.Initialize(menu, script);
            return guide;
        }

        private void Initialize(MainMenuController owner, TutorialScript script)
        {
            menu = owner;
            Sprite[] frames = UnitSpriteFrames.GetOperatorFrames(script.speaker, out float frameDuration);
            dialogue = TutorialDialogueUI.Create(script.speakerName, frames, frameDuration);
            dialogue.SetLayout(TutorialDialogueLayout.StageMap);
            dialogue.SetSpotlight(true);
            dialogue.Advanced += HandleAdvanced;
            dialogue.SkipConfirmed += HandleSkipConfirmed;
            StartCoroutine(Run(script));
        }

        private void OnDestroy()
        {
            if (dialogue == null) return;
            dialogue.Advanced -= HandleAdvanced;
            dialogue.SkipConfirmed -= HandleSkipConfirmed;
            Destroy(dialogue.gameObject);
        }

        private void Update()
        {
            if (dialogue != null) dialogue.SetShown(!closed && menu != null && menu.IsStageSelectionOpen);
        }

        private void HandleAdvanced() => advanced = true;

        private void HandleSkipConfirmed()
        {
            TutorialDirector.MarkTutorialDone();
            Close();
        }

        private IEnumerator Run(TutorialScript script)
        {
            // Let the map lay its markers out first
            yield return null;

            TutorialStep request = null;
            foreach (TutorialStep step in script.mapIntro)
            {
                if (step == null) continue;
                if (step.action == TutorialAction.OpenTutorialStage)
                {
                    request = step;
                    break;
                }
                if (introHeard) continue;

                advanced = false;
                dialogue.Say(step.text, false, step.emote);
                while (!advanced && !closed)
                {
                    dialogue.PointAt(step.highlight == TutorialHighlight.StageNode ? menu.GetStageNodeRect(step.stageNumber) : null);
                    yield return null;
                }
                if (closed) yield break;
            }
            introHeard = true;

            if (request == null)
            {
                Close();
                yield break;
            }

            // Open STAGE 00, then PLAY; the pointer follows whichever is next
            dialogue.Say(request.text, true, request.emote);
            while (!closed)
            {
                dialogue.PointAt(menu.IsTutorialDossierOpen ? menu.PlayStageButtonRect : menu.GetStageNodeRect(0));
                yield return null;
            }
        }

        private void Close()
        {
            if (closed) return;
            closed = true;
            Destroy(gameObject);
        }
    }
}
