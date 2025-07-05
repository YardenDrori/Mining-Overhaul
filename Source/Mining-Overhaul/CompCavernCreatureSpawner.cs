using RimWorld;
using System;
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
        public float baseSpawnFrequencyHours = 1.0f;                  // Frequency at 50% instability (in hours)
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
        private const int VALIDATION_INTERVAL = 3600; // Revalidate every 60 seconds
        private const int CELLS_PER_VALIDATION = 25; // Process fewer cells per tick to prevent lag
        private int validationIndex = 0;
        private bool hasInitialValidation = false;
        
        // NEW: Cache expensive AllCells collection
        private IntVec3[] allCellsArray = null;
        private int allCellsCount = 0;
        
        // NEW: Cache exit position to avoid repeated searches
        private IntVec3 cachedExitPosition = IntVec3.Invalid;
        private int lastExitPositionCheck = -999999;
        private const int EXIT_POSITION_CACHE_INTERVAL = 600; // 10 seconds

        public CompProperties_CavernCreatureSpawner Props => (CompProperties_CavernCreatureSpawner)props;

        private CavernEntrance CavernEntrance => parent as CavernEntrance;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (Props.debugMode)
            {
                MOLog.Message($"CompCavernCreatureSpawner: Component spawned, {Props.spawnConfigs.Count} configs loaded - waiting for pocket map");
            }
            // Don't initialize timers until pocket map exists
            
            // Initialize cached arrays when pocket map becomes available
            if (CavernEntrance?.PocketMapExists == true)
            {
                InitializeCellsArray();
            }
        }

        private void InitializeSpawnTicks()
        {
            nextSpawnTicks.Clear();
            foreach (var config in Props.spawnConfigs)
            {
                CalculateNextSpawnTick(config);
                if (Props.debugMode)
                {
                    MOLog.Message($"CompCavernCreatureSpawner: Initialized timer for {config.label}");
                }
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            if (CavernEntrance == null)
            {
                if (Props.debugMode && Find.TickManager.TicksGame % 600 == 0) // Every 10 seconds
                {
                    MOLog.Warning("CompCavernCreatureSpawner: CavernEntrance is null");
                }
                return;
            }

            if (!CavernEntrance.PocketMapExists)
            {
                if (Props.debugMode && Find.TickManager.TicksGame % 600 == 0) // Every 10 seconds
                {
                    MOLog.Message("CompCavernCreatureSpawner: No pocket map exists yet");
                }
                return;
            }

            // Initialize timers when pocket map first becomes available
            if (nextSpawnTicks.Count == 0)
            {
                InitializeSpawnTicks();
                if (Props.debugMode)
                {
                    MOLog.Message("CompCavernCreatureSpawner: Pocket map detected - initializing timers");
                }
            }
            
            // Initialize cached cells array if needed
            if (allCellsArray == null)
            {
                InitializeCellsArray();
            }

            // Update valid spawn cells periodically
            if (ShouldUpdateValidCells())
            {
                UpdateValidSpawnCells();
            }

            // Check each config for spawn timing
            var currentTick = Find.TickManager.TicksGame;
            var configsToSpawn = new List<CreatureSpawnConfig>();
            
            // Debug timer status every 10 seconds
            if (Props.debugMode && currentTick % 600 == 0 && nextSpawnTicks.Count > 0)
            {
                MOLog.Message($"CompCavernCreatureSpawner: Timer check - Current tick: {currentTick}");
                foreach (var kvp in nextSpawnTicks)
                {
                    var timeLeft = (kvp.Value - currentTick) / 60000f;
                    MOLog.Message($"  {kvp.Key.label}: Next spawn in {timeLeft:F2} days (tick {kvp.Value})");
                }
            }
            
            foreach (var kvp in nextSpawnTicks.ToList())
            {
                if (currentTick >= kvp.Value)
                {
                    configsToSpawn.Add(kvp.Key);
                }
            }

            foreach (var config in configsToSpawn)
            {
                if (Props.debugMode)
                {
                    MOLog.Message($"CompCavernCreatureSpawner: Timer triggered for {config.label}");
                }
                TrySpawnCreatures(config);
                CalculateNextSpawnTick(config);
            }
        }

        private bool ShouldUpdateValidCells()
        {
            // Force initial validation when pocket map first exists
            if (!hasInitialValidation && CavernEntrance?.PocketMapExists == true)
            {
                return true;
            }
            
            return Find.TickManager.TicksGame >= lastValidationTick + VALIDATION_INTERVAL;
        }

        // OPTIMIZED: Use cached array instead of expensive AllCells.ToList()
        private void UpdateValidSpawnCells()
        {
            var pocketMap = CavernEntrance?.GetPocketMap();
            if (pocketMap == null)
            {
                validSpawnCells.Clear();
                return;
            }

            // Initialize cached array if needed
            if (allCellsArray == null)
            {
                InitializeCellsArray();
            }

            int endIndex = Mathf.Min(validationIndex + CELLS_PER_VALIDATION, allCellsCount);

            // First pass: clear old cells if starting fresh
            if (validationIndex == 0)
            {
                validSpawnCells.Clear();
            }

            // Process chunk of cells using cached array (O(1) access)
            for (int i = validationIndex; i < endIndex; i++)
            {
                IntVec3 cell = allCellsArray[i];
                if (IsValidSpawnCell(cell))
                {
                    validSpawnCells.Add(cell);
                }
            }

            validationIndex = endIndex;

            // Complete validation cycle
            if (validationIndex >= allCellsCount)
            {
                validationIndex = 0;
                lastValidationTick = Find.TickManager.TicksGame;
                
                // Only log when there's an issue or first time
                if (Props.debugMode && validSpawnCells.Count == 0)
                {
                    MOLog.Warning($"CompCavernCreatureSpawner: No valid spawn cells found after full validation");
                }
                else if (Props.debugMode && !hasInitialValidation)
                {
                    MOLog.Message($"CompCavernCreatureSpawner: Initial validation complete - {validSpawnCells.Count} valid spawn cells");
                }
                
                hasInitialValidation = true;
            }
        }

        // OPTIMIZED: Faster validation with caching and optimized loops
        private bool IsValidSpawnCell(IntVec3 cell)
        {
            var map = CavernEntrance?.GetPocketMap();
            if (map == null)
                return false;

            // Basic cell validation (cheap operations first)
            if (!cell.InBounds(map) || !cell.Walkable(map) || cell.Fogged(map))
                return false;

            // Check for blocking things - optimized loop instead of LINQ
            var thingList = cell.GetThingList(map);
            for (int i = 0; i < thingList.Count; i++)
            {
                var thing = thingList[i];
                if (thing.def.category == ThingCategory.Building || 
                    thing.def.category == ThingCategory.Pawn ||
                    thing.def.passability == Traversability.Impassable)
                    return false;
            }

            // Avoidance logic with cached exit position
            if (Props.useExitAvoidance)
            {
                var exitPosition = GetCachedExitPosition();
                if (exitPosition.IsValid && cell.DistanceTo(exitPosition) < Props.exitAvoidanceRadius)
                    return false;
            }
            else
            {
                // Avoid map center
                if (cell.DistanceTo(map.Center) < Props.centerAvoidanceRadius)
                    return false;
            }

            // Check minimum distance from colonists - optimized loop
            var colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                if (colonists[i].Position.DistanceTo(cell) < 8)
                    return false;
            }

            return true;
        }

        // NEW: Cached exit position to avoid repeated expensive searches
        private IntVec3 GetCachedExitPosition()
        {
            if (Find.TickManager.TicksGame > lastExitPositionCheck + EXIT_POSITION_CACHE_INTERVAL)
            {
                cachedExitPosition = FindCavernExitPosition();
                lastExitPositionCheck = Find.TickManager.TicksGame;
            }
            return cachedExitPosition;
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
            {
                if (Props.debugMode)
                {
                    MOLog.Warning($"CompCavernCreatureSpawner: Cannot spawn {config.label} - CavernEntrance: {CavernEntrance != null}, ValidCells: {validSpawnCells.Count}");
                }
                return;
            }

            float currentStabilityLoss = GetCurrentStabilityLossPercent();
            
            if (Props.debugMode)
            {
                MOLog.Message($"CompCavernCreatureSpawner: {config.label} - Stability: {currentStabilityLoss:P1}, Range: {config.minStabilityLoss:P1}-{config.maxStabilityLoss:P1}");
            }
            
            // Check if config is valid for current stability
            if (currentStabilityLoss < config.minStabilityLoss || currentStabilityLoss > config.maxStabilityLoss)
            {
                if (Props.debugMode)
                {
                    MOLog.Message($"CompCavernCreatureSpawner: {config.label} skipped - stability {currentStabilityLoss:P1} outside range {config.minStabilityLoss:P1}-{config.maxStabilityLoss:P1}");
                }
                return;
            }

            if (!Rand.Chance(config.spawnChance))
            {
                if (Props.debugMode)
                {
                    MOLog.Message($"CompCavernCreatureSpawner: {config.label} failed spawn chance ({config.spawnChance:P1})");
                }
                return;
            }

            if (Props.debugMode)
            {
                MOLog.Message($"CompCavernCreatureSpawner: {config.label} passed all checks - spawning creatures!");
            }

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

        // OPTIMIZED: Don't re-validate all cells synchronously at spawn time
        private IntVec3 ChooseSpawnCell(CreatureSpawnConfig config, List<Pawn> alreadySpawned)
        {
            if (validSpawnCells.Count == 0)
                return IntVec3.Invalid;

            // Try to spawn near already spawned creatures if within radius
            if (alreadySpawned.Count > 0 && config.spawnRadius > 0)
            {
                var centerPoint = alreadySpawned[0].Position;
                
                // Quick validation of a few nearby cells (max 10 to prevent lag)
                int maxChecks = Math.Min(10, validSpawnCells.Count);
                var nearbyCells = validSpawnCells
                    .Where(cell => cell.DistanceTo(centerPoint) <= config.spawnRadius)
                    .Take(maxChecks)
                    .ToList();

                // Quick validation of just the nearby cells
                foreach (var cell in nearbyCells)
                {
                    if (IsValidSpawnCell(cell))
                        return cell;
                }
            }

            // Fall back to checking a few random cells from the pre-validated list
            int attempts = 0;
            while (attempts < 5 && validSpawnCells.Count > 0)
            {
                var randomCell = validSpawnCells.RandomElement();
                if (IsValidSpawnCell(randomCell))
                    return randomCell;
                
                attempts++;
            }

            // Last resort: return any cell from validated list (may be slightly stale but better than lag)
            return validSpawnCells.Count > 0 ? validSpawnCells.RandomElement() : IntVec3.Invalid;
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

            // Only send notification if verbose logging is enabled
            if (MiningOverhaulMod.settings?.verboseLogging == true)
            {
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
        }

        private void CalculateNextSpawnTick(CreatureSpawnConfig config)
        {
            float currentStabilityLoss = GetCurrentStabilityLossPercent();
            
            // If stability is 0, use base frequency as-is (no scaling)
            if (currentStabilityLoss <= 0.01f)
            {
                float baseIntervalHours = config.baseSpawnFrequencyHours;
                int baseTicks = Mathf.RoundToInt(baseIntervalHours * 2500f); // 1 hour = 2500 ticks
                nextSpawnTicks[config] = Find.TickManager.TicksGame + baseTicks;
                
                if (Props.debugMode)
                {
                    MOLog.Message($"CompCavernCreatureSpawner: {config.label} - Zero stability, using base interval: {baseIntervalHours:F1} hours");
                }
                return;
            }
            
            // Calculate effective stability for scaling: apply scaling factor and normalize to 50% baseline
            float effectiveStability = currentStabilityLoss * config.instabilityScalingFactor;
            
            // At 50% (0.5) instability, we want 1.0 frequency multiplier (baseline)
            // Higher instability = more frequent spawning (shorter intervals)
            // Lower instability = less frequent spawning (longer intervals)
            float frequencyMultiplier = effectiveStability / 0.5f;
            
            // Ensure reasonable bounds: 0.1x to 10x frequency (caves need quick response)
            frequencyMultiplier = Mathf.Clamp(frequencyMultiplier, 0.1f, 10f);
            
            // Calculate scaled interval: higher frequency multiplier = shorter interval
            float scaledIntervalHours = config.baseSpawnFrequencyHours / frequencyMultiplier;
            
            // Convert to ticks (1 hour = 2500 ticks)
            int intervalTicks = Mathf.RoundToInt(scaledIntervalHours * 2500f);
            
            nextSpawnTicks[config] = Find.TickManager.TicksGame + intervalTicks;
            
            if (Props.debugMode)
            {
                MOLog.Message($"CompCavernCreatureSpawner: {config.label} - Next spawn in {scaledIntervalHours:F1} hours");
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref validSpawnCells, "validSpawnCells", LookMode.Value);
            Scribe_Values.Look(ref lastValidationTick, "lastValidationTick", -999999);
            Scribe_Values.Look(ref validationIndex, "validationIndex", 0);
            Scribe_Values.Look(ref hasInitialValidation, "hasInitialValidation", false);

            if (validSpawnCells == null)
                validSpawnCells = new List<IntVec3>();
            if (nextSpawnTicks == null)
                nextSpawnTicks = new Dictionary<CreatureSpawnConfig, int>();

            // Reinitialize spawn ticks if needed after loading
            if (Scribe.mode == LoadSaveMode.PostLoadInit && nextSpawnTicks.Count == 0)
            {
                InitializeSpawnTicks();
            }
            
            // Reinitialize cached arrays after loading if pocket map exists
            if (Scribe.mode == LoadSaveMode.PostLoadInit && CavernEntrance?.PocketMapExists == true && allCellsArray == null)
            {
                InitializeCellsArray();
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
        
        // NEW: Initialize cached collections for O(1) access
        private void InitializeCellsArray()
        {
            var pocketMap = CavernEntrance?.GetPocketMap();
            if (pocketMap != null)
            {
                allCellsArray = pocketMap.AllCells.ToArray();
                allCellsCount = allCellsArray.Length;
            }
        }
    }
}