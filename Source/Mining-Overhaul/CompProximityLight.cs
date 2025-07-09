using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MiningOverhaul
{
    public class CompProperties_ProximityLight : CompProperties
    {
        public float radius = 5f;
        public float glowRadius = 12f;
        public ColorInt glowColor = new ColorInt(217, 217, 217, 0);
        public int stayOnTicks = 300;

        public CompProperties_ProximityLight()
        {
            compClass = typeof(CompProximityLight);
        }
    }

    public class CompProximityLight : ThingComp
    {
        private bool lightOn = false;
        private int ticksUntilOff = 0;
        private bool devOverride = false;
        private CompGlower glowerComp;
        private bool hasDeregistered = false;

        public CompProperties_ProximityLight Props => (CompProperties_ProximityLight)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // Create a CompGlower and add it to the thing
            glowerComp = new CompGlower();
            glowerComp.parent = parent;
            glowerComp.props = new CompProperties_Glower
            {
                glowRadius = Props.glowRadius,
                glowColor = Props.glowColor
            };
            
            parent.AllComps.Add(glowerComp);
            glowerComp.PostSpawnSetup(respawningAfterLoad);
        }

        public override void CompTick()
        {
            // On first tick, deregister the glower to start with light off
            if (!hasDeregistered && glowerComp != null)
            {
                parent.Map.glowGrid.DeRegisterGlower(glowerComp);
                hasDeregistered = true;
            }
            
            if (Find.TickManager.TicksGame % 60 == 0)
            {
                CheckForPawns();
            }
            
            if (ticksUntilOff > 0)
            {
                ticksUntilOff--;
                if (ticksUntilOff <= 0 && lightOn && !devOverride)
                {
                    lightOn = false;
                    if (glowerComp != null)
                    {
                        parent.Map.glowGrid.DeRegisterGlower(glowerComp);
                    }
                }
            }
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            if (glowerComp != null && previousMap != null)
            {
                previousMap.glowGrid.DeRegisterGlower(glowerComp);
                parent.AllComps.Remove(glowerComp);
            }
            base.PostDestroy(mode, previousMap);
        }

        private void CheckForPawns()
        {
            if (devOverride) return;

            bool foundPawn = false;
            foreach (Pawn pawn in parent.Map.mapPawns.FreeColonists)
            {
                if (pawn.Position.DistanceTo(parent.Position) <= Props.radius)
                {
                    foundPawn = true;
                    break;
                }
            }

            if (foundPawn)
            {
                if (!lightOn)
                {
                    lightOn = true;
                    if (glowerComp != null)
                    {
                        parent.Map.glowGrid.RegisterGlower(glowerComp);
                    }
                }
                ticksUntilOff = Props.stayOnTicks;
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!Prefs.DevMode) yield break;

            yield return new Command_Toggle
            {
                defaultLabel = "Dev Override",
                defaultDesc = "Force light on/off",
                isActive = () => devOverride,
                toggleAction = () => {
                    devOverride = !devOverride;
                    if (glowerComp != null)
                    {
                        if (devOverride)
                        {
                            parent.Map.glowGrid.RegisterGlower(glowerComp);
                        }
                        else
                        {
                            parent.Map.glowGrid.DeRegisterGlower(glowerComp);
                        }
                    }
                }
            };
        }

        public override string CompInspectStringExtra()
        {
            int nearbyColonists = 0;
            foreach (Pawn pawn in parent.Map.mapPawns.FreeColonists)
            {
                if (pawn.Position.DistanceTo(parent.Position) <= Props.radius)
                {
                    nearbyColonists++;
                }
            }
            
            return $"Light: {((lightOn || devOverride) ? "ON" : "OFF")} | Colonists nearby: {nearbyColonists} | Timer: {ticksUntilOff}";
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref lightOn, "lightOn", false);
            Scribe_Values.Look(ref ticksUntilOff, "ticksUntilOff", 0);
            Scribe_Values.Look(ref devOverride, "devOverride", false);
            Scribe_Values.Look(ref hasDeregistered, "hasDeregistered", false);
        }
    }
}