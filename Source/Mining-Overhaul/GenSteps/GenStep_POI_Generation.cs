using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;

namespace MiningOverhaul
{
    // POI Generation Rules
    public class POIGenStepDef : Def
    {
        public float minDistanceBetweenPOIs = 10f;    // Distance between POIs of same type
        public float spawnChancePercent = 20f;        // % chance for POI to spawn per grid cell
        public float minDistanceFromEntrance = 0f;   // Minimum distance from cave entrance
        public POIContentDef poiContent;              // What this POI spawns
        public List<string> allowedBiomes = new List<string>(); // empty = all biomes
    }

    // POI Content Definition
    public class POIContentDef : Def
    {
        public float density = 2f;                    // How close together spawned things are (radius)
        public List<POISpawnGroup> spawnGroups = new List<POISpawnGroup>(); // Grouped spawning
    }
    
    // NEW: Spawn groups for related things
    public class POISpawnGroup
    {
        public string label = "Unnamed Group";        // For debugging/organization
        public float spawnChance = 1f;               // Chance this group spawns (0.0-1.0)
        public SpawnPattern pattern = SpawnPattern.Clustered; // How things in this group are arranged
        public float groupRadius = 3f;               // Radius for this group's spawns
        public List<POISpawnOption> spawnOptions = new List<POISpawnOption>();
        public List<POIAnimalOption> animalOptions = new List<POIAnimalOption>();
    }
    
    public enum SpawnPattern
    {
        Clustered,    // Things spawn close together
        Scattered,    // Things spawn spread out in the radius
        Ring,         // Things spawn in a circle around the center
        Line          // Things spawn in a rough line
    }

    public class POISpawnOption
    {
        public ThingDef thingDef;
        public TerrainDef terrainDef;
        public IntRange thingCount = new IntRange(1, 1);      // How many of this thing spawn
        public IntRange stackCount = new IntRange(1, 1);      // Stack size per thing
        public QualityRange qualityRange = QualityRange.All; // Quality range
        public float weight = 1f;                             // Selection weight
    }

    // NEW: Animal spawn options
    public class POIAnimalOption
    {
        public PawnKindDef pawnKindDef;               // What kind of animal
        public IntRange animalCount = new IntRange(1, 1); // How many animals
        public string faction = "null";               // Faction (null = wild, Pirates, etc.)
        public FloatRange ageRange = new FloatRange(0.1f, 0.8f); // Age as % of lifespan
        public Gender? gender = null;                 // null = random, Male, Female
        public float weight = 1f;                     // Selection weight
    }

    // The GenStep that does the work
    public class GenStep_POIGeneration : GenStep
    {
        public POIGenStepDef genStepDef;
        private List<IntVec3> usedPositions = new List<IntVec3>();
        private Map map;

        public override int SeedPart => 123456789;

        public override void Generate(Map map, GenStepParams parms)
        {
            this.map = map;
            usedPositions.Clear();

            if (genStepDef?.poiContent?.spawnGroups == null)
            {
                MOLog.Warning($"POI GenStep {genStepDef?.defName ?? "unknown"} has no spawn groups defined");
                return;
            }

            MOLog.Message($"Starting POI generation for {genStepDef.defName} on map {map.uniqueID}");
            MOLog.Message($"POI Config: minDistance={genStepDef.minDistanceBetweenPOIs}, spawnChance={genStepDef.spawnChancePercent}%, minFromEntrance={genStepDef.minDistanceFromEntrance}");
            MOLog.Message($"Spawn groups: {genStepDef.poiContent.spawnGroups.Count}");

            // Check biome restriction
            if (!genStepDef.allowedBiomes.NullOrEmpty())
            {
                MOLog.Message($"Biome restrictions: {string.Join(", ", genStepDef.allowedBiomes)}");
                // Add your cave biome detection here when ready
                // For now, always allow
            }

            GeneratePOIs();
            
            MOLog.Message($"POI generation complete. Spawned {usedPositions.Count} POIs of type {genStepDef.defName}");
        }

        private void GeneratePOIs()
        {
            int gridSize = Mathf.RoundToInt(genStepDef.minDistanceBetweenPOIs);
            if (gridSize < 1) gridSize = 1;

            int gridCells = 0;
            int spawnAttempts = 0;
            int validSpots = 0;
            IntVec3 entrancePos = FindCaveEntrance();
            
            MOLog.Message($"Grid size: {gridSize}, Map size: {map.Size}, Entrance at: {entrancePos}");

            // Go through map in grid pattern
            for (int x = 0; x < map.Size.x; x += gridSize)
            {
                for (int z = 0; z < map.Size.z; z += gridSize)
                {
                    gridCells++;
                    
                    // Roll for spawn chance
                    if (Rand.Range(0f, 100f) <= genStepDef.spawnChancePercent)
                    {
                        spawnAttempts++;
                        IntVec3 centerPos = new IntVec3(x + Rand.Range(0, gridSize), 0, z + Rand.Range(0, gridSize));
                        centerPos = centerPos.ClampInsideMap(map);
                        
                        if (IsValidPOISpot(centerPos))
                        {
                            validSpots++;
                            MOLog.Message($"Spawning POI at {centerPos} (distance from entrance: {centerPos.DistanceTo(entrancePos):F1})");
                            SpawnPOI(centerPos);
                        }
                        else
                        {
                            MOLog.Message($"Invalid POI spot at {centerPos} (distance from entrance: {centerPos.DistanceTo(entrancePos):F1})");
                        }
                    }
                }
            }
            
            MOLog.Message($"POI Generation Stats: {gridCells} grid cells, {spawnAttempts} spawn attempts, {validSpots} valid spots, {usedPositions.Count} POIs spawned");
        }

        private bool IsValidPOISpot(IntVec3 pos)
        {
            if (!pos.InBounds(map))
            {
                MOLog.Message($"Position {pos} out of bounds");
                return false;
            }

            // Check distance from other POIs of same type
            foreach (IntVec3 usedPos in usedPositions)
            {
                float distance = pos.DistanceTo(usedPos);
                if (distance < genStepDef.minDistanceBetweenPOIs)
                {
                    MOLog.Message($"Position {pos} too close to existing POI at {usedPos} (distance: {distance:F1}, required: {genStepDef.minDistanceBetweenPOIs})");
                    return false;
                }
            }

            // Check distance from cave entrance if specified
            if (genStepDef.minDistanceFromEntrance > 0f)
            {
                IntVec3 entrancePos = FindCaveEntrance();
                if (entrancePos != IntVec3.Invalid)
                {
                    float entranceDistance = pos.DistanceTo(entrancePos);
                    if (entranceDistance < genStepDef.minDistanceFromEntrance)
                    {
                        MOLog.Message($"Position {pos} too close to entrance at {entrancePos} (distance: {entranceDistance:F1}, required: {genStepDef.minDistanceFromEntrance})");
                        return false;
                    }
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
            MOLog.Message($"Spawning POI at {centerPos} with {genStepDef.poiContent.spawnGroups.Count} potential groups");

            // Track positions used by THIS POI to avoid overlap
            List<IntVec3> thisPOIPositions = new List<IntVec3>();
            int groupsSpawned = 0;

            // Spawn each group
            foreach (POISpawnGroup group in genStepDef.poiContent.spawnGroups)
            {
                // Check if this group should spawn
                if (Rand.Chance(group.spawnChance))
                {
                    MOLog.Message($"Spawning group '{group.label}' (chance: {group.spawnChance:P0}, pattern: {group.pattern}, radius: {group.groupRadius})");
                    SpawnGroup(group, centerPos, thisPOIPositions);
                    groupsSpawned++;
                }
                else
                {
                    MOLog.Message($"Skipping group '{group.label}' (failed {group.spawnChance:P0} chance roll)");
                }
            }
            
            MOLog.Message($"POI at {centerPos} complete: {groupsSpawned}/{genStepDef.poiContent.spawnGroups.Count} groups spawned, {thisPOIPositions.Count} total things placed");
        }
        
        private void SpawnGroup(POISpawnGroup group, IntVec3 centerPos, List<IntVec3> thisPOIPositions)
        {
            // Get positions for this group based on pattern
            List<IntVec3> groupPositions = GetGroupPositions(group, centerPos, thisPOIPositions);
            MOLog.Message($"Group '{group.label}': found {groupPositions.Count} valid positions for pattern {group.pattern}");
            
            int itemsSpawned = 0;
            int animalsSpawned = 0;
            
            // Spawn regular items/terrain
            foreach (POISpawnOption option in group.spawnOptions)
            {
                int thingsToSpawn = option.thingCount.RandomInRange;
                MOLog.Message($"Attempting to spawn {thingsToSpawn} {option.thingDef?.defName ?? "null thing"}");
                
                for (int i = 0; i < thingsToSpawn; i++)
                {
                    IntVec3 spawnPos = GetBestPositionFromList(groupPositions, thisPOIPositions);
                    if (spawnPos != IntVec3.Invalid)
                    {
                        bool success = SpawnSingleThing(option, spawnPos);
                        if (success)
                        {
                            itemsSpawned++;
                            thisPOIPositions.Add(spawnPos);
                            groupPositions.Remove(spawnPos);
                        }
                    }
                    else
                    {
                        MOLog.Message($"No valid position found for {option.thingDef?.defName}");
                    }
                }
            }

            // Spawn animals
            foreach (POIAnimalOption option in group.animalOptions)
            {
                int animalsToSpawn = option.animalCount.RandomInRange;
                MOLog.Message($"Attempting to spawn {animalsToSpawn} {option.pawnKindDef?.defName ?? "null pawn"}");
                
                for (int i = 0; i < animalsToSpawn; i++)
                {
                    IntVec3 spawnPos = GetBestPositionFromList(groupPositions, thisPOIPositions);
                    if (spawnPos != IntVec3.Invalid)
                    {
                        bool success = SpawnSingleAnimal(option, spawnPos);
                        if (success)
                        {
                            animalsSpawned++;
                            thisPOIPositions.Add(spawnPos);
                            groupPositions.Remove(spawnPos);
                        }
                    }
                    else
                    {
                        MOLog.Message($"No valid position found for {option.pawnKindDef?.defName}");
                    }
                }
            }
            
            MOLog.Message($"Group '{group.label}' complete: {itemsSpawned} items, {animalsSpawned} animals spawned");
        }
        
        private List<IntVec3> GetGroupPositions(POISpawnGroup group, IntVec3 centerPos, List<IntVec3> usedPositions)
        {
            List<IntVec3> positions = new List<IntVec3>();
            int maxRadius = Mathf.RoundToInt(group.groupRadius);
            
            switch (group.pattern)
            {
                case SpawnPattern.Clustered:
                    // Get nearby positions, favor closer ones
                    for (int radius = 0; radius <= maxRadius; radius++)
                    {
                        foreach (IntVec3 cell in GenRadial.RadialCellsAround(centerPos, radius, true))
                        {
                            if (cell.InBounds(map) && IsValidSpawnSpot(cell, usedPositions))
                            {
                                positions.Add(cell);
                            }
                        }
                    }
                    break;
                    
                case SpawnPattern.Scattered:
                    // Get random positions within radius
                    for (int attempts = 0; attempts < 50; attempts++)
                    {
                        IntVec3 randomCell = centerPos + GenRadial.RadialPattern[Rand.Range(0, GenRadial.NumCellsInRadius(maxRadius))];
                        if (randomCell.InBounds(map) && IsValidSpawnSpot(randomCell, usedPositions))
                        {
                            positions.Add(randomCell);
                        }
                    }
                    break;
                    
                case SpawnPattern.Ring:
                    // Get positions in a ring around the center
                    int ringRadius = Mathf.Max(1, maxRadius - 1);
                    foreach (IntVec3 cell in GenRadial.RadialCellsAround(centerPos, ringRadius, false))
                    {
                        if (cell.InBounds(map) && IsValidSpawnSpot(cell, usedPositions))
                        {
                            positions.Add(cell);
                        }
                    }
                    break;
                    
                case SpawnPattern.Line:
                    // Get positions in a rough line
                    Rot4 direction = Rot4.Random;
                    for (int i = 0; i < maxRadius; i++)
                    {
                        IntVec3 linePos = centerPos + (direction.FacingCell * i);
                        if (linePos.InBounds(map) && IsValidSpawnSpot(linePos, usedPositions))
                        {
                            positions.Add(linePos);
                        }
                        // Add some randomness to the line
                        IntVec3 offsetPos = linePos + GenRadial.RadialPattern[Rand.Range(0, 9)];
                        if (offsetPos.InBounds(map) && IsValidSpawnSpot(offsetPos, usedPositions))
                        {
                            positions.Add(offsetPos);
                        }
                    }
                    break;
            }
            
            return positions;
        }
        
        private IntVec3 GetBestPositionFromList(List<IntVec3> positions, List<IntVec3> usedPositions)
        {
            if (positions.NullOrEmpty()) return IntVec3.Invalid;
            
            // Try to find a position that's not used
            foreach (IntVec3 pos in positions)
            {
                if (!usedPositions.Contains(pos))
                {
                    return pos;
                }
            }
            
            // If all positions are used, return the first one anyway
            return positions.First();
        }

        private IntVec3 GetSpawnPositionNear(IntVec3 center, float density, List<IntVec3> usedPositionsThisPOI)
        {
            // Try to find a valid spot within density radius
            int maxRadius = Mathf.RoundToInt(density);
            
            for (int attempts = 0; attempts < 20; attempts++)
            {
                IntVec3 candidate = center + GenRadial.RadialPattern[Rand.Range(0, GenRadial.NumCellsInRadius(maxRadius))];
                
                if (candidate.InBounds(map) && IsValidSpawnSpot(candidate, usedPositionsThisPOI))
                {
                    return candidate;
                }
            }
            
            // Fallback to center if no good spot found
            return center.InBounds(map) && IsValidSpawnSpot(center, usedPositionsThisPOI) ? center : IntVec3.Invalid;
        }

        private bool IsValidSpawnSpot(IntVec3 pos, List<IntVec3> usedPositionsThisPOI)
        {
            // Basic validity checks
            if (!pos.InBounds(map) || pos.Filled(map))
                return false;

            // Check if we already used this position for this POI
            if (usedPositionsThisPOI.Contains(pos))
                return false;

            // Check if there's already a plant or pawn here
            List<Thing> thingsHere = map.thingGrid.ThingsListAt(pos);
            foreach (Thing thing in thingsHere)
            {
                if (thing.def.category == ThingCategory.Plant || thing.def.category == ThingCategory.Pawn)
                    return false;
            }

            return true;
        }

        private bool SpawnSingleAnimal(POIAnimalOption animalOption, IntVec3 pos)
        {
            if (animalOption.pawnKindDef == null)
            {
                MOLog.Error($"Null pawnKindDef in animal option");
                return false;
            }

            try
            {
                // Get faction (null = wild animals)
                Faction faction = null;
                if (!animalOption.faction.NullOrEmpty() && animalOption.faction != "null")
                {
                    faction = Find.FactionManager.FirstFactionOfDef(FactionDef.Named(animalOption.faction));
                    if (faction == null)
                    {
                        MOLog.Warning($"Could not find faction '{animalOption.faction}' for {animalOption.pawnKindDef.defName}");
                    }
                }

                // Generate the pawn
                PawnGenerationRequest request = new PawnGenerationRequest(
                    animalOption.pawnKindDef,
                    faction: faction,
                    context: PawnGenerationContext.NonPlayer,
                    tile: map.Tile,
                    forceGenerateNewPawn: true,
                    fixedGender: animalOption.gender
                );

                Pawn pawn = PawnGenerator.GeneratePawn(request);
                if (pawn == null)
                {
                    MOLog.Error($"Failed to generate pawn of type {animalOption.pawnKindDef.defName}");
                    return false;
                }

                // Set age
                float targetAge = pawn.RaceProps.lifeExpectancy * animalOption.ageRange.RandomInRange;
                pawn.ageTracker.AgeBiologicalTicks = (long)(targetAge * 3600000f);
                pawn.ageTracker.AgeChronologicalTicks = pawn.ageTracker.AgeBiologicalTicks;

                // Spawn the animal
                GenSpawn.Spawn(pawn, pos, map);
                MOLog.Message($"Spawned {animalOption.pawnKindDef.defName} at {pos} (age: {targetAge:F1} years)");
                return true;
            }
            catch (System.Exception e)
            {
                MOLog.Error($"Exception spawning {animalOption.pawnKindDef?.defName}: {e.Message}");
                return false;
            }
        }

        private bool SpawnSingleThing(POISpawnOption option, IntVec3 pos)
        {
            try
            {
                if (option.thingDef != null)
                {
                    Thing thing = ThingMaker.MakeThing(option.thingDef);
                    if (thing == null)
                    {
                        MOLog.Error($"Failed to make thing of type {option.thingDef.defName}");
                        return false;
                    }

                    // Set stack count
                    if (thing.def.stackLimit > 1)
                    {
                        int stackSize = option.stackCount.RandomInRange;
                        thing.stackCount = Mathf.Min(stackSize, thing.def.stackLimit);
                        MOLog.Message($"Set stack count to {thing.stackCount} (requested: {stackSize}, limit: {thing.def.stackLimit})");
                    }

                    // Set quality if applicable
                    CompQuality qualityComp = thing.TryGetComp<CompQuality>();
                    if (qualityComp != null && option.qualityRange != QualityRange.All)
                    {
                        var validQualities = System.Enum.GetValues(typeof(QualityCategory))
                            .Cast<QualityCategory>()
                            .Where(q => option.qualityRange.Includes(q))
                            .ToArray();
                        
                        if (validQualities.Length > 0)
                        {
                            QualityCategory randomQuality = validQualities.RandomElement();
                            qualityComp.SetQuality(randomQuality, ArtGenerationContext.Outsider);
                            MOLog.Message($"Set quality to {randomQuality}");
                        }
                    }

                    GenSpawn.Spawn(thing, pos, map);
                    MOLog.Message($"Spawned {option.thingDef.defName} at {pos}");
                    return true;
                }
                else if (option.terrainDef != null)
                {
                    map.terrainGrid.SetTerrain(pos, option.terrainDef);
                    MOLog.Message($"Set terrain to {option.terrainDef.defName} at {pos}");
                    return true;
                }
                else
                {
                    MOLog.Error($"POI spawn option has neither thingDef nor terrainDef");
                    return false;
                }
            }
            catch (System.Exception e)
            {
                MOLog.Error($"Exception spawning thing {option.thingDef?.defName}: {e.Message}");
                return false;
            }
        }
    }
}