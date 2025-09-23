We use BepInEx to mod Enter the Gungeon.
I use net 9.0 






The goal is to push field and description into a local host to be able to used for training data 
for a pytorch model that will learn the game and play



| Field | Description |
| --- | --- |
| `sequence` | Incrementing integer sequence number. |
| `realtime` | `Time.realtimeSinceStartup` from Unity, representing the time since the game started in seconds. |
| `level_name` | Current dungeon floor name (when available), e.g. "Base Camp", "Nucleus", etc. |
| `player.position` | `[x, y]` world-space center of the player in grid coordinates. |
| `player.velocity` | `[x, y]` player velocity in grid coordinates per second. |
| `player.health`, `player.max_health`, `player.armor` | Vital stats from `HealthHaver`: current health, maximum health, and armor points. |
| `player.blanks`, `player.money`, `player.keys` | Consumables: number of banks, money, and keys. |
| `player.current_gun_id`, `player.current_gun_ammo` | Current weapon information: gun ID and ammo count. |
| `player.passive_item_ids` | Pickup IDs for passives (can be mapped via the wikia or the game database). |
| `enemies[]` | Array of active enemies with position, health, boss flag, and distance to player in grid coordinates. |
| `projectiles[]` | Array of active projectiles with position, direction, speed, and `is_enemy` flag, all in grid coordinates. |
| `room.*` | Current room metadata: grid coordinates, size, remaining enemies, boss room flag. |
| `reward` | A reward function that takes in the current game state and returns a reward value. | (Example: getting hit, killing an enemy, etc.)

