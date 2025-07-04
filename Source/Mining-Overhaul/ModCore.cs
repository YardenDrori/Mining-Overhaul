using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MiningOverhaul
{
    [StaticConstructorOnStartup]
    public class ModCore
    {
        static ModCore()
        {
            new Harmony("blacksparrow.MiningOverhaul").PatchAll();
            Log.Message("Mining Overhaul: Mod loaded successfully!");
            MOLog.Message("Harmony patches applied");
        }
    }
    
}