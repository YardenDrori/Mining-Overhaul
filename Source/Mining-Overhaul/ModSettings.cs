using RimWorld;
using UnityEngine;
using Verse;

namespace MiningOverhaul
{
    public class MiningOverhaulSettings : ModSettings
    {
        public bool verboseLogging = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref verboseLogging, "verboseLogging", false);
            base.ExposeData();
        }
    }

    public class MiningOverhaulMod : Mod
    {
        public static MiningOverhaulSettings settings;

        public MiningOverhaulMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<MiningOverhaulSettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);
            
            listingStandard.Label("Mining Overhaul Settings");
            listingStandard.Gap();
            
            listingStandard.CheckboxLabeled("Verbose Logging", ref settings.verboseLogging, 
                "Enable detailed logging for debugging purposes. This will log POI generation, cave stability, and other mod activities.");
            
            listingStandard.Gap();
            listingStandard.Label("Note: Changes take effect immediately but some logging may require restarting the game.");
            
            listingStandard.End();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "Mining Overhaul";
        }
    }

    public static class MOLog
    {
        public static void Message(string message)
        {
            if (MiningOverhaulMod.settings?.verboseLogging == true)
            {
                Log.Message($"[Mining Overhaul] {message}");
            }
        }

        public static void Warning(string message)
        {
            Log.Warning($"[Mining Overhaul] {message}");
        }

        public static void Error(string message)
        {
            Log.Error($"[Mining Overhaul] {message}");
        }
    }
}