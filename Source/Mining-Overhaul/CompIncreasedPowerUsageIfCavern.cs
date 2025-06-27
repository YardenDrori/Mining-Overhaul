using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;
using UnityEngine;

namespace MiningOverhaul
{
    // Static manager to efficiently track cavern state across all buildings
    [StaticConstructorOnStartup]
    public static class CavernStateManager
    {
        private static Dictionary<Map, bool> cavernStates = new Dictionary<Map, bool>();
        private static Dictionary<Map, int> lastCheckTicks = new Dictionary<Map, int>();
        private const int CHECK_INTERVAL = 300; // 5 seconds

        public static bool HasActiveCaverns(Map map)
        {
            if (map == null) return false;

            int currentTick = Find.TickManager.TicksGame;
            
            // Check if we need to refresh this map's state
            if (!lastCheckTicks.ContainsKey(map) || 
                currentTick >= lastCheckTicks[map] + CHECK_INTERVAL)
            {
                RefreshCavernState(map);
                lastCheckTicks[map] = currentTick;
            }

            return cavernStates.GetValueOrDefault(map, false);
        }

        private static void RefreshCavernState(Map map)
        {
            bool hasActive = false;

            foreach (Building building in map.listerBuildings.allBuildingsColonist)
            {
                switch (building.def.defName)
                {
                    case "CavernEntrance":
                    case "CrystalCavernsEntrance":
                        hasActive = true;
                        break;
                }

                if (hasActive) break; // Early exit
            }

            cavernStates[map] = hasActive;
        }

        // Clean up when maps are destroyed
        public static void CleanupMap(Map map)
        {
            cavernStates.Remove(map);
            lastCheckTicks.Remove(map);
        }
    }

    // Properties class for XML configuration
    public class CompProperties_IncreasedPowerUsageIfCavern : CompProperties
    {
        private float basePowerConsumption = 50f;
        public float activePowerConsumption = 150f;

        public float PowerConsumption
        {
            get
            {
                if (Current.ProgramState == ProgramState.Entry)
                    return basePowerConsumption;
                
                // This will be overridden by the component's logic
                return basePowerConsumption;
            }
        }

        public CompProperties_IncreasedPowerUsageIfCavern()
        {
            compClass = typeof(IncreasedPowerUsageIfCavern);
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats(StatRequest req)
        {
            foreach (StatDrawEntry item in base.SpecialDisplayStats(req))
            {
                yield return item;
            }

            if (basePowerConsumption > 0f)
            {
                yield return new StatDrawEntry(
                    StatCategoryDefOf.Building, 
                    "PowerConsumption".Translate(), 
                    basePowerConsumption.ToString("F0") + " W", 
                    "Base power consumption when no caverns are active", 
                    5000
                );
            }

            if (activePowerConsumption > 0f)
            {
                yield return new StatDrawEntry(
                    StatCategoryDefOf.Building, 
                    "PowerConsumptionActive".Translate().CapitalizeFirst(), 
                    activePowerConsumption.ToString("F0") + " W", 
                    "Power consumption when caverns are detected", 
                    5001
                );
            }
        }
    }

    // The actual component that gets attached to buildings
    public class IncreasedPowerUsageIfCavern : ThingComp
    {
        private bool lastCavernState = false;
        private CompPowerTrader powerComp;

        // Get the properties from XML
        public CompProperties_IncreasedPowerUsageIfCavern Props => (CompProperties_IncreasedPowerUsageIfCavern)props;

        public float CurrentPowerConsumption
        {
            get
            {
                bool cavernActive = CavernStateManager.HasActiveCaverns(parent.Map);
                return cavernActive ? Props.activePowerConsumption : Props.PowerConsumption;
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerComp = parent.GetComp<CompPowerTrader>();
            
            // Set initial power consumption
            UpdatePowerConsumption();
        }

        public override void CompTick()
        {
            base.CompTick();

            // Only update if cavern state changed (very efficient)
            bool currentCavernState = CavernStateManager.HasActiveCaverns(parent.Map);
            if (currentCavernState != lastCavernState)
            {
                UpdatePowerConsumption();
                lastCavernState = currentCavernState;
            }
        }

        private void UpdatePowerConsumption()
        {
            if (powerComp == null) return;

            float newPowerConsumption = CurrentPowerConsumption;
            powerComp.PowerOutput = -newPowerConsumption; // Negative for consumption
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref lastCavernState, "lastCavernState", false);
        }

        public override string CompInspectStringExtra()
        {
            if (powerComp == null) return "";
            
            bool cavernActive = CavernStateManager.HasActiveCaverns(parent.Map);
            string status = cavernActive ? "Active (Caverns detected)" : "Idle";
            float currentPower = CurrentPowerConsumption;
            
            return $"Power mode: {status}";
        }
    }
}