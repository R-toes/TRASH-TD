using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Enemies
{
    /// <summary>
    /// Oculite Spawn enemy (Grunt archetype).
    /// Features:
    /// - High DEF, low RES.
    /// - Immune to Stage 6 acid rain and acid/trap tiles.
    /// - Not normally spawned in stage waves; spawned when Oculoid Carrier is defeated.
    /// </summary>
    public class OculiteSpawnEnemy : EnemyBase
    {
        public override bool IsImmuneToAcidRain => true;
        public override bool IsImmuneToAcidTiles => true;
    }
}

