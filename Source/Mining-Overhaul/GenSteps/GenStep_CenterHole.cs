using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;

namespace MiningOverhaul
{//unused / legacy
    public class GenStep_CenterHole : GenStep
    {
        // Configurable fields (can be set via XML)
        public int holeRadius = 1;                    // Radius from center (1 = 3x3, 2 = 5x5, etc.)
        public TerrainDef replacementTerrain = null;  // What terrain to put in the hole (null = no change)
        public bool removeRoof = false;                // Whether to remove roofs in the hole
        public bool destroyThings = true;             // Whether to destroy things in the hole
        public bool destroyBuildings = true;          // Whether to destroy buildings in the hole
        public bool createRoughEdges = false;         // Whether to make jagged edges instead of perfect square
        public float roughnessChance = 0.3f;          // Chance for edge cells to be excluded (if createRoughEdges = true)
        
        public override int SeedPart => 420694201;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (map.TileInfo.WaterCovered)
            {
                Log.Message("GenStep_CenterHole: Skipping hole generation on water-covered map");
                return;
            }

            Log.Message($"GenStep_CenterHole: Creating hole at map center with radius {holeRadius}");

            IntVec3 mapCenter = map.Center;
            List<IntVec3> holeCells = GetHoleCells(mapCenter, map);

            int cellsProcessed = 0;
            int thingsDestroyed = 0;
            int buildingsDestroyed = 0;
            int roofsRemoved = 0;
            int terrainsChanged = 0;

            // Process each cell in the hole area
            foreach (IntVec3 cell in holeCells)
            {
                if (!cell.InBounds(map)) continue;

                cellsProcessed++;

                // Destroy things if enabled
                if (destroyThings || destroyBuildings)
                {
                    List<Thing> thingsAtCell = map.thingGrid.ThingsListAt(cell).ToList(); // ToList to avoid modification issues
                    foreach (Thing thing in thingsAtCell)
                    {
                        bool shouldDestroy = false;

                        if (destroyBuildings && (thing.def.category == ThingCategory.Building || thing is Building))
                        {
                            shouldDestroy = true;
                            buildingsDestroyed++;
                        }
                        else if (destroyThings && thing.def.category != ThingCategory.Pawn) // Don't destroy pawns
                        {
                            shouldDestroy = true;
                            thingsDestroyed++;
                        }

                        if (shouldDestroy)
                        {
                            thing.Destroy(DestroyMode.Vanish);
                        }
                    }
                }

                // Remove roof if enabled
                if (removeRoof && cell.Roofed(map))
                {
                    map.roofGrid.SetRoof(cell, null);
                    roofsRemoved++;
                }

                // Change terrain if specified
                if (replacementTerrain != null)
                {
                    TerrainDef currentTerrain = cell.GetTerrain(map);
                    if (currentTerrain != replacementTerrain)
                    {
                        map.terrainGrid.SetTerrain(cell, replacementTerrain);
                        terrainsChanged++;
                    }
                }
            }

            Log.Message($"GenStep_CenterHole: Processed {cellsProcessed} cells");
            Log.Message($"GenStep_CenterHole: Destroyed {thingsDestroyed} things, {buildingsDestroyed} buildings");
            Log.Message($"GenStep_CenterHole: Removed {roofsRemoved} roofs, changed {terrainsChanged} terrains");
        }

        /// <summary>
        /// Gets all cells that should be part of the hole
        /// </summary>
        private List<IntVec3> GetHoleCells(IntVec3 center, Map map)
        {
            List<IntVec3> cells = new List<IntVec3>();

            // Generate cells in a square pattern around the center
            for (int x = -holeRadius; x <= holeRadius; x++)
            {
                for (int z = -holeRadius; z <= holeRadius; z++)
                {
                    IntVec3 cell = center + new IntVec3(x, 0, z);
                    
                    if (!cell.InBounds(map)) continue;

                    // Apply roughness if enabled
                    if (createRoughEdges && IsEdgeCell(x, z, holeRadius))
                    {
                        // Edge cells have a chance to be excluded for roughness
                        if (Rand.Chance(roughnessChance))
                            continue;
                    }

                    cells.Add(cell);
                }
            }

            return cells;
        }

        /// <summary>
        /// Determines if a cell is on the edge of the hole pattern
        /// </summary>
        private bool IsEdgeCell(int x, int z, int radius)
        {
            return Mathf.Abs(x) == radius || Mathf.Abs(z) == radius;
        }
    }
}