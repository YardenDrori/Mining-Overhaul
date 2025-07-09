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
        public int fadeInTicks = 180;  // 3 second fade in
        public int fadeOutTicks = 180; // 3 second fade out

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
        private bool isFading = false;
        private int fadeTicks = 0;
        private bool fadingIn = false;
        private float currentRadius = 0f;
        
        // Performance optimizations
        private int nextPawnCheck = 0;
        private float radiusSquared = 0f; // Cache squared radius for faster distance checks
        private int lastGlowUpdateTick = 0;
        private static readonly int PAWN_CHECK_INTERVAL = 30; // Check pawns every 0.5 seconds instead of 1 second
        private static readonly int GLOW_UPDATE_INTERVAL = 10; // Update glow every 10 ticks during fade

        public CompProperties_ProximityLight Props => (CompProperties_ProximityLight)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // Cache squared radius for faster distance calculations
            radiusSquared = Props.radius * Props.radius;
            
            // Stagger initial pawn checks to spread load across ticks
            nextPawnCheck = Find.TickManager.TicksGame + Rand.Range(0, PAWN_CHECK_INTERVAL);
            
            // Create a CompGlower and add it to the thing
            glowerComp = new CompGlower();
            glowerComp.parent = parent;
            glowerComp.props = new CompProperties_Glower
            {
                glowRadius = 0f, // Start with 0 radius
                glowColor = Props.glowColor
            };
            
            parent.AllComps.Add(glowerComp);
            glowerComp.PostSpawnSetup(respawningAfterLoad);
        }

        public override void CompTick()
        {
            int currentTick = Find.TickManager.TicksGame;
            
            // Handle fade transitions
            if (isFading)
            {
                fadeTicks++;
                int totalFadeTicks = fadingIn ? Props.fadeInTicks : Props.fadeOutTicks;
                
                if (fadeTicks >= totalFadeTicks)
                {
                    // Fade complete
                    isFading = false;
                    fadeTicks = 0;
                    currentRadius = fadingIn ? Props.glowRadius : 0f;
                    
                    if (!fadingIn)
                    {
                        // Fade out complete - deregister
                        lightOn = false;
                        if (glowerComp != null)
                        {
                            parent.Map.glowGrid.DeRegisterGlower(glowerComp);
                        }
                    }
                    
                    // Final glow update
                    UpdateGlowRadius();
                }
                else
                {
                    // Calculate current fade progress and update radius
                    float progress = (float)fadeTicks / totalFadeTicks;
                    currentRadius = fadingIn ? (Props.glowRadius * progress) : (Props.glowRadius * (1f - progress));
                    
                    // Update glow radius less frequently for better performance
                    if (currentTick - lastGlowUpdateTick >= GLOW_UPDATE_INTERVAL)
                    {
                        UpdateGlowRadius();
                        lastGlowUpdateTick = currentTick;
                    }
                }
            }
            
            // Staggered pawn checking for better performance
            if (currentTick >= nextPawnCheck)
            {
                CheckForPawns();
                nextPawnCheck = currentTick + PAWN_CHECK_INTERVAL;
            }
            
            // Timer countdown
            if (ticksUntilOff > 0)
            {
                ticksUntilOff--;
                if (ticksUntilOff <= 0 && lightOn && !devOverride && !isFading)
                {
                    StartFadeOut();
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

        private void StartFadeIn()
        {
            if (!lightOn && !isFading)
            {
                lightOn = true;
                isFading = true;
                fadingIn = true;
                fadeTicks = 0;
                currentRadius = 0f;
                
                if (glowerComp != null)
                {
                    parent.Map.glowGrid.RegisterGlower(glowerComp);
                }
            }
        }
        
        private void StartFadeOut()
        {
            if (lightOn && !isFading)
            {
                isFading = true;
                fadingIn = false;
                fadeTicks = 0;
                currentRadius = Props.glowRadius;
            }
        }

        private void UpdateGlowRadius()
        {
            if (glowerComp != null)
            {
                ((CompProperties_Glower)glowerComp.props).glowRadius = currentRadius;
                
                if (lightOn)
                {
                    parent.Map.glowGrid.DeRegisterGlower(glowerComp);
                    parent.Map.glowGrid.RegisterGlower(glowerComp);
                }
            }
        }
        
        private void CheckForPawns()
        {
            if (devOverride) return;

            // Use squared distance for faster calculations (no sqrt needed)
            IntVec3 lightPos = parent.Position;
            bool foundPawn = false;
            
            // Check free colonists using cached collection
            List<Pawn> colonists = parent.Map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                if (pawn?.Spawned == true)
                {
                    IntVec3 pawnPos = pawn.Position;
                    float distSq = (lightPos.x - pawnPos.x) * (lightPos.x - pawnPos.x) + 
                                   (lightPos.z - pawnPos.z) * (lightPos.z - pawnPos.z);
                    
                    if (distSq <= radiusSquared)
                    {
                        foundPawn = true;
                        break;
                    }
                }
            }

            if (foundPawn)
            {
                if (!lightOn && !isFading)
                {
                    StartFadeIn();
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
            // Only calculate nearby colonists if being inspected (less frequent)
            int nearbyColonists = 0;
            if (Prefs.DevMode) // Only show detailed info in dev mode
            {
                IntVec3 lightPos = parent.Position;
                List<Pawn> colonists = parent.Map.mapPawns.FreeColonists;
                for (int i = 0; i < colonists.Count; i++)
                {
                    Pawn pawn = colonists[i];
                    if (pawn?.Spawned == true)
                    {
                        float distSq = (lightPos.x - pawn.Position.x) * (lightPos.x - pawn.Position.x) + 
                                       (lightPos.z - pawn.Position.z) * (lightPos.z - pawn.Position.z);
                        if (distSq <= radiusSquared)
                        {
                            nearbyColonists++;
                        }
                    }
                }
                return $"Light: {((lightOn || devOverride) ? "ON" : "OFF")} | Colonists: {nearbyColonists} | Timer: {ticksUntilOff} | Radius: {currentRadius:F1}";
            }
            
            return $"Light: {((lightOn || devOverride) ? "ON" : "OFF")}";
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref lightOn, "lightOn", false);
            Scribe_Values.Look(ref ticksUntilOff, "ticksUntilOff", 0);
            Scribe_Values.Look(ref devOverride, "devOverride", false);
            Scribe_Values.Look(ref isFading, "isFading", false);
            Scribe_Values.Look(ref fadeTicks, "fadeTicks", 0);
            Scribe_Values.Look(ref fadingIn, "fadingIn", false);
            Scribe_Values.Look(ref currentRadius, "currentRadius", 0f);
            
            // Recalculate cached values after loading
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                radiusSquared = Props.radius * Props.radius;
                nextPawnCheck = Find.TickManager.TicksGame + Rand.Range(0, PAWN_CHECK_INTERVAL);
            }
        }
    }
}