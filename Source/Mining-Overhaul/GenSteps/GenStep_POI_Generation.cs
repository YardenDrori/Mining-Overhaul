using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;

namespace MiningOverhaul
{
    // Simplified POI Definition - combines all the old classes
    public class SimplePOIDef : Def
    {
        public float spawnChance = 0.1f;                       // Chance for POI to spawn per grid cell (0.0-1.0)
        public float minDistance = 10f;                        // Distance between POIs of same type
        public float minDistanceFromEntrance = 0f;             // Minimum distance from cave entrance
        public List<POIItem> coreItems = new List<POIItem>();  // Main spawns (hives, buildings, etc.)
        public List<POIItem> nearbyItems = new List<POIItem>(); // Spawns near core (jelly, decorations, etc.)
        public float nearbyRadius = 3f;                        // Radius for nearby items
        public List<string> allowedBiomes = new List<string>(); // empty = all biomes
    }

    // Simple item definition - handles both things and creatures
    public class POIItem
    {
        public ThingDef thingDef;                              // For items, buildings, filth
        public PawnKindDef pawnKindDef;                        // For creatures
        public IntRange count = new IntRange(1, 1);            // How many to spawn
        public IntRange stackCount = new IntRange(1, 1);       // Stack size per item
        public float weight = 1f;                              // Selection weight
    }

    // Simplified GenStep that does the work
    public class GenStep_POIGeneration : GenStep
    {
        public SimplePOIDef poiDef;
        private List<IntVec3> usedPositions = new List<IntVec3>();
        private Map map;

        public override int SeedPart => 123456789;

        public override void Generate(Map map, GenStepParams parms)
        {
            this.map = map;
            usedPositions.Clear();

            if (poiDef == null)
            {
                MOLog.Warning($"POI GenStep has no POI definition");
                return;
            }

            if (poiDef.coreItems.NullOrEmpty())
            {
                MOLog.Warning($"POI {poiDef.defName} has no core items defined");
                return;
            }

            MOLog.Message($"Starting POI generation for {poiDef.defName} (chance: {poiDef.spawnChance:P1})");

            // Check biome restriction
            if (!poiDef.allowedBiomes.NullOrEmpty())
            {
                MOLog.Message($"Biome restrictions: {string.Join(", ", poiDef.allowedBiomes)}");
                // Add your cave biome detection here when ready
                // For now, always allow
            }

            GeneratePOIs();
            
            MOLog.Message($"POI generation complete. Spawned {usedPositions.Count} POIs of type {poiDef.defName}");
        }

        private void GeneratePOIs()
        {
            int gridSize = Mathf.RoundToInt(poiDef.minDistance);
            if (gridSize < 1) gridSize = 1;

            int gridCells = 0;
            int spawnAttempts = 0;
            int validSpots = 0;
            IntVec3 entrancePos = FindCaveEntrance();
            
            MOLog.Message($"Grid size: {gridSize}, Entrance at: {entrancePos}");

            // Go through map in grid pattern
            for (int x = 0; x < map.Size.x; x += gridSize)
            {
                for (int z = 0; z < map.Size.z; z += gridSize)
                {
                    gridCells++;
                    
                    // Roll for spawn chance
                    if (Rand.Chance(poiDef.spawnChance))
                    {
                        spawnAttempts++;
                        IntVec3 centerPos = new IntVec3(x + Rand.Range(0, gridSize), 0, z + Rand.Range(0, gridSize));
                        centerPos = centerPos.ClampInsideMap(map);
                        
                        if (IsValidPOISpot(centerPos))
                        {
                            validSpots++;
                            SpawnPOI(centerPos);
                        }
                    }
                }
            }
            
            MOLog.Message($"POI Generation Stats: {gridCells} grid cells, {spawnAttempts} spawn attempts, {validSpots} valid spots, {usedPositions.Count} POIs spawned");
        }

        private bool IsValidPOISpot(IntVec3 pos)
        {
            if (!pos.InBounds(map)) return false;

            // Check distance from other POIs of same type
            foreach (IntVec3 usedPos in usedPositions)
            {
                float distance = pos.DistanceTo(usedPos);
                if (distance < poiDef.minDistance)
                    return false;
            }

            // Check distance from cave entrance if specified
            if (poiDef.minDistanceFromEntrance > 0f)
            {
                IntVec3 entrancePos = FindCaveEntrance();
                if (entrancePos != IntVec3.Invalid)
                {
                    float entranceDistance = pos.DistanceTo(entrancePos);
                    if (entranceDistance < poiDef.minDistanceFromEntrance)
                        return false;
                }
            }

            return true;
        }

        private IntVec3 FindCaveEntrance()
        {
            // Look for the cave entrance (where players spawn)
            // In RimWorld pocket maps, the entrance is usually where the MapParent connection is
            
            // First, try to find a CavernExit building (the exit that leads back to surface)
            foreach (Building building in map.listerBuildings.allBuildingsColonist)
            {
                if (building.def.defName == "CavernExit")
                {
                    MOLog.Message($"Found CavernExit building at {building.Position}");
                    return building.Position;
                }
            }
            
            // If no building found, try to find it in all things
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing.def.defName == "CavernExit")
                {
                    MOLog.Message($"Found CavernExit thing at {thing.Position}");
                    return thing.Position;
                }
            }
            
            // Fallback: use map center if no exit found
            MOLog.Warning($"No CavernExit found, using map center {map.Center} as entrance position");
            return map.Center;
        }

        private void SpawnPOI(IntVec3 centerPos)
        {
            usedPositions.Add(centerPos);

            // Track positions used by THIS POI to avoid overlap
            List<IntVec3> thisPOIPositions = new List<IntVec3>();
            int itemsSpawned = 0;

            // Spawn core items first
            foreach (POIItem item in poiDef.coreItems)
            {
                int countToSpawn = item.count.RandomInRange;
                for (int i = 0; i < countToSpawn; i++)
                {
                    IntVec3 spawnPos = GetSpawnPosition(centerPos, 1f, thisPOIPositions);
                    if (spawnPos.IsValid && SpawnItem(item, spawnPos))
                    {
                        thisPOIPositions.Add(spawnPos);
                        itemsSpawned++;
                    }
                }
            }

            // Spawn nearby items around the core
            foreach (POIItem item in poiDef.nearbyItems)
            {
                int countToSpawn = item.count.RandomInRange;
                for (int i = 0; i < countToSpawn; i++)
                {
                    IntVec3 spawnPos = GetSpawnPosition(centerPos, poiDef.nearbyRadius, thisPOIPositions);
                    if (spawnPos.IsValid && SpawnItem(item, spawnPos))
                    {
                        thisPOIPositions.Add(spawnPos);
                        itemsSpawned++;
                    }
                }
            }
            
            MOLog.Message($"POI at {centerPos}: {itemsSpawned} items spawned");
        }
        
        private IntVec3 GetSpawnPosition(IntVec3 center, float radius, List<IntVec3> usedPositions)
        {
            int maxRadius = Mathf.RoundToInt(radius);
            
            // Try to find a valid spot within radius
            for (int attempts = 0; attempts < 20; attempts++)
            {
                IntVec3 candidate = center + GenRadial.RadialPattern[Rand.Range(0, GenRadial.NumCellsInRadius(maxRadius))];
                
                if (candidate.InBounds(map) && IsValidSpawnSpot(candidate, usedPositions))
                {
                    return candidate;
                }
            }
            
            return IntVec3.Invalid;
        }

        private bool IsValidSpawnSpot(IntVec3 pos, List<IntVec3> usedPositions)
        {
            if (!pos.InBounds(map) || pos.Filled(map))
                return false;

            // Check if we already used this position
            if (usedPositions.Contains(pos))
                return false;

            // Check for blocking things
            List<Thing> thingsHere = map.thingGrid.ThingsListAt(pos);
            foreach (Thing thing in thingsHere)
            {
                if (thing.def.category == ThingCategory.Building || 
                    thing.def.category == ThingCategory.Pawn ||
                    thing.def.passability == Traversability.Impassable)
                    return false;
            }

            return true;
        }
        
        private bool SpawnItem(POIItem item, IntVec3 pos)
        {
            try
            {
                // Handle creatures
                if (item.pawnKindDef != null)
                {
                    Pawn pawn = PawnGenerator.GeneratePawn(item.pawnKindDef);
                    if (pawn == null)
                    {
                        MOLog.Error($"Failed to generate pawn of type {item.pawnKindDef.defName}");
                        return false;
                    }
                    
                    GenSpawn.Spawn(pawn, pos, map);
                    return true;
                }
                
                // Handle things (items, buildings, filth)
                if (item.thingDef != null)
                {
                    Thing thing = ThingMaker.MakeThing(item.thingDef);
                    if (thing == null)
                    {
                        MOLog.Error($"Failed to make thing of type {item.thingDef.defName}");
                        return false;
                    }

                    // Set stack count for stackable items
                    if (thing.def.stackLimit > 1)
                    {
                        int stackSize = item.stackCount.RandomInRange;
                        thing.stackCount = Mathf.Min(stackSize, thing.def.stackLimit);
                    }

                    GenSpawn.Spawn(thing, pos, map);
                    return true;
                }
                
                MOLog.Error($"POI item has neither thingDef nor pawnKindDef");
                return false;
            }
            catch (System.Exception e)
            {
                MOLog.Error($"Exception spawning item: {e.Message}");
                return false;
            }
        }
        
        // Removed all the old complex spawn methods - replaced with simple SpawnItem method above
    }
}