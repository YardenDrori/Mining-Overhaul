using Verse;

namespace MiningOverhaul
{
    public class StabilityParameters : DefModExtension
    {
        public int stabilityDurationTicks = 36000;
        public int miningInstabilityIncrease = 1000; 
        public float collapseKillThreshold = 1.5f;
    }
}