namespace TrashTD.Data
{
    /// <summary>
    /// The five operator classes as defined in GDD 1.3.
    /// </summary>
    public enum OperatorClass
    {
        Guard,      // Melee DPS — high single-target damage, blocks 1–2
        Defender,   // Tank — high DEF/HP, blocks 2–4, low damage
        Sniper,     // Ranged DPS — high single-target damage, no blocking
        Caster,     // Ranged AoE (magic) — Arts damage bypasses DEF, hits RES
        Medic       // Healer — restores HP to allies in range
    }

    /// <summary>
    /// Operator rarity tiers (GDD 1.6).
    /// Card draft offers 1★–3★ only; 4★ and 5★ are reached via upgrading.
    /// </summary>
    public enum OperatorRarity
    {
        Star1 = 1,
        Star2 = 2,
        Star3 = 3,
        Star4 = 4,
        Star5 = 5
    }

    /// <summary>
    /// Whether an operator deploys on melee (Low Ground) or ranged (High Ground) tiles.
    /// </summary>
    public enum OperatorPosition
    {
        Melee,
        Ranged
    }

    /// <summary>
    /// Damage types for the combat system (GDD 1.4.5).
    /// Physical → mitigated by DEF; Arts → mitigated by RES.
    /// </summary>
    public enum DamageType
    {
        Physical,
        Arts
    }

    /// <summary>
    /// Tile types for the grid system (GDD 1.4.1).
    /// </summary>
    public enum TileType
    {
        LowGround,     // Melee-deployable ground, including the enemy lane
        HighGround,     // Ranged-deployable (sometimes melee)
        Blocked,        // Hazard — spikes, water, unwalkable
        EnemyPath,      // Legacy alias for a ground enemy lane; use LowGround for new stages
        SpawnPoint,     // Enemy entry point
        ExitPoint       // Objective — enemies reaching here cost life points
    }

    /// <summary>
    /// Enemy movement categories (GDD 1.5).
    /// Ground enemies follow paths; Air enemies ignore ground pathing.
    /// </summary>
    public enum EnemyMovementType
    {
        Ground,
        Air
    }

    /// <summary>
    /// Enemy archetype categories (GDD 1.5).
    /// </summary>
    public enum EnemyArchetype
    {
        Grunt,      // Basic ground unit
        Rusher,     // Fast movement
        Tank,       // High HP/DEF
        Caster,     // Ranged, ignores blockers
        Flyer       // Ignores ground path (Air movement)
    }

    /// <summary>
    /// Stage difficulty variants (GDD 1.4.7).
    /// Life points: Easy=10, Normal=5, Hard=1.
    /// </summary>
    public enum StageDifficulty
    {
        Easy,
        Normal,
        Hard
    }
}
