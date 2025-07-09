using RimWorld;
using UnityEngine;
using Verse;

namespace MiningOverhaul
{
    public class Hediff_CaveGlow : Hediff
    {
        private Thing glowThing;
        
        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            
            // Create a glow thing that follows the pawn
            if (pawn?.Spawned == true)
            {
                CreateGlowThing();
            }
        }

        public override void PostRemoved()
        {
            base.PostRemoved();
            
            // Remove the glow thing
            DestroyGlowThing();
        }

        public override void Tick()
        {
            base.Tick();
            
            if (pawn?.Spawned == true)
            {
                // Create glow thing if it doesn't exist
                if (glowThing == null)
                {
                    CreateGlowThing();
                }
                // Update glow thing position to follow pawn
                else if (glowThing.Position != pawn.Position)
                {
                    // Move the glow thing to the pawn's current position
                    if (glowThing.Spawned)
                    {
                        glowThing.DeSpawn();
                    }
                    GenSpawn.Spawn(glowThing, pawn.Position, pawn.Map);
                }
            }
            else if (glowThing != null)
            {
                // Pawn not spawned, remove glow thing
                DestroyGlowThing();
            }
        }

        private void CreateGlowThing()
        {
            if (glowThing != null || pawn?.Spawned != true) return;
            
            try
            {
                // Use the dedicated PawnCaveLight def
                var lightDef = DefDatabase<ThingDef>.GetNamed("PawnCaveLight", false);
                if (lightDef != null)
                {
                    glowThing = ThingMaker.MakeThing(lightDef);
                    GenSpawn.Spawn(glowThing, pawn.Position, pawn.Map);
                    MOLog.Message($"Created pawn cave light for {pawn.Name}");
                }
                else
                {
                    MOLog.Error("Could not find PawnCaveLight def for pawn glow");
                }
            }
            catch (System.Exception ex)
            {
                MOLog.Error($"Failed to create glow thing for {pawn.Name}: {ex.Message}");
            }
        }

        private void DestroyGlowThing()
        {
            if (glowThing == null) return;
            
            try
            {
                if (glowThing.Spawned)
                {
                    glowThing.DeSpawn();
                }
                glowThing = null;
                
                MOLog.Message($"Removed glow thing from {pawn.Name}");
            }
            catch (System.Exception ex)
            {
                MOLog.Error($"Failed to remove glow thing from {pawn.Name}: {ex.Message}");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref glowThing, "glowThing");
        }
    }
}