using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace MiningOverhaul
{
    [System.Serializable]
    public class DepthScanData
    {
        public int depth;
        public string label;
        public List<string> possibleThingDefs;
        public float scanTimeMultiplier = 1f;
    }

    public class CompProperties_CustomStructureScanner : CompProperties_Scanner
    {
        public List<DepthScanData> depthOptions;

        public CompProperties_CustomStructureScanner()
        {
            compClass = typeof(CompCustomStructureScanner);
            depthOptions = new List<DepthScanData>();
            
            // Set default values here instead
            scanFindMtbDays = 3f;
            scanFindGuaranteedDays = 6f;
        }
    }

    // Custom scanner that inherits from CompScanner to work with vanilla systems
    public class CompCustomStructureScanner : CompScanner
    {
        private int selectedDepth = 1;
        private float originalScanMtbDays;
        private float originalGuaranteedDays;
        
        public new CompProperties_CustomStructureScanner Props => props as CompProperties_CustomStructureScanner;

        public override AcceptanceReport CanUseNow
        {
            get
            {
                // No bedrock requirement since we're not dealing with deep resources
                return base.CanUseNow;
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            // Store original scan times
            originalScanMtbDays = Props.scanFindMtbDays;
            originalGuaranteedDays = Props.scanFindGuaranteedDays;
            UpdateScanTimes();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            // Depth selection buttons
            for (int i = 1; i <= 5; i++)
            {
                int depth = i; // Capture for lambda
                DepthScanData depthData = GetDepthData(depth);
                
                yield return new Command_Action
                {
                    defaultLabel = $"Depth {depth}",
                    defaultDesc = depthData?.label ?? $"Scan at depth level {depth}",
                    action = delegate
                    {
                        if (selectedDepth != depth)
                        {
                            selectedDepth = depth;
                            ResetProgress();
                            UpdateScanTimes();
                        }
                    }
                };
            }
        }

        public override void PostDrawExtraSelectionOverlays()
        {
            // No special overlay needed for structure scanning
        }

        protected override void DoFind(Pawn worker)
        {
            Map map = parent.Map;
            
            // Find a suitable location to place the structure
            if (!TryFindValidPlacementCell(map, out IntVec3 placementCell))
            {
                Messages.Message("Could not find a suitable location for the discovered structure.", MessageTypeDefOf.RejectInput);
                return;
            }

            // Choose a random ThingDef from our depth list
            ThingDef chosenThingDef = ChooseRandomThingDefForDepth(selectedDepth);
            if (chosenThingDef == null)
            {
                Messages.Message("Scanner malfunction: No valid structure type configured for this depth.", MessageTypeDefOf.RejectInput);
                return;
            }

            // Place the chosen structure
            Thing newThing = GenSpawn.Spawn(chosenThingDef, placementCell, map);

            // Send notification letter
            string letterLabel = $"Ancient Structure Discovered at Depth {selectedDepth}: " + chosenThingDef.LabelCap;
            string letterText = $"{worker.LabelShort} has discovered an ancient {chosenThingDef.label} buried deep beneath the surface!";
            
            Find.LetterStack.ReceiveLetter(
                letterLabel, 
                letterText, 
                LetterDefOf.PositiveEvent, 
                new LookTargets(newThing)
            );
        }

        private DepthScanData GetDepthData(int depth)
        {
            return Props.depthOptions?.FirstOrDefault(d => d.depth == depth);
        }

        private void ResetProgress()
        {
            // Reset scanning progress using reflection since progressDays is private
            var progressField = typeof(CompScanner).GetField("progressDays", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            progressField?.SetValue(this, 0f);
        }

        private void UpdateScanTimes()
        {
            DepthScanData depthData = GetDepthData(selectedDepth);
            float multiplier = depthData?.scanTimeMultiplier ?? selectedDepth;
            
            // Update scan times based on depth
            Props.scanFindMtbDays = originalScanMtbDays * multiplier;
            Props.scanFindGuaranteedDays = originalGuaranteedDays * multiplier;
        }

        private bool TryFindValidPlacementCell(Map map, out IntVec3 result)
        {
            // Try to find a valid cell that's not near the edge and can support structures
            return CellFinderLoose.TryFindRandomNotEdgeCellWith(
                15, // edge distance
                (IntVec3 cell) => IsValidPlacementCell(cell, map),
                map,
                out result
            );
        }

        private bool IsValidPlacementCell(IntVec3 cell, Map map)
        {
            // Check if the cell is valid for placing structures
            if (!cell.InBounds(map))
                return false;

            // Check if we have a clear 8x8 area centered on this cell
            CellRect rect = new CellRect(cell.x - 4, cell.z - 4, 8, 8);
            
            // Make sure the entire 8x8 area is within map bounds
            if (!rect.InBounds(map))
                return false;

            // Check every cell in the 8x8 area
            foreach (IntVec3 checkCell in rect)
            {
                // Check if cell is not blocked by existing things
                if (checkCell.Filled(map))
                    return false;

                // Check if terrain supports construction
                TerrainDef terrain = checkCell.GetTerrain(map);
                if (terrain.IsWater && terrain.passability == Traversability.Impassable)
                    return false;
            }

            return true;
        }

        private ThingDef ChooseRandomThingDefForDepth(int depth)
        {
            DepthScanData depthData = GetDepthData(depth);
            if (depthData?.possibleThingDefs.NullOrEmpty() != false)
            {
                return null;
            }

            string randomDefName = depthData.possibleThingDefs.RandomElement();
            return DefDatabase<ThingDef>.GetNamedSilentFail(randomDefName);
        }

        public override string CompInspectStringExtra()
        {
            string baseString = base.CompInspectStringExtra();
            DepthScanData depthData = GetDepthData(selectedDepth);
            string depthInfo = $"Scanning depth: {selectedDepth} ({depthData?.label ?? "Unknown"})";
            
            if (baseString.NullOrEmpty())
                return depthInfo;
            else
                return baseString + "\n" + depthInfo;
        }

        // Use the correct method name for ThingComp save/load
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref selectedDepth, "selectedDepth", 1);
        }
    }

    // Extension method to safely check if list is null or empty
    public static class ListExtensions
    {
        public static bool NullOrEmpty<T>(this List<T> list)
        {
            return list == null || list.Count == 0;
        }
    }
}