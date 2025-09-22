# Enter the Gungeon RL Tracking Kit

This repository contains a BepInEx plugin and matching Python utilities that expose
important runtime information from **Enter the Gungeon** to a reinforcement-learning
pipeline. The goal is to make it straightforward to collect state observations for a
PyTorch agent that learns to play the game.

## Repository layout

```
bepinex/GungeonRLTracker/   # BepInEx plugin source
python/                     # Python helper code and DQN training skeleton
```

## 1. Install BepInEx for Enter the Gungeon

1. Download the x86 version of [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases).
2. Extract it into the Enter the Gungeon installation directory so that the folder looks like:
   `Enter the Gungeon/BepInEx/`.
3. Launch the game once to allow BepInEx to create its folder structure, then close it.

> The default Steam install path on Windows is
> `C:\Program Files (x86)\Steam\steamapps\common\Enter the Gungeon`.

## 2. Build and deploy the tracker plugin

The plugin is implemented in `bepinex/GungeonRLTracker/GungeonRLTracker.cs`.
A minimal `csproj` (`net35`) is provided to make compilation repeatable.

1. Install the [.NET SDK 6+](https://dotnet.microsoft.com/en-us/download).
2. Adjust the MSBuild properties so the project can locate the game assemblies:

   ```powershell
   # From the repository root
   $game = "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Enter the Gungeon\\EnterTheGungeon_Data\\Managed"
   $bepinex = "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Enter the Gungeon\\BepInEx"
   dotnet build .\bepinex\GungeonRLTracker\GungeonRLTracker.csproj -c Release `
     -p:GameAssembliesDir=$game -p:BepInExDir=$bepinex
   ```

   The post-build step copies `GungeonRLTracker.dll` into
   `BepInEx/plugins/GungeonRLTracker/`.

3. Start the game. On launch you should see a log entry similar to
   `Gungeon RL Tracker initialising` in `BepInEx/LogOutput.log`.

## 3. Data exposed to Python

The plugin opens a TCP socket on `127.0.0.1:18475` and emits newline-delimited JSON.
The first message is a handshake:

```json
{"message_type":"handshake","schema_version":1,"plugin_version":"1.0.0","game":"Enter the Gungeon"}
```

Subsequent messages have `message_type: "snapshot"`. A condensed schema is shown below.
All numeric values are floating-point unless noted otherwise.

| Field | Description |
| --- | --- |
| `sequence` | Incrementing integer sequence number. |
| `realtime` | `Time.realtimeSinceStartup` from Unity. |
| `level_name` | Current dungeon floor name (when available). |
| `player.position` | `[x, y]` world-space center of the player. |
| `player.velocity` | `[x, y]` player velocity. |
| `player.health`, `player.max_health`, `player.armor` | Vital stats from `HealthHaver`. |
| `player.blanks`, `player.money`, `player.keys` | Consumables. |
| `player.current_gun_id`, `player.current_gun_ammo` | Current weapon information. |
| `player.passive_item_ids` | Pickup IDs for passives (can be mapped via the wikia or the game database). |
| `enemies[]` | Array of active enemies with position, health, boss flag, and distance to player. |
| `projectiles[]` | Array of active projectiles with position, direction, speed, and `is_enemy` flag. |
| `room.*` | Current room metadata: grid coordinates, size, remaining enemies, boss room flag. |

Snapshots are emitted at ~30 Hz when a bridge client is connected.

## 4. Python bridge and example agent

The `python/` folder contains:

- `gungeon_bridge.py`: a resilient TCP client that turns the plugin feed into `Snapshot` objects.
- `rl_agent_example.py`: a lightweight DQN-style training skeleton that demonstrates how to
  turn snapshots into feature vectors and push them through a PyTorch model.

Install the Python dependencies with:

```bash
python -m venv .venv
source .venv/bin/activate  # On Windows use .venv\Scripts\activate
pip install -r requirements.txt
```

Run the bridge or the training skeleton after starting the game:

```bash
python python/rl_agent_example.py
```

The example focuses on state ingestion and learning. It **does not** send actions back to the
game. For automated control you can extend the plugin with an input command channel or use a
separate tool (e.g. Windows input simulation) to press keys based on the agent's chosen action.

## 5. Extending the tracker

The plugin is intentionally conservative—only data that is cheap to query each frame is included.
Some ideas for extensions:

- Mirror extra player metrics (cooldowns, curse, active synergies).
- Include tile-based room layouts by iterating `room.Cells`. Cache static data to avoid allocations.
- Add a second socket or message type for action commands. Unity's `BraveInput` can be used to
  queue dodge-rolls, movement vectors, and firing directions.
- Log gameplay transitions (floor load/unload events) for episodic RL resets.

Because the JSON schema is small, it is easy to version. Bump `schema_version` whenever you change
fields and handle older versions in the Python client.

## 6. Troubleshooting

- If the Python bridge cannot connect, verify that no firewall blocks `127.0.0.1:18475` and that
  the plugin log confirms the listener started.
- When the feed stalls, check `BepInEx/LogOutput.log` for exceptions—most often a reflection call
  failed because an enemy object was destroyed mid-snapshot.
- Use the `Bridge reader error` logs in Python to inspect disconnects; the client automatically
  reconnects with exponential backoff.

Happy modding and experimenting!
