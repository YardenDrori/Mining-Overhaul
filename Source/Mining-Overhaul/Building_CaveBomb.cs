using RimWorld;
using Verse;
using System.Collections.Generic;
using UnityEngine;
namespace MiningOverhaul
{
    public class Building_CaveBomb : Building
    {
        private int spawnTick = -1;
        private const int SecondsToDetonate = 15;
        
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
                int ticksToDetonate = SecondsToDetonate * 60; // 60 ticks per second
                if (Find.TickManager.TicksGame >= spawnTick + ticksToDetonate)
                {
                    Detonate();
                }
            }
        }
        
        private void Detonate()
        {
            Map map = this.Map;
            IntVec3 position = this.Position;
            
            // VISUAL EFFECTS FIRST!
            
            // Small explosion effect
            GenExplosion.DoExplosion(
                center: position,
                map: map,
                radius: 3.5f,
                damType: DamageDefOf.Bomb,
                instigator: this,
                damAmount: 50,
                armorPenetration: 0.2f,
                explosionSound: DefDatabase<SoundDef>.GetNamed("Explosion_Bomb"),
                weapon: null,
                projectile: null,
                intendedTarget: null,
                postExplosionSpawnThingDef: null,
                postExplosionSpawnChance: 0f,
                postExplosionSpawnThingCount: 1,
                preExplosionSpawnThingDef: null,
                preExplosionSpawnChance: 0f,
                preExplosionSpawnThingCount: 1,
                chanceToStartFire: 0.1f,
                damageFalloff: true
            );
            
            // Dust/smoke effects
            for (int i = 0; i < 3; i++)
            {
                IntVec3 randomCell = position + GenRadial.RadialPattern[Rand.Range(0, GenRadial.NumCellsInRadius(2f))];
                if (randomCell.InBounds(map))
                {
                    FleckMaker.ThrowDustPuffThick(randomCell.ToVector3(), map, Rand.Range(1.5f, 3f), Color.gray);
                }
            }
            
            // Screen shake for dramatic effect
            Find.CameraDriver.shaker.DoShake(0.5f);
            
            // Find and destroy cavern entrance
            Thing cavernEntrance = map.thingGrid.ThingAt(position, CavingDefOf.CavernAcessPointTierOne);
            if (cavernEntrance != null)
            {
                cavernEntrance.def.destroyable = true;
                cavernEntrance.Destroy(DestroyMode.Vanish);
            }
            
            // Dramatic message
            Messages.Message("CaveBombDetonated".Translate(), 
                new TargetInfo(position, map), MessageTypeDefOf.TaskCompletion);
            
            // Destroy self
            this.Destroy(DestroyMode.Vanish);
            
            // Spawn the spawner
            ThingDef spawnerDef = CavingDefOf.CavernSpawnerTierOne;
            if (spawnerDef != null)
            {
                Thing spawner = ThingMaker.MakeThing(spawnerDef);
                GenSpawn.Spawn(spawner, position, map);
            }
        }
        
        public override string GetInspectString()
        {
            string baseString = base.GetInspectString();
            
            if (spawnTick != -1)
            {
                int ticksRemaining = (spawnTick + (SecondsToDetonate * 60)) - Find.TickManager.TicksGame;
                
                if (ticksRemaining > 0)
                {
                    float secondsLeft = (float)ticksRemaining / 60f;
                    string timerText = "DetonateTimeRemaining".Translate(secondsLeft.ToString("F1"));
                    
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
    }
}