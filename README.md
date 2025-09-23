# Enter the Gungeon ML Tracker (Personal Project Plan)

## Summary

This personal project explores capturing **Enter the Gungeon** game state through **BepInEx** plugins and turning those observations into datasets for training **PyTorch** agents. The long-term dream is to let a learned model reason about the game and eventually pilot a character through runs without human input.

---

## Objectives

1. **Instrument the Game:** Use a BepInEx 5.4.x plugin (built with .NET 9.0) to access the live Unity runtime and extract player, enemy, projectile, and room metadata.
2. **Stream Observations:** Serialize each frame’s state to JSON and push it to a localhost API for storage and downstream processing.
3. **Curate Datasets:** Organize captured logs into clean, versioned datasets suitable for both supervised and reinforcement learning experiments.
4. **Model Training:** Prototype training pipelines in PyTorch that learn from the captured data (predict next actions, evaluate reward functions, etc.).
5. **Closed-Loop Control (Future):** Investigate how to drive in-game inputs with a trained model, creating a full “model plays the game” loop.

---

## Architecture Sketch

```
Enter the Gungeon (Unity)
        │
        ▼
BepInEx Plugin (.NET 9.0)
        │  (Hooks into Unity API)
        ▼
Game State Extractor
        │  (Serialize → JSON)
        ▼
Localhost Ingestion Service (Python FastAPI/Flask TBD)
        │
        ├─► Disk Logging (JSON/CSV/Parquet)
        └─► Live Monitoring / Debug Dashboards
        ▼
Data Processing & Feature Engineering
        │
        ▼
PyTorch Training Pipelines
        │
        ▼
Experimental Agents / Evaluation Harness
```

---

## Repository Layout (Planned)

| Path/Module                | Purpose                                                                 |
|----------------------------|-------------------------------------------------------------------------|
| `GungeonRLTracker/`        | Unity/BepInEx plugin source (C# / .NET 9.0).                            |
| `scripts/`                 | Tooling for log parsing, dataset assembly, schema validation.           |
| `notebooks/`               | Exploratory analysis, prototyping reward functions, sanity checks.     |
| `training/`                | PyTorch training loops, models, evaluation utilities.                  |
| `docs/`                    | Additional design notes, schema definitions, personal experiment logs. |

*(Most directories are aspirational; create them as milestones are hit.)*

---

## Data Schema (First Draft)

| Field                                                | Description                                                                                   |
|------------------------------------------------------|-----------------------------------------------------------------------------------------------|
| `sequence`                                           | Incrementing identifier per snapshot.                                                         |
| `realtime`                                           | Seconds since level load (`Time.realtimeSinceStartup`).                                        |
| `level_name`                                         | Current floor name (e.g., “Base Camp”).                                                       |
| `player.position`                                    | `[x, y]` world coordinates (center).                                                          |
| `player.velocity`                                    | `[x, y]` velocity vector.                                                                     |
| `player.health`, `player.max_health`, `player.armor` | Vital stats from `HealthHaver`.                                                               |
| `player.blanks`, `player.money`, `player.keys`       | Consumables inventory.                                                                        |
| `player.current_gun_id`, `player.current_gun_ammo`   | Active weapon identifier and ammo count.                                                      |
| `player.passive_item_ids`                            | List of passive item pickup IDs.                                                              |
| `enemies[]`                                          | Per enemy: position, health, boss flag, distance to player.                                   |
| `projectiles[]`                                      | Per projectile: position, direction, speed, `is_enemy` flag.                                  |
| `room.*`                                             | Current room metadata: grid coords, dimensions, remaining enemies, boss flag.                 |
| `reward`                                             | Scalar reward (damage dealt, damage taken, room cleared, etc.).                               |
| `meta.run_id`                                        | Unique identifier per run/session (helps join multi-file logs).                               |
| `meta.version`                                       | Schema version for backward compatibility.                                                    |

---

## Milestones & Tasks

### Phase 1 – BepInEx Setup
- [ ] Install BepInEx 5.4.x into Enter the Gungeon directory.
- [ ] Scaffold a .NET 9.0 class library project targeting BepInEx plugin conventions.
- [ ] Verify plugin loads (simple console/log output).

### Phase 2 – State Extraction
- [ ] Locate Unity components for player stats, enemies, projectiles, and rooms.
- [ ] Implement safe null-checks and error handling for runtime state access.
- [ ] Serialize a minimal JSON payload every frame (or on fixed timestep).
- [ ] Add configuration toggles (e.g., sampling rate, log level).

### Phase 3 – Localhost Service
- [ ] Prototype a FastAPI or Flask endpoint (`/state`) that accepts JSON payloads.
- [ ] Implement disk logging with rolling files and timestamped directories.
- [ ] Add basic schema validation to reject malformed data.
- [ ] Document service ports, environment variables, and launch scripts.

### Phase 4 – Dataset Engineering
- [ ] Create scripts to merge raw logs, deduplicate sequences, and tag with run metadata.
- [ ] Implement feature computation (e.g., normalized positions, one-hot item IDs).
- [ ] Draft reward shaping heuristics (damage taken, enemies cleared, etc.).
- [ ] Version datasets and keep changelog entries for each iteration.

### Phase 5 – Training Experiments
- [ ] Build a PyTorch Dataset/Dataloader that streams processed logs.
- [ ] Train a baseline supervised model (predict next action or movement direction).
- [ ] Evaluate metrics: accuracy, survival time proxy, room-clear success.
- [ ] Log experiments (tensorboard or lightweight CSV summaries).

### Phase 6 – Control Loop Prototype (Future)
- [ ] Design an input bridge (e.g., virtual controller) to send actions back to the game.
- [ ] Implement safety constraints to avoid runaway behavior.
- [ ] Test inference latency and frame-sync requirements.

---

## Tooling & Environment Notes

- **Language/Runtime:** C# with .NET 9.0 for plugins; Python 3.11+ for services and ML.
- **Dependencies:** BepInEx 5.4.x, Unity assemblies from Enter the Gungeon installation, PyTorch, FastAPI/Flask, pandas, numpy, msgpack/protobuf (future).
- **Logging:** Consider Serilog (C#) and structlog/loguru (Python) for structured logs.
- **Configuration Management:** `.env` files for service ports, output directories, sampling intervals.

---

## Open Questions & Future Ideas

- Should observations be frame-synced or event-driven? Measure performance impact.
- Evaluate compression (protobuf/msgpack) vs. raw JSON once data volume is known.
- Track cooperative mode? Multi-agent handling will complicate schema and reward functions.
- Curriculum strategy: train on early floors first, progressively unlock later floors.
- Visualization dashboard for inspecting logged runs (e.g., simple web UI).

---

## Getting Started (Personal Checklist)

1. Back up the original Enter the Gungeon install before modding.
2. Drop BepInEx files and confirm the launcher runs without crashing.
3. Build the plugin project and copy the DLL into `BepInEx/plugins`.
4. Start the localhost service (`uvicorn main:app --reload --port 8000`) and tail logs.
5. Launch the game, play a short run, and ensure JSON snapshots arrive and are stored.
6. Review logs for missing fields or malformed entries; iterate on plugin serialization.
7. Begin writing analysis notebooks to inspect the collected data.

---

*This README documents a personal experimentation roadmap—timelines are flexible, scope may evolve, and all tasks are for personal learning and curiosity.*
