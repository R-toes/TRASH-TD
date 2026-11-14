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
    /// Thornchin — 2-Star Defender (3 Block).
    /// Ability: Bristling Spines — Whenever Thornchin is attacked, it retaliates against
    /// the attacker with sharp urchin spines, dealing Physical damage equal to Thornchin's ATK.
    /// 1x Upgraded: Retaliate spikes deal an additional DOT of 5 Arts damage for 5 seconds (stacks up to 4 times) and deal additional AoE damage in adjacent tiles.
    /// 2x Upgraded: AoE damage expands to all surrounding tiles.
    /// </summary>
    public class ThornchinOperator : DefenderOperator
    {
        [Header("Audio")]
        [Tooltip("Custom sound played when retaliating directly against an attacker. If null, loads from SFX/Operators/Defender/Thornchin/Ability_Retaliate (supports Ability_Retaliate, Retaliate, Ability...), falling back to MeleeAttack.")]
        [SerializeField] private AudioClip retaliateSound;
        [Tooltip("Optional array of retaliate sounds to randomize between in Inspector.")]
        [SerializeField] private AudioClip[] retaliateSounds;

        [Tooltip("Custom sound played when adjacent/surrounding spikes burst into neighboring tiles. If null, loads from SFX/Operators/Defender/Thornchin/Ability_Surrounding (supports Surrounding, Adjacent, AoE...), falling back to retaliate sound.")]
        [SerializeField] private AudioClip surroundingSound;
        [Tooltip("Optional array of surrounding/adjacent sounds to randomize between in Inspector.")]
        [SerializeField] private AudioClip[] surroundingSounds;

        public AudioClip RetaliateSound
        {
            get => retaliateSound;
            set => retaliateSound = value;
        }

        public AudioClip[] RetaliateSounds
        {
            get => retaliateSounds;
            set => retaliateSounds = value;
        }

        public AudioClip SurroundingSound
        {
            get => surroundingSound;
            set => surroundingSound = value;
        }

        public AudioClip[] SurroundingSounds
        {
            get => surroundingSounds;
            set => surroundingSounds = value;
        }

        public AudioClip AbilitySound
        {
            get => retaliateSound;
            set => retaliateSound = value;
        }

        public AudioClip[] AbilitySounds
        {
            get => retaliateSounds;
            set => retaliateSounds = value;
        }

        public AudioClip AdjacentSound
        {
            get => surroundingSound;
            set => surroundingSound = value;
        }

        public AudioClip[] AdjacentSounds
        {
            get => surroundingSounds;
            set => surroundingSounds = value;
        }

        public AudioClip AoESound
        {
            get => surroundingSound;
            set => surroundingSound = value;
        }

        public AudioClip[] AoESounds
        {
            get => surroundingSounds;
            set => surroundingSounds = value;
        }

        private static readonly string[] RetaliateSoundNames = new[] { "Ability_Retaliate", "Retaliate", "Ability" };
        private static readonly string[] SurroundingSoundNames = new[] { "Ability_Surrounding", "Surrounding", "Ability_Adjacent", "Adjacent", "Ability_AoE", "AoE" };

        public void PlayRetaliateSound()
        {
            // 1. Inspector array (randomized)
            if (retaliateSounds != null && retaliateSounds.Length > 0)
            {
                AudioClip clip = retaliateSounds[Random.Range(0, retaliateSounds.Length)];
                if (clip != null)
                {
                    AudioManager.Instance?.PlaySfx(clip, 1.0f, Random.Range(0.96f, 1.04f));
                    return;
                }
            }

            // 2. Inspector single slot
            if (retaliateSound != null)
            {
                AudioManager.Instance?.PlaySfx(retaliateSound, 1.0f, Random.Range(0.96f, 1.04f));
                return;
            }

            // 3. Conventional resource lookup (SFX/Operators/Defender/Thornchin/Ability_Retaliate, Retaliate, Ability...)
            string folder = data != null ? UnitSfx.GetFolder(data) : "SFX/Operators/Defender/Thornchin";
            if (UnitSfx.PlayRandomFromFolder(folder, RetaliateSoundNames, out AudioSource voice, out AudioClip resClip, 1.0f))
            {
                return;
            }

            // 4. Shared melee/spikes sound fallback
            AudioManager.Instance?.PlaySfx(SfxId.GameplayMeleeAttack, 0.9f, Random.Range(0.96f, 1.04f));
        }

        public void PlaySurroundingSound()
        {
            // 1. Inspector array (randomized)
            if (surroundingSounds != null && surroundingSounds.Length > 0)
            {
                AudioClip clip = surroundingSounds[Random.Range(0, surroundingSounds.Length)];
                if (clip != null)
                {
                    AudioManager.Instance?.PlaySfx(clip, 1.0f, Random.Range(0.96f, 1.04f));
                    return;
                }
            }

            // 2. Inspector single slot
            if (surroundingSound != null)
            {
                AudioManager.Instance?.PlaySfx(surroundingSound, 1.0f, Random.Range(0.96f, 1.04f));
                return;
            }

            // 3. Conventional resource lookup (SFX/Operators/Defender/Thornchin/Ability_Surrounding, Surrounding, Ability_Adjacent, Adjacent, Ability_AoE, AoE...)
            string folder = data != null ? UnitSfx.GetFolder(data) : "SFX/Operators/Defender/Thornchin";
            if (UnitSfx.PlayRandomFromFolder(folder, SurroundingSoundNames, out AudioSource voice, out AudioClip resClip, 1.0f))
            {
                return;
            }

            // 4. Fall back to retaliate sound if no dedicated surrounding sound exists
            PlayRetaliateSound();
        }

        private static readonly Color SpineColor = new Color(0.75f, 0.2f, 0.85f, 1f);

        private static readonly Vector2Int[] BaseRangePattern = new[]
        {
            Vector2Int.zero
        };

        private static readonly Vector2Int[] AdjacentRangePattern = new[]
        {
            Vector2Int.zero,
            Vector2Int.left,
            Vector2Int.right,
            Vector2Int.up,
            Vector2Int.down
        };

        private static readonly Vector2Int[] SurroundingRangePattern = new[]
        {
            Vector2Int.zero,
            Vector2Int.left,
            Vector2Int.right,
            Vector2Int.up,
            Vector2Int.down,
            new Vector2Int(-1, 1),
            new Vector2Int(1, 1),
            new Vector2Int(-1, -1),
            new Vector2Int(1, -1)
        };

        private static readonly Vector2Int[] AdjacentDirs = new[]
        {
            Vector2Int.left,
            Vector2Int.right,
            Vector2Int.up,
            Vector2Int.down
        };

        private static readonly Vector2Int[] SurroundingDirs = new[]
        {
            Vector2Int.left,
            Vector2Int.right,
            Vector2Int.up,
            Vector2Int.down,
            new Vector2Int(-1, 1),  // Up-Left
            new Vector2Int(1, 1),   // Up-Right
            new Vector2Int(-1, -1), // Down-Left
            new Vector2Int(1, -1)   // Down-Right
        };

        private GridManager cachedGridManager;
        private GridManager GridManagerCache => cachedGridManager != null ? cachedGridManager : (cachedGridManager = FindAnyObjectByType<GridManager>());

        public bool HasFirstUpgrade => currentRarity >= OperatorRarity.Star3;
        public bool HasSecondUpgrade => currentRarity >= OperatorRarity.Star4;

        public void SetRarityForTesting(OperatorRarity rarity)
        {
            currentRarity = rarity;
        }

        public override Vector2Int[] GetRangePattern()
        {
            if (HasSecondUpgrade)
            {
                return SurroundingRangePattern;
            }
            if (HasFirstUpgrade)
            {
                return AdjacentRangePattern;
            }
            return data != null && data.rangePattern != null ? data.rangePattern : BaseRangePattern;
        }

        public static Vector2Int[] GetRangePatternForRarity(OperatorData operatorData, OperatorRarity rarity)
        {
            if (rarity >= OperatorRarity.Star4)
            {
                return SurroundingRangePattern;
            }
            if (rarity >= OperatorRarity.Star3)
            {
                return AdjacentRangePattern;
            }
            return operatorData != null && operatorData.rangePattern != null ? operatorData.rangePattern : BaseRangePattern;
        }

        public override void TakeDamage(int rawATK, DamageType damageType, EnemyBase attacker = null)
        {
            if (IsCombatPaused) return;

            base.TakeDamage(rawATK, damageType, attacker);

            // Retaliate against the attacker when attacked by an enemy.
            // Environmental damage (acid tiles, acid rain, etc.) has no attacker and will not trigger retaliation.
            if (attacker != null && !attacker.IsDead)
            {
                Retaliate(attacker);
            }
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            MeleeSwipeVisual.Play(transform.position, target.VisualPosition, SpineColor, 1f, MeleeStyle.Spikes);
            base.Attack(target);
        }

        private void Retaliate(EnemyBase target)
        {
            if (target == null || target.IsDead) return;

            // Direct attacker retaliation: shoot urchin spike projectile
            if (!Application.isPlaying)
            {
                ApplyRetaliationDamage(target);
            }

            CombatProjectileVisual.Fire(
                ProjectileOrigin,
                target,
                SpineColor,
                16f,
                0.09f,
                0.04f,
                false,
                () =>
                {
                    if (Application.isPlaying && target != null && !target.IsDead)
                    {
                        ApplyRetaliationDamage(target);
                    }
                },
                ProjectileStyle.ThornchinSpike);

            // 2x Upgrade (4★): Deals additional AOE damage in all 8 surrounding tiles
            if (HasSecondUpgrade)
            {
                TriggerSurroundingSpikes(target);
            }
            // 1x Upgrade (3★): Deals additional AOE damage in the 4 adjacent tiles
            else if (HasFirstUpgrade)
            {
                TriggerAdjacentSpikes(target);
            }
        }

        private void TriggerAdjacentSpikes(EnemyBase directAttacker)
        {
            PlaySurroundingSound();
            TriggerSpikesInDirections(AdjacentDirs, directAttacker);
        }

        private void TriggerSurroundingSpikes(EnemyBase directAttacker)
        {
            PlaySurroundingSound();
            TriggerSpikesInDirections(SurroundingDirs, directAttacker);
        }

        private void TriggerSpikesInDirections(Vector2Int[] dirs, EnemyBase directAttacker)
        {
            GridManager grid = GridManagerCache;
            Vector2Int myGridPos = deployedCell != null
                ? deployedCell.GridPosition
                : (grid != null ? grid.WorldToGridPosition(transform.position) : Vector2Int.zero);

            float cellSize = grid != null ? grid.CellSize : 1f;

            for (int i = 0; i < dirs.Length; i++)
            {
                Vector2Int dir = dirs[i];
                Vector2Int adjGridPos = myGridPos + dir;
                GridCell adjCell = grid != null ? grid.GetCell(adjGridPos) : null;
                Vector3 targetWorldPos = adjCell != null
                    ? adjCell.WorldPosition
                    : transform.position + (Vector3)(Vector2)dir * cellSize;

                if (!Application.isPlaying)
                {
                    ApplySurroundingAoEDamage(adjCell, targetWorldPos, directAttacker);
                }

                CombatProjectileVisual.Fire(
                    ProjectileOrigin,
                    targetWorldPos,
                    SpineColor,
                    16f,
                    0.09f,
                    0.04f,
                    false,
                    () =>
                    {
                        if (Application.isPlaying)
                        {
                            ApplySurroundingAoEDamage(adjCell, targetWorldPos, directAttacker);
                        }
                    },
                    ProjectileStyle.ThornchinSpike);
            }
        }

        private void ApplySurroundingAoEDamage(GridCell cell, Vector3 worldPos, EnemyBase directAttacker)
        {
            List<EnemyBase> enemies = null;
            if (cell != null && EnemyManager.Instance != null)
            {
                enemies = EnemyManager.Instance.GetEnemiesInCells(new List<GridCell> { cell });
            }
            else if (EnemyManager.Instance != null)
            {
                enemies = EnemyManager.Instance.GetEnemiesInRadius(worldPos, 0.6f, OperatorPosition.Melee);
            }
            else
            {
                // Fallback for unit tests when EnemyManager singleton is not instantiated
                var allEnemies = Object.FindObjectsByType<EnemyBase>();
                if (allEnemies != null && allEnemies.Length > 0)
                {
                    enemies = new List<EnemyBase>();
                    for (int i = 0; i < allEnemies.Length; i++)
                    {
                        var e = allEnemies[i];
                        if (e != null && !e.IsDead && Vector2.Distance(e.transform.position, worldPos) <= 0.6f)
                        {
                            enemies.Add(e);
                        }
                    }
                }
            }

            if (enemies == null || enemies.Count == 0) return;

            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyBase enemy = enemies[i];
                if (enemy == null || enemy.IsDead) continue;
                if (enemy == directAttacker) continue; // Direct attacker already hit

                ApplySpikeDamageAndDot(enemy);
            }
        }

        private void ApplySpikeDamageAndDot(EnemyBase target)
        {
            int damage = DamageCalculator.CalculateDamage(currentATK, target.CurrentDEF);
            target.TryTakeAttackDamage(damage, data != null ? data.damageType : DamageType.Physical, this, canHitAir: true);

            // 1x Upgrade: Retaliate spikes deal additional DOT that deals 5 Arts damage for 5 seconds (stacks up to 4 times)
            if (HasFirstUpgrade && !target.IsDead)
            {
                ThornchinSpikeDOT.Apply(target, this);
            }
        }

        public void ApplyRetaliationDamage(EnemyBase target)
        {
            if (target == null || target.IsDead) return;

            PlayRetaliateSound();
            ApplySpikeDamageAndDot(target);
        }
    }
}
