using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;


namespace MiningOverhaul
{
    public class CompProperties_CavernStabilizer : CompProperties
    {
        
        public CompProperties_CavernStabilizer()
        {
            compClass = typeof(CompCavernStabilizer);
        }
    }
    public class CompCavernStabilizer : ThingComp
    {
        private int tickCounter = 0;

        public override void CompTick()
        {
            base.CompTick();

            tickCounter++;
            if (tickCounter >= 6)
            {
                parent.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, 1));
                tickCounter = 0;
            }
        }
        public static bool HasStabilizerCondition(Map map)
        {
            return map.GameConditionManager.ConditionIsActive(cachedConditionDef);
        }
        public CompProperties_CavernStabilizer Props => (CompProperties_CavernStabilizer)props;
        
        // Cache the def to avoid lookups
        private static GameConditionDef cachedConditionDef;
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // Cache the def once
            if (cachedConditionDef == null)
                cachedConditionDef = DefDatabase<GameConditionDef>.GetNamed("CavernStabilizerActive", false);
                
            if (!respawningAfterLoad)
            {
                AddOrIncrementCondition();
            }
        }
        
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            RemoveOrDecrementCondition(previousMap);
        }
        
        private void AddOrIncrementCondition()
        {
            var condition = parent.Map.GameConditionManager.GetActiveCondition(cachedConditionDef);
            if (condition == null)
            {
                // Create new condition
                condition = GameConditionMaker.MakeCondition(cachedConditionDef);
                condition.Duration = 99999999;
                condition.startTick = Find.TickManager.TicksGame;
                parent.Map.gameConditionManager.RegisterCondition(condition);
            }
            // Condition now exists, stabilizer count is implicit (just check if condition exists)
        }
        
        private void RemoveOrDecrementCondition(Map map)
        {
            // Count remaining stabilizers on this map
            int stabilizerCount = 0;
            foreach (Building building in map.listerBuildings.allBuildingsColonist)
            {
                if (building.def.defName == "CavernStabilizer" && building != parent)
                {
                    stabilizerCount++;
                }
            }
            
            // If no other stabilizers, remove condition
            if (stabilizerCount == 0)
            {
                var condition = map.GameConditionManager.GetActiveCondition(cachedConditionDef);
                condition?.End();
            }
        }
    }
}