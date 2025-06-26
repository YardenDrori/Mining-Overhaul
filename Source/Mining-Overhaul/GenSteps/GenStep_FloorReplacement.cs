using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MiningOverhaul
{
    public class GenStep_FloorReplacement : GenStep
    {
        // Configurable fields that can be set via XML
        public TerrainDef floorTile1 = null;
        public float floorTile1Percentage = 100f;
        
        public TerrainDef floorTile2 = null;
        public float floorTile2Percentage = 0f;
        
        // Blotch terrain configuration
        public TerrainDef blotchTerrain = null;
        public float blotchPercentage = 0f;
        
        // Blotch generation parameters (configurable via XML)
        public int blotchCount = 5;                    // Number of separate blotches to generate
        public int minBlotchSize = 10;                 // Minimum cells per blotch
        public int maxBlotchSize = 50;                 // Maximum cells per blotch
        public bool use8DirectionalWalk = true;        // Use 8-directional movement (vs 4-directional)
        public float blotchDensity = 0.8f;             // How "tight" the blotches are (0.1-1.0)
        
        // Optional: only replace specific terrain types (if null, replaces all floors)
        public List<TerrainDef> targetTerrains = null;
        
        public override int SeedPart => 289163482;

        public override void Generate(Map map, GenStepParams parms)
        {
            // Step 1: Validate and normalize the tile configuration
            List<(TerrainDef terrain, float weight)> validTiles = PrepareTerrainWeights();
            
            if (validTiles.Count == 0)
            {
                Log.Warning("GenStep_FloorReplacement: No valid floor tiles configured, skipping generation");
                return;
            }

            Log.Message($"Floor replacement using {validTiles.Count} terrain types");

            // Step 2: Generate blotch clusters first
            HashSet<IntVec3> blotchCells = GenerateBlotchClusters(map);

            // Step 3: Replace floors across the entire map
            foreach (IntVec3 cell in map.AllCells)
            {
                // Skip if this cell isn't a valid target for replacement
                if (!ShouldReplaceTerrainAt(cell, map))
                    continue;

                TerrainDef chosenTerrain;

                // Check if this cell should be blotch terrain (from clusters)
                if (blotchCells.Contains(cell))
                {
                    chosenTerrain = blotchTerrain;
                }
                else
                {
                    // Choose between tile1 and tile2 only
                    var nonBlotchOptions = validTiles.FindAll(t => t.terrain != blotchTerrain);
                    chosenTerrain = ChooseWeightedTerrain(nonBlotchOptions);
                }
                
                // Replace the terrain
                map.terrainGrid.SetTerrain(cell, chosenTerrain);
            }

            Log.Message($"Floor replacement completed with {blotchCells.Count} blotch cells in {blotchCount} clusters");
        }

        /// <summary>
        /// Generates connected blotch clusters using configurable random walks
        /// </summary>
        private HashSet<IntVec3> GenerateBlotchClusters(Map map)
        {
            HashSet<IntVec3> blotchCells = new HashSet<IntVec3>();
            
            if (blotchTerrain == null || blotchPercentage <= 0f || blotchCount <= 0)
                return blotchCells;

            // Calculate how many cells should be blotch terrain
            int totalValidCells = 0;
            foreach (IntVec3 cell in map.AllCells)
            {
                if (ShouldReplaceTerrainAt(cell, map))
                    totalValidCells++;
            }

            int targetBlotchCount = (int)Math.Round(totalValidCells * (blotchPercentage / 100f));
            
            // Clamp blotch sizes to reasonable bounds
            int clampedMinSize = Math.Max(1, minBlotchSize);
            int clampedMaxSize = Math.Max(clampedMinSize, maxBlotchSize);
            
            Log.Message($"Generating {blotchCount} blotches, target total cells: {targetBlotchCount}, size range: {clampedMinSize}-{clampedMaxSize}");

            // Create the specified number of blotches
            for (int i = 0; i < blotchCount && blotchCells.Count < targetBlotchCount; i++)
            {
                // Determine this blotch's size
                int thisBlotchSize = Rand.Range(clampedMinSize, clampedMaxSize + 1);
                
                // Don't exceed our target total
                thisBlotchSize = Math.Min(thisBlotchSize, targetBlotchCount - blotchCells.Count);
                
                // Generate this blotch
                HashSet<IntVec3> newBlotch = GenerateSingleBlotch(map, thisBlotchSize);
                
                // Add to our total collection
                foreach (IntVec3 cell in newBlotch)
                {
                    blotchCells.Add(cell);
                }
            }

            return blotchCells;
        }

        /// <summary>
        /// Generates a single blotch using random walk algorithm
        /// </summary>
        private HashSet<IntVec3> GenerateSingleBlotch(Map map, int targetSize)
        {
            HashSet<IntVec3> blotchCells = new HashSet<IntVec3>();
            
            // Find a random starting point
            IntVec3 startCell = GetRandomValidCell(map);
            if (startCell == IntVec3.Invalid) return blotchCells;

            // Random walk to create blotch
            IntVec3 currentCell = startCell;
            
            for (int i = 0; i < targetSize * 2; i++) // *2 to account for density factor
            {
                // Add current cell based on density factor
                if (ShouldReplaceTerrainAt(currentCell, map) && Rand.Range(0f, 1f) <= blotchDensity)
                {
                    blotchCells.Add(currentCell);
                    
                    // Stop if we've reached our target size
                    if (blotchCells.Count >= targetSize)
                        break;
                }

                // Move to adjacent cell (drunk walk)
                List<IntVec3> neighbors = GetValidNeighbors(currentCell, map);
                
                if (neighbors.Count > 0)
                {
                    currentCell = neighbors.RandomElement();
                }
                else
                {
                    // If stuck, try to find a new valid cell near our blotch
                    currentCell = GetRandomValidCell(map);
                    if (currentCell == IntVec3.Invalid) break;
                }
            }

            return blotchCells;
        }

        /// <summary>
        /// Gets valid neighboring cells based on directional configuration
        /// </summary>
        private List<IntVec3> GetValidNeighbors(IntVec3 center, Map map)
        {
            List<IntVec3> neighbors = new List<IntVec3>();
            
            if (use8DirectionalWalk)
            {
                // 8-directional movement (includes diagonals)
                for (int x = -1; x <= 1; x++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        if (x == 0 && z == 0) continue; // Skip center
                        
                        IntVec3 neighbor = center + new IntVec3(x, 0, z);
                        if (neighbor.InBounds(map))
                            neighbors.Add(neighbor);
                    }
                }
            }
            else
            {
                // 4-directional movement (cardinal directions only)
                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                {
                    IntVec3 neighbor = center + offset;
                    if (neighbor.InBounds(map))
                        neighbors.Add(neighbor);
                }
            }

            return neighbors;
        }

        /// <summary>
        /// Gets a random valid cell for blotch generation
        /// </summary>
        private IntVec3 GetRandomValidCell(Map map)
        {
            for (int attempts = 0; attempts < 100; attempts++)
            {
                IntVec3 cell = map.AllCells.RandomElement();
                if (ShouldReplaceTerrainAt(cell, map))
                    return cell;
            }
            return IntVec3.Invalid;
        }

        /// <summary>
        /// Validates tile configs and converts percentages to normalized weights
        /// </summary>
        private List<(TerrainDef terrain, float weight)> PrepareTerrainWeights()
        {
            List<(TerrainDef terrain, float weight)> validTiles = new List<(TerrainDef, float)>();

            // Collect all valid terrain definitions
            if (floorTile1 != null && floorTile1Percentage > 0f)
                validTiles.Add((floorTile1, floorTile1Percentage));
            
            if (floorTile2 != null && floorTile2Percentage > 0f)
                validTiles.Add((floorTile2, floorTile2Percentage));
            
            if (blotchTerrain != null && blotchPercentage > 0f)
                validTiles.Add((blotchTerrain, blotchPercentage));

            // If no tiles configured, return empty list
            if (validTiles.Count == 0)
                return validTiles;

            // Calculate total weight for normalization
            float totalWeight = 0f;
            foreach (var tile in validTiles)
            {
                totalWeight += tile.weight;
            }

            // Normalize weights to sum to 100 (makes the logic cleaner)
            if (totalWeight > 0f)
            {
                for (int i = 0; i < validTiles.Count; i++)
                {
                    var tile = validTiles[i];
                    validTiles[i] = (tile.terrain, (tile.weight / totalWeight) * 100f);
                }
            }

            // Debug logging
            foreach (var tile in validTiles)
            {
                Log.Message($"Terrain {tile.terrain.defName}: {tile.weight:F1}%");
            }

            return validTiles;
        }

        /// <summary>
        /// Determines if terrain at this cell should be replaced
        /// </summary>
        private bool ShouldReplaceTerrainAt(IntVec3 cell, Map map)
        {
            TerrainDef currentTerrain = cell.GetTerrain(map);

            // Keep it simple - check if there are any non-plant things at this cell
            List<Thing> thingsAtCell = map.thingGrid.ThingsListAt(cell);
            if (thingsAtCell != null && thingsAtCell.Count > 0)
            {
                // Skip if there are any buildings, items, or other stuff (but allow plants)
                foreach (Thing thing in thingsAtCell)
                {
                    if (thing.def.category == ThingCategory.Building || 
                        thing.def.category == ThingCategory.Item ||
                        (thing.def.building != null && thing.def.building.isResourceRock))
                    {
                        return false;
                    }
                }
            }

            // If specific target terrains are defined, only replace those
            if (targetTerrains != null && targetTerrains.Count > 0)
            {
                return targetTerrains.Contains(currentTerrain);
            }

            // Otherwise, replace any "natural" floor terrain
            return currentTerrain.natural || currentTerrain == TerrainDefOf.Soil;
        }

        /// <summary>
        /// Selects a terrain based on weighted random selection
        /// </summary>
        private TerrainDef ChooseWeightedTerrain(List<(TerrainDef terrain, float weight)> terrains)
        {
            if (terrains.Count == 1)
                return terrains[0].terrain;

            // Generate random number between 0-100
            float randomValue = Rand.Range(0f, 100f);
            float cumulativeWeight = 0f;

            // Find which terrain this random value falls into
            foreach (var terrain in terrains)
            {
                cumulativeWeight += terrain.weight;
                if (randomValue <= cumulativeWeight)
                {
                    return terrain.terrain;
                }
            }

            // Fallback (shouldn't happen with proper normalization)
            return terrains[terrains.Count - 1].terrain;
        }
    }
}