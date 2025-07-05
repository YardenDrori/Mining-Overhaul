using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MiningOverhaul
{
    [System.Serializable]
    public class CreatureSpawnConfig
    {
        public string label = "Spawn Group";
        public List<string> creatureDefNames = new List<string>();
        public FloatRange baseSpawnCountRange = new FloatRange(1, 3);  // Count at 50% instability
        public float baseSpawnFrequencyDays = 1.0f;                   // Frequency at 50% instability
        public float instabilityScalingFactor = 1.0f;                 // How much instability affects frequency/count
        public float spawnChance = 1.0f;
        public float minStabilityLoss = 0.0f;
        public float maxStabilityLoss = 1.0f;
        public int spawnRadius = 2;
        public float manhunterChance = 0.0f;                          // Chance for spawned creatures to be manhunters
        public List<string> spawnEffects = new List<string>();
        public List<string> spawnSounds = new List<string>();
    }

    public class CompProperties_CavernCreatureSpawner : CompProperties
    {
        public List<CreatureSpawnConfig> spawnConfigs = new List<CreatureSpawnConfig>();
        public int exitAvoidanceRadius = 5;
        public int centerAvoidanceRadius = 3;
        public bool useExitAvoidance = true;
        public bool debugMode = false;

        public CompProperties_CavernCreatureSpawner()
        {
            compClass = typeof(CompCavernCreatureSpawner);
        }
    }

    public class CompCavernCreatureSpawner : ThingComp
    {
        private Dictionary<CreatureSpawnConfig, int> nextSpawnTicks = new Dictionary<CreatureSpawnConfig, int>();
        private List<IntVec3> validSpawnCells = new List<IntVec3>();
        private int lastValidationTick = -999999;
        private const int VALIDATION_INTERVAL = 600; // Revalidate every 10 seconds
        private const int CELLS_PER_VALIDATION = 25; // Process cells incrementally
        private int validationIndex = 0;

        public CompProperties_CavernCreatureSpawner Props => (CompProperties_CavernCreatureSpawner)props;

        private CavernEntrance CavernEntrance => parent as CavernEntrance;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                InitializeSpawnTicks();
            }
        }

        private void InitializeSpawnTicks()
        {
            nextSpawnTicks.Clear();
            foreach (var config in Props.spawnConfigs)
            {
                CalculateNextSpawnTick(config);
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            if (CavernEntrance == null || !CavernEntrance.PocketMapExists)
                return;

            // Update valid spawn cells periodically
            if (ShouldUpdateValidCells())
            {
                UpdateValidSpawnCells();
            }

            // Check each config for spawn timing
            var currentTick = Find.TickManager.TicksGame;
            var configsToSpawn = new List<CreatureSpawnConfig>();
            
            foreach (var kvp in nextSpawnTicks.ToList())
            {
                if (currentTick >= kvp.Value)
                {
                    configsToSpawn.Add(kvp.Key);
                }
            }

            foreach (var config in configsToSpawn)
            {
                TrySpawnCreatures(config);
                CalculateNextSpawnTick(config);
            }
        }

        private bool ShouldUpdateValidCells()
        {
            return Find.TickManager.TicksGame >= lastValidationTick + VALIDATION_INTERVAL;
        }

        private void UpdateValidSpawnCells()
        {
            var pocketMap = CavernEntrance?.GetPocketMap();
            if (pocketMap == null)
            {
                validSpawnCells.Clear();
                return;
            }

            // Incremental validation to spread CPU load
            var allCells = pocketMap.AllCells.ToList();
            int totalCells = allCells.Count;
            int endIndex = Mathf.Min(validationIndex + CELLS_PER_VALIDATION, totalCells);

            // First pass: clear old cells if starting fresh
            if (validationIndex == 0)
            {
                validSpawnCells.Clear();
            }

            // Process chunk of cells
            for (int i = validationIndex; i < endIndex; i++)
            {
                IntVec3 cell = allCells[i];
                if (IsValidSpawnCell(cell))
                {
                    validSpawnCells.Add(cell);
                }
            }

            validationIndex = endIndex;

            // Complete validation cycle
            if (validationIndex >= totalCells)
            {
                validationIndex = 0;
                lastValidationTick = Find.TickManager.TicksGame;
                
                if (Props.debugMode)
                {
                    MOLog.Message($"CompCavernCreatureSpawner: Found {validSpawnCells.Count} valid spawn cells");
                }
            }
        }

        private bool IsValidSpawnCell(IntVec3 cell)
        {
            var map = CavernEntrance?.GetPocketMap();
            if (map == null)
                return false;

            // Basic cell validation
            if (!cell.InBounds(map) || !cell.Walkable(map) || cell.Fogged(map))
                return false;

            // Check for blocking things
            if (cell.GetThingList(map).Any(t => t.def.category == ThingCategory.Building || 
                                                t.def.category == ThingCategory.Pawn ||
                                                t.def.passability == Traversability.Impassable))
                return false;

            // Avoidance logic
            if (Props.useExitAvoidance)
            {
                // Avoid cavern exit
                var exitPosition = FindCavernExitPosition();
                if (exitPosition.IsValid && cell.DistanceTo(exitPosition) < Props.exitAvoidanceRadius)
                    return false;
            }
            else
            {
                // Avoid map center
                if (cell.DistanceTo(map.Center) < Props.centerAvoidanceRadius)
                    return false;
            }

            // Check minimum distance from colonists
            var colonists = map.mapPawns.FreeColonists;
            if (colonists.Any(p => p.Position.DistanceTo(cell) < 8))
                return false;

            return true;
        }

        private IntVec3 FindCavernExitPosition()
        {
            var pocketMap = CavernEntrance?.GetPocketMap();
            if (pocketMap == null)
                return IntVec3.Invalid;

            // Find CavernExit building
            var exitBuilding = pocketMap.listerBuildings.allBuildingsColonist
                .FirstOrDefault(b => b.def.defName == "CavernExit");

            if (exitBuilding != null)
                return exitBuilding.Position;

            // Fallback to map center
            return pocketMap.Center;
        }

        private void TrySpawnCreatures(CreatureSpawnConfig config)
        {
            if (CavernEntrance == null || validSpawnCells.Count == 0)
                return;

            float currentStabilityLoss = GetCurrentStabilityLossPercent();
            
            // Check if config is valid for current stability
            if (currentStabilityLoss < config.minStabilityLoss || currentStabilityLoss > config.maxStabilityLoss)
                return;

            if (!Rand.Chance(config.spawnChance))
                return;

            SpawnCreaturesFromConfig(config, currentStabilityLoss);
        }

        private float GetCurrentStabilityLossPercent()
        {
            if (CavernEntrance == null)
                return 0f;

            // Access the stability system from CavernEntrance
            // Using reflection to access private GetStabilityPercent method
            var method = typeof(CavernEntrance).GetMethod("GetStabilityPercent", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (method != null)
            {
                return (float)method.Invoke(CavernEntrance, null);
            }

            return 0f;
        }

        private void SpawnCreaturesFromConfig(CreatureSpawnConfig config, float currentStabilityLoss)
        {
            if (config.creatureDefNames.Count == 0 || validSpawnCells.Count == 0)
                return;

            // Calculate scaled spawn count using the new scaling system
            int spawnCount = CalculateScaledSpawnCount(config, currentStabilityLoss);
            var spawnedPawns = new List<Pawn>();

            for (int i = 0; i < spawnCount; i++)
            {
                // Pick a random creature type for EACH individual spawn
                var chosenCreatureDef = config.creatureDefNames.RandomElement();
                var pawnKindDef = DefDatabase<PawnKindDef>.GetNamedSilentFail(chosenCreatureDef);

                if (pawnKindDef == null)
                {
                    if (Props.debugMode)
                    {
                        MOLog.Warning($"CompCavernCreatureSpawner: Could not find PawnKindDef '{chosenCreatureDef}'");
                    }
                    continue; // Skip this spawn but continue with others
                }

                var spawnCell = ChooseSpawnCell(config, spawnedPawns);
                if (!spawnCell.IsValid)
                    continue;

                var pawn = SpawnCreature(pawnKindDef, spawnCell);
                if (pawn != null)
                {
                    // Apply manhunter mental state if chance succeeds
                    if (config.manhunterChance > 0 && Rand.Chance(config.manhunterChance))
                    {
                        ApplyManhunterState(pawn);
                    }
                    
                    spawnedPawns.Add(pawn);
                    PlaySpawnEffects(config, spawnCell);
                }
            }

            if (spawnedPawns.Count > 0)
            {
                SendSpawnNotification(config, spawnedPawns);
            }
        }

        private int CalculateScaledSpawnCount(CreatureSpawnConfig config, float currentStabilityLoss)
        {
            // Calculate effective stability for scaling: apply scaling factor and normalize to 50% baseline
            float effectiveStability = currentStabilityLoss * config.instabilityScalingFactor;
            
            // At 50% (0.5) instability, we want 1.0 multiplier
            // At 100% (1.0) instability with scaling factor 1, we want 2.0 multiplier  
            // At 10% (0.1) instability with scaling factor 1, we want 0.2 multiplier
            float countMultiplier = effectiveStability / 0.5f;
            
            // Apply multiplier to base count range
            float scaledMin = config.baseSpawnCountRange.min * countMultiplier;
            float scaledMax = config.baseSpawnCountRange.max * countMultiplier;
            
            // Get random value in scaled range and round up to ensure at least 1 creature
            float randomCount = Rand.Range(scaledMin, scaledMax);
            return Mathf.Max(1, Mathf.RoundToInt(randomCount));
        }

        private IntVec3 ChooseSpawnCell(CreatureSpawnConfig config, List<Pawn> alreadySpawned)
        {
            var availableCells = validSpawnCells.Where(cell => IsValidSpawnCell(cell)).ToList();

            if (availableCells.Count == 0)
                return IntVec3.Invalid;

            // Try to spawn near already spawned creatures if within radius
            if (alreadySpawned.Count > 0 && config.spawnRadius > 0)
            {
                var centerPoint = alreadySpawned[0].Position;
                var nearbySpawnCells = availableCells.Where(cell => 
                    cell.DistanceTo(centerPoint) <= config.spawnRadius).ToList();

                if (nearbySpawnCells.Count > 0)
                    return nearbySpawnCells.RandomElement();
            }

            return availableCells.RandomElement();
        }

        private Pawn SpawnCreature(PawnKindDef pawnKindDef, IntVec3 spawnCell)
        {
            var pocketMap = CavernEntrance?.GetPocketMap();
            if (pocketMap == null)
                return null;

            try
            {
                var pawn = PawnGenerator.GeneratePawn(pawnKindDef);
                GenSpawn.Spawn(pawn, spawnCell, pocketMap);
                return pawn;
            }
            catch (System.Exception ex)
            {
                if (Props.debugMode)
                {
                    MOLog.Error($"CompCavernCreatureSpawner: Failed to spawn {pawnKindDef.defName}: {ex.Message}");
                }
                return null;
            }
        }

        private void ApplyManhunterState(Pawn pawn)
        {
            if (pawn?.mindState == null || pawn.Dead)
                return;

            // Only apply to animals that can become manhunters
            if (pawn.RaceProps.Animal && pawn.RaceProps.manhunterOnDamageChance > 0)
            {
                // Apply manhunter mental state
                var manhunterState = DefDatabase<MentalStateDef>.GetNamedSilentFail("Manhunter");
                if (manhunterState != null)
                {
                    pawn.mindState.mentalStateHandler.TryStartMentalState(manhunterState);
                    
                    if (Props.debugMode)
                    {
                        MOLog.Message($"CompCavernCreatureSpawner: Applied manhunter state to {pawn.def.label}");
                    }
                }
            }
        }

        private void PlaySpawnEffects(CreatureSpawnConfig config, IntVec3 spawnCell)
        {
            var pocketMap = CavernEntrance?.GetPocketMap();
            if (pocketMap == null)
                return;

            // Play visual effects
            foreach (var effectName in config.spawnEffects)
            {
                var effectDef = DefDatabase<EffecterDef>.GetNamedSilentFail(effectName);
                if (effectDef != null)
                {
                    effectDef.Spawn(spawnCell, pocketMap).Cleanup();
                }
            }

            // Play sound effects
            foreach (var soundName in config.spawnSounds)
            {
                var soundDef = DefDatabase<SoundDef>.GetNamedSilentFail(soundName);
                if (soundDef != null)
                {
                    soundDef.PlayOneShot(new TargetInfo(spawnCell, pocketMap));
                }
            }

            // Default effect if none specified
            if (config.spawnEffects.Count == 0)
            {
                EffecterDefOf.ImpactSmallDustCloud.Spawn(spawnCell, pocketMap).Cleanup();
            }
        }

        private void SendSpawnNotification(CreatureSpawnConfig config, List<Pawn> spawnedPawns)
        {
            if (spawnedPawns.Count == 0)
                return;

            string message;
            if (spawnedPawns.Count == 1)
            {
                message = $"A {spawnedPawns[0].def.label} has emerged from the depths of the cavern.";
            }
            else
            {
                // Check if all creatures are the same type
                var creatureTypes = spawnedPawns.Select(p => p.def.label).Distinct().ToList();
                if (creatureTypes.Count == 1)
                {
                    // All same type
                    message = $"{spawnedPawns.Count} {creatureTypes[0]}s have emerged from the depths of the cavern.";
                }
                else
                {
                    // Mixed types - use config label or generic message
                    message = spawnedPawns.Count <= 3 
                        ? $"{string.Join(", ", spawnedPawns.Select(p => p.def.label))} have emerged from the depths of the cavern."
                        : $"{spawnedPawns.Count} creatures from {config.label} have emerged from the depths of the cavern.";
                }
            }

            Messages.Message(message, new LookTargets(spawnedPawns), MessageTypeDefOf.NeutralEvent);
        }

        private void CalculateNextSpawnTick(CreatureSpawnConfig config)
        {
            float currentStabilityLoss = GetCurrentStabilityLossPercent();
            
            // Calculate effective stability for scaling: apply scaling factor and normalize to 50% baseline
            float effectiveStability = currentStabilityLoss * config.instabilityScalingFactor;
            
            // At 50% (0.5) instability, we want 1.0 frequency multiplier (baseline)
            // Higher instability = more frequent spawning (shorter intervals)
            // Lower instability = less frequent spawning (longer intervals)
            float frequencyMultiplier = effectiveStability / 0.5f;
            
            // Calculate scaled interval: higher frequency multiplier = shorter interval
            float scaledInterval = frequencyMultiplier > 0 ? config.baseSpawnFrequencyDays / frequencyMultiplier : config.baseSpawnFrequencyDays * 10f;
            
            // Convert to ticks (1 day = 60000 ticks)
            int intervalTicks = Mathf.RoundToInt(scaledInterval * 60000f);
            
            nextSpawnTicks[config] = Find.TickManager.TicksGame + intervalTicks;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref validSpawnCells, "validSpawnCells", LookMode.Value);
            Scribe_Values.Look(ref lastValidationTick, "lastValidationTick", -999999);
            Scribe_Values.Look(ref validationIndex, "validationIndex", 0);

            if (validSpawnCells == null)
                validSpawnCells = new List<IntVec3>();
            if (nextSpawnTicks == null)
                nextSpawnTicks = new Dictionary<CreatureSpawnConfig, int>();

            // Reinitialize spawn ticks if needed after loading
            if (Scribe.mode == LoadSaveMode.PostLoadInit && nextSpawnTicks.Count == 0)
            {
                InitializeSpawnTicks();
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Force Spawn All",
                    defaultDesc = "Force spawn creatures from all configs now",
                    action = delegate
                    {
                        float currentStabilityLoss = GetCurrentStabilityLossPercent();
                        foreach (var config in Props.spawnConfigs)
                        {
                            if (currentStabilityLoss >= config.minStabilityLoss && currentStabilityLoss <= config.maxStabilityLoss)
                            {
                                TrySpawnCreatures(config);
                                CalculateNextSpawnTick(config);
                            }
                        }
                    }
                };

                yield return new Command_Action
                {
                    defaultLabel = "DEV: Refresh Spawn Cells",
                    defaultDesc = "Refresh valid spawn cell cache",
                    action = delegate
                    {
                        validationIndex = 0;
                        lastValidationTick = -999999;
                        UpdateValidSpawnCells();
                    }
                };

                string debugInfo = $"Valid Spawn Cells: {validSpawnCells.Count}";
                debugInfo += $"\nStability Loss: {GetCurrentStabilityLossPercent():P1}";
                debugInfo += $"\nActive Configs: {Props.spawnConfigs.Count}";
                
                var stabilityLoss = GetCurrentStabilityLossPercent();
                var validConfigs = Props.spawnConfigs.Where(config => 
                    stabilityLoss >= config.minStabilityLoss && 
                    stabilityLoss <= config.maxStabilityLoss).Count();
                debugInfo += $"\nValid Configs: {validConfigs}";

                yield return new Command_Action
                {
                    defaultLabel = debugInfo,
                    action = delegate { }
                };
            }
        }
    }
}