using System;
using System.Collections.Generic;
using System.Linq; // ← ADD THIS LINE
using RimWorld;
using RimWorld.Planet;
using Unity.Mathematics;
using UnityEngine;
using Verse;

namespace MiningOverhaul
{
    public class GenStep_CaveShape : GenStep
    {

        //CAVE VARIABLES HERE
        public int minAutomataCycles = 4;        // Minimum cellular automata iterations
        public int maxAutomataCycles = 7;        // Maximum cellular automata iterations  
        public float initialAirChance = 0.60f;   // Starting cave density (0.0-1.0)
        public float maxCaveDistancePath = 6;

        //RUINS VARIABLES HERE
        public int MinRoomSizeWidth = 3;
        public int MinRoomSizeHeight = 3;
        public int MaxRoomSizeWidth = 9;
        public int MaxRoomSizeHeight = 9;
        public ThingDef WallDef = null;
        public int DamagedWallPrecentage = 5;


        //GLOBAL VARIABLES HERE
        public ThingDef overrideRockDef = null;  // If null, uses RockDefAt (default behavior)

        private class RoofThreshold
        {
            public RoofDef roofDef;
            public float minGridVal;
        }

        // Cave generation styles - easy to add more
        #region cave types
        public enum CaveStyles
        {
            Original,    // RimWorld's default branching caves
            CellularAutomata,    // Room-based systems
            Ruins
        }
        #endregion

        private const int MinRoofedCellsPerGroup = 20;
        public override int SeedPart => 1182952823;

        public static ThingDef RockDefAt(IntVec3 c)
        {
            ThingDef thingDef = null;
            float num = -999999f;
            for (int i = 0; i < RockNoises.rockNoises.Count; i++)
            {
                float value = RockNoises.rockNoises[i].noise.GetValue(c);
                if (value > num)
                {
                    thingDef = RockNoises.rockNoises[i].rockDef;
                    num = value;
                }
            }
            if (thingDef == null)
            {
                IntVec3 intVec = c;
                Log.ErrorOnce("Did not get rock def to generate at " + intVec.ToString(), 50812);
                thingDef = ThingDefOf.Sandstone;
            }
            return thingDef;
        }

        /// <summary>
        /// Main cave generation dispatcher - chooses which style to use
        /// </summary>
        #region gen genric cave
        private BoolGrid GenerateCaves(Map map)
        {
            BoolGrid caves;
            CaveStyles style = CaveStyles.CellularAutomata;
            Log.Message($"Generating {style} caves");
            if (style == CaveStyles.CellularAutomata)
                caves = GenerateCellularAutomataCaves(map);
            else if (style == CaveStyles.Original)
                caves = GenerateOriginalCaves(map);
            else
                caves = GenerateRuinsMap(map);

            caves = CleanUpCave(caves, map);
            return caves;
        }

        #endregion

        /// <summary>
        /// RimWorld's original cave generation - our baseline
        /// </summary>
        private BoolGrid GenerateOriginalCaves(Map map)
        {
            // Get the original caves noise
            MapGenFloatGrid originalCaves = MapGenerator.Caves;

            // Convert to BoolGrid for consistency
            BoolGrid caves = new BoolGrid(map);
            foreach (IntVec3 cell in map.AllCells)
            {
                caves[cell] = originalCaves[cell] > 0f;
            }

            // Debug: Report coverage
            int caveCells = 0;
            int totalCells = 0;
            foreach (IntVec3 cell in map.AllCells)
            {
                totalCells++;
                if (caves[cell]) caveCells++;
            }
            float coverage = (float)caveCells / totalCells;
            Log.Message($"Original caves coverage: {coverage:P1}");

            return caves;
        }

        #region Cellular Automata
        private BoolGrid GenerateCellularAutomataCaves(Map map)
        {
            // Use configurable values instead of hardcoded ones
            int iterationQuota = Rand.RangeInclusive(minAutomataCycles, maxAutomataCycles);
            Log.Message($"Using {iterationQuota} automata cycles (range: {minAutomataCycles}-{maxAutomataCycles})");
            
            BoolGrid caves = new BoolGrid(map);

            // Step 1: Random initialization using configurable air chance
            foreach (IntVec3 cell in map.AllCells)
            {
                caves[cell] = Rand.Chance(initialAirChance);
            }

            for (int iteration = 0; iteration < iterationQuota; iteration++)
            {
                caves = ApplyCellularRules(caves, map);
            }
            return caves;
        }
        
        private ThingDef GetRockDefForCell(IntVec3 cell)
        {
            // Use override if specified, otherwise fall back to default behavior
            if (overrideRockDef != null)
            {
                return overrideRockDef;
            }
            return RockDefAt(cell);  // Your existing random rock logic
        }

        private BoolGrid ApplyCellularRules(BoolGrid oldCaves, Map map)
        {
            BoolGrid newCaves = new BoolGrid(map);

            foreach (IntVec3 cell in map.AllCells)
            {
                int rockNeighbors = CountRockNeighbors(oldCaves, cell, map);

                // The magic rules:
                if (rockNeighbors >= 4)
                    newCaves[cell] = false; // Becomes rock
                else
                    newCaves[cell] = true;  // Becomes cave
            }

            return newCaves;
        }

        private int CountRockNeighbors(BoolGrid caves, IntVec3 cell, Map map)
        {
            int count = 0;
            
            // Check all 8 neighbors (including diagonals)
            for(int x = -1; x <= 1; x++)
            {
                for(int z = -1; z <= 1; z++)
                {
                    if(x == 0 && z == 0) continue; // Skip the center cell
                    
                    IntVec3 neighbor = cell + new IntVec3(x, 0, z);
                    
                    // Treat out-of-bounds as rock (creates natural walls)
                    if(!neighbor.InBounds(map) || !caves[neighbor])
                        count++;
                }
            }
            
            return count;
        }
        #endregion
        
        #region Ruins
        private BoolGrid GenerateRuinsMap(Map map)
        {
            BoolGrid caves = new BoolGrid(map);
            //TO DO
            return caves;
        }
        #endregion


        private List<List<IntVec3>> FindCaveRegions(BoolGrid caves, Map map)
        {
            BoolGrid visited = new BoolGrid(map);
            List<List<IntVec3>> regions = new List<List<IntVec3>>();

            foreach (IntVec3 cell in map.AllCells)
            {
                // Skip if not a cave or already visited
                if (!caves[cell] || visited[cell]) continue;

                // Found a new cave region - collect all connected caves
                List<IntVec3> region = new List<IntVec3>();
                map.floodFiller.FloodFill(cell, (IntVec3 x) => caves[x] && !visited[x], delegate (IntVec3 x)
                {
                    visited[x] = true;
                    region.Add(x);
                    // Log.Message("Added Region with " + regions.Count + " Cells");
                });
                regions.Add(region);
                Log.Message($"Found region with {region.Count} cells");

            }


            Log.Message("there are: " + regions.Count + " regions in this cavern");

            return regions;
        }

        private BoolGrid FillRegions(BoolGrid caves, Map map, List<List<IntVec3>> AllRegions)
        {
            foreach (List<IntVec3> region in AllRegions)
            {
                if (region.Count < 40)
                {
                    Log.Message("Closing a small region with " + region.Count + " cells");
                    foreach (IntVec3 cell in region)
                    {
                        caves[cell] = false;
                    }
                }
            }
            return caves;
        }

        List<(IntVec3 PointA, IntVec3 PointB)> FindCloseRegionsCells(BoolGrid caves, Map map, List<List<IntVec3>> AllRegions)
        {
            List<(IntVec3, IntVec3)> getPairs = new List<(IntVec3, IntVec3)>();
            List<(IntVec3, IntVec3)> CloseCells = new List<(IntVec3, IntVec3)>();
            for (int i = 0; i < AllRegions.Count; i++)
            {
                for (int j = i+1; j < AllRegions.Count; j++)
                {
                    getPairs = GetCloseCells(AllRegions[i], AllRegions[j], caves, map);

                    if (getPairs != null && getPairs.Count > 0)
                    {
                        CloseCells.Add((getPairs[0].Item1, getPairs[0].Item2));
                    }
                }
            }
            return CloseCells;
        }

        private List<(IntVec3, IntVec3)> GetCloseCells(List<IntVec3> RegionA, List<IntVec3> RegionB, BoolGrid caves, Map map)
        {
            List<IntVec3> EdgesRegionA = FindEdgeCells(caves, map, RegionA);
            List<IntVec3> EdgesRegionB = FindEdgeCells(caves, map, RegionB);
            List<(IntVec3, IntVec3)> allConnections = new List<(IntVec3, IntVec3)>();
            
            // Find all possible connections
            for (int i = 0; i < EdgesRegionA.Count; i++)
            {
                for (int j = 0; j < EdgesRegionB.Count; j++)
                {
                    float distance = EdgesRegionA[i].DistanceTo(EdgesRegionB[j]);
                    if (distance < maxCaveDistancePath)
                    {
                        allConnections.Add((EdgesRegionA[i], EdgesRegionB[j]));
                    }
                }
            }
            
            if (allConnections.Count == 0) return null;
            
            // Smart selection: 
            // - Always include the shortest connection
            // - Add 1-2 more if regions are large enough
            allConnections = allConnections.OrderBy(pair => 
                pair.Item1.DistanceTo(pair.Item2)).ToList();
            
            List<(IntVec3, IntVec3)> finalConnections = new List<(IntVec3, IntVec3)>();
            finalConnections.Add(allConnections[0]); // Always include shortest
            
            // Add more connections for larger regions
            int maxConnections = Mathf.Min(3, 1 + (RegionA.Count + RegionB.Count) / 200);
            for (int i = 1; i < allConnections.Count && finalConnections.Count < maxConnections; i++)
            {
                // Only add if it's not too close to existing connections
                bool tooClose = false;
                foreach (var existing in finalConnections)
                {
                    if (allConnections[i].Item1.DistanceTo(existing.Item1) < 8 ||
                        allConnections[i].Item2.DistanceTo(existing.Item2) < 8)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (!tooClose)
                {
                    finalConnections.Add(allConnections[i]);
                }
            }
            
            Log.Message($"Selected {finalConnections.Count} connections from {allConnections.Count} possibilities");
            return finalConnections;
        }

        private List<IntVec3> FindEdgeCells(BoolGrid caves, Map map, List<IntVec3> Regions)
        {
            List<IntVec3> EdgeCells = new List<IntVec3>();
            foreach (IntVec3 cell in Regions)
            {
                if (CountRockNeighbors(caves, cell, map) > 0)
                {
                    EdgeCells.Add(cell);
                }
            }
            return EdgeCells;
        }

        private BoolGrid CarveShortTunnel(BoolGrid caves, Map map, IntVec3 Origin, IntVec3 Dest) {
            Log.Message("carving tunnel");
            IntVec3 current = Origin;
            while (current != Dest)
            {
                IntVec3 direction = Dest - current;
                caves[current] = true;
                if (Math.Abs(direction.x) > Math.Abs(direction.z))
                {
                    current += new IntVec3(Math.Sign(direction.x), 0, 0);
                }
                else
                {
                    current += new IntVec3(0, 0, Math.Sign(direction.z));
                }
                if (!current.InBounds(map)) break;
            }
            caves[current] = true;
            return caves;
        }

        private BoolGrid EnsureCaveConnectivity(BoolGrid caves, Map map, List<List<IntVec3>> AllRegions)
        {
            //connect close caves
            List<(IntVec3, IntVec3)> CloseRegionCells = FindCloseRegionsCells(caves, map, AllRegions);
            foreach ((IntVec3, IntVec3) CellPair in CloseRegionCells)
            {
                caves = CarveShortTunnel(caves, map, CellPair.Item1, CellPair.Item2);
            }

            return caves;
        }



        private BoolGrid CleanUpCave(BoolGrid caves, Map map)
        {
            List<List<IntVec3>> AllRegions = FindCaveRegions(caves, map);
            caves = FillRegions(caves, map, AllRegions);
            caves = EnsureCaveConnectivity(caves, map, AllRegions);
            return caves;
        }















        // private BoolGrid EnsureCaveConnectivity(BoolGrid caves, Map map)
        // {

        // }

        // private BoolGrid EnsureCaveConnectivity(BoolGrid caves, Map map)
        // {
        //     // Find all separate cave regions
        //     List<List<IntVec3>> caveRegions = FindCaveRegions(caves, map);

        //     Log.Message($"Found {caveRegions.Count} separate cave regions");

        //     if (caveRegions.Count <= 1)
        //     {
        //         Log.Message("Caves already connected!");
        //         return caves; // Already connected or no caves
        //     }

        //     // Find the biggest region (this becomes our "main" cave system)
        //     List<IntVec3> mainRegion = caveRegions[0];
        //     for (int i = 1; i < caveRegions.Count; i++)
        //     {
        //         if (caveRegions[i].Count > mainRegion.Count)
        //             mainRegion = caveRegions[i];
        //     }

        //     Log.Message($"Main region has {mainRegion.Count} cells");

        //     // Connect all other regions to the main one
        //     foreach (List<IntVec3> region in caveRegions)
        //     {
        //         if (region == mainRegion) continue; // Skip the main region

        //         Log.Message($"Connecting region with {region.Count} cells to main region");
        //         ConnectTwoRegions(caves, mainRegion, region, map);
        //     }

        //     return caves;
        // }


        // private void ConnectTwoRegions(BoolGrid caves, List<IntVec3> mainRegion, List<IntVec3> targetRegion, Map map)
        // {
        //     // Find the closest points between regions
        //     IntVec3 bestStart = mainRegion[0];
        //     IntVec3 bestEnd = targetRegion[0];
        //     float shortestDistance = float.MaxValue;

        //     // Check a sample of points to find the closest connection
        //     int sampleSize = Math.Min(20, mainRegion.Count);
        //     for (int i = 0; i < sampleSize; i++)
        //     {
        //         IntVec3 mainPoint = mainRegion[i * mainRegion.Count / sampleSize];

        //         for (int j = 0; j < Math.Min(20, targetRegion.Count); j++)
        //         {
        //             IntVec3 targetPoint = targetRegion[j * targetRegion.Count / targetRegion.Count];
        //             float distance = mainPoint.DistanceTo(targetPoint);

        //             if (distance < shortestDistance)
        //             {
        //                 shortestDistance = distance;
        //                 bestStart = mainPoint;
        //                 bestEnd = targetPoint;
        //             }
        //         }
        //     }

        //     Log.Message($"Connecting points at distance {shortestDistance:F1}");

        //     // Always connect, but use different strategies based on distance
        //     if (shortestDistance < 15f)
        //     {
        //         CarveMinimalTunnel(caves, bestStart, bestEnd, map);
        //     }
        //     else
        //     {
        //         Log.Message("Regions far apart - using winding tunnel");
        //         CarveWindingTunnel(caves, bestStart, bestEnd, map);
        //     }
        // }


        // private void CarveMinimalTunnel(BoolGrid caves, IntVec3 start, IntVec3 end, Map map)
        // {
        //     // Just carve a thin, direct path - minimal disruption
        //     IntVec3 current = start;

        //     while (current.DistanceTo(end) > 1)
        //     {
        //         caves[current] = true; // Just one cell wide

        //         // Move directly toward target (no randomness)
        //         IntVec3 direction = end - current;
        //         if (Math.Abs(direction.x) > Math.Abs(direction.z))
        //             current += new IntVec3(direction.x > 0 ? 1 : -1, 0, 0);
        //         else
        //             current += new IntVec3(0, 0, direction.z > 0 ? 1 : -1);

        //         if (!current.InBounds(map)) break;
        //     }
        // }

        // private void CarveWindingTunnel(BoolGrid caves, IntVec3 start, IntVec3 end, Map map)
        // {
        //     IntVec3 current = start;

        //     while (current.DistanceTo(end) > 2)
        //     {
        //         caves[current] = true;

        //         // Move toward target with occasional curves
        //         IntVec3 direction = end - current;

        //         if (Rand.Chance(0.85f)) // Higher chance to move toward target
        //         {
        //             if (Math.Abs(direction.x) > Math.Abs(direction.z))
        //                 current += new IntVec3(direction.x > 0 ? 1 : -1, 0, 0);
        //             else
        //                 current += new IntVec3(0, 0, direction.z > 0 ? 1 : -1);
        //         }
        //         else // Occasional curve for more natural look
        //         {
        //             current += GenAdj.CardinalDirections.RandomElement();
        //         }

        //         if (!current.InBounds(map)) break;
        //     }
        // }

        #region main generate
        public override void Generate(Map map, GenStepParams parms)
        {
            if (map.TileInfo.WaterCovered)
            {
                return;
            }

            map.regionAndRoomUpdater.Enabled = false;

            // Elevation threshold for rock generation
            float elevationThreshold = 0.7f;

            // Setup roof types
            List<RoofThreshold> roofThresholds = new List<RoofThreshold>();
            roofThresholds.Add(new RoofThreshold
            {
                roofDef = RoofDefOf.RoofRockThick,
                minGridVal = elevationThreshold * 1.14f
            });
            roofThresholds.Add(new RoofThreshold
            {
                roofDef = RoofDefOf.RoofRockThin,
                minGridVal = elevationThreshold * 1.04f
            });

            // Get terrain data
            MapGenFloatGrid elevation = MapGenerator.Elevation;

            // Generate caves using our system
            BoolGrid caves = GenerateCaves(map);

            // Generate rocks and roofs
            foreach (IntVec3 cell in map.AllCells)
            {
                float cellElevation = elevation[cell];

                // Only generate rocks above elevation threshold
                if (cellElevation <= elevationThreshold)
                    continue;

                // Generate rock if this cell is NOT a cave
                if (!caves[cell])
                {
                    GenSpawn.Spawn(GetRockDefForCell(cell), cell, map);  // Use new method
                }

                // Add appropriate roof type based on elevation
                for (int i = 0; i < roofThresholds.Count; i++)
                {
                    if (cellElevation > roofThresholds[i].minGridVal)
                    {
                        map.roofGrid.SetRoof(cell, roofThresholds[i].roofDef);
                        break;
                    }
                }
            }

            // Remove small isolated roof sections
            RemoveSmallRoofSections(map);

            map.regionAndRoomUpdater.Enabled = true;
        }

        #endregion
        /// <summary>
        /// Removes small isolated roof sections (RimWorld's original logic)
        /// </summary>
        private void RemoveSmallRoofSections(Map map)
        {
            BoolGrid visited = new BoolGrid(map);
            List<IntVec3> toRemove = new List<IntVec3>();

            foreach (IntVec3 cell in map.AllCells)
            {
                if (visited[cell] || !IsNaturalRoofAt(cell, map))
                    continue;

                toRemove.Clear();
                map.floodFiller.FloodFill(cell,
                    (IntVec3 x) => IsNaturalRoofAt(x, map),
                    delegate (IntVec3 x)
                    {
                        visited[x] = true;
                        toRemove.Add(x);
                    });

                // Remove roof sections smaller than minimum size
                if (toRemove.Count < MinRoofedCellsPerGroup)
                {
                    for (int i = 0; i < toRemove.Count; i++)
                    {
                        map.roofGrid.SetRoof(toRemove[i], null);
                    }
                }
            }
        }

        private bool IsNaturalRoofAt(IntVec3 c, Map map)
        {
            if (c.Roofed(map))
            {
                return c.GetRoof(map).isNatural;
            }
            return false;
        }
    }
}