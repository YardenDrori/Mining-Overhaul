using RimWorld;
using Verse;
using System.Collections.Generic; 

namespace MiningOverhaul
{
    public class Building_CavernAccessPoint : Building
    {
        private int spawnTick = -1;
        private const int DaysToCollapse = 3;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (spawnTick == -1)
            {
                spawnTick = Find.TickManager.TicksGame;
            }
        }

        protected override void Tick()
        {
            base.Tick();

            if (spawnTick != -1)
            {
                int ticksToCollapse = DaysToCollapse * GenDate.TicksPerDay;
                if (Find.TickManager.TicksGame >= spawnTick + ticksToCollapse)
                {
                    Messages.Message("CavernEntranceCollapsed".Translate(this.Label),
                        this, MessageTypeDefOf.NeutralEvent);

                    // Force it to be destroyable, then destroy it
                    this.def.destroyable = true;
                    this.Destroy(DestroyMode.Vanish);
                }
            }
        }

        // ADD THIS METHOD HERE
        public override string GetInspectString()
        {
            string baseString = base.GetInspectString();

            if (spawnTick != -1)
            {
                int ticksRemaining = (spawnTick + (DaysToCollapse * GenDate.TicksPerDay)) - Find.TickManager.TicksGame;

                if (ticksRemaining > 0)
                {
                    string timeLeft = ticksRemaining.ToStringTicksToPeriod();
                    string timerText = "CollapseTimeRemaining".Translate(timeLeft);

                    if (baseString.NullOrEmpty())
                        return timerText;
                    else
                        return baseString + "\n" + timerText;
                }
            }

            return baseString;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref spawnTick, "spawnTick", -1);
        }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            // Debug button - dev mode only, no god mode required
            if (DebugSettings.ShowDevGizmos)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: +10 hours",
                    defaultDesc = "Advance collapse timer by 10 hours",
                    action = delegate
                    {
                        // Add 10 hours to the current game time (effectively making it "later")
                        spawnTick -= (int)(GenDate.TicksPerHour * 10);
                        
                        // Or better yet, just check if it should collapse now
                        int ticksToCollapse = DaysToCollapse * GenDate.TicksPerDay;
                        int destructionTime = spawnTick + ticksToCollapse;
                        int currentTime = Find.TickManager.TicksGame;
                        
                        // If we're past destruction time after the advance, trigger it immediately
                        if (currentTime >= destructionTime)
                        {
                            Messages.Message("CavernEntranceCollapsed".Translate(this.Label),
                                this, MessageTypeDefOf.NeutralEvent);
                            this.Destroy(DestroyMode.Vanish);
                        }
                    }
                };
            }
        }

    }
}