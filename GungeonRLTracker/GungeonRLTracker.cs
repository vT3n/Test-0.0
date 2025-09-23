using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using BepInEx;
using Dungeonator;
using UnityEngine;
using Newtonsoft.Json; // NEW

namespace GungeonRLTracker
{
    [BepInPlugin("com.example.gungeon.rltracker", "Gungeon RL Tracker", "1.0.0")]
    public class GungeonRLTrackerPlugin : BaseUnityPlugin
    {
        private const int DefaultPort = 18475;
        private const float SnapshotIntervalSeconds = 1f / 30f;

        private readonly object _clientLock = new object();

        private TcpListener _listener;
        private Thread _listenerThread;
        private TcpClient _client;
        private StreamWriter _writer;
        private volatile bool _shutdown;
        private float _lastSnapshotTime;
        private int _sequenceId;

        private void Awake()
        {
            Logger.LogInfo("Gungeon RL Tracker initialising");
            TryStartListener(DefaultPort);
        }

        private void Update()
        {
            if (!GameManager.HasInstance || GameManager.Instance.PrimaryPlayer == null)
                return;

            if (Time.realtimeSinceStartup - _lastSnapshotTime < SnapshotIntervalSeconds)
                return;

            _lastSnapshotTime = Time.realtimeSinceStartup;
            TrySendSnapshot();
        }

        private void OnDestroy()
        {
            Logger.LogInfo("Gungeon RL Tracker shutting down");
            _shutdown = true;

            if (_listener != null)
            {
                try { _listener.Stop(); }
                catch (Exception ex) { Logger.LogError($"Failed to stop listener: {ex}"); }
            }

            if (_listenerThread != null && _listenerThread.IsAlive)
                _listenerThread.Join(TimeSpan.FromSeconds(1));

            lock (_clientLock)
            {
                CloseClient();
            }
        }

        private void TryStartListener(int port)
        {
            if (_listener != null) return;

            try
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
                _listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "GungeonRLTrackerListener"
                };
                _listenerThread.Start();
                Logger.LogInfo($"Listening for RL bridge connections on 127.0.0.1:{port}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to start listener on port {port}: {ex.Message}");
                _listener = null;
            }
        }

        private void ListenLoop()
        {
            while (!_shutdown && _listener != null)
            {
                try
                {
                    var client = _listener.AcceptTcpClient();

                    lock (_clientLock)
                    {
                        CloseClient();
                        _client = client;
                        _writer = new StreamWriter(client.GetStream(), Encoding.UTF8)
                        {
                            AutoFlush = true,
                            NewLine = "\n"
                        };
                    }

                    Logger.LogInfo("RL bridge connected");
                    SendHandshake();
                }
                catch (SocketException)
                {
                    if (!_shutdown)
                        Logger.LogWarning("Socket exception while waiting for client; continuing");
                }
                catch (Exception ex)
                {
                    if (!_shutdown)
                        Logger.LogError($"Unexpected error while waiting for client: {ex}");
                }
            }
        }

        private void SendHandshake()
        {
            var handshake = new Handshake
            {
                schema_version = 1,
                plugin_version = "1.0.0",
                game = "Enter the Gungeon"
            };

            SendMessage(handshake);
        }

        private void TrySendSnapshot()
        {
            lock (_clientLock)
            {
                if (_writer == null) return;
            }

            try
            {
                var snapshot = BuildSnapshot();
                if (snapshot != null)
                    SendMessage(snapshot);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to send snapshot: {ex}");
                lock (_clientLock)
                {
                    CloseClient();
                }
            }
        }

        private void SendMessage<T>(T payload)
        {
            string json;
            try
            {
                json = JsonConvert.SerializeObject(
                    payload,
                    new JsonSerializerSettings { NullValueHandling = NullValueHandling.Include }
                );
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to serialise {typeof(T).Name}: {ex.Message}");
                return;
            }

            lock (_clientLock)
            {
                if (_writer == null) return;

                try
                {
                    _writer.WriteLine(json);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to send payload: {ex.Message}");
                    CloseClient();
                }
            }
        }

        private Snapshot BuildSnapshot()
        {
            var gameManager = GameManager.Instance;
            var player = gameManager.PrimaryPlayer;
            if (player == null) return null;

            var snapshot = new Snapshot
            {
                sequence = ++_sequenceId,
                realtime = Time.realtimeSinceStartup,
                // Some ETG builds don't expose a friendly floor name on Dungeon; fall back to GameObject name
                level_name = gameManager.Dungeon != null ? gameManager.Dungeon.gameObject?.name : null,
                player = BuildPlayerState(player)
            };

            snapshot.enemies = BuildEnemyStates(player);
            snapshot.projectiles = BuildProjectileStates();
            snapshot.room = BuildRoomState(player.CurrentRoom, player.specRigidbody.UnitCenter);

            return snapshot;
        }

        private PlayerState BuildPlayerState(PlayerController player)
        {
            var passiveIds = player.passiveItems != null
                ? player.passiveItems.Select(p => p?.PickupObjectId ?? -1).Where(id => id >= 0).ToArray()
                : Array.Empty<int>();

            var activeItem = player.activeItems != null && player.activeItems.Count > 0
                ? player.activeItems[0]
                : null;

            var center = player.specRigidbody != null ? player.specRigidbody.UnitCenter : Vector2.zero;

            return new PlayerState
            {
                position = ToVector(center),
                velocity = ToVector(player.Velocity),
                health = player.healthHaver?.GetCurrentHealth() ?? 0f,
                max_health = player.healthHaver?.GetMaxHealth() ?? 0f,
                armor = player.healthHaver?.Armor ?? 0f,
                blanks = player.Blanks,
                money = player.carriedConsumables.Currency,
                keys = player.carriedConsumables.KeyBullets,
                is_dodge_rolling = player.IsDodgeRolling,
                current_gun_id = player.CurrentGun != null ? player.CurrentGun.PickupObjectId : -1,
                current_gun_ammo = player.CurrentGun != null ? player.CurrentGun.CurrentAmmo : 0,
                active_item_id = activeItem != null ? activeItem.PickupObjectId : -1,
                passive_item_ids = passiveIds
            };
        }

        private EnemyState[] BuildEnemyStates(PlayerController player)
        {
            var center = player.specRigidbody != null ? player.specRigidbody.UnitCenter : Vector2.zero;
            var enemies = StaticReferenceManager.AllEnemies;

            if (enemies == null) return Array.Empty<EnemyState>();

            var result = new List<EnemyState>(enemies.Count);
            foreach (var enemy in enemies)
            {
                if (enemy == null || enemy.healthHaver == null || enemy.healthHaver.IsDead)
                    continue;

                var enemyCenter = enemy.specRigidbody != null ? enemy.specRigidbody.UnitCenter : enemy.CenterPosition;
                result.Add(new EnemyState
                {
                    guid = enemy.EnemyGuid,
                    position = ToVector(enemyCenter),
                    health = enemy.healthHaver.GetCurrentHealth(),
                    max_health = enemy.healthHaver.GetMaxHealth(),
                    is_boss = enemy.healthHaver != null && enemy.healthHaver.IsBoss,
                    distance_to_player = Vector2.Distance(center, enemyCenter)
                });
            }

            return result.ToArray();
        }

        private ProjectileState[] BuildProjectileStates()
        {
            var projectiles = StaticReferenceManager.AllProjectiles;
            if (projectiles == null) return Array.Empty<ProjectileState>();

            var result = new List<ProjectileState>(projectiles.Count);
            foreach (var projectile in projectiles)
            {
                if (projectile == null || projectile.specRigidbody == null)
                    continue;

                var owner = projectile.Owner;
                var ownerAi = owner != null ? owner.aiActor : null;
                var ownerPlayer = owner != null ? owner.GetComponent<PlayerController>() : null;

                result.Add(new ProjectileState
                {
                    position = ToVector(projectile.specRigidbody.UnitCenter),
                    direction = ToVector(projectile.Direction),
                    speed = projectile.Speed,
                    is_enemy = ownerAi != null && ownerPlayer == null
                });
            }

            return result.ToArray();
        }

        private RoomState BuildRoomState(RoomHandler room, Vector2 playerCenter)
        {
            if (room == null || room.area == null) return null;

            var area = room.area;
            var basePosition = new[] { area.basePosition.x, area.basePosition.y };
            var dimensions = new[] { area.dimensions.x, area.dimensions.y };

            var activeEnemyCount = room.GetActiveEnemies(RoomHandler.ActiveEnemyType.RoomClear)?.Count ?? 0;

            bool isBossRoom = area.PrototypeRoomCategory == PrototypeDungeonRoom.RoomCategory.BOSS;

            return new RoomState
            {
                room_name = room.GetRoomName(),
                base_position = basePosition,
                dimensions = dimensions,
                is_boss_room = isBossRoom,
                enemies_remaining = activeEnemyCount,
                player_relative_position = new[]
                {
                    playerCenter.x - area.basePosition.x,
                    playerCenter.y - area.basePosition.y
                }
            };
        }

        private void CloseClient()
        {
            if (_writer != null)
            {
                try { _writer.Dispose(); } catch { }
                _writer = null;
            }

            if (_client != null)
            {
                try { _client.Close(); } catch { }
                _client = null;
            }
        }

        private static float[] ToVector(Vector2 vector) => new[] { vector.x, vector.y };

        [Serializable]
        private class Handshake
        {
            public string message_type = "handshake";
            public int schema_version;
            public string plugin_version;
            public string game;
        }

        [Serializable]
        private class Snapshot
        {
            public string message_type = "snapshot";
            public int sequence;
            public float realtime;
            public string level_name;
            public PlayerState player;
            public EnemyState[] enemies;
            public ProjectileState[] projectiles;
            public RoomState room;
        }

        [Serializable]
        private class PlayerState
        {
            public float[] position;
            public float[] velocity;
            public float health;
            public float max_health;
            public float armor;
            public int blanks;
            public int money;
            public int keys;
            public bool is_dodge_rolling;
            public int current_gun_id;
            public int current_gun_ammo;
            public int active_item_id;
            public int[] passive_item_ids;
        }

        [Serializable]
        private class EnemyState
        {
            public string guid;
            public float[] position;
            public float health;
            public float max_health;
            public bool is_boss;
            public float distance_to_player;
        }

        [Serializable]
        private class ProjectileState
        {
            public float[] position;
            public float[] direction;
            public float speed;
            public bool is_enemy;
        }

        [Serializable]
        private class RoomState
        {
            public string room_name;
            public int[] base_position;
            public int[] dimensions;
            public bool is_boss_room;
            public int enemies_remaining;
            public float[] player_relative_position;
        }
    }
}
