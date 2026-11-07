using System.Collections.Generic;
using UnityEngine;
using TrashTD.Audio;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Flurmonica — 2-Star Specialist (buffer). Doesn't attack or block.
    /// Range: Chillpath's 2x3 block ahead plus the tile on each side (8 tiles, not its own).
    /// Every attack interval (2 s) it plays Harmony on one ally in range: 1 stack of <see cref="HarmonyBuff"/>
    /// (4% per stack, max 3, each stack lasting 8 s on its own timer). The bonus depends on the ally's class:
    /// Guard RES, Defender DEF, Sniper/Medic attack speed, Caster Arts damage, Specialist max HP.
    /// Target: a random ally with the fewest stacks (so unbuffed allies come first). When everyone is at
    /// 3 stacks, a random ally has its oldest stack's timer restarted.
    /// Each stack's note flies to the ally and then circles it until the stack runs out (see HarmonyBuff).
    /// </summary>
    public class FlurmonicaOperator : SpecialistOperator
    {
        // Music Notes Sheet: 3 rows of 9 notes (teal, cream, gold). The note's row shows the stack count it gave.
        [SerializeField] private Sprite[] noteSprites;

        private const int NotesPerRow = 9;

        private GridManager gridManager;
        private readonly List<OperatorBase> alliesInRange = new List<OperatorBase>();

        public bool HasFirstUpgrade => currentRarity >= OperatorRarity.Star3;
        public bool HasSecondUpgrade => currentRarity >= OperatorRarity.Star4;

        public float CurrentBonusPerStack => HasSecondUpgrade ? 0.08f : (HasFirstUpgrade ? 0.06f : 0.04f);
        public int CurrentMaxStacks => (HasFirstUpgrade || HasSecondUpgrade) ? 4 : 3;
        public int StacksGrantedPerCast => HasSecondUpgrade ? 2 : 1;

        public void SetRarityForTesting(OperatorRarity rarity)
        {
            currentRarity = rarity;
        }

        protected override void Update()
        {
            if (!isDeployed) return;

            float interval = GetAttackInterval();
            attackTimer += Time.deltaTime;

            // No casting between waves; the cast stays charged.
            if (IsCombatPaused)
            {
                attackTimer = Mathf.Min(attackTimer, interval);
                return;
            }

            if (isStunned || isBound)
            {
                attackTimer = Mathf.Min(attackTimer, interval);
                if (isStunned) UpdateStunStatus(Time.deltaTime);
                if (isBound) UpdateBindStatus(Time.deltaTime);
                return;
            }

            equippedSkill?.UpdateSkill(Time.deltaTime);

            if (attackTimer < interval - HarmonyBuff.TimerSlack) return;

            OperatorBase target = ChooseBuffTarget(GetAlliesInRange());
            if (target == null)
            {
                attackTimer = interval;
                return;
            }

            // Keep the overshoot so casts stay exactly one interval apart: the 8 s stacks run on the same
            // clock, so 4 casts refresh a stack in the moment it runs out.
            attackTimer = Mathf.Clamp(attackTimer - interval, 0f, interval);
            CastHarmony(target);
        }

        /// <summary>Flurmonica never attacks enemies; its "attack" is <see cref="CastHarmony"/>.</summary>
        protected override EnemyBase FindTarget() => null;

        public override void Attack(EnemyBase target) { }

        /// <summary>
        /// Gives the ally Harmony stack(s). Music notes fly to it, then join the notes circling the ally
        /// (appearing in the orbit when the flight lands).
        /// 1x Upgraded: 6% bonus per stack, cap 4.
        /// 2x Upgraded: 8% bonus per stack, cap 4, gives 2 stacks default per cast.
        /// </summary>
        public void CastHarmony(OperatorBase target)
        {
            if (target == null || isStunned || isBound) return;

            int count = StacksGrantedPerCast;
            float bonus = CurrentBonusPerStack;
            int maxCap = CurrentMaxStacks;
            HarmonyBuff buff = HarmonyBuff.GetOrAdd(target);

            for (int i = 0; i < count; i++)
            {
                int currentActive = HarmonyBuff.ActiveStacksOn(target);
                int stacksAfter = Mathf.Min(currentActive + 1, maxCap);
                Color color = HarmonyBuff.GetStackColor(stacksAfter);
                Sprite note = PickNote(stacksAfter);

                float flightDelay = MusicNoteVisual.FlightSeconds + (i * 0.08f);
                buff.AddStack(note, color, flightDelay, bonus, maxCap);
                MusicNoteVisual.Fire(ProjectileOrigin, target, note, color);
            }

            UnitSfx.Play(data, UnitSfxEvent.Attack);
        }

        /// <summary>
        /// Picks who gets the next stack: a random ally among those with the fewest active stacks
        /// (<see cref="HarmonyBuff.ActiveStacksOn"/>). This covers every case in the spec: unbuffed allies first,
        /// then the lowest stack count, and when all are at 3, any of them (the stack then refreshes the oldest timer).
        /// </summary>
        public static OperatorBase ChooseBuffTarget(IReadOnlyList<OperatorBase> allies, System.Func<int, int> randomIndex = null)
        {
            if (allies == null || allies.Count == 0) return null;

            var fewest = new List<OperatorBase>(allies.Count);
            int lowest = int.MaxValue;
            for (int i = 0; i < allies.Count; i++)
            {
                OperatorBase ally = allies[i];
                if (ally == null) continue;

                int stacks = HarmonyBuff.ActiveStacksOn(ally);
                if (stacks < lowest)
                {
                    lowest = stacks;
                    fewest.Clear();
                }
                if (stacks == lowest) fewest.Add(ally);
            }

            if (fewest.Count == 0) return null;
            int index = randomIndex != null ? randomIndex(fewest.Count) : Random.Range(0, fewest.Count);
            return fewest[Mathf.Clamp(index, 0, fewest.Count - 1)];
        }

        private List<OperatorBase> GetAlliesInRange()
        {
            alliesInRange.Clear();
            if (OperatorManager.Instance == null || deployedCell == null || data == null || data.rangePattern == null)
                return alliesInRange;

            if (gridManager == null) gridManager = FindAnyObjectByType<GridManager>();
            if (gridManager == null) return alliesInRange;

            GridCell[] cells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
            List<OperatorBase> found = OperatorManager.Instance.GetOperatorsInCells(cells);
            for (int i = 0; i < found.Count; i++)
            {
                OperatorBase ally = found[i];
                if (ally == null || ReferenceEquals(ally, this) || !ally.IsDeployed || ally.CurrentHP <= 0) continue;
                alliesInRange.Add(ally);
            }
            return alliesInRange;
        }

        private Sprite PickNote(int stacks)
        {
            if (noteSprites == null || noteSprites.Length == 0) return null;

            int row = Mathf.Clamp(stacks, 1, CurrentMaxStacks) - 1;
            int rowStart = row * NotesPerRow;
            if (rowStart + NotesPerRow > noteSprites.Length)
            {
                rowStart = Mathf.Max(0, (noteSprites.Length / NotesPerRow - 1) * NotesPerRow);
            }
            int count = Mathf.Min(NotesPerRow, noteSprites.Length - rowStart);
            if (count <= 0) return null;
            return noteSprites[rowStart + Random.Range(0, count)];
        }
    }
}
