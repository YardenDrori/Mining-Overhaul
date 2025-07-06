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
    public class CreatureOption
    {
        public string creatureDef;
        public float weight = 1f;
        public float manhunterChance = 0f;
    }

    [System.Serializable]
    public class ThreatTier
    {
        public float minInstability = 0f;
        public float maxInstability = 1f;
        public List<CreatureOption> creatures = new List<CreatureOption>();
        public float maxCreaturesSpawned = 1f;
        public float spawnIntervalHours = 2f;
        public float spawnChance = 1f;
    }

    [System.Serializable]
    public class SimpleCreatureSpawn
    {
        public string label = "Spawn Group";
        public List<ThreatTier> threatTiers = new List<ThreatTier>();
        public float instabilityMultiplier = 1f;
    }

    public class CompProperties_CavernCreatureSpawner : CompProperties
    {
        public List<SimpleCreatureSpawn> spawnConfigs = new List<SimpleCreatureSpawn>();
        public int avoidanceRadius = 5;
        public bool debugMode = false;

        public CompProperties_CavernCreatureSpawner()
        {
            compClass = typeof(CompCavernCreatureSpawner);
        }
    }

    public class CompCavernCreatureSpawner : ThingComp
    {
        private Dictionary<ThreatTier, int> nextSpawnTicks = new Dictionary<ThreatTier, int>();
        private List<IntVec3> validSpawnCells = new List<IntVec3>();
        private int lastValidationTick = -999999;
        private const int VALIDATION_INTERVAL = 3600; // Revalidate every 60 seconds

        public CompProperties_CavernCreatureSpawner Props => (CompProperties_CavernCreatureSpawner)props;

        private CavernEntrance CavernEntrance => parent as CavernEntrance;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (Props.debugMode)
            {
                MOLog.Message($"CompCavernCreatureSpawner: Component spawned, {Props.spawnConfigs.Count} configs loaded - waiting for pocket map");
            }
        }

        private void InitializeSpawnTicks()
        {
            nextSpawnTicks.Clear();
            foreach (var config in Props.spawnConfigs)
            {
                foreach (var tier in config.threatTiers)
                {
                    CalculateNextSpawnTick(tier);
                    if (Props.debugMode)
                    {
                        MOLog.Message($"CompCavernCreatureSpawner: Initialized timer for {config.label} tier {tier.minInstability:P0}-{tier.maxInstability:P0}");
                    }
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
            
            // Update valid spawn cells periodically
            if (ShouldUpdateValidCells())
            {
                UpdateValidSpawnCells();
            }

            // Check each tier for spawn timing
            var currentTick = Find.TickManager.TicksGame;
            var tiersToSpawn = new List<ThreatTier>();
            
            foreach (var kvp in nextSpawnTicks.ToList())
            {
                if (currentTick >= kvp.Value)
                {
                    tiersToSpawn.Add(kvp.Key);
                }
            }

            foreach (var tier in tiersToSpawn)
            {
                if (Props.debugMode)
                {
                    MOLog.Message($"CompCavernCreatureSpawner: Timer triggered for tier {tier.minInstability:P0}-{tier.maxInstability:P0}");
                }
                TrySpawnCreatures(tier);
                CalculateNextSpawnTick(tier);
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

            validSpawnCells.Clear();
            
            foreach (IntVec3 cell in pocketMap.AllCells)
            {
                if (IsValidSpawnCell(cell))
                {
                    validSpawnCells.Add(cell);
                }
            }
            
            lastValidationTick = Find.TickManager.TicksGame;
            
            if (Props.debugMode)
            {
                MOLog.Message($"CompCavernCreatureSpawner: Updated spawn cells - {validSpawnCells.Count} valid positions");
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
            var thingList = cell.GetThingList(map);
            foreach (var thing in thingList)
            {
                if (thing.def.category == ThingCategory.Building || 
                    thing.def.category == ThingCategory.Pawn ||
                    thing.def.passability == Traversability.Impassable)
                    return false;
            }

            // Simple avoidance - stay away from exit and colonists
            var exitPosition = FindCavernExitPosition();
            if (exitPosition.IsValid && cell.DistanceTo(exitPosition) < Props.avoidanceRadius)
                return false;

            // Check minimum distance from colonists
            foreach (var colonist in map.mapPawns.FreeColonists)
            {
                if (colonist.Position.DistanceTo(cell) < 8)
                    return false;
            }

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

        private void TrySpawnCreatures(ThreatTier tier)
        {
            if (CavernEntrance == null || validSpawnCells.Count == 0)
            {
                if (Props.debugMode)
                {
                    MOLog.Warning($"CompCavernCreatureSpawner: Cannot spawn - CavernEntrance: {CavernEntrance != null}, ValidCells: {validSpawnCells.Count}");
                }
                return;
            }

            float currentInstability = GetCurrentInstabilityPercent();
            
            // Check if tier is valid for current instability
            if (currentInstability < tier.minInstability || currentInstability > tier.maxInstability)
            {
                if (Props.debugMode)
                {
                    MOLog.Message($"CompCavernCreatureSpawner: Tier skipped - instability {currentInstability:P1} outside range {tier.minInstability:P1}-{tier.maxInstability:P1}");
                }
                return;
            }

            // Roll spawn chance
            if (!Rand.Chance(tier.spawnChance))
            {
                if (Props.debugMode)
                {
                    MOLog.Message($"CompCavernCreatureSpawner: Tier failed spawn chance ({tier.spawnChance:P1})");
                }
                return;
            }

            if (Props.debugMode)
            {
                MOLog.Message($"CompCavernCreatureSpawner: Spawning from tier {tier.minInstability:P0}-{tier.maxInstability:P0}!");
            }

            SpawnCreaturesFromTier(tier, currentInstability);
        }

        private float GetCurrentInstabilityPercent()
        {
            if (CavernEntrance == null)
                return 0f;

            // Direct property access instead of reflection
            return CavernEntrance.StabilityLoss;
        }

        private void SpawnCreaturesFromTier(ThreatTier tier, float currentInstability)
        {
            if (tier.creatures.Count == 0 || validSpawnCells.Count == 0)
                return;

            // Calculate spawn count using logarithmic scaling
            int spawnCount = CalculateSpawnCount(tier, currentInstability);
            var spawnedPawns = new List<Pawn>();

            for (int i = 0; i < spawnCount; i++)
            {
                // Pick creature using weights
                var chosenCreature = tier.creatures.RandomElementByWeight(c => c.weight);
                var pawnKindDef = DefDatabase<PawnKindDef>.GetNamedSilentFail(chosenCreature.creatureDef);

                if (pawnKindDef == null)
                {
                    if (Props.debugMode)
                    {
                        MOLog.Warning($"CompCavernCreatureSpawner: Could not find PawnKindDef '{chosenCreature.creatureDef}'");
                    }
                    continue;
                }

                var spawnCell = ChooseSpawnCell(spawnedPawns);
                if (!spawnCell.IsValid)
                    continue;

                var pawn = SpawnCreature(pawnKindDef, spawnCell);
                if (pawn != null)
                {
                    // Apply manhunter using per-creature chance
                    if (chosenCreature.manhunterChance > 0 && Rand.Chance(chosenCreature.manhunterChance))
                    {
                        ApplyManhunterState(pawn);
                    }
                    
                    spawnedPawns.Add(pawn);
                }
            }

            if (spawnedPawns.Count > 0)
            {
                SendSpawnNotification(spawnedPawns);
            }
        }

        private int CalculateSpawnCount(ThreatTier tier, float currentInstability)
        {
            // Allow instability >100% but cap spawning effectiveness at 100%
            float spawnInstability = Mathf.Min(currentInstability, 1f);
            
            // Simple logarithmic scaling: 0% = 0 spawns, 100% = max spawns
            // Logarithmic curve provides fast early ramp, controlled late game
            float scaledInstability = Mathf.Log(1 + spawnInstability * ((float)Math.E - 1));
            
            // Find the config this tier belongs to for the multiplier
            float multiplier = 1f;
            foreach (var config in Props.spawnConfigs)
            {
                if (config.threatTiers.Contains(tier))
                {
                    multiplier = config.instabilityMultiplier;
                    break;
                }
            }
            
            float spawnRate = tier.maxCreaturesSpawned * scaledInstability * multiplier;
            
            // Ensure at least 1 creature if instability > 0
            return currentInstability > 0f ? Mathf.Max(1, Mathf.RoundToInt(spawnRate)) : 0;
        }

        private IntVec3 ChooseSpawnCell(List<Pawn> alreadySpawned)
        {
            if (validSpawnCells.Count == 0)
                return IntVec3.Invalid;

            // Try to spawn near already spawned creatures (simple clustering)
            if (alreadySpawned.Count > 0)
            {
                var centerPoint = alreadySpawned[0].Position;
                var nearbyCells = validSpawnCells
                    .Where(cell => cell.DistanceTo(centerPoint) <= 3)
                    .ToList();

                if (nearbyCells.Count > 0)
                    return nearbyCells.RandomElement();
            }

            // Fall back to any valid cell
            return validSpawnCells.RandomElement();
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


        private void SendSpawnNotification(List<Pawn> spawnedPawns)
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
                    var creatureTypes = spawnedPawns.Select(p => p.def.label).Distinct().ToList();
                    if (creatureTypes.Count == 1)
                    {
                        message = $"{spawnedPawns.Count} {creatureTypes[0]}s have emerged from the depths of the cavern.";
                    }
                    else
                    {
                        message = $"{spawnedPawns.Count} creatures have emerged from the depths of the cavern.";
                    }
                }

                Messages.Message(message, new LookTargets(spawnedPawns), MessageTypeDefOf.NeutralEvent);
            }
        }

        private void CalculateNextSpawnTick(ThreatTier tier)
        {
            // Simple calculation: use tier's interval directly
            int intervalTicks = Mathf.RoundToInt(tier.spawnIntervalHours * 2500f);
            nextSpawnTicks[tier] = Find.TickManager.TicksGame + intervalTicks;
            
            if (Props.debugMode)
            {
                MOLog.Message($"CompCavernCreatureSpawner: Next spawn in {tier.spawnIntervalHours:F1} hours");
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref validSpawnCells, "validSpawnCells", LookMode.Value);
            Scribe_Values.Look(ref lastValidationTick, "lastValidationTick", -999999);

            if (validSpawnCells == null)
                validSpawnCells = new List<IntVec3>();
            if (nextSpawnTicks == null)
                nextSpawnTicks = new Dictionary<ThreatTier, int>();

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
                    defaultDesc = "Force spawn creatures from all tiers now",
                    action = delegate
                    {
                        foreach (var config in Props.spawnConfigs)
                        {
                            foreach (var tier in config.threatTiers)
                            {
                                TrySpawnCreatures(tier);
                                CalculateNextSpawnTick(tier);
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
                        lastValidationTick = -999999;
                        UpdateValidSpawnCells();
                    }
                };

                string debugInfo = $"Valid Spawn Cells: {validSpawnCells.Count}";
                debugInfo += $"\nInstability: {GetCurrentInstabilityPercent():P1}";
                debugInfo += $"\nActive Configs: {Props.spawnConfigs.Count}";

                yield return new Command_Action
                {
                    defaultLabel = debugInfo,
                    action = delegate { }
                };
            }
        }
        
    }
}