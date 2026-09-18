using TrashTD.Core.Grid;
using TrashTD.Data;

namespace TrashTD.Operators
{
    /// <summary>
    /// Interface for entities that can be deployed onto the grid.
    /// Part of the composition-based design for software reuse.
    /// </summary>
    public interface IDeployable
    {
        /// <summary>Deploy this entity onto a grid cell.</summary>
        bool Deploy(GridCell cell);

        /// <summary>Retreat/remove this entity from the grid.</summary>
        void Retreat();

        /// <summary>Get the Deployment Point cost.</summary>
        int GetDPCost();

        /// <summary>Whether this entity is currently deployed.</summary>
        bool IsDeployed { get; }

        /// <summary>The grid cell this entity is deployed on (null if not deployed).</summary>
        GridCell DeployedCell { get; }
    }

    /// <summary>
    /// Interface for entities that can attack enemies.
    /// </summary>
    public interface IAttacker
    {
        /// <summary>Execute an attack against a target.</summary>
        void Attack(Enemies.EnemyBase target);

        /// <summary>Get the attack range pattern.</summary>
        UnityEngine.Vector2Int[] GetRangePattern();

        /// <summary>Get the attack interval in seconds.</summary>
        float GetAttackInterval();

        /// <summary>Get the damage type dealt.</summary>
        DamageType GetDamageType();
    }

    /// <summary>
    /// Interface for entities that can block enemy movement.
    /// </summary>
    public interface IBlocker
    {
        /// <summary>Attempt to block an enemy. Returns true if successfully blocked.</summary>
        bool TryBlock(Enemies.EnemyBase enemy);

        /// <summary>Release a blocked enemy.</summary>
        void ReleaseBlock(Enemies.EnemyBase enemy);

        /// <summary>Get the maximum number of enemies this entity can block.</summary>
        int GetBlockCount();

        /// <summary>Get the current number of enemies being blocked.</summary>
        int GetCurrentBlockCount();
    }

    /// <summary>
    /// Interface for skill behaviors — composable so new skill types
    /// can be added without modifying existing operator classes.
    /// </summary>
    public interface ISkill
    {
        /// <summary>Display name of the skill.</summary>
        string SkillName { get; }

        /// <summary>Activate the skill.</summary>
        void Activate(OperatorBase caster);

        /// <summary>Get the cooldown duration in seconds.</summary>
        float GetCooldown();

        /// <summary>Whether the skill is ready to use.</summary>
        bool IsReady();

        /// <summary>Update skill state (cooldown tick, etc).</summary>
        void UpdateSkill(float deltaTime);
    }

    /// <summary>
    /// Interface for entities that can heal allies.
    /// </summary>
    public interface IHealer
    {
        /// <summary>Heal a target operator.</summary>
        void Heal(OperatorBase target);

        /// <summary>Get the healing power.</summary>
        int GetHealAmount();
    }
}
