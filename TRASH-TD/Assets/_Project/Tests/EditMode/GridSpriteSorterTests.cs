using NUnit.Framework;
using UnityEngine;
using TrashTD.Systems;

namespace TrashTD.Tests
{
    public class GridSpriteSorterTests
    {
        private const int GridHeight = 6;

        [Test]
        public void TileDepth_TopLeftIsZero_AndGrowsRightAndDown()
        {
            // Grid y is counted upward, so the top row is GridHeight - 1.
            Assert.AreEqual(0, GridSpriteSorter.GetTileDepth(new Vector2Int(0, GridHeight - 1), GridHeight));
            Assert.AreEqual(1, GridSpriteSorter.GetTileDepth(new Vector2Int(1, GridHeight - 1), GridHeight));
            Assert.AreEqual(1, GridSpriteSorter.GetTileDepth(new Vector2Int(0, GridHeight - 2), GridHeight));
            Assert.AreEqual(GridHeight - 1 + 4, GridSpriteSorter.GetTileDepth(new Vector2Int(4, 0), GridHeight));
        }

        [Test]
        public void DeeperTile_DrawsAboveShallowerTile_RegardlessOfArrival()
        {
            int shallowFirst = GridSpriteSorter.GetSortingOrder(3, 0, false);
            int deepLast = GridSpriteSorter.GetSortingOrder(4, GridSpriteSorter.SlotsPerTile - 1, false);
            Assert.Greater(deepLast, shallowFirst);
        }

        [Test]
        public void SameTile_FirstArrivalDrawsOnTop()
        {
            int first = GridSpriteSorter.GetSortingOrder(5, 0, false);
            int second = GridSpriteSorter.GetSortingOrder(5, 1, false);
            Assert.Greater(first, second);
        }

        [Test]
        public void Flyers_DrawAboveDeepestGroundTile_AndSortByDepthAmongThemselves()
        {
            int deepestGround = GridSpriteSorter.GetSortingOrder(40, 0, false);
            int shallowFlyer = GridSpriteSorter.GetSortingOrder(0, GridSpriteSorter.SlotsPerTile - 1, true);
            int deeperFlyer = GridSpriteSorter.GetSortingOrder(1, GridSpriteSorter.SlotsPerTile - 1, true);
            Assert.Greater(shallowFlyer, deepestGround);
            Assert.Greater(deeperFlyer, GridSpriteSorter.GetSortingOrder(0, 0, true));
        }
    }
}
