# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Commands

### Building the Mod
- Build the C# project: `dotnet build Source/Mining-Overhaul/Mining-Overhaul.sln`
- The compiled assemblies are output to `Assemblies/` directory
- Uses .NET Framework 4.7.2 with RimWorld 1.6 references

### Dependencies
- **Harmony** (brrainz.harmony) - Required for patching RimWorld code
- **Krafs.Rimworld.Ref** (1.6.4491-beta) - RimWorld reference assemblies
- **Lib.Harmony** (2.3.6) - Harmony library for runtime patching

## Architecture Overview

### Core Components

**CavernEntrance** (`Building_CavernEntrance.cs`):
- Main portal system managing cave stability and collapse mechanics
- Inherits from `MapPortal` to create pocket maps (underground caves)
- Features complex stability system with visual effects, partial collapse, and full collapse
- Optimized incremental processing for performance (processes cells in chunks)
- Configurable collapse avoidance (center vs exit-based)
- Building detection system for powered structures on surface

**Generation System** (`GenSteps/` directory):
- `GenStep_CaveShape.cs` - Generates cave layouts
- `GenStep_CenterHole.cs` - Creates central cave features
- `GenStep_DeepSteelVeins.cs` - Places deep resource veins
- `GenStep_FloorReplacement.cs` - Replaces cave floors
- `GenStep_POI_Generation.cs` - Places points of interest
- `GenStep_PlaceCavernExit.cs` - Places cave exits

**Components**:
- `CompCavernScanner.cs` - Scanning functionality for finding caves
- `CompCavernStabilizer.cs` - Stabilization system for caves
- `BuildingCavernSpawner.cs` - Spawns cave entrances
- `Building_CavernAccessPoint.cs` - Access point structures
- `Building_CaveBomb.cs` - Explosive devices for cave creation

### Key Systems

**Stability System**:
- Time-based degradation (36000 ticks = 1 RimWorld day)
- Colonist presence accelerates instability (25% per colonist)
- Mining operations add instability bursts
- Four visual effect stages based on stability percentage
- Partial collapse starts at 50% stability, full collapse at 100%

**Collapse Mechanics**:
- Incremental cell blocking system for performance
- Strategic cell selection (adjacent to rock walls)
- Avoidance zones around exits or map center
- Cached adjacency checks for optimization
- Accelerated collapse during final phase

**Cave Types** (planned, see `todo` file):
- Level 1: Steel slag access (Rocky, Flooded)
- Level 2: Steel + silver (Vegetation, Frozen)
- Level 3: Steel + silver + uranium + jade (Infested, Fungal)
- Level 4: All resources (Magma, Radioactive, Bunker)
- Level 5: Oxygen-deprived levels (future Odyssey content)

### XML Structure

**Defs Organization**:
- `1.6/Defs/` - All game definitions for RimWorld 1.6
- `CaveEntrances.xml` - Portal definitions with inheritance
- `CavernGeneration.xml` - Cave generation parameters
- `DeepResources.xml` - Underground resource definitions
- `GenStepDefs/` - World generation step definitions

**Localization**:
- `Languages/English/Keyed/Keys.xml` - Translation keys

### Performance Optimizations

**Incremental Processing**:
- `CELLS_PER_REFRESH = 50` - Cells processed per tick for blockable cell discovery
- `CELLS_PER_VALIDATION = 20` - Cells validated per tick
- `CACHE_REFRESH_INTERVAL = 300` - Cache refresh every 5 seconds
- `THING_CHECK_INTERVAL = 300` - Building detection every 5 seconds

**Caching Systems**:
- Adjacent rock cache for expensive terrain checks
- Blockable cells set for O(1) lookups
- Incremental validation instead of full list processing

## Development Notes

### Mod ID and Namespace
- Package ID: `blacksparrow.miningoverhaul`
- Namespace: `MiningOverhaul`
- Harmony ID: `blacksparrow.MiningOverhaul`

### Important Constants
- `StabilityDurationTicks = 36000` (1 day)
- `MiningInstabilityIncrease = 1000` (per mining operation)
- `PartialCollapseInterval = 120` (2 seconds)
- `AcceleratedCollapseInterval = 15` (0.25 seconds)

### DefOf Pattern
- `CavingDefOf.cs` contains static references to ThingDefs
- Used for type-safe access to mod definitions
- Populated automatically by RimWorld's reflection system

### POI (Point of Interest) System
**Architecture** (`GenStep_POI_Generation.cs`):
- **POIGenStepDef**: Controls where and how often POIs spawn
- **POIContentDef**: Defines what spawns in each POI using spawn groups
- **POISpawnGroup**: Groups of related items/creatures that spawn together
- **SpawnPattern**: Controls spatial arrangement (Clustered, Scattered, Ring, Line)

**Key Features**:
- **Grouped Spawning**: Related items spawn together (e.g., insect hive + glowpods + defenders)
- **Spawn Patterns**: Different spatial arrangements for variety
- **Conditional Spawning**: Groups have spawn chances for randomness
- **Legacy Support**: Automatically converts old format definitions

**Example Usage**:
```xml
<!-- Insect hive with glowpods and defenders -->
<spawnGroups>
    <li>
        <label>Hive Core</label>
        <spawnChance>1.0</spawnChance>
        <pattern>Clustered</pattern>
        <groupRadius>1</groupRadius>
        <spawnOptions>
            <li><thingDef>Hive</thingDef><thingCount>1~2</thingCount></li>
        </spawnOptions>
    </li>
    <li>
        <label>Glowpods</label>
        <spawnChance>0.8</spawnChance>
        <pattern>Ring</pattern>
        <groupRadius>4</groupRadius>
        <spawnOptions>
            <li><thingDef>Plant_GlowPod</thingDef><thingCount>3~8</thingCount></li>
        </spawnOptions>
    </li>
</spawnGroups>
```

### PlaceWorkers
- `PlaceWorker_CavernAccessPoint.cs` - Placement validation for access points
- `PlaceWorker_OnlyInCavern.cs` - Restricts placement to cave environments

### Debugging
- Extensive dev gizmos for stability testing
- Debug info shows colonist count, stability percentage, and system states
- Warning and error logging for development feedback