// Assembly-CSharp, Version=1.6.9303.4720, Culture=neutral, PublicKeyToken=null
// RimWorld.PlaceWorker_OnSteamGeyser
using System.Collections.Generic;
using RimWorld;
using Verse;


namespace MiningOverhaul
{
    public class AllowedBiomesExtension : DefModExtension
    {
        public List<string> allowedBiomes = new List<string>();
    }
    public class PlaceWorker_OnlyInCavern : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            // Get the building def and cast it to access custom properties
            ThingDef buildingDef = checkingDef as ThingDef;
            if (buildingDef?.modExtensions == null)
            {
                return "No biome restrictions defined";
            }

            // Get your custom mod extension that contains the biome list
            AllowedBiomesExtension biomeExt = buildingDef.GetModExtension<AllowedBiomesExtension>();
            if (biomeExt == null)
            {
                return "No biome restrictions found";
            }

            // Check if current biome is in the allowed list  
            if (!biomeExt.allowedBiomes.Contains(map.Biome.defName))
            {
                return "CannotBuildInThisBiome".Translate();
            }

            return true;
        }

        // Remove the ForceAllowPlaceOver and DrawMouseAttachments since you don't need them anymore
    }
}