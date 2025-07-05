# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a RimWorld mod called "Mining Overhaul" that implements a dynamic cave system with temporal stability mechanics. The mod adds deep mining, cavern exploration, and creature spawning systems to RimWorld.

## Development Commands

### Building the Project
```bash
# Build the C# project
dotnet build Source/Mining-Overhaul/Mining-Overhaul.csproj

# The compiled DLL will be placed in Assemblies/Mining-Overhaul.dll
```

### Testing
- No automated tests - testing is done in-game using RimWorld's development mode
- Enable debug mode in ModSettings.cs for verbose logging
- Use RimWorld's dev tools for testing cave generation and stability mechanics

## Architecture Overview

### Core Systems
1. **Cave Entrance System** (`Building_CavernEntrance.cs`) - Central hub managing cave stability and lifecycle
2. **Creature Spawning System** (`CompCavernCreatureSpawner.cs`) - Stability-based creature spawning with configurable scaling
3. **Stabilization System** (`CompCavernStabilizer.cs`) - Prevents cave collapse when active
4. **Cave Generation** (`GenStep_CaveShape.cs`) - Multiple algorithms for cave layout generation
5. **Access Points** (`Building_CavernAccessPoint.cs`) - Temporary structures for cave entry

### Key Architecture Patterns
- **Component-Based Design**: Uses RimWorld's `ThingComp` system for modular functionality
- **Temporal State Management**: Complex state tracking with incremental processing
- **Performance Optimization**: Caching, incremental processing, and batch operations
- **Configuration-Driven**: Extensive XML configuration for customization

### Data Flow
Cave Creation → Entrance Spawning → Component Attachment → Stability Degradation → Creature Spawning → Collapse Prevention → Final Collapse

## Important Files

### Core Classes
- `ModCore.cs` - Main mod entry point with Harmony patches
- `Building_CavernEntrance.cs` - Primary cave management system
- `CompCavernCreatureSpawner.cs` - Creature spawning logic
- `CompCavernStabilizer.cs` - Cave stabilization mechanics
- `ModSettings.cs` - User configuration and logging

### Generation Steps
- `GenStep_CaveShape.cs` - Cave generation algorithms
- `GenStep_POI_Generation.cs` - Point of interest placement
- `GenStep_DeepSteelVeins.cs` - Resource vein generation

### Patches
- `MiningInstabilityPatch.cs` - Harmony patches for mining mechanics

## Configuration Files

### XML Definitions
- `1.6/Defs/` - All game definitions (buildings, items, biomes, etc.)
- `1.6/Defs/GenStepDefs/` - Cave generation parameters
- `About/About.xml` - Mod metadata

### Key Configuration Concepts
- **Stability Loss**: Percentage-based system (0.0 = stable, 1.0 = fully unstable)
- **Scaling Factor**: How much instability affects spawn rates and counts
- **Baseline at 50%**: All spawn configurations use 50% instability as baseline

## Performance Considerations

### Optimization Patterns Used
- **Incremental Processing**: Spreads expensive operations across multiple game ticks
- **Caching**: Expensive lookups cached (adjacent rocks, exit positions)
- **Batch Operations**: Processes multiple cells/entities together
- **Array Access**: Uses O(1) cell access patterns

### Memory Management
- Cache invalidation on map changes
- Null checking and bounds validation
- Graceful degradation when pocket maps don't exist

## Development Notes

### Logging
- Use `MOLog.Message()` for consistent logging format
- Enable debug mode in settings for verbose output
- Conditional logging to avoid performance impact

### Testing Approach
- Use RimWorld's development mode for testing
- Enable debug gizmos for cave entrances
- Test with different stability levels and creature configurations

### Common Patterns
- Always check for null pocket maps before operations
- Use incremental processing for expensive operations
- Follow RimWorld's save/load patterns for persistence
- Implement proper cleanup in destroy methods

## Dependencies
- **RimWorld 1.6** (Krafs.Rimworld.Ref package)
- **Harmony 2.3.6** for runtime patching
- **Brrainz.Harmony** mod dependency

## File Structure Notes
- C# source in `Source/Mining-Overhaul/`
- Compiled assemblies in `Assemblies/`
- Game definitions in `1.6/Defs/`
- Localization in `Languages/English/`
- The `todo` file contains development roadmap and known issues