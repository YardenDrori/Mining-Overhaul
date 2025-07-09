using HarmonyLib;
using RimWorld;
using Verse;

namespace MiningOverhaul
{
    [HarmonyPatch]
    public static class CaveGlowPatches
    {
        // Patch when pawns enter a cave portal
        [HarmonyPatch(typeof(MapPortal), "OnEntered")]
        [HarmonyPostfix]
        public static void OnEntered_Postfix(MapPortal __instance, Pawn pawn)
        {
            if (pawn?.IsColonist == true && IsEnteringCave(__instance))
            {
                AddCaveGlowHediff(pawn);
            }
        }

        // Patch when pawns exit a cave (through PocketMapExit)
        [HarmonyPatch(typeof(PocketMapExit), "OnEntered")]
        [HarmonyPostfix]
        public static void PocketMapExit_OnEntered_Postfix(PocketMapExit __instance, Pawn pawn)
        {
            if (pawn?.IsColonist == true && IsExitingCave(__instance))
            {
                RemoveCaveGlowHediff(pawn);
            }
        }

        private static bool IsEnteringCave(MapPortal portal)
        {
            // Check if the portal is leading to a cave/cavern
            if (portal?.def?.defName?.Contains("Cavern") == true)
                return true;

            // Check if the destination map is a cave biome
            var destMap = portal?.GetOtherMap();
            if (destMap?.Biome?.defName?.Contains("Cavern") == true)
                return true;

            return false;
        }

        private static bool IsExitingCave(PocketMapExit exit)
        {
            // Check if we're exiting from a cave map
            if (exit?.Map?.Biome?.defName?.Contains("Cavern") == true)
                return true;

            // Check if the exit's def indicates it's a cavern exit
            if (exit?.def?.defName?.Contains("Cavern") == true)
                return true;

            return false;
        }

        private static void AddCaveGlowHediff(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return;

            // Check if pawn already has the hediff
            var existingHediff = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.CaveGlow);
            if (existingHediff != null) return;

            // Add the cave glow hediff
            var hediff = HediffMaker.MakeHediff(HediffDefOf.CaveGlow, pawn);
            pawn.health.AddHediff(hediff);

            MOLog.Message($"Added cave glow to {pawn.Name} entering cave");
        }

        private static void RemoveCaveGlowHediff(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return;

            // Find and remove the cave glow hediff
            var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.CaveGlow);
            if (hediff != null)
            {
                pawn.health.RemoveHediff(hediff);
                MOLog.Message($"Removed cave glow from {pawn.Name} exiting cave");
            }
        }
    }

    // Static reference to our hediff def
    [DefOf]
    public static class HediffDefOf
    {
        public static HediffDef CaveGlow;
    }
}