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
        private const int StabilityDurationTicks = 36000; // Time until collapse starts
        private const int MiningInstabilityIncrease = 1000; // Adjust this value as needed
        private const int PartialCollapseInterval = 120; // Ticks between blocking cells
        private const int AcceleratedCollapseInterval = 30; // Much faster blocking during collapse (5 blocks every 30 ticks = 25 blocks/second)
        
        // Performance optimization constants
        private const int CELLS_PER_REFRESH = 25; // Process this many cells per tick during normal operation
        private const int CELLS_PER_VALIDATION = 10; // Validate this many cells per tick during normal operation
        private const int COLLAPSE_CELLS_PER_REFRESH = 75; // Faster processing during collapse
        private const int COLLAPSE_CELLS_PER_VALIDATION = 30; // Faster validation during collapse
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
        private int collapseTick = -999999;
        private IntVec3 caveExitPosition = IntVec3.Invalid;

        // Optimized partial collapse system
        private int lastPartialCollapseTick = -999999;
        private List<IntVec3> blockableCells = new List<IntVec3>();
        private HashSet<IntVec3> blockableCellsSet = new HashSet<IntVec3>(); // For O(1) lookups
        
        // Performance optimization fields
        private int refreshIndex = 0; // For incremental refreshing
        private int validationIndex = 0; // For spread validation
        private Dictionary<IntVec3, bool> adjacentRockCache = new Dictionary<IntVec3, bool>(); // Cache expensive lookups
        private int lastCacheRefresh = -999999; // When we last refreshed cache
        private bool hasCompletedFullRefresh = false; // Track if we've done a complete refresh cycle
        
        // NEW: Cache expensive collections
        private IntVec3[] allCellsArray = null;
        private int allCellsCount = 0;
        private int cachedColonistCount = 0;
        private int lastColonistCountCheck = -999999;
        private const int COLONIST_COUNT_CACHE_INTERVAL = 60; // 1 second

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

            // Initialize collections if null after loading
            if (blockableCells == null) blockableCells = new List<IntVec3>();
            if (blockableCellsSet == null) blockableCellsSet = new HashSet<IntVec3>(blockableCells);
            if (adjacentRockCache == null) adjacentRockCache = new Dictionary<IntVec3, bool>();
            
            // Reinitialize cached arrays after loading if pocket map exists
            if (base.PocketMapExists && pocketMap != null && allCellsArray == null)
            {
                InitializeCellsArray();
            }
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
            
            // Initialize cached collections when pocket map becomes available
            if (base.PocketMapExists && pocketMap != null)
            {
                InitializeCellsArray();
            }
        }

        protected override void Tick()
        {
            base.Tick();

            if (isCollapsing)
            {
                HandleCollapsingState();
            }
            else
            {
                HandleStabilityDegradation();
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
                    int colonistCount = GetCachedColonistCount();
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

            // Begin full collapse at 100%
            if (stabilityPercent >= 1.0f)
            {
                BeginCollapse();
            }
        }

        // NEW: Method to handle entrance-only collapse when no pocket map exists
        private void CollapseEntrance()
        {
            // Sound effects on the surface
            SoundDefOf.PitGateCollapsing_End.PlayOneShot(new TargetInfo(base.Position, base.Map));
            
            // Visual effect at entrance
            EffecterDefOf.ImpactDustCloud.Spawn(base.Position, base.Map).Cleanup();
            Find.CameraDriver.shaker.DoShake(0.1f);
            
            MOLog.Message("Cave entrance collapsed before anyone entered");
            
            // Destroy the entrance
            Thing.allowDestroyNonDestroyable = true;
            Destroy(DestroyMode.Deconstruct);
            Thing.allowDestroyNonDestroyable = false;
        }
        

        private int CalculateStabilityIncrease()
        {
            if (!base.PocketMapExists || pocketMap == null) return 1;

            int colonistCount = GetCachedColonistCount();
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

        // OPTIMIZED: Now incremental with cached array instead of expensive LINQ
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

            // Initialize cells array if needed
            if (allCellsArray == null)
            {
                InitializeCellsArray();
            }

            // Use different refresh rates based on collapse state
            int refreshRate = isCollapsing ? COLLAPSE_CELLS_PER_REFRESH : CELLS_PER_REFRESH;
            int endIndex = Mathf.Min(refreshIndex + refreshRate, allCellsCount);

            // Process a chunk of cells this tick using cached array (O(1) access)
            for (int i = refreshIndex; i < endIndex; i++)
            {
                IntVec3 cell = allCellsArray[i];
                if (IsValidBlockableCell(cell))
                {
                    blockableCells.Add(cell);
                    blockableCellsSet.Add(cell);
                }
            }

            refreshIndex = endIndex;
            
            // Mark refresh as complete when we've processed all cells
            if (refreshIndex >= allCellsCount)
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
            
            // Check for blocking things - optimized loop
            if (HasImpassableThings(cell)) return false;
            
            // During collapse, avoid exit area but allow any other walkable cell
            if (isCollapsing)
            {
                // Only avoid the immediate exit area during final collapse
                if (caveExitPosition.IsValid && cell.DistanceToSquared(caveExitPosition) < 4) // 2*2 around exit
                    return false;
                    
                // Any other walkable cell is fair game during collapse
                return true;
            }
            
            // During partial collapse, use normal avoidance logic
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
            
            // Most expensive check last - use caching (only during partial collapse)
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

        // OPTIMIZED: Now incremental validation with batch removal to avoid list shifting
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
            
            // Use different validation rates based on collapse state
            int validationRate = isCollapsing ? COLLAPSE_CELLS_PER_VALIDATION : CELLS_PER_VALIDATION;
            int cellsToValidate = Mathf.Min(validationRate, blockableCells.Count - validationIndex);
            var cellsToRemove = new List<IntVec3>();
            
            // Check validity without removing items yet
            for (int i = 0; i < cellsToValidate; i++)
            {
                int currentIndex = validationIndex + i;
                if (currentIndex >= blockableCells.Count) break;
                
                IntVec3 cell = blockableCells[currentIndex];
                if (!IsValidBlockableCell(cell))
                {
                    cellsToRemove.Add(cell);
                }
            }
            
            // Batch remove invalid cells to avoid list shifting overhead
            foreach (var cell in cellsToRemove)
            {
                blockableCells.Remove(cell);
                blockableCellsSet.Remove(cell);
            }
            
            validationIndex += cellsToValidate;
            
            // Reset validation index when we've checked all cells
            if (validationIndex >= blockableCells.Count)
            {
                validationIndex = 0;
            }
        }

        // FIXED: Ensures collapse works properly while maintaining performance
        private void TryBlockCaveCells()
        {
            // Early exit if no pocket map exists
            if (!base.PocketMapExists || pocketMap == null)
            {
                return;
            }
            
            // Spread expensive operations, but ensure collapse always works
            int currentTick = Find.TickManager.TicksGame;
            
            // During collapse phase, always do validation to ensure progress
            if (isCollapsing)
            {
                ValidateBlockableCells();
            }
            else if (currentTick % 3 == 0) // Normal phase: reduce validation frequency
            {
                ValidateBlockableCells();
            }
            
            // Aggressive refresh during collapse, reduced during normal operation
            bool needsRefresh = (blockableCells.Count < 10 && refreshIndex == 0) || refreshIndex > 0;
            if (isCollapsing && needsRefresh)
            {
                RefreshBlockableCells(); // Always refresh during collapse
            }
            else if (currentTick % 5 == 0 && needsRefresh)
            {
                RefreshBlockableCells(); // Reduced frequency during normal operation
            }
            
            // Check for final collapse - more frequently during collapse phase
            int collapseCheckInterval = isCollapsing ? 3 : 10;
            if (currentTick % collapseCheckInterval == 0)
            {
                // During collapse: trigger final destruction when cave is mostly filled
                if (isCollapsing)
                {
                    // Count remaining walkable cells
                    int totalWalkableCells = 0;
                    int walkableCellsNearExit = 0;
                    
                    if (allCellsArray != null)
                    {
                        for (int i = 0; i < allCellsCount; i++)
                        {
                            IntVec3 cell = allCellsArray[i];
                            if (cell.Walkable(pocketMap))
                            {
                                totalWalkableCells++;
                                
                                // Count cells near exit (keep some open for escape)
                                if (caveExitPosition.IsValid && cell.DistanceToSquared(caveExitPosition) < 16) // 4x4 around exit
                                {
                                    walkableCellsNearExit++;
                                }
                            }
                        }
                    }
                    
                    // Trigger final collapse when less than 20% of cave remains walkable
                    // OR when we can't find blockable cells despite having walkable space
                    if (totalWalkableCells < (allCellsCount * 0.2f) || 
                        (blockableCells.Count == 0 && hasCompletedFullRefresh && totalWalkableCells > walkableCellsNearExit))
                    {
                        MOLog.Message($"Cave collapse complete - {totalWalkableCells}/{allCellsCount} cells remaining, triggering final destruction");
                        Collapse();
                        return;
                    }
                }
                else
                {
                    // During partial collapse: only check if we've run out of valid cells
                    if (blockableCells.Count == 0 && hasCompletedFullRefresh)
                    {
                        // If not collapsing yet, just wait - we might find more cells as things change
                    }
                }
            }

            // Block multiple cells during collapse for speed and realism
            int blocksToPlace = isCollapsing ? 5 : 1; // 5x faster during collapse
            
            for (int i = 0; i < blocksToPlace && blockableCells.Count > 0; i++)
            {
                IntVec3 cellToBlock = ChooseStrategicCellToBlock();

                if (cellToBlock != IntVec3.Invalid)
                {
                    // Spawn blocking rock
                    GenSpawn.Spawn(ThingDef.Named("MO_WeakRock"), cellToBlock, pocketMap);
                    
                    // Remove from both data structures for O(1) performance
                    blockableCells.Remove(cellToBlock);
                    blockableCellsSet.Remove(cellToBlock);
                    
                    // Always invalidate cache during collapse to ensure proper cell discovery
                    if (isCollapsing || currentTick % 3 == 0)
                    {
                        InvalidateCacheAround(cellToBlock);
                    }
                    
                    // Reset the refresh completion flag since placing a rock might create new blockable cells
                    hasCompletedFullRefresh = false;

                    // Visual effects (only when viewing cavern and spread out for performance)
                    if (Find.CurrentMap == pocketMap && (i == 0 || currentTick % 3 == 0))
                    {
                        EffecterDefOf.ImpactSmallDustCloud.Spawn(cellToBlock, pocketMap).Cleanup();

                        if (isCollapsing && i == 0) // Only shake once per tick
                        {
                            Find.CameraDriver.shaker.DoShake(0.08f);
                        }
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

        private IntVec3 ChooseStrategicCellToBlock()
        {
            if (blockableCells.Count == 0) return IntVec3.Invalid;

            // NEW: Realistic collapse - multiple blocks per call for speed
            if (isCollapsing)
            {
                // During full collapse: block multiple cells at once for speed
                return ChooseRealisticCollapseCell();
            }
            else
            {
                // During partial collapse: random debris falls
                return ChooseRandomDebrisCell();
            }
        }
        
        private IntVec3 ChooseRealisticCollapseCell()
        {
            if (!base.PocketMapExists || pocketMap == null) 
                return blockableCells.RandomElement();
            
            // Prioritize open areas (ceiling collapse) over wall-adjacent cells
            var openAreaCells = blockableCells.Where(cell => 
            {
                // Count adjacent blocked cells - fewer = more open area
                int blockedNeighbors = 0;
                foreach (IntVec3 neighbor in GenAdj.AdjacentCells)
                {
                    IntVec3 adjCell = cell + neighbor;
                    if (!adjCell.InBounds(pocketMap) || !adjCell.Walkable(pocketMap))
                        blockedNeighbors++;
                }
                // Prefer cells with fewer blocked neighbors (open areas)
                return blockedNeighbors <= 3; // 3 or fewer blocked sides = relatively open
            }).ToList();
            
            if (openAreaCells.Count > 0)
            {
                return openAreaCells.RandomElement();
            }
            
            // Fallback to any available cell
            return blockableCells.RandomElement();
        }
        
        private IntVec3 ChooseRandomDebrisCell()
        {
            // Simple random selection for partial collapse debris
            return blockableCells.RandomElement();
        }
        #endregion

        #region Full Collapse System
        private void HandleCollapsingState()
        {
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
        
        #region Performance Helper Methods
        // NEW: Initialize cached collections for O(1) access
        private void InitializeCellsArray()
        {
            if (pocketMap != null)
            {
                allCellsArray = pocketMap.AllCells.ToArray();
                allCellsCount = allCellsArray.Length;
            }
        }
        
        // NEW: Cache colonist count to avoid expensive calls
        private int GetCachedColonistCount()
        {
            if (Find.TickManager.TicksGame > lastColonistCountCheck + COLONIST_COUNT_CACHE_INTERVAL)
            {
                if (pocketMap != null)
                {
                    cachedColonistCount = pocketMap.mapPawns.FreeColonists.Count();
                }
                lastColonistCountCheck = Find.TickManager.TicksGame;
            }
            return cachedColonistCount;
        }
        
        // NEW: Optimized thing list check
        private bool HasImpassableThings(IntVec3 cell)
        {
            var thingList = cell.GetThingList(pocketMap);
            if (thingList.Count == 0) return false;
            
            // Use for loop instead of LINQ for better performance
            for (int i = 0; i < thingList.Count; i++)
            {
                if (thingList[i].def.passability == Traversability.Impassable)
                    return true;
            }
            return false;
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