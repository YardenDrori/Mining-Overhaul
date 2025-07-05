# CompCavernCreatureSpawner

A RimWorld component that spawns creatures inside cavern pocket maps with a sophisticated scaling system based on stability loss percentage.

## Features

### 📈 Advanced Scaling System
- **50% Instability Baseline**: All spawn rates and counts are defined at 50% instability
- **Configurable Scaling Factor**: Controls how much instability affects frequency and count
- **Independent Config Timers**: Each spawn config has its own independent timer
- **Dynamic Scaling**: At 100% instability with scaling factor 1.0, spawns are 2x more frequent and 2x larger

### 🚫 Intelligent Avoidance
- **Exit Avoidance**: Creatures avoid spawning near cavern exits (configurable radius)
- **Center Avoidance**: Alternative mode to avoid map center instead
- **Colonist Avoidance**: Minimum 8-cell distance from colonists

### 🎮 XML Configuration
- Multiple spawn configurations per cavern entrance
- Creature type selection by stability loss percentage
- Per-config scaling factors for fine-tuned behavior
- Support for both hostile and neutral creatures

### 🎨 Visual Effects
- Configurable spawn effects (dust clouds, debris, ambience)
- Sound effects for dramatic spawning
- Default fallback effects if none specified

### ⚡ Performance Optimized
- Incremental cell validation (25 cells per tick)
- Cached valid spawn locations
- Periodic refresh system (every 10 seconds)

## How the Scaling System Works

The scaling system uses **50% instability as the baseline** where your configured values apply exactly as written:

### Example with Scaling Factor 1.0:
- **10% Instability**: 0.2x frequency, 0.2x count (1/5th of baseline)
- **25% Instability**: 0.5x frequency, 0.5x count (half of baseline)
- **50% Instability**: 1.0x frequency, 1.0x count (exactly as configured)
- **75% Instability**: 1.5x frequency, 1.5x count (1.5x baseline)
- **100% Instability**: 2.0x frequency, 2.0x count (double baseline)

### Example with Scaling Factor 2.0:
- **10% Instability**: 0.4x frequency, 0.4x count
- **25% Instability**: 1.0x frequency, 1.0x count (baseline reached earlier)
- **50% Instability**: 2.0x frequency, 2.0x count (double baseline)
- **75% Instability**: 3.0x frequency, 3.0x count
- **100% Instability**: 4.0x frequency, 4.0x count

## XML Configuration

### Component Properties
```xml
<li Class="MiningOverhaul.CompProperties_CavernCreatureSpawner">
    <exitAvoidanceRadius>6</exitAvoidanceRadius>            <!-- Distance from exit to avoid -->
    <centerAvoidanceRadius>4</centerAvoidanceRadius>        <!-- Distance from center to avoid -->
    <useExitAvoidance>true</useExitAvoidance>               <!-- Exit vs center avoidance -->
    <debugMode>false</debugMode>                            <!-- Enable debug output -->
    <spawnConfigs>
        <!-- Your spawn configurations here -->
    </spawnConfigs>
</li>
```

### Individual Spawn Configuration
```xml
<li>
    <label>Cave Bears</label>                               <!-- Display name -->
    <creatureDefNames>                                      <!-- Creature types to spawn -->
        <li>Bear_Grizzly</li>
        <li>Bear_Polar</li>
    </creatureDefNames>
    <baseSpawnCountRange>1~2</baseSpawnCountRange>          <!-- Count at 50% instability -->
    <baseSpawnFrequencyDays>2.0</baseSpawnFrequencyDays>   <!-- Frequency at 50% instability -->
    <instabilityScalingFactor>1.5</instabilityScalingFactor> <!-- How much instability affects this config -->
    <spawnChance>0.5</spawnChance>                          <!-- Probability (0-1) -->
    <minStabilityLoss>0.5</minStabilityLoss>                <!-- Min stability loss (0-1) -->
    <maxStabilityLoss>0.75</maxStabilityLoss>               <!-- Max stability loss (0-1) -->
    <spawnRadius>4</spawnRadius>                            <!-- Group spawn radius -->
    <manhunterChance>0.6</manhunterChance>                  <!-- Chance for manhunters (0-1) -->
    <spawnEffects>                                          <!-- Visual effects -->
        <li>ImpactDustCloud</li>
        <li>UndercaveCeilingDebris</li>
    </spawnEffects>
    <spawnSounds>                                           <!-- Sound effects -->
        <li>Pawn_Bear_Injured</li>
    </spawnSounds>
</li>
```

## Configuration Parameters Explained

### Core Scaling Parameters
- **`baseSpawnCountRange`**: How many creatures to spawn when instability is exactly 50% (e.g., "1~3" = 1-3 creatures)
- **`baseSpawnFrequencyDays`**: How often to attempt spawning when instability is exactly 50% (e.g., 2.0 = every 2 days)
- **`instabilityScalingFactor`**: Multiplier for how much instability affects frequency and count (1.0 = normal, 2.0 = double effect)

### Activation Conditions
- **`minStabilityLoss`**: Minimum instability percentage for this config to activate (0.0 = 0%, 0.5 = 50%, 1.0 = 100%)
- **`maxStabilityLoss`**: Maximum instability percentage for this config to be active
- **`spawnChance`**: Probability that spawning actually happens when timer triggers (0.0 = never, 1.0 = always, 0.5 = 50% chance)

### Creature Selection & Behavior
- **`creatureDefNames`**: List of creature PawnKindDef names - **picks randomly for each individual creature spawned**
- **`label`**: Display name for this spawn group (used in notifications and debug)
- **`spawnRadius`**: How close together creatures in the same spawn event appear
- **`manhunterChance`**: Probability that spawned creatures become manhunters (0.0 = never, 1.0 = always, 0.5 = 50% chance)

### Visual & Audio
- **`spawnEffects`**: List of EffecterDef names to play when spawning (e.g., "ImpactDustCloud")
- **`spawnSounds`**: List of SoundDef names to play when spawning

## Example Scenarios

### Mixed Creature Spawning - "Apex Predators"
```xml
<li>
    <label>Apex Predators</label>
    <creatureDefNames>
        <li>Thrumbo</li>           <!-- Each spawn picks randomly -->
        <li>Bear_Grizzly</li>      <!-- Could spawn 1 Thrumbo, 2 Bears -->
        <li>Bear_Polar</li>        <!-- Or 3 Thrumbos, etc. -->
    </creatureDefNames>
    <baseSpawnCountRange>1~3</baseSpawnCountRange>          <!-- At 50% instability -->
    <baseSpawnFrequencyDays>4.0</baseSpawnFrequencyDays>    <!-- Every 4 days at 50% -->
    <instabilityScalingFactor>1.2</instabilityScalingFactor>
    <spawnChance>0.3</spawnChance>                          <!-- Only 30% chance when timer triggers -->
    <manhunterChance>0.4</manhunterChance>                  <!-- 40% chance for manhunters -->
    <minStabilityLoss>0.4</minStabilityLoss>
    <maxStabilityLoss>0.8</maxStabilityLoss>
</li>
```
**Result**: Each time this spawns, it might create 1 Thrumbo, or 2 Grizzly Bears, or 1 Polar Bear + 1 Thrumbo, etc.

### Low-Impact Early Warning (Scaling Factor 0.8)
```xml
<li>
    <label>Cave Vermin</label>
    <creatureDefNames>
        <li>Rat</li>
        <li>Squirrel</li>
    </creatureDefNames>
    <baseSpawnCountRange>2~4</baseSpawnCountRange>
    <baseSpawnFrequencyDays>3.0</baseSpawnFrequencyDays>
    <instabilityScalingFactor>0.8</instabilityScalingFactor>
    <spawnChance>0.8</spawnChance>                          <!-- 80% chance -->
    <minStabilityLoss>0.0</minStabilityLoss>
    <maxStabilityLoss>0.25</maxStabilityLoss>
</li>
```
Perfect for harmless creatures that give early warning without overwhelming the player.

### Aggressive Late-Game Threats (Scaling Factor 2.0)
```xml
<li>
    <label>Insect Swarm</label>
    <creatureDefNames>
        <li>Megaspider</li>
        <li>Spelopede</li>
        <li>Megascarab</li>
    </creatureDefNames>
    <baseSpawnCountRange>2~4</baseSpawnCountRange>
    <baseSpawnFrequencyDays>1.5</baseSpawnFrequencyDays>
    <instabilityScalingFactor>2.0</instabilityScalingFactor>
    <spawnChance>0.9</spawnChance>                          <!-- Almost guaranteed -->
    <minStabilityLoss>0.75</minStabilityLoss>
    <maxStabilityLoss>1.0</maxStabilityLoss>
</li>
```
Creates intense pressure when the cave is about to collapse.

## How `spawnChance` Works

Even when the timer triggers, spawning only happens if the random chance succeeds:

- **`spawnChance: 1.0`** = Always spawns when timer triggers
- **`spawnChance: 0.5`** = 50% chance to spawn when timer triggers  
- **`spawnChance: 0.1`** = Only 10% chance to spawn when timer triggers

This lets you have frequent timers but rare actual spawns, or guaranteed spawns with longer timers.

## Stability Range Examples

- **`minStabilityLoss: 0.0, maxStabilityLoss: 0.25`** = Only active from 0-25% instability
- **`minStabilityLoss: 0.5, maxStabilityLoss: 1.0`** = Only active from 50-100% instability  
- **`minStabilityLoss: 0.0, maxStabilityLoss: 1.0`** = Always active (entire instability range)

## Manhunter Chance Examples

- **`manhunterChance: 0.0`** = Creatures spawn peaceful (good for harmless animals like rats)
- **`manhunterChance: 0.3`** = 30% chance for manhunters (moderate threat)
- **`manhunterChance: 0.8`** = 80% chance for manhunters (high threat level)
- **`manhunterChance: 1.0`** = All spawned creatures are manhunters (maximum aggression)

**Note**: Manhunter state only applies to animals that can become manhunters (most animals except insects). The system automatically checks if the creature type supports manhunter behavior.

## Integration

Simply add the component to your `CavernEntrance` ThingDef:

```xml
<ThingDef ParentName="CavernEntranceBase">
    <defName>MyCavernEntrance</defName>
    <!-- other properties -->
    <comps>
        <li Class="MiningOverhaul.CompProperties_CavernCreatureSpawner">
            <!-- your configuration -->
        </li>
    </comps>
</ThingDef>
```

The component automatically:
- Reads cave stability from the existing stability system
- Spawns creatures according to your scaling rules
- Avoids exits and colonists
- Provides visual and audio feedback
- Offers debug tools when dev mode is enabled