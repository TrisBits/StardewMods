using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Pathoschild.Stardew.Common;
using Pathoschild.Stardew.DataLayers.Framework;
using StardewValley;
using SObject = StardewValley.Object;

namespace Pathoschild.Stardew.DataLayers.Layers.Coverage
{
    /// <summary>A data layer which shows bee house coverage.</summary>
    internal class BeeHouseLayer : BaseLayer
    {
        /*********
        ** Fields
        *********/
        /// <summary>The legend entry for tiles covered by a bee house.</summary>
        private readonly LegendEntry Covered;

        /// <summary>The border color for the bee house under the cursor.</summary>
        private readonly Color SelectedColor;

        /// <summary>The maximum number of tiles from the center a bee house can cover.</summary>
        private readonly int MaxRadius = 5;

        /// <summary>The relative tile coordinates covered by a bee house.</summary>
        private readonly Vector2[] RelativeRange;


        /*********
        ** Public methods
        *********/
        /// <summary>Construct an instance.</summary>
        /// <param name="config">The data layer settings.</param>
        /// <param name="colors">The colors to render.</param>
        public BeeHouseLayer(LayerConfig config, ColorScheme colors)
            : base(I18n.BeeHouses_Name(), config)
        {
            const string layerId = "BeeHouseCoverage";

            this.SelectedColor = colors.Get(layerId, "Selected", Color.Blue);
            this.Legend = [
                this.Covered = new LegendEntry(I18n.Keys.BeeHouses_Range, colors.Get(layerId, "Covered", Color.Green))
            ];
            this.RelativeRange = BeeHouseLayer
                .GetRelativeCoverage()
                .ToArray();
        }

        /// <summary>Get the updated data layer tiles.</summary>
        /// <param name="location">The current location.</param>
        /// <param name="visibleArea">The tile area currently visible on the screen.</param>
        /// <param name="visibleTiles">The tile positions currently visible on the screen.</param>
        /// <param name="cursorTile">The tile position under the cursor.</param>
        public override TileGroup[] Update(GameLocation location, in Rectangle visibleArea, in Vector2[] visibleTiles, in Vector2 cursorTile)
        {
            // get bee houses
            Vector2[] searchTiles = visibleArea.Expand(this.MaxRadius).GetTiles().ToArray();
            SObject[] beeHouses =
                (
                    from Vector2 tile in searchTiles
                    where location.objects.ContainsKey(tile)
                    let beeHouse = location.objects[tile]
                    where this.IsBeeHouse(beeHouse)
                    select beeHouse
                )
                .ToArray();

            // yield coverage
            var groups = new List<TileGroup>();
            foreach (SObject beeHouse in beeHouses)
            {
                TileData[] tiles = this
                    .GetCoverage(location, beeHouse.TileLocation)
                    .Select(pos => new TileData(pos, this.Covered))
                    .ToArray();

                groups.Add(new TileGroup(tiles, outerBorderColor: beeHouse.TileLocation == cursorTile ? this.SelectedColor : this.Covered.Color));
            }

            // yield bee house being placed
            SObject heldObj = Game1.player.ActiveObject;
            if (this.IsBeeHouse(heldObj))
            {
                var tiles = this
                    .GetCoverage(location, cursorTile)
                    .Select(pos => new TileData(pos, this.Covered, color: this.Covered.Color * 0.75f));
                groups.Add(new TileGroup(tiles, outerBorderColor: this.SelectedColor, shouldExport: false));
            }

            return groups.ToArray();
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Get whether a map object is a bee house.</summary>
        /// <param name="obj">The map object.</param>
        private bool IsBeeHouse(SObject? obj)
        {
            return obj != null && obj.bigCraftable.Value && obj.Name == "Bee House";
        }

        /// <summary>Get a bee house tile radius.</summary>
        /// <param name="location">The bee house's location.</param>
        /// <param name="origin">The bee house's tile.</param>
        /// <remarks>Derived from <see cref="SObject.checkForAction"/> and <see cref="Utility.findCloseFlower"/>.</remarks>
        private IEnumerable<Vector2> GetCoverage(GameLocation location, Vector2 origin)
        {
            if (!location.IsOutdoors)
                yield break; // bee houses are hardcoded to only work outdoors

            foreach (Vector2 relativeTile in this.RelativeRange)
                yield return origin + relativeTile;
        }

        /// <summary>Get the relative tiles covered by a bee house.</summary>
        /// <remarks>Derived from <see cref="Utility.findCloseFlower"/>.</remarks>
        private static IEnumerable<Vector2> GetRelativeCoverage()
        {
            const int range = 5;

            Queue<Vector2> queue = new Queue<Vector2>();
            HashSet<Vector2> visited = [];
            queue.Enqueue(Vector2.Zero);
            while (queue.Count > 0)
            {
                Vector2 tile = queue.Dequeue();
                yield return tile;
                foreach (Vector2 adjacentTile in Utility.getAdjacentTileLocations(tile))
                {
                    if (!visited.Contains(adjacentTile) && (Math.Abs(adjacentTile.X) + (double)Math.Abs(adjacentTile.Y)) <= range)
                        queue.Enqueue(adjacentTile);
                }
                visited.Add(tile);
            }
        }
    }
}
