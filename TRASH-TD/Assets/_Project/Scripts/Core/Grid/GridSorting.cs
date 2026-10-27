using UnityEngine;
using UnityEngine.Rendering;

namespace TrashTD.Core.Grid
{
    /// <summary>
    /// Stage sorting layers, back to front:
    /// Ground (map art and decals beneath units), Units (operators and enemies, ordered per tile
    /// by <c>GridSpriteSorter</c>), Default (everything drawn above units: FX, weather, overlays).
    /// </summary>
    public static class GridSorting
    {
        public const string GroundLayerName = "Ground";
        public const string UnitsLayerName = "Units";
        public const string DefaultLayerName = "Default";

        public static int GroundLayerId => SortingLayer.NameToID(GroundLayerName);
        public static int UnitsLayerId => SortingLayer.NameToID(UnitsLayerName);
        public static int DefaultLayerId => SortingLayer.NameToID(DefaultLayerName);

        /// <summary>Moves a renderer to the Ground layer, beneath every unit.</summary>
        public static void PlaceOnGround(Renderer renderer)
        {
            if (renderer != null) renderer.sortingLayerID = GroundLayerId;
        }

        /// <summary>
        /// Lifts an overlay parented to a unit (health bar, badge, heat bar) out of the unit's
        /// SortingGroup so it draws on the Default layer above every unit.
        /// </summary>
        public static void SortAboveUnits(GameObject overlay, int sortingOrder)
        {
            if (overlay == null) return;

            SortingGroup group = overlay.GetComponent<SortingGroup>();
            if (group == null) group = overlay.AddComponent<SortingGroup>();
            group.sortAtRoot = true;
            group.sortingLayerID = DefaultLayerId;
            group.sortingOrder = sortingOrder;
        }
    }
}
