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

            if (genStepDef?.poiContent?.spawnGroups == null) return;

            // Check biome restriction
            if (!genStepDef.allowedBiomes.NullOrEmpty())
            {
                // Add your cave biome detection here when ready
                // For now, always allow
            }

            GeneratePOIs();
        }

        private void GeneratePOIs()
        {
            int gridSize = Mathf.RoundToInt(genStepDef.minDistanceBetweenPOIs);
            if (gridSize < 1) gridSize = 1;

            // Go through map in grid pattern
            for (int x = 0; x < map.Size.x; x += gridSize)
            {
                for (int z = 0; z < map.Size.z; z += gridSize)
                {
                    // Roll for spawn chance
                    if (Rand.Range(0f, 100f) <= genStepDef.spawnChancePercent)
                    {
                        IntVec3 centerPos = new IntVec3(x + Rand.Range(0, gridSize), 0, z + Rand.Range(0, gridSize));
                        centerPos = centerPos.ClampInsideMap(map);
                        
                        if (IsValidPOISpot(centerPos))
                        {
                            SpawnPOI(centerPos);
                        }
                    }
                }
            }
        }

        private bool IsValidPOISpot(IntVec3 pos)
        {
            if (!pos.InBounds(map)) return false;

            // Check distance from other POIs of same type
            foreach (IntVec3 usedPos in usedPositions)
            {
                if (pos.DistanceTo(usedPos) < genStepDef.minDistanceBetweenPOIs)
                    return false;
            }

            return true;
        }

        private void SpawnPOI(IntVec3 centerPos)
        {
            usedPositions.Add(centerPos);

            // Track positions used by THIS POI to avoid overlap
            List<IntVec3> thisPOIPositions = new List<IntVec3>();

            // Spawn each group
            foreach (POISpawnGroup group in genStepDef.poiContent.spawnGroups)
            {
                // Check if this group should spawn
                if (Rand.Chance(group.spawnChance))
                {
                    SpawnGroup(group, centerPos, thisPOIPositions);
                }
            }
        }
        
        private void SpawnGroup(POISpawnGroup group, IntVec3 centerPos, List<IntVec3> thisPOIPositions)
        {
            // Get positions for this group based on pattern
            List<IntVec3> groupPositions = GetGroupPositions(group, centerPos, thisPOIPositions);
            
            // Spawn regular items/terrain
            foreach (POISpawnOption option in group.spawnOptions)
            {
                int thingsToSpawn = option.thingCount.RandomInRange;
                
                for (int i = 0; i < thingsToSpawn; i++)
                {
                    IntVec3 spawnPos = GetBestPositionFromList(groupPositions, thisPOIPositions);
                    if (spawnPos != IntVec3.Invalid)
                    {
                        SpawnSingleThing(option, spawnPos);
                        thisPOIPositions.Add(spawnPos);
                        groupPositions.Remove(spawnPos); // Don't use this position again
                    }
                }
            }

            // Spawn animals
            foreach (POIAnimalOption option in group.animalOptions)
            {
                int animalsToSpawn = option.animalCount.RandomInRange;
                
                for (int i = 0; i < animalsToSpawn; i++)
                {
                    IntVec3 spawnPos = GetBestPositionFromList(groupPositions, thisPOIPositions);
                    if (spawnPos != IntVec3.Invalid)
                    {
                        SpawnSingleAnimal(option, spawnPos);
                        thisPOIPositions.Add(spawnPos);
                        groupPositions.Remove(spawnPos); // Don't use this position again
                    }
                }
            }
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

        private void SpawnSingleAnimal(POIAnimalOption animalOption, IntVec3 pos)
        {
            if (animalOption.pawnKindDef == null) return;

            // Get faction (null = wild animals)
            Faction faction = null;
            if (!animalOption.faction.NullOrEmpty() && animalOption.faction != "null")
            {
                faction = Find.FactionManager.FirstFactionOfDef(FactionDef.Named(animalOption.faction));
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
            if (pawn == null) return;

            // Set age
            float targetAge = pawn.RaceProps.lifeExpectancy * animalOption.ageRange.RandomInRange;
            pawn.ageTracker.AgeBiologicalTicks = (long)(targetAge * 3600000f); // Convert years to ticks
            pawn.ageTracker.AgeChronologicalTicks = pawn.ageTracker.AgeBiologicalTicks;

            // Spawn the animal
            GenSpawn.Spawn(pawn, pos, map);
        }

        private void SpawnSingleThing(POISpawnOption option, IntVec3 pos)
        {
            if (option.thingDef != null)
            {
                Thing thing = ThingMaker.MakeThing(option.thingDef);
                if (thing == null) return;

                // Set stack count
                if (thing.def.stackLimit > 1)
                {
                    thing.stackCount = Mathf.Min(option.stackCount.RandomInRange, thing.def.stackLimit);
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
                    }
                }

                GenSpawn.Spawn(thing, pos, map);
            }
            else if (option.terrainDef != null)
            {
                map.terrainGrid.SetTerrain(pos, option.terrainDef);
            }
        }
    }
}