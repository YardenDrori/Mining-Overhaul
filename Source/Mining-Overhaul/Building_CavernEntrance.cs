using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;
using UnityEngine;

namespace MiningOverhaul
{
    [StaticConstructorOnStartup]
    public class CavernEntrance : MapPortal
    {
        #region Constants
        private const int PartialCollapseInterval = 120; // Ticks between blocking cells
        private const int AcceleratedCollapseInterval = 15; // Much faster blocking during collapse
        
        // Performance optimization constants
        private const int CELLS_PER_REFRESH = 50; // Process this many cells per tick
        private const int CELLS_PER_VALIDATION = 20; // Validate this many cells per tick
        private const int CACHE_REFRESH_INTERVAL = 300; // Refresh cache every 5 seconds
        #endregion

        #region Configurable Fields (XML accessible)
        // Visual effect frequencies for each stability stage (lower = more frequent)
        public float stage1EffectFrequency = 1f;     // 0-25% stability
        public float stage2EffectFrequency = 1f;     // 25-50% stability  
        public float stage3EffectFrequency = 0.5f;   // 50-75% stability
        public float stage4EffectFrequency = 0.5f;   // 75-100% stability

        // Collapse avoidance settings
        public int centerAvoidanceRange = 0;         // Distance from map center to avoid
        public bool useCenterAvoidance = false;       // Toggle between center vs exit avoidance
        #endregion

        #region Fields
        // Core stability tracking
        private int stabilityLost = 0;
        private int tickOpened = -999999;

        // Collapse states
        private bool isPartiallyCollapsing = false;
        private bool isCollapsing = false;
        private bool isDestroying = false; // Safety flag to prevent double destruction
        private int collapseTick = -999999;
        private IntVec3 caveExitPosition = IntVec3.Invalid;

        // Optimized partial collapse system
        private int lastPartialCollapseTick = -999999;
        
        // Track which pawns we've already logged forced collapse for (to prevent spam)
        private HashSet<int> forcedCollapseLoggedPawns = new HashSet<int>();
        private List<IntVec3> blockableCells = new List<IntVec3>();
        private HashSet<IntVec3> blockableCellsSet = new HashSet<IntVec3>(); // For O(1) lookups
        
        // Performance optimization fields
        private int refreshIndex = 0; // For incremental refreshing
        private int validationIndex = 0; // For spread validation
        private Dictionary<IntVec3, bool> adjacentRockCache = new Dictionary<IntVec3, bool>(); // Cache expensive lookups
        private int lastCacheRefresh = -999999; // When we last refreshed cache
        private bool hasCompletedFullRefresh = false; // Track if we've done a complete refresh cycle

        // Monitored building states
        private bool hasStabilizer = false;
        private bool hasGenerator = false;
        private bool hasDefenseSystem = false;
        // Add more bools here as needed

        private int lastThingCheckTick = -999999;
        private const int THING_CHECK_INTERVAL = 300; // Check every 5 seconds

        // Legacy/compatibility
        public float pointsMultiplier = 1f;
        private int lastIncidentTick = -999999;
        private int nextIncidentTick = -999999;
        #endregion

        #region Rimworld Overrides
        public override void ExposeData()
        {
            base.ExposeData();

            // Core data
            Scribe_Values.Look(ref stabilityLost, "stabilityLost", 0);
            Scribe_Values.Look(ref tickOpened, "tickOpened", 0);
            Scribe_Values.Look(ref pointsMultiplier, "pointsMultiplier", 1f);

            // Configurable fields
            Scribe_Values.Look(ref stage1EffectFrequency, "stage1EffectFrequency", 1f);
            Scribe_Values.Look(ref stage2EffectFrequency, "stage2EffectFrequency", 1f);
            Scribe_Values.Look(ref stage3EffectFrequency, "stage3EffectFrequency", 0.5f);
            Scribe_Values.Look(ref stage4EffectFrequency, "stage4EffectFrequency", 0.5f);
            Scribe_Values.Look(ref centerAvoidanceRange, "centerAvoidanceRange", 3);
            Scribe_Values.Look(ref useCenterAvoidance, "useCenterAvoidance", true);

            // Collapse states
            Scribe_Values.Look(ref isPartiallyCollapsing, "isPartiallyCollapsing", false);
            Scribe_Values.Look(ref isCollapsing, "isCollapsing", false);
            Scribe_Values.Look(ref isDestroying, "isDestroying", false);
            Scribe_Values.Look(ref collapseTick, "collapseTick", -999999);

            // Partial collapse data
            Scribe_Values.Look(ref lastPartialCollapseTick, "lastPartialCollapseTick", -999999);
            Scribe_Collections.Look(ref blockableCells, "blockableCells", LookMode.Value);
            Scribe_Values.Look(ref caveExitPosition, "caveExitPosition");

            // Performance optimization data
            Scribe_Values.Look(ref refreshIndex, "refreshIndex", 0);
            Scribe_Values.Look(ref validationIndex, "validationIndex", 0);
            Scribe_Values.Look(ref hasCompletedFullRefresh, "hasCompletedFullRefresh", false);

            // Building detection states
            Scribe_Values.Look(ref hasStabilizer, "hasStabilizer", false);
            Scribe_Values.Look(ref hasGenerator, "hasGenerator", false);
            Scribe_Values.Look(ref hasDefenseSystem, "hasDefenseSystem", false);
            Scribe_Values.Look(ref lastThingCheckTick, "lastThingCheckTick", -999999);

            // Legacy
            Scribe_Values.Look(ref lastIncidentTick, "lastIncidentTick", 0);
            Scribe_Values.Look(ref nextIncidentTick, "nextIncidentTick", 0);

            // Forced collapse tracking
            Scribe_Collections.Look(ref forcedCollapseLoggedPawns, "forcedCollapseLoggedPawns", LookMode.Value);

            // Initialize collections if null after loading
            if (blockableCells == null) blockableCells = new List<IntVec3>();
            if (blockableCellsSet == null) blockableCellsSet = new HashSet<IntVec3>(blockableCells);
            if (adjacentRockCache == null) adjacentRockCache = new Dictionary<IntVec3, bool>();
            if (forcedCollapseLoggedPawns == null) forcedCollapseLoggedPawns = new HashSet<int>();
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                tickOpened = Find.TickManager.TicksGame;
                stabilityLost = 0;

                // Spawn opening effects
                EffecterDefOf.ImpactDustCloud.Spawn(base.Position, base.Map).Cleanup();
                Find.CameraDriver.shaker.DoShake(0.1f, 120);
                SoundDefOf.PitGateOpen.PlayOneShot(SoundInfo.InMap(this));
            }
        }

        protected override void Tick()
        {
            base.Tick();

            // Always handle stability degradation to allow instability to continue increasing
            // past 100% for the forced collapse mechanism at 150%
            HandleStabilityDegradation();

            if (isCollapsing)
            {
                HandleCollapsingState();
            }

            // Check for powered buildings periodically
            if (ShouldCheckForBuildings())
            {
                CheckForPoweredBuildings();
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            // Debug gizmos
            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: +10% Instability",
                    action = delegate
                    {
                        stabilityLost += StabilityDurationTicks / 10;
                    }
                };
                yield return new Command_Action
                {
                    defaultLabel = "DEV: -10% Instability",
                    action = delegate
                    {
                        stabilityLost -= StabilityDurationTicks / 10;
                    }
                };
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Collapse Immediatley",
                    action = delegate
                    {
                        Collapse();
                    }
                };
                

                string debugInfo = $"DEV: Stability: {GetStabilityPercent():P1} ({stabilityLost}/{StabilityDurationTicks})";
                if (base.PocketMapExists && pocketMap != null)
                {
                    int colonistCount = pocketMap.mapPawns.FreeColonists.Count();
                    float multiplier = GetStabilityMultiplier(colonistCount);
                    debugInfo += $"\nColonists: {colonistCount} (x{multiplier:F2} rate)";
                    debugInfo += $"\nPartial Collapse: {isPartiallyCollapsing}";
                    debugInfo += $"\nFull Collapse: {isCollapsing}";
                    debugInfo += $"\nStabilizer: {hasStabilizer} | Generator: {hasGenerator} | Defense: {hasDefenseSystem}";
                    // debugInfo += $"\nBlockable Cells: {blockableCells.Count}";
                    // debugInfo += $"\nRefresh Progress: {refreshIndex} | Validation: {validationIndex}";
                    // debugInfo += $"\nCache Entries: {adjacentRockCache.Count}";
                    // debugInfo += $"\nCenter Avoidance: {useCenterAvoidance} (range: {centerAvoidanceRange})";
                    // debugInfo += $"\nEffect Freq: {stage1EffectFrequency}/{stage2EffectFrequency}/{stage3EffectFrequency}/{stage4EffectFrequency}";
                    // debugInfo += $"\nCurrent Map: {(Find.CurrentMap == pocketMap ? "Cavern" : "Surface")}";
                }

                yield return new Command_Action
                {
                    defaultLabel = debugInfo,
                    action = delegate { } // Display only
                };
            }
        }
        #endregion

        #region Stability System
        private void HandleStabilityDegradation()
        {
            // Increase instability over time
            int StabilityIncrease = CalculateStabilityIncrease();
            if (StabilityIncrease > 0 || stabilityLost > StabilityDurationTicks/3)
                stabilityLost += StabilityIncrease;

            float stabilityPercent = GetStabilityPercent();

            // Update visual effects
            UpdateStabilityEffects(stabilityPercent);

            // Start partial collapse at 50%
            if (stabilityPercent >= 0.5f && !isPartiallyCollapsing)
            {
                BeginPartialCollapse();
            }

            // Continue partial/accelerated collapse
            int collapseInterval = isCollapsing ? AcceleratedCollapseInterval : PartialCollapseInterval;
            if (isPartiallyCollapsing && ShouldProcessPartialCollapse(collapseInterval))
            {
                // Only try to block cells if we have a pocket map
                if (base.PocketMapExists && pocketMap != null)
                {
                    TryBlockCaveCells();
                }
                lastPartialCollapseTick = Find.TickManager.TicksGame;
            }

            // Begin full collapse at 100% (but only trigger once)
            if (stabilityPercent >= 1.0f && !isCollapsing)
            {
                BeginCollapse();
            }
        }

        // NEW: Method to handle entrance-only collapse when no pocket map exists
        private void CollapseEntrance()
        {
            // Safety check to prevent double destruction
            if (isDestroying || Destroyed)
            {
                return;
            }
            
            // Mark that we're beginning destruction
            isDestroying = true;
            
            // Sound effects on the surface (only if map still exists)
            if (base.Map != null)
            {
                SoundDefOf.PitGateCollapsing_End.PlayOneShot(new TargetInfo(base.Position, base.Map));
                
                // Visual effect at entrance
                EffecterDefOf.ImpactDustCloud.Spawn(base.Position, base.Map).Cleanup();
                Find.CameraDriver.shaker.DoShake(0.1f);
            }
            
            MOLog.Message("Cave entrance collapsed before anyone entered");
            
            // Destroy the entrance
            Thing.allowDestroyNonDestroyable = true;
            Destroy(DestroyMode.Deconstruct);
            Thing.allowDestroyNonDestroyable = false;
        }
        

        private int CalculateStabilityIncrease()
        {
            if (!base.PocketMapExists || pocketMap == null) return 1;

            int colonistCount = pocketMap.mapPawns.FreeColonists.Count();
            float multiplier = GetStabilityMultiplier(colonistCount);
            int baseIncrease = Mathf.RoundToInt(1 * multiplier);

            // Only check stabilizer if pocket map exists
            if (pocketMap != null && CompCavernStabilizer.HasStabilizerCondition(pocketMap))
            {
                isPartiallyCollapsing = false;
                isCollapsing = false;
                baseIncrease -= 3;
            }
            
            return baseIncrease;
        }
        private float GetStabilityMultiplier(int colonistCount)
        {
            return 1f + (colonistCount * 0.25f); // Each colonist adds 25% instability rate
        }

        public void OnMiningCompleted()
        {
            stabilityLost += MiningInstabilityIncrease;

            // Optional: Add some flavor feedback
            if (Find.CurrentMap == pocketMap && Rand.Chance(0.3f))
            {
                EffecterDefOf.UndercaveCeilingDebris.Spawn(GetRandomCaveCell(), pocketMap).Cleanup();
                // Maybe a subtle screen shake?
                Find.CameraDriver.shaker.DoShake(0.02f);
            }
        }

        // Public property for creature spawner access
        public float StabilityLoss => GetStabilityPercent();
        
        private float GetStabilityPercent()
        {
            return (float)stabilityLost / StabilityDurationTicks;
        }

        private bool ShouldProcessPartialCollapse(int interval)
        {
            return Find.TickManager.TicksGame >= lastPartialCollapseTick + interval;
        }
        #endregion

        #region Visual Effects
        private void UpdateStabilityEffects(float stabilityPercent)
        {
            if (!base.PocketMapExists || pocketMap == null) return;
            if (Find.CurrentMap != pocketMap) return; // Only show effects when viewing cavern

            if (stabilityPercent < 0.25f)
            {
                // 0-25%: Occasional ambient effects
                if (Rand.MTBEventOccurs(stage1EffectFrequency, 60f, 1f))
                {
                    EffecterDefOf.UndercaveMapAmbience.Spawn(GetRandomCaveCell(), pocketMap).Cleanup();
                    EffecterDefOf.UndercaveMapAmbienceWater.Spawn(GetRandomCaveCell(), pocketMap).Cleanup();
                }
            }
            else if (stabilityPercent < 0.5f)
            {
                // 25-50%: Light debris and water effects (no shake yet)
                if (Rand.MTBEventOccurs(stage2EffectFrequency, 60f, 1f))
                {
                    EffecterDefOf.UndercaveMapAmbience.Spawn(GetRandomCaveCell(), pocketMap).Cleanup();
                    EffecterDefOf.UndercaveMapAmbienceWater.Spawn(GetRandomCaveCell(), pocketMap).Cleanup();
                    EffecterDefOf.UndercaveCeilingDebris.Spawn(GetRandomCaveCell(), pocketMap).Cleanup();
                }
            }
            else if (stabilityPercent < 0.75f)
            {
                // 50-75%: Heavy debris and screen shake starts
                if (Rand.MTBEventOccurs(stage3EffectFrequency, 60f, 1f))
                {
                    EffecterDefOf.UndercaveMapAmbienceWater.Spawn(GetRandomCaveCell(), pocketMap).Cleanup();
                    EffecterDefOf.UndercaveMapAmbience.Spawn(GetRandomCaveCell(), pocketMap).Cleanup();
                    EffecterDefOf.UndercaveCeilingDebris.Spawn(GetRandomCaveCell(), pocketMap);
                    Find.CameraDriver.shaker.DoShake(0.03f);
                }
            }
            else
            {
                // 75-100%: Intense effects leading to collapse
                if (Rand.MTBEventOccurs(stage4EffectFrequency, 60f, 1f))
                {
                    EffecterDefOf.UndercaveMapAmbienceWater.Spawn(GetRandomCaveCell(), pocketMap).Cleanup();
                    EffecterDefOf.UndercaveMapAmbience.Spawn(GetRandomCaveCell(), pocketMap).Cleanup();
                    EffecterDefOf.UndercaveCeilingDebris.Spawn(GetRandomCaveCell(), pocketMap);
                    EffecterDefOf.ImpactDustCloud.Spawn(GetRandomCaveCell(), pocketMap).Cleanup();
                    Find.CameraDriver.shaker.DoShake(0.08f);
                }
            }
        }

        private IntVec3 GetRandomCaveCell()
        {
            if (!base.PocketMapExists || pocketMap == null)
                return IntVec3.Invalid;

            // Try to find a walkable cell
            for (int i = 0; i < 10; i++)
            {
                IntVec3 cell = pocketMap.AllCells.RandomElement();
                if (cell.Walkable(pocketMap))
                    return cell;
            }
            return pocketMap.AllCells.RandomElement(); // Fallback
        }
        #endregion

        #region Optimized Partial Collapse System
        private void BeginPartialCollapse()
        {
            isPartiallyCollapsing = true;
            
            // Only check stabilizer if pocket map exists
            if (pocketMap != null && CompCavernStabilizer.HasStabilizerCondition(pocketMap))
            {
                isPartiallyCollapsing = false;
                isCollapsing = false;
                return;
            }
            
            // If no pocket map exists, we can't do partial collapse blocking, but we still track the state
            if (!base.PocketMapExists || pocketMap == null)
            {
                // Just visual feedback that something is happening
                if (Find.CurrentMap == base.Map)
                {
                    Find.CameraDriver.shaker.DoShake(0.05f);
                    EffecterDefOf.ImpactSmallDustCloud.Spawn(base.Position, base.Map).Cleanup();
                }
                lastPartialCollapseTick = Find.TickManager.TicksGame;
                return;
            }
            
            // Normal partial collapse setup for existing pocket maps
            refreshIndex = 0; // Start incremental refresh
            validationIndex = 0; // Reset validation
            hasCompletedFullRefresh = false; // Reset refresh completion flag
            RefreshBlockableCells(); // Start the process
            lastPartialCollapseTick = Find.TickManager.TicksGame;

            // Visual feedback for partial collapse start
            if (Find.CurrentMap == pocketMap)
            {
                Find.CameraDriver.shaker.DoShake(0.1f);
            }
            
            // Cache exit position if we're using exit avoidance
            if (!useCenterAvoidance)
            {
                CacheExitPosition();
            }
        }

        private void CacheExitPosition()
        {
            if (caveExitPosition != IntVec3.Invalid) return; // Already cached
            
            if (!base.PocketMapExists || pocketMap == null)
            {
                caveExitPosition = IntVec3.Invalid;
                return;
            }

            // Find the CavernExit building in the pocket map
            foreach (Building building in pocketMap.listerBuildings.allBuildingsColonist)
            {
                if (building.def.defName == "CavernExit")
                {
                    caveExitPosition = building.Position;
                    return;
                }
            }
            
            // Fallback: try all things if the above doesn't work
            var allThings = pocketMap.listerThings.AllThings;
            foreach (Thing thing in allThings)
            {
                if (thing.def.defName == "CavernExit")
                {
                    caveExitPosition = thing.Position;
                    return;
                }
            }
            
            // If no exit found, set to map center as fallback
            caveExitPosition = pocketMap.Center;
        }

        // Get stability parameters from XML or defaults
        private StabilityParameters GetStabilityParameters()
        {
            return def.GetModExtension<StabilityParameters>() ?? new StabilityParameters();
        }
        
        private int StabilityDurationTicks => GetStabilityParameters().stabilityDurationTicks;
        private int MiningInstabilityIncrease => GetStabilityParameters().miningInstabilityIncrease;
        private float CollapseKillThreshold => GetStabilityParameters().collapseKillThreshold;

        // OPTIMIZED: Now incremental instead of processing all cells at once
        private void RefreshBlockableCells()
        {
            // Don't start over if we're already refreshing
            if (refreshIndex == 0)
            {
                blockableCells.Clear();
                blockableCellsSet.Clear();
                hasCompletedFullRefresh = false; // Starting a new refresh cycle
                
                // Refresh adjacent rock cache if it's old
                if (Find.TickManager.TicksGame > lastCacheRefresh + CACHE_REFRESH_INTERVAL)
                {
                    adjacentRockCache.Clear();
                    lastCacheRefresh = Find.TickManager.TicksGame;
                }
            }

            if (!base.PocketMapExists || pocketMap == null) 
            {
                refreshIndex = 0;
                hasCompletedFullRefresh = true; // No map = nothing to refresh
                return;
            }

            var allCells = pocketMap.AllCells;
            int totalCells = allCells.Count();
            int endIndex = Mathf.Min(refreshIndex + CELLS_PER_REFRESH, totalCells);

            // Process a chunk of cells this tick
            for (int i = refreshIndex; i < endIndex; i++)
            {
                IntVec3 cell = allCells.ElementAt(i);
                if (IsValidBlockableCell(cell))
                {
                    blockableCells.Add(cell);
                    blockableCellsSet.Add(cell);
                }
            }

            refreshIndex = endIndex;
            
            // Mark refresh as complete when we've processed all cells
            if (refreshIndex >= totalCells)
            {
                refreshIndex = 0;
                hasCompletedFullRefresh = true; // We've checked every cell
            }
        }

        // OPTIMIZED: Early exits, caching, and faster operations
        private bool IsValidBlockableCell(IntVec3 cell)
        {
            // Early exit checks first (cheapest operations)
            if (!cell.InBounds(pocketMap)) return false;
            if (!cell.Walkable(pocketMap)) return false;
            
            // Check edifice (still cheap)
            if (cell.GetEdifice(pocketMap) != null) return false;
            
            // Check for blocking things - optimize the LINQ away
            var thingList = cell.GetThingList(pocketMap);
            for (int i = 0; i < thingList.Count; i++)
            {
                if (thingList[i].def.passability == Traversability.Impassable)
                    return false;
            }
            
            // Avoidance logic (quick distance checks using squared distance to avoid sqrt)
            if (useCenterAvoidance)
            {
                if (cell.DistanceToSquared(pocketMap.Center) < centerAvoidanceRange * centerAvoidanceRange) 
                    return false;
            }
            else
            {
                if (caveExitPosition.IsValid && cell.DistanceToSquared(caveExitPosition) < 9) // 3*3
                    return false;
            }
            
            // Most expensive check last - use caching
            return HasAdjacentRock(cell);
        }

        // NEW: Cached method for expensive adjacent rock check
        private bool HasAdjacentRock(IntVec3 cell)
        {
            // Check cache first
            if (adjacentRockCache.TryGetValue(cell, out bool cachedResult))
                return cachedResult;
            
            // Calculate and cache the result
            bool hasRock = false;
            foreach (IntVec3 neighbor in GenAdj.AdjacentCells)
            {
                IntVec3 adjCell = cell + neighbor;
                if (!adjCell.InBounds(pocketMap)) continue;

                var edifice = adjCell.GetEdifice(pocketMap);
                if (edifice != null && 
                    (edifice.def.building.isNaturalRock || edifice.def.defName == "MO_WeakRock"))
                {
                    hasRock = true;
                    break;
                }
            }
            
            adjacentRockCache[cell] = hasRock;
            return hasRock;
        }

        // OPTIMIZED: Now incremental validation instead of full list every time
        private void ValidateBlockableCells()
        {
            if (!base.PocketMapExists || pocketMap == null)
            {
                blockableCells.Clear();
                blockableCellsSet.Clear();
                validationIndex = 0;
                return;
            }
            
            if (blockableCells.Count == 0)
            {
                validationIndex = 0;
                return;
            }
            
            // Only validate a few cells per tick to spread the load
            int cellsToValidate = Mathf.Min(CELLS_PER_VALIDATION, blockableCells.Count - validationIndex);
            
            for (int i = 0; i < cellsToValidate; i++)
            {
                int currentIndex = validationIndex + i;
                if (currentIndex >= blockableCells.Count) break;
                
                IntVec3 cell = blockableCells[currentIndex];
                if (!IsValidBlockableCell(cell))
                {
                    // Remove invalid cell from both data structures
                    blockableCells.RemoveAt(currentIndex);
                    blockableCellsSet.Remove(cell);
                    // Don't increment validation index since we removed an item
                    i--; // Check the same index again since items shifted
                    cellsToValidate = Mathf.Min(cellsToValidate, blockableCells.Count - validationIndex);
                }
            }
            
            validationIndex += cellsToValidate;
            
            // Reset validation index when we've checked all cells
            if (validationIndex >= blockableCells.Count)
            {
                validationIndex = 0;
            }
        }

        // UPDATED: Uses new optimized validation and refresh systems
        private void TryBlockCaveCells()
        {
            // Early exit if no pocket map exists
            if (!base.PocketMapExists || pocketMap == null)
            {
                return;
            }
            
            // Do incremental validation instead of full validation
            ValidateBlockableCells();
            
            // If we're running low on cells and not currently refreshing, start a refresh
            if (blockableCells.Count < 10 && refreshIndex == 0)
            {
                RefreshBlockableCells();
            }
            
            // Continue with incremental refresh if in progress
            if (refreshIndex > 0)
            {
                RefreshBlockableCells();
            }
            
            // Only trigger final collapse if we've completed a full refresh and found no cells
            if (blockableCells.Count == 0 && hasCompletedFullRefresh)
            {
                if (isCollapsing)
                {
                    MOLog.Message("Cave completely filled - triggering final collapse");
                    Collapse();
                    return;
                }
                // If not collapsing yet, just wait - we might find more cells as things change
            }

            IntVec3 cellToBlock = ChooseStrategicCellToBlock();

            if (cellToBlock != IntVec3.Invalid && CanSpawnRockAt(cellToBlock))
            {
                // Spawn blocking rock
                GenSpawn.Spawn(ThingDef.Named("MO_WeakRock"), cellToBlock, pocketMap);
                
                // Remove from both data structures for O(1) performance
                blockableCells.Remove(cellToBlock);
                blockableCellsSet.Remove(cellToBlock);
                
                // Invalidate cache for this cell and neighbors
                InvalidateCacheAround(cellToBlock);
                
                // Reset the refresh completion flag since placing a rock might create new blockable cells
                hasCompletedFullRefresh = false;

                // Visual effects (only when viewing cavern)
                if (Find.CurrentMap == pocketMap)
                {
                    EffecterDefOf.ImpactSmallDustCloud.Spawn(cellToBlock, pocketMap).Cleanup();

                    if (isCollapsing)
                    {
                        Find.CameraDriver.shaker.DoShake(0.06f);
                    }
                }
            }
        }

        // NEW: Helper method to invalidate cache around a position
        private void InvalidateCacheAround(IntVec3 center)
        {
            // Remove cache entries for cells that might be affected by the new rock
            foreach (IntVec3 offset in GenAdj.AdjacentCellsAndInside)
            {
                IntVec3 cell = center + offset;
                adjacentRockCache.Remove(cell);
            }
        }

        // NEW: Check if it's safe to spawn a rock at this position (no living creatures)
        private bool CanSpawnRockAt(IntVec3 cell)
        {
            // Don't spawn rocks on living creatures - give them a chance to escape!
            var thingsAtCell = cell.GetThingList(pocketMap);
            foreach (var thing in thingsAtCell)
            {
                if (thing is Pawn pawn && !pawn.Dead)
                {
                    // However, if we're WAY past collapse point (150%+), crush the pawn
                    // This prevents infinite caves when pawns refuse to leave
                    float stabilityPercent = GetStabilityPercent();
                    if (stabilityPercent >= CollapseKillThreshold) // 150% instability = forced collapse
                    {
                        // Only log once per pawn to avoid spam
                        if (!HasLoggedForcedCollapseFor(pawn))
                        {
                            MOLog.Message($"Cave WAY past collapse point ({stabilityPercent:P0}) - {pawn.NameShortColored} crushed by falling rocks");
                            MarkForcedCollapseLogged(pawn);
                        }
                        
                        // Crush the pawn dramatically - guaranteed death
                        pawn.Kill(new DamageInfo(DamageDefOf.Crush, 9999f), null);
                        
                        return true; // Continue with rock placement
                    }
                    return false; // Living creature here, skip this cell (for now)
                }
            }
            return true; // Safe to spawn rock
        }

        // Helper methods for tracking logged pawns to prevent spam
        private bool HasLoggedForcedCollapseFor(Pawn pawn)
        {
            return forcedCollapseLoggedPawns.Contains(pawn.thingIDNumber);
        }
        
        private void MarkForcedCollapseLogged(Pawn pawn)
        {
            forcedCollapseLoggedPawns.Add(pawn.thingIDNumber);
        }

        private IntVec3 ChooseStrategicCellToBlock()
        {
            if (blockableCells.Count == 0) return IntVec3.Invalid;

            // TODO: Add strategic logic (narrow passages, important areas, etc.)
            return blockableCells.RandomElement();
        }
        #endregion

        #region Full Collapse System
        private void HandleCollapsingState()
        {
            // Safety check to prevent double destruction
            if (isDestroying || Destroyed)
            {
                return;
            }
            
            // Only check stabilizer if pocket map exists
            if (pocketMap != null && CompCavernStabilizer.HasStabilizerCondition(pocketMap))
            {
                isPartiallyCollapsing = false;
                isCollapsing = false;
                return;
            }
            
            // If no pocket map exists, just destroy the entrance after a short delay
            if (!base.PocketMapExists || pocketMap == null)
            {
                // Give it a few ticks of collapse state, then destroy
                if (Find.TickManager.TicksGame > collapseTick + 60) // 1 second delay
                {
                    CollapseEntrance();
                }
                return;
            }
            
            // Continue the accelerated blocking during collapse
            int collapseInterval = AcceleratedCollapseInterval;
            if (ShouldProcessPartialCollapse(collapseInterval))
            {
                TryBlockCaveCells();
                lastPartialCollapseTick = Find.TickManager.TicksGame;
            }

            // Ongoing collapse effects (only when viewing cavern)
            if (Find.CurrentMap == pocketMap && Rand.MTBEventOccurs(1f, 60f, 1f))
            {
                Find.CameraDriver.shaker.DoShake(0.15f);
                EffecterDefOf.UndercaveCeilingDebris.Spawn(GetRandomCaveCell(), pocketMap);
                EffecterDefOf.UndercaveMapAmbience.Spawn(GetRandomCaveCell(), pocketMap);
                EffecterDefOf.UndercaveMapAmbienceWater.Spawn(GetRandomCaveCell(), pocketMap);
                EffecterDefOf.ImpactDustCloud.Spawn(GetRandomCaveCell(), pocketMap).Cleanup();
            }
        }

        public void BeginCollapse()
        {
            isCollapsing = true;
            collapseTick = Find.TickManager.TicksGame; // Set when collapse started
            // The accelerated blocking system will handle filling the cave
            MOLog.Message("Cave collapse started - accelerated blocking begins");
        }

        public void Collapse()
        {
            // Safety check to prevent double destruction
            if (isDestroying || Destroyed)
            {
                return;
            }
            
            // Mark that we're beginning destruction
            isDestroying = true;
            
            // Sound effects
            if (Find.CurrentMap == pocketMap)
            {
                SoundDefOf.UndercaveCollapsing_End.PlayOneShotOnCamera();
            }
            else
            {
                SoundDefOf.PitGateCollapsing_End.PlayOneShot(new TargetInfo(base.Position, base.Map));
            }

            // Kill everything in the cavern
            if (base.PocketMapExists)
            {
                DamageInfo damageInfo = new DamageInfo(DamageDefOf.Crush, 99999f, 999f);
                for (int i = pocketMap.mapPawns.AllPawns.Count - 1; i >= 0; i--)
                {
                    Pawn pawn = pocketMap.mapPawns.AllPawns[i];
                    pawn.TakeDamage(damageInfo);
                    if (!pawn.Dead)
                    {
                        pawn.Kill(damageInfo);
                    }
                }
                PocketMapUtility.DestroyPocketMap(pocketMap);
            }

            // Destroy the entrance
            Thing.allowDestroyNonDestroyable = true;
            Destroy(DestroyMode.Deconstruct);
            Thing.allowDestroyNonDestroyable = false;
        }
        #endregion

        #region Building Detection System
        private bool ShouldCheckForBuildings()
        {
            return Find.TickManager.TicksGame >= lastThingCheckTick + THING_CHECK_INTERVAL;
        }

        private void CheckForPoweredBuildings()
        {
            if (base.Map == null) return;
            
            // Reset all flags
            hasStabilizer = false;
            hasGenerator = false;
            hasDefenseSystem = false;
            
            // Check all buildings on the SURFACE map (where the entrance is)
            foreach (Building building in base.Map.listerBuildings.allBuildingsColonist)
            {
                // Check if powered
                var powerComp = building.GetComp<CompPowerTrader>();
                bool isPowered = powerComp == null || powerComp.PowerOn;
                
                if (!isPowered) continue;
                
                // Set flags based on building type
                switch (building.def.defName)
                {
                    case "CavernStabilizer":
                        hasStabilizer = true;
                        break;
                    case "MyGenerator": 
                        hasGenerator = true;
                        break;
                    case "MyDefenseSystem":
                        hasDefenseSystem = true;
                        break;
                    // Add more cases here when you need them
                }
            }
            
            lastThingCheckTick = Find.TickManager.TicksGame;
        }
        #endregion
        
        #region Misc
        public Map GetPocketMap()
        {
            return PocketMapExists ? pocketMap : null;
        }
        #endregion
    }
}