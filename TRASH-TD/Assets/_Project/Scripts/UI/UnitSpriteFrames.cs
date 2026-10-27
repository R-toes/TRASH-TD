using System.Collections.Generic;
using UnityEngine;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Operators;

namespace TrashTD.UI
{
    /// <summary>
    /// Resolves the frames a unit plays in gameplay, straight from its data and prefab (no instantiation),
    /// for screens that show units outside a stage: the title-screen parade and Reclaimers' Intel.
    /// </summary>
    public static class UnitSpriteFrames
    {
        /// <summary>
        /// The enemy's walk cycle as gameplay plays it: <see cref="EnemyData.animationFrames"/>, otherwise the
        /// prefab's sheet animation (only when the prefab's sprite isn't overridden, as in EnemyManager).
        /// Returns false when the enemy has fewer than two frames (e.g. placeholder art).
        /// </summary>
        public static bool TryGetEnemyAnimation(EnemyData data, out Sprite[] frames, out float frameRate)
        {
            frames = CollectFrames(data != null ? data.animationFrames : null);
            frameRate = data != null && data.animationFrameRate > 0f ? data.animationFrameRate : 8f;
            if (frames.Length >= 2 || data == null) return frames.Length >= 2;

            if (data.enemyPrefab != null)
            {
                IEnemySpriteFrames prefabAnimation = data.enemyPrefab.GetComponentInChildren<IEnemySpriteFrames>(true);
                SpriteRenderer renderer = data.enemyPrefab.GetComponentInChildren<SpriteRenderer>(true);
                bool spriteOverridden = data.sprite != null && renderer != null && renderer.sprite != data.sprite;
                if (prefabAnimation != null && !spriteOverridden)
                {
                    frames = CollectFrames(prefabAnimation.Frames);
                    frameRate = prefabAnimation.FrameDuration > 0f ? 1f / prefabAnimation.FrameDuration : 4f;
                    if (frames.Length >= 2) return true;
                }
            }

            return false;
        }

        /// <summary>The enemy's frames, or its single sprite when it has no animation.</summary>
        public static Sprite[] GetEnemyFrames(EnemyData data, out float frameRate)
        {
            if (TryGetEnemyAnimation(data, out Sprite[] frames, out frameRate)) return frames;

            Sprite still = data != null ? data.sprite : null;
            if (still == null && data != null && data.enemyPrefab != null)
            {
                SpriteRenderer renderer = data.enemyPrefab.GetComponentInChildren<SpriteRenderer>(true);
                if (renderer != null) still = renderer.sprite;
            }
            return still != null ? new[] { still } : new Sprite[0];
        }

        /// <summary>
        /// The operator's idle loop as gameplay plays it: a prefab animation with assigned frames
        /// (Bulkhead, Deadeye, ...), otherwise the sheet sliced the way <see cref="OperatorSpriteAnimation"/> does.
        /// </summary>
        public static Sprite[] GetOperatorFrames(OperatorData data, out float frameDuration)
        {
            frameDuration = 0.25f;
            if (data == null) return new Sprite[0];

            Sprite sample = data.portrait;
            if (data.operatorPrefab != null)
            {
                foreach (IOperatorSpriteFrames animation in data.operatorPrefab.GetComponentsInChildren<IOperatorSpriteFrames>(true))
                {
                    Sprite[] assigned = CollectFrames(animation.Frames);
                    if (assigned.Length >= 2 && assigned.Length == (animation.Frames?.Length ?? 0))
                    {
                        frameDuration = animation.FrameDuration > 0f ? animation.FrameDuration : 0.25f;
                        return assigned;
                    }
                    if (animation is OperatorSpriteAnimation sheetAnimation) frameDuration = sheetAnimation.FrameDuration;
                }

                SpriteRenderer renderer = data.operatorPrefab.GetComponentInChildren<SpriteRenderer>(true);
                if (sample == null && renderer != null) sample = renderer.sprite;
            }

            if (sample == null) return new Sprite[0];

            frameDuration = OperatorSpriteAnimation.ResolveFrameDuration(sample, frameDuration);
            Sprite[] sliced = CollectFrames(OperatorSpriteAnimation.SliceSheet(sample));
            return sliced.Length > 0 ? sliced : new[] { sample };
        }

        public static Sprite[] CollectFrames(Sprite[] source)
        {
            var frames = new List<Sprite>();
            if (source != null)
            {
                foreach (Sprite frame in source)
                {
                    if (frame != null) frames.Add(frame);
                }
            }
            return frames.ToArray();
        }
    }
}
