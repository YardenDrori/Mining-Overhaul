using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;

namespace MiningOverhaul
{
    // POI spatial behaviors - controls distribution patterns only, not frequency
    public enum POIBehavior
    {
        Structure,          // Large spacing, clustered placement, avoids entrance
        Vegetation,         // Small spacing, dense coverage, natural patterns  
        Decorative,         // Medium spacing, scattered placement
        Encounter           // Large spacing, clustered placement, far from entrance
    }

    // Clear POI Definition - behavior controls patterns, rarity controls frequency
    public class SimplePOIDef : Def
    {
        public POIBehavior behavior = POIBehavior.Structure;    // Spatial distribution pattern
        public string rarity = "Common";                        // How often to spawn: VeryCommon, Common, Uncommon, Rare, VeryRare
        public List<POISpawnGroup> spawnGroups = new List<POISpawnGroup>(); // Groups of items that spawn together
        public List<string> allowedBiomes = new List<string>(); // empty = all biomes
        
        // Count controls
        public int minCount = 0;                                // Minimum number to spawn
        public int maxCount = 10;                               // Maximum number to spawn
    }

    // A group of items that spawn together - balanced approach
    public class POISpawnGroup
    {
        public List<POIItem> items = new List<POIItem>();       // Items in this group
        public float radius = 4f;                              // How far from center items can spawn
        public string importance = "Required";                  // Required, Optional
        public string groupName = "default";                   // Name for debugging
    }

    // Simple item definition - numbers where they make sense
    public class POIItem
    {
        public ThingDef thingDef;                              // For items, buildings, filth
        public PawnKindDef pawnKindDef;                        // For creatures
        public IntRange count = new IntRange(1, 1);            // How many to spawn
        public IntRange stackCount = new IntRange(1, 1);       // Stack size per item
        public float chance = 1f;                              // Chance this item spawns (0.0-1.0)
    }

    // Simplified GenStep that does the work
    public class GenStep_POIGeneration : GenStep
    {
        public SimplePOIDef poiDef;
        private List<IntVec3> usedPositions = new List<IntVec3>();
        private Map map;
        private IntVec3 caveEntrancePos = IntVec3.Invalid;  // Cached cave entrance position

        public override int SeedPart => 123456789;

        public override void Generate(Map map, GenStepParams parms)
        {
            this.map = map;
            usedPositions.Clear();
            caveEntrancePos = IntVec3.Invalid;  // Reset cached entrance position

            if (poiDef == null)
            {
                MOLog.Warning($"POI GenStep has no POI definition");
                return;
            }

            if (poiDef.spawnGroups.NullOrEmpty())
            {
                MOLog.Warning($"POI {poiDef.defName} has no spawn groups defined");
                return;
            }
            
            // Apply behavior-based defaults
            ApplyBehaviorDefaults();

            if (MiningOverhaulMod.settings.verboseLogging)
            {
                MOLog.Message($"Starting POI generation: {poiDef.defName}");
            }

            // Check biome restriction
            if (!poiDef.allowedBiomes.NullOrEmpty())
            {
                // Add your cave biome detection here when ready
                // For now, always allow
            }

            int spawnedCount = GeneratePOIs();
            
            if (MiningOverhaulMod.settings.verboseLogging)
            {
                MOLog.Message($"POI Summary: {poiDef.defName} spawned {spawnedCount} instances (target: {poiDef.minCount}-{poiDef.maxCount})");
            }
        }

        private int GeneratePOIs()
        {
            int gridSize = Mathf.RoundToInt(GetDefaultMinDistance());
            if (gridSize < 1) gridSize = 1;

            int currentCount = 0;
            List<IntVec3> allCandidatePositions = new List<IntVec3>();

            // First pass: collect all valid candidate positions based on distribution pattern
            CollectCandidatePositions(allCandidatePositions, gridSize);

            // Shuffle candidates for random selection
            allCandidatePositions.Shuffle();
            
            if (MiningOverhaulMod.settings.verboseLogging)
            {
                MOLog.Message($"POI {poiDef.defName}: Found {allCandidatePositions.Count} candidate positions");
            }

            // Try to spawn POIs respecting min/max constraints
            int maxRetries = Mathf.RoundToInt(allCandidatePositions.Count * GetRetryMultiplier());
            int retryCount = 0;
            
            foreach (IntVec3 candidatePos in allCandidatePositions)
            {
                if (currentCount >= poiDef.maxCount)
                    break;

                if (Rand.Chance(GetDefaultSpawnChance()) && IsValidPOISpot(candidatePos))
                {
                    if (TrySpawnPOI(candidatePos))
                    {
                        currentCount++;
                    }
                }
            }

            // If we haven't met minimum requirements, try harder
            while (currentCount < poiDef.minCount && retryCount < maxRetries)
            {
                foreach (IntVec3 candidatePos in allCandidatePositions)
                {
                    if (currentCount >= poiDef.maxCount)
                        break;

                    if (!usedPositions.Contains(candidatePos) && IsValidPOISpot(candidatePos))
                    {
                        if (TrySpawnPOI(candidatePos))
                        {
                            currentCount++;
                            break;
                        }
                    }
                }
                retryCount++;
            }

            if (MiningOverhaulMod.settings.verboseLogging && currentCount < poiDef.minCount)
            {
                MOLog.Warning($"POI {poiDef.defName}: Only spawned {currentCount}/{poiDef.minCount} minimum required after {retryCount} retries");
            }

            return currentCount;
        }

        private void ApplyBehaviorDefaults()
        {
            // Behavior no longer sets count defaults - they're explicit in XML
            // Just validate the values
            if (poiDef.minCount < 0) poiDef.minCount = 0;
            if (poiDef.maxCount < poiDef.minCount) poiDef.maxCount = poiDef.minCount + 5;
        }
        
        private int GetDefaultMinCount()
        {
            // Use explicit minCount from XML, no behavior-based defaults
            return poiDef.minCount;
        }
        
        private int GetDefaultMaxCount()
        {
            // Use explicit maxCount from XML, no behavior-based defaults
            return poiDef.maxCount;
        }
        
        private float GetDefaultMinDistance()
        {
            // Behavior only controls spatial patterns
            switch (poiDef.behavior)
            {
                case POIBehavior.Vegetation: return 4f;  // Accounts for typical vegetation radius
                case POIBehavior.Decorative: return 12f;
                case POIBehavior.Structure: return 25f;
                case POIBehavior.Encounter: return 30f;
                default: return 15f;
            }
        }
        
        private float GetDefaultSpawnChance()
        {
            // Convert rarity to spawn chance
            switch (poiDef.rarity.ToLower())
            {
                case "verycommon": return 0.9f;
                case "common": return 0.6f;
                case "uncommon": return 0.3f;
                case "rare": return 0.15f;
                case "veryrare": return 0.05f;
                default: return 0.3f;
            }
        }

        private bool IsValidPOISpot(IntVec3 pos)
        {
            if (!pos.InBounds(map)) return false;

            // Check distance from other POIs of same type
            float minDistance = GetDefaultMinDistance();
            foreach (IntVec3 usedPos in usedPositions)
            {
                float distance = pos.DistanceTo(usedPos);
                if (distance < minDistance)
                    return false;
            }

            // Check distance from cave entrance based on avoidEntrance setting
            float requiredDistance = GetEntranceAvoidanceDistance();
            if (requiredDistance > 0f)
            {
                IntVec3 entrancePos = FindCaveEntrance();
                if (entrancePos != IntVec3.Invalid)
                {
                    float entranceDistance = pos.DistanceTo(entrancePos);
                    if (entranceDistance < requiredDistance)
                        return false;
                }
            }

            return true;
        }

        private IntVec3 FindCaveEntrance()
        {
            // Return cached position if already found
            if (caveEntrancePos.IsValid)
                return caveEntrancePos;

            // Look for the cave entrance (where players spawn)
            // In RimWorld pocket maps, the entrance is usually where the MapParent connection is
            
            // First, try to find a CavernExit building (the exit that leads back to surface)
            foreach (Building building in map.listerBuildings.allBuildingsColonist)
            {
                if (building.def.defName == "CavernExit")
                {
                    caveEntrancePos = building.Position;
                    if (MiningOverhaulMod.settings.verboseLogging)
                        MOLog.Message($"Found CavernExit building at {caveEntrancePos}");
                    return caveEntrancePos;
                }
            }
            
            // If no building found, try to find it in all things
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing.def.defName == "CavernExit")
                {
                    caveEntrancePos = thing.Position;
                    if (MiningOverhaulMod.settings.verboseLogging)
                        MOLog.Message($"Found CavernExit thing at {caveEntrancePos}");
                    return caveEntrancePos;
                }
            }
            
            // Fallback: use map center if no exit found
            caveEntrancePos = map.Center;
            if (MiningOverhaulMod.settings.verboseLogging)
                MOLog.Warning($"No CavernExit found, using map center {caveEntrancePos} as entrance position");
            return caveEntrancePos;
        }

        private bool TrySpawnPOI(IntVec3 centerPos)
        {
            List<IntVec3> thisPOIPositions = new List<IntVec3>();
            int itemsSpawned = 0;
            bool hasRequiredGroup = false;

            // Process each spawn group
            foreach (POISpawnGroup group in poiDef.spawnGroups)
            {
                int groupItemsSpawned = 0;
                
                // Spawn items in this group
                foreach (POIItem item in group.items)
                {
                    // Check if this item should spawn based on its chance
                    if (!Rand.Chance(item.chance))
                        continue;
                        
                    int countToSpawn = item.count.RandomInRange;
                    for (int i = 0; i < countToSpawn; i++)
                    {
                        IntVec3 spawnPos = GetSpawnPosition(centerPos, group.radius, thisPOIPositions);
                        if (spawnPos.IsValid && SpawnItem(item, spawnPos))
                        {
                            thisPOIPositions.Add(spawnPos);
                            groupItemsSpawned++;
                            itemsSpawned++;
                        }
                    }
                }
                
                // Check if required group succeeded
                bool groupRequired = group.importance.ToLower() == "required";
                if (groupRequired && groupItemsSpawned > 0)
                {
                    hasRequiredGroup = true;
                }
                else if (groupRequired && groupItemsSpawned == 0)
                {
                    // Required group failed, POI fails
                    if (MiningOverhaulMod.settings.verboseLogging)
                    {
                        MOLog.Warning($"POI Failed: {poiDef.defName} - Required group '{group.groupName}' failed to spawn at {centerPos}");
                    }
                    return false;
                }
            }

            // POI succeeds if we spawned something and all required groups succeeded
            bool success = itemsSpawned > 0 && (hasRequiredGroup || !poiDef.spawnGroups.Any(g => g.importance.ToLower() == "required"));
            if (success)
            {
                usedPositions.Add(centerPos);
            }

            // Log result
            if (MiningOverhaulMod.settings.verboseLogging)
            {
                if (success)
                {
                    MOLog.Message($"POI Spawned: {poiDef.defName} with {itemsSpawned} items across {poiDef.spawnGroups.Count} groups at {centerPos}");
                }
                else
                {
                    MOLog.Warning($"POI Empty: {poiDef.defName} - No items spawned at {centerPos} (tried {poiDef.spawnGroups.Count} groups)");
                }
            }

            return success;
        }
        
        private float GetRetryMultiplier()
        {
            // Smart retry based on behavior
            switch (poiDef.behavior)
            {
                case POIBehavior.Vegetation:
                case POIBehavior.Decorative:
                    return 1.5f;  // Easy to place
                case POIBehavior.Structure:
                case POIBehavior.Encounter:
                    return 3f;    // Harder to place
                default:
                    return 2f;
            }
        }

        private void CollectCandidatePositions(List<IntVec3> candidatePositions, int gridSize)
        {
            // Use behavior to determine spatial pattern only
            switch (poiDef.behavior)
            {
                case POIBehavior.Vegetation:
                    CollectDensePositions(candidatePositions, GetGridSize(gridSize, 0.5f));
                    break;
                case POIBehavior.Decorative:
                    CollectScatteredPositions(candidatePositions, GetGridSize(gridSize, 0.8f));
                    break;
                case POIBehavior.Encounter:
                case POIBehavior.Structure:
                default:
                    CollectClusteredPositions(candidatePositions, GetGridSize(gridSize, 1.5f));
                    break;
            }
        }

        private int GetGridSize(int baseGridSize, float multiplier)
        {
            // Apply coverage-based multiplier
            float coverageMultiplier = GetCoverageMultiplier();
            int effectiveSize = Mathf.RoundToInt(baseGridSize / (multiplier * coverageMultiplier));
            return Mathf.Max(1, effectiveSize);
        }
        
        private float GetCoverageMultiplier()
        {
            // No density field anymore - behavior controls this
            return 1f;
        }

        private void CollectDensePositions(List<IntVec3> candidatePositions, int gridSize)
        {
            // Dense pattern: Small grid with slight randomization for natural look
            int smallGrid = Mathf.Max(1, gridSize / 2);
            for (int x = 0; x < map.Size.x; x += smallGrid)
            {
                for (int z = 0; z < map.Size.z; z += smallGrid)
                {
                    IntVec3 centerPos = new IntVec3(x + Rand.Range(0, smallGrid), 0, z + Rand.Range(0, smallGrid));
                    centerPos = centerPos.ClampInsideMap(map);
                    
                    if (IsValidPOISpot(centerPos))
                    {
                        candidatePositions.Add(centerPos);
                    }
                }
            }
        }

        private void CollectScatteredPositions(List<IntVec3> candidatePositions, int gridSize)
        {
            // Scattered pattern: Random positions with minimum distance constraints
            int attempts = (map.Size.x * map.Size.z) / (gridSize * gridSize);
            for (int i = 0; i < attempts; i++)
            {
                IntVec3 randomPos = new IntVec3(Rand.Range(0, map.Size.x), 0, Rand.Range(0, map.Size.z));
                if (IsValidPOISpot(randomPos))
                {
                    candidatePositions.Add(randomPos);
                }
            }
        }

        private void CollectSparsePositions(List<IntVec3> candidatePositions, int gridSize)
        {
            // Sparse pattern: Large grid with extra spacing
            int largeGrid = gridSize * 2;
            for (int x = 0; x < map.Size.x; x += largeGrid)
            {
                for (int z = 0; z < map.Size.z; z += largeGrid)
                {
                    IntVec3 centerPos = new IntVec3(x + Rand.Range(0, largeGrid), 0, z + Rand.Range(0, largeGrid));
                    centerPos = centerPos.ClampInsideMap(map);
                    
                    if (IsValidPOISpot(centerPos))
                    {
                        candidatePositions.Add(centerPos);
                    }
                }
            }
        }

        private void CollectClusteredPositions(List<IntVec3> candidatePositions, int gridSize)
        {
            // Clustered pattern: Standard grid (original behavior)
            for (int x = 0; x < map.Size.x; x += gridSize)
            {
                for (int z = 0; z < map.Size.z; z += gridSize)
                {
                    IntVec3 centerPos = new IntVec3(x + Rand.Range(0, gridSize), 0, z + Rand.Range(0, gridSize));
                    centerPos = centerPos.ClampInsideMap(map);
                    
                    if (IsValidPOISpot(centerPos))
                    {
                        candidatePositions.Add(centerPos);
                    }
                }
            }
        }
        
        private float GetEntranceAvoidanceDistance()
        {
            // Behavior determines entrance avoidance
            switch (poiDef.behavior)
            {
                case POIBehavior.Encounter: return 25f;  // Far from entrance
                case POIBehavior.Structure: return 15f;  // Some distance
                default: return 0f;                     // No avoidance
            }
        }
        
        // Removed overcomplicated string-to-number conversions
        
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
                        if (MiningOverhaulMod.settings.verboseLogging)
                            MOLog.Error($"Failed to generate pawn of type {item.pawnKindDef.defName}");
                        return false;
                    }
                    
                    GenSpawn.Spawn(pawn, pos, map);
                    return true;
                }
                
                // Handle things (items, buildings, filth)
                if (item.thingDef != null)
                {
                    // Check for plant overlap and fertility before spawning
                    if (item.thingDef.category == ThingCategory.Plant)
                    {
                        // Check if terrain can support plant growth
                        float fertility = map.fertilityGrid.FertilityAt(pos);
                        if (fertility <= 0f)
                        {
                            // No fertility (rock, water, etc.), skip spawning
                            return false;
                        }

                        List<Thing> thingsHere = map.thingGrid.ThingsListAt(pos);
                        foreach (Thing existingThing in thingsHere)
                        {
                            if (existingThing.def.category == ThingCategory.Plant)
                            {
                                // Already has a plant, skip spawning
                                return false;
                            }
                        }
                    }

                    Thing thing = ThingMaker.MakeThing(item.thingDef);
                    if (thing == null)
                    {
                        if (MiningOverhaulMod.settings.verboseLogging)
                            MOLog.Error($"Failed to make thing of type {item.thingDef.defName} - ThingMaker returned null");
                        return false;
                    }

                    // Set stack count for stackable items
                    if (thing.def.stackLimit > 1)
                    {
                        int stackSize = item.stackCount.RandomInRange;
                        thing.stackCount = Mathf.Min(stackSize, thing.def.stackLimit);
                    }

                    // Set random maturity for plants (50% to 100%)
                    if (thing.def.category == ThingCategory.Plant && thing is Plant plant)
                    {
                        float randomMaturity = Rand.Range(0.5f, 1.0f);
                        plant.Growth = randomMaturity;
                    }

                    GenSpawn.Spawn(thing, pos, map);
                    if (MiningOverhaulMod.settings.verboseLogging)
                        MOLog.Message($"Successfully spawned {thing.def.defName} at {pos}");
                    return true;
                }
                
                if (MiningOverhaulMod.settings.verboseLogging)
                    MOLog.Error($"POI item has neither thingDef nor pawnKindDef");
                return false;
            }
            catch (System.Exception e)
            {
                if (MiningOverhaulMod.settings.verboseLogging)
                    MOLog.Error($"Exception spawning item: {e.Message}");
                return false;
            }
        }
    }
}