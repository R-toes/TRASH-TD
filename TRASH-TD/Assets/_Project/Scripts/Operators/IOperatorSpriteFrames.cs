using UnityEngine;

namespace TrashTD.Operators
{
    /// <summary>
    /// The frames an operator prefab's sprite animation cycles through, so other code (e.g. Reclaimers' Intel)
    /// can play the same animation without instantiating the prefab. Mirrors <c>IEnemySpriteFrames</c>.
    /// </summary>
    public interface IOperatorSpriteFrames
    {
        Sprite[] Frames { get; }
        float FrameDuration { get; }
    }
}
