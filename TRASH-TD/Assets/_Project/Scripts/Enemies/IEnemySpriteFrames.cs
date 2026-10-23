using UnityEngine;

namespace TrashTD.Enemies
{
    /// <summary>
    /// The frames an enemy prefab's sprite-sheet animation cycles through, so other code (e.g. the title-screen
    /// parade, MainMenuBackdrop) can play the same animation without instantiating the prefab.
    /// </summary>
    public interface IEnemySpriteFrames
    {
        Sprite[] Frames { get; }
        float FrameDuration { get; }
    }
}
