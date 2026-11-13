namespace TrashTD.Operators
{
    /// <summary>The stats an operator's info panel shows.</summary>
    public enum OperatorStat
    {
        HP,
        ATK,
        DEF,
        RES,
        Block,
        AtkSpeed
    }

    /// <summary>
    /// One thing that currently changes an operator's stats, for the SHOW MORE panel: where it comes from
    /// (a rank-up, another operator's aura, the operator's own passive, a stage hazard), what it does, and which
    /// stats it touches. Built by <see cref="OperatorBase.CollectStatEffects"/>.
    /// </summary>
    public readonly struct StatEffect
    {
        public readonly string Source;       // e.g. "HARMONY x2 (Flurmonica)", "Bubblets' aura", "PROTEAN"
        public readonly string Description;  // e.g. "+8% RES", "ATK +25% while blocking"
        public readonly string Stats;        // short tag of the stats it touches, e.g. "ATK", "DEF / RES"
        public readonly bool Positive;       // a buff (true) or a debuff (false)

        public StatEffect(string source, string description, string stats, bool positive = true)
        {
            Source = source;
            Description = description;
            Stats = stats;
            Positive = positive;
        }
    }
}
