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
        public List<POISpawnOption> spawnOptions = new List<POISpawnOption>();
        public List<POIAnimalOption> animalOptions = new List<POIAnimalOption>(); // NEW: Animal spawning
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

            if (genStepDef?.poiContent?.spawnOptions == null) return;

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

            // Spawn regular items/terrain
            foreach (POISpawnOption option in genStepDef.poiContent.spawnOptions)
            {
                int thingsToSpawn = option.thingCount.RandomInRange;
                
                for (int i = 0; i < thingsToSpawn; i++)
                {
                    IntVec3 spawnPos = GetSpawnPositionNear(centerPos, genStepDef.poiContent.density, thisPOIPositions);
                    if (spawnPos != IntVec3.Invalid)
                    {
                        SpawnSingleThing(option, spawnPos);
                        thisPOIPositions.Add(spawnPos);
                    }
                }
            }

            // Spawn animals
            foreach (POIAnimalOption option in genStepDef.poiContent.animalOptions)
            {
                int animalsToSpawn = option.animalCount.RandomInRange;
                
                for (int i = 0; i < animalsToSpawn; i++)
                {
                    IntVec3 spawnPos = GetSpawnPositionNear(centerPos, genStepDef.poiContent.density, thisPOIPositions);
                    if (spawnPos != IntVec3.Invalid)
                    {
                        SpawnSingleAnimal(option, spawnPos);
                        thisPOIPositions.Add(spawnPos);
                    }
                }
            }
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