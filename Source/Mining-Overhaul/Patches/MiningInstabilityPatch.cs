using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;
using UnityEngine;
using HarmonyLib;


namespace MiningOverhaul
{

    [HarmonyPatch]
    public static class MiningInstabilityPatch
    {
        [HarmonyPatch(typeof(Mineable), "TrySpawnYield", new Type[] { typeof(Map), typeof(bool), typeof(Pawn) })]
        [HarmonyPostfix]
        public static void OnMiningCompleted(Mineable __instance, Map map, bool moteOnWaste, Pawn pawn) // Changed "miner" to "pawn"
        {
            // Find any CavernEntrance that has this map as its pocket map
            foreach (Map parentMap in Find.Maps)
            {
                var cavernEntrance = parentMap.listerThings?.ThingsOfDef(ThingDef.Named("CavernEntrance"))
                    ?.OfType<CavernEntrance>()
                    ?.FirstOrDefault(c => c.GetPocketMap() == map);
                    
                if (cavernEntrance != null)
                {
                    cavernEntrance.OnMiningCompleted();
                    break;
                }
            }
        }
    }
}