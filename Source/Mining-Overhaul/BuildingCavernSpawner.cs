// Assembly-CSharp, Version=1.6.9303.4720, Culture=neutral, PublicKeyToken=null
// RimWorld.BuildingGroundSpawner
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MiningOverhaul
{
    [System.Serializable]
    public class WeightedThingDef
    {
        public ThingDef thingDef;
        public float weight = 1f;
    }

    // Custom ThingDef that can hold our spawn options
    public class CavernSpawnerThingDef : ThingDef
    {
        public List<string> possibleThingsList;
        public List<float> possibleThingsWeights;
        private List<WeightedThingDef> possibleThings;
        
        public List<WeightedThingDef> GetPossibleThings()
        {
            if (possibleThings == null && !possibleThingsList.NullOrEmpty())
            {
                possibleThings = new List<WeightedThingDef>();
                for (int i = 0; i < possibleThingsList.Count; i++)
                {
                    var thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(possibleThingsList[i]);
                    if (thingDef != null)
                    {
                        float weight = 1f;
                        if (possibleThingsWeights != null && i < possibleThingsWeights.Count)
                        {
                            weight = possibleThingsWeights[i];
                        }
                        possibleThings.Add(new WeightedThingDef { thingDef = thingDef, weight = weight });
                    }
                }
            }
            return possibleThings;
        }
    }

    public class BuildingCavernSpawner : GroundSpawner
    {
        protected Thing thingToSpawn;
        protected ThingDef selectedThingDef; // Store the randomly selected ThingDef

        public IntRange? emergeDelay;
        public List<string> questTagsToForward;

        protected override IntRange ResultSpawnDelay => emergeDelay ?? def.building.groundSpawnerSpawnDelay;

        protected override SoundDef SustainerSound => def.building.groundSpawnerSustainerSound ?? SoundDefOf.Tunnel;

        // Modified to return the randomly selected ThingDef instead of a fixed one
        protected virtual ThingDef ThingDefToSpawn => selectedThingDef ?? GetRandomThingDef();

        // New method to get random ThingDef from the list
        protected virtual ThingDef GetRandomThingDef()
        {
            // Get our custom ThingDef
            var cavernDef = def as CavernSpawnerThingDef;
            var possibleThings = cavernDef?.GetPossibleThings();

            // Use our converted list if available
            if (!possibleThings.NullOrEmpty())
            {
                return possibleThings.RandomElementByWeight(x => x.weight).thingDef;
            }

            // Fallback to single thing (backwards compatibility)
            return def.building.groundSpawnerThingToSpawn;
        }

        public Thing ThingToSpawn => thingToSpawn;

        public override void PostMake()
        {
            base.PostMake();
            PostMakeInt();
        }

        protected virtual void PostMakeInt()
        {
            // Select random ThingDef once when the spawner is created
            selectedThingDef = GetRandomThingDef();
            thingToSpawn = ThingMaker.MakeThing(selectedThingDef);
        }

        protected override void Spawn(Map map, IntVec3 pos)
        {
            TerrainDef newTerr = map.Biome.TerrainForAffordance(ThingDefToSpawn.terrainAffordanceNeeded);
            foreach (IntVec3 item in GenAdj.OccupiedRect(pos, Rot4.North, ThingDefToSpawn.Size))
            {
                map.terrainGrid.RemoveTopLayer(item, doLeavings: false);
                if (!item.GetAffordances(map).Contains(ThingDefToSpawn.terrainAffordanceNeeded))
                {
                    map.terrainGrid.SetTerrain(item, newTerr);
                }
            }
            GenSpawn.Spawn(thingToSpawn, pos, map, Rot4.North, WipeMode.FullRefund, respawningAfterLoad: false, forbidLeavings: true);
            thingToSpawn.questTags = questTagsToForward;
            BuildingProperties building = def.building;
            if (building != null && building.groundSpawnerDestroyAdjacent)
            {
                foreach (IntVec3 item2 in GenAdj.CellsAdjacentCardinal(thingToSpawn))
                {
                    Building edifice = item2.GetEdifice(map);
                    if (edifice != null && edifice.def.destroyable)
                    {
                        edifice.Destroy(DestroyMode.Refund);
                    }
                }
            }
            Find.TickManager.slower.SignalForceNormalSpeedShort();
            if (def.building?.groundSpawnerLetterLabel != null && def.building?.groundSpawnerLetterText != null)
            {
                Find.LetterStack.ReceiveLetter(def.building.groundSpawnerLetterLabel, def.building.groundSpawnerLetterText, LetterDefOf.NegativeEvent, new TargetInfo(thingToSpawn));
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref emergeDelay, "emergeDelay");
            Scribe_Deep.Look(ref thingToSpawn, "thingToSpawn");
            
            // Save ThingDef by its defName string instead of reference
            string selectedThingDefName = selectedThingDef?.defName;
            Scribe_Values.Look(ref selectedThingDefName, "selectedThingDefName");
            
            // Restore ThingDef from defName on load
            if (Scribe.mode == LoadSaveMode.LoadingVars && !selectedThingDefName.NullOrEmpty())
            {
                selectedThingDef = DefDatabase<ThingDef>.GetNamedSilentFail(selectedThingDefName);
            }
            
            Scribe_Collections.Look(ref questTagsToForward, "questTagsToForward", LookMode.Value);
        }
    }
}