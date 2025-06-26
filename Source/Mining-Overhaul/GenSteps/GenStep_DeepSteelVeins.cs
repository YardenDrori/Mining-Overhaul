using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using System;

namespace MiningOverhaul
{
    public class GenStep_DeepSteelVeins : GenStep
    {
        // Log.Message("entered");
        public override int SeedPart => 874651234;
        public String Resource = "MO_MineableSteelDeep";
        private const int MaxVeins = 400;
        private const int MinDistanceFromExit = 15;
        private const int MinVeinSize = 5;
        private const int MaxVeinSize = 15;

        public override void Generate(Map map, GenStepParams parms)
        {
            // Find the cavern exit position
            IntVec3 exitPos = FindCavernExit(map);
            if (exitPos == IntVec3.Invalid){
                Log.Message("Couldnt find cavern exit womp womp");
                return; // No exit found, skip
            }


            // Get all valid rock positions (away from exit)
            List<IntVec3> validRockPositions = GetValidRockPositions(map, exitPos);

            // Generate up to 50 steel veins
            int totalSteelPlaced = 0;
            int attempts = 0;

            while (totalSteelPlaced < MaxVeins && attempts < 200)
            {
                Log.Message(attempts + " attempts and " + totalSteelPlaced + " steel placed");
                attempts++;
                if (!validRockPositions.Any()) break;

                // Pick random starting position
                IntVec3 veinStart = validRockPositions.RandomElement();

                // Generate a vein from this position
                List<IntVec3> vein = GenerateVein(map, veinStart, validRockPositions);

                if (vein.Count >= MinVeinSize && HasAirContact(map, vein))
                {
                    foreach (IntVec3 pos in vein)
                    {
                        if (totalSteelPlaced >= MaxVeins) break;

                        // Remove existing rock edifice
                        var existingEdifice = pos.GetEdifice(map);
                        if (existingEdifice != null)
                            existingEdifice.Destroy();

                        // Spawn deep-steel
                        GenSpawn.Spawn(ThingDef.Named(Resource), pos, map);
                        totalSteelPlaced++;
                        validRockPositions.Remove(pos);
                    }
                }

                // Clean up used positions
                foreach (IntVec3 pos in vein)
                    validRockPositions.Remove(pos);
            }
        }

        private IntVec3 FindCavernExit(Map map)
        {
            foreach (IntVec3 cell in map.AllCells)
            {
                var edifice = cell.GetEdifice(map);
                // Check if edifice is not null before accessing its properties
                if (edifice != null && edifice.def == ThingDef.Named("CavernExit"))
                    return cell;
            }
            return IntVec3.Invalid;
        }

        private List<IntVec3> GetValidRockPositions(Map map, IntVec3 exitPos)
        {
            var validPositions = new List<IntVec3>();

            foreach (IntVec3 cell in map.AllCells)
            {
                var edifice = cell.GetEdifice(map);
                // Must be natural rock and far enough from exit
                if (edifice == null || !edifice.def.building.isNaturalRock) continue;
                if (cell.DistanceTo(exitPos) < MinDistanceFromExit) continue;

                validPositions.Add(cell);
            }

            return validPositions;
        }

        private List<IntVec3> GenerateVein(Map map, IntVec3 start, List<IntVec3> validPositions)
        {
            var vein = new List<IntVec3>();
            var candidates = new List<IntVec3> { start };
            int targetSize = Rand.Range(MinVeinSize, MaxVeinSize);
            IntVec3 lastDirection = GenAdj.CardinalDirections.RandomElement(); // Pick initial direction

            while (vein.Count < targetSize && candidates.Any())
            {
                IntVec3 current;
                
                // 80% chance to pick directional candidate, 20% random
                if (Rand.Chance(0.8f) && vein.Count > 0)
                {
                    current = PickDirectionalCandidate(candidates, lastDirection);
                }
                else
                {
                    current = candidates.RandomElement();
                }
                
                candidates.Remove(current);

                if (!validPositions.Contains(current) || vein.Contains(current))
                    continue;

                vein.Add(current);
                
                // Update direction based on growth
                if (vein.Count > 1)
                {
                    lastDirection = current - vein[vein.Count - 2];
                }

                // Add adjacent rock cells
                foreach (var adj in GenAdj.CardinalDirections)
                {
                    var neighbor = current + adj;
                    if (neighbor.InBounds(map) &&
                        validPositions.Contains(neighbor) &&
                        !vein.Contains(neighbor))
                    {
                        candidates.Add(neighbor);
                    }
                }
            }

            return vein;
        }

        private IntVec3 PickDirectionalCandidate(List<IntVec3> candidates, IntVec3 preferredDirection)
        {
            // Try to find candidates in the preferred direction
            var directionalCandidates = candidates.Where(c => 
            {
                // Check if any adjacent cell would continue in preferred direction
                return GenAdj.AdjacentCells.Any(adj => adj == preferredDirection);
            }).ToList();

            if (directionalCandidates.Any())
                return directionalCandidates.RandomElement();
            
            // Fallback to any candidate
            return candidates.RandomElement();
        }
        private bool HasAirContact(Map map, List<IntVec3> vein)
        {
            foreach (IntVec3 pos in vein)
            {
                foreach (var adj in GenAdj.CardinalDirections)
                {
                    var neighbor = pos + adj;
                    if (!neighbor.InBounds(map)) continue;

                    // Passable + no natural-rock edifice = "air"
                    if (neighbor.Walkable(map))
                    {
                        var edifice = neighbor.GetEdifice(map);
                        if (edifice == null || !edifice.def.building.isNaturalRock)
                            return true;
                    }
                }
            }
            return false;
        }
    }
}