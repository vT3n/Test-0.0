#if BEPINEX
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using System;
using System.Reflection;
using UnityEngine.SceneManagement;

namespace EtG.Plugin
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid    = "com.t3nfive.etg.test0";
        public const string PluginName    = "Test0";
        public const string PluginVersion = "0.1.0";
        private string _markLabel = "manual";
        private string _cachedLevelMember = null; // e.g., "DungeonFloorName" / "DungeonShortName" / "PrefabPath" / "go.name" / "scene"
        private string _lastStableLevelName = "Unknown"; // last non-loading, non-empty name we saw



        internal static ManualLogSource LogSrc;
        private Harmony _harmony;
        private StateEmitter _emit;
        private int _seq = 0;
        private float _accum = 0f;

        private Component _player; // cached PlayerController instance (if found)
        private Type _playerType;

        private void Awake()
        {
            LogSrc = Logger;
            LogSrc.LogInfo(PluginName + " " + PluginVersion + " initializing…");

            _harmony = new Harmony(PluginGuid);
            // _harmony.PatchAll();

            // Output directory sits next to this plugin DLL. To also POST to localhost,
            // pass "http://127.0.0.1:5055/ingest" as the 2nd argument.
            var pluginDir = System.IO.Path.GetDirectoryName(Info.Location);
            _emit = new StateEmitter(pluginDir, "http://127.0.0.1:5055/ingest/");
            _emit.Start();

            _playerType = TryGetType("PlayerController");

            LogSrc.LogInfo(PluginName + " initialized.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F6))
                LogSrc.LogInfo("F6 pressed (debug hook).");

            if (Input.GetKeyDown(KeyCode.F7))
            {
                var m = new Marker
                {
                    sequence = _seq,
                    realtime = Time.realtimeSinceStartup,
                    label = _markLabel
                };
                _emit.Enqueue(m);
                LogSrc.LogInfo("Marked: " + _markLabel + " at seq " + _seq);
            }

            // Emit ~5 times per second
            const float interval = 0.2f;
            _accum += Time.deltaTime;
            if (_accum < interval) return;
            _accum = 0f;

            CaptureTick();
        }

        private void CaptureTick()
        {
            // Find PlayerController once and cache it
            if (_player == null && _playerType != null)
            {
                try
                {
                    var arr = UnityEngine.Object.FindObjectsOfType(_playerType);
                    if (arr != null && arr.Length > 0)
                    {
                        _player = (Component)arr[0];
                        LogSrc.LogInfo("PlayerController found and cached.");
                    }
                }
                catch { }
            }

            Vector3 pos = Vector3.zero;
            Vector2 vel = Vector2.zero;
            float hp = -1f, maxHp = -1f;
            string levelName = TryGetLevelName();

            if (_player != null)
            {
                // Position
                try { pos = _player.transform.position; } catch { }

                // Velocity via specRigidbody.{Velocity|velocity}
                try
                {
                    var spec = GetFieldOrProp(_player, "specRigidbody");
                    if (spec != null)
                    {
                        object vObj = GetFieldOrProp(spec, "Velocity"); // property
                        if (vObj == null) vObj = GetFieldOrProp(spec, "velocity"); // field
                        if (vObj is Vector2) vel = (Vector2)vObj;
                    }
                }
                catch { }

                // Health via healthHaver (methods or backing fields)
                try
                {
                    var hh = GetFieldOrProp(_player, "healthHaver");
                    if (hh != null)
                    {
                        var t = hh.GetType();
                        var mGet = t.GetMethod("GetCurrentHealth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (mGet != null) hp = Convert.ToSingle(mGet.Invoke(hh, null));
                        var mMax = t.GetMethod("GetMaxHealth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (mMax != null) maxHp = Convert.ToSingle(mMax.Invoke(hh, null));

                        if (hp < 0f)
                        {
                            var f = t.GetField("m_currentHealth", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            if (f != null) hp = Convert.ToSingle(f.GetValue(hh));
                        }
                        if (maxHp < 0f)
                        {
                            var f = t.GetField("m_maxHealth", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            if (f != null) maxHp = Convert.ToSingle(f.GetValue(hh));
                        }
                    }
                }
                catch { }
            }

            var tick = new PlayerTick
            {
                sequence = ++_seq,
                realtime = Time.realtimeSinceStartup,
                level_name = levelName,
                px = pos.x, py = pos.y,
                vx = vel.x, vy = vel.y,
                health = hp, max_health = maxHp
            };

            _emit.Enqueue(tick);
        }

        private static Type TryGetType(string name)
        {
            try { return AccessTools.TypeByName(name); } catch { return null; }
        }

        private static object GetFieldOrProp(object obj, string name)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) return p.GetValue(obj, null);
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return f.GetValue(obj);
            return null;
        }

        


        private string TryGetLevelName()
        {
            try
            {
                // Get GameManager.Instance
                var gmType = AccessTools.TypeByName("GameManager");
                object gm = null;
                if (gmType != null)
                {
                    var instProp = gmType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (instProp != null) gm = instProp.GetValue(null, null);
                    if (gm == null)
                    {
                        var instField = gmType.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        if (instField != null) gm = instField.GetValue(null);
                    }
                }

                // Try to get Dungeon object
                object dungeon = null;
                if (gm != null)
                {
                    dungeon = GetFieldOrProp(gm, "Dungeon");
                    if (dungeon == null) dungeon = GetFieldOrProp(gm, "m_Dungeon");
                    if (dungeon == null) dungeon = GetFieldOrProp(gm, "dungeon");
                }

                // If we already found which member works, use it first
                if (_cachedLevelMember != null)
                {
                    string name = GetLevelNameByMember(dungeon, _cachedLevelMember);
                    name = MapDungeonIdToFriendly(name);
                    if (!IsLoadingish(name))
                    {
                        if (name != _lastStableLevelName)
                            LogSrc.LogInfo("Level detected: " + name);
                        _lastStableLevelName = name;
                        return name;
                    }
                }

                // Try known members in order of niceness
                string[] candidates = new string[] {
                    "DungeonFloorName",  // friendly
                    "DungeonShortName",  // short friendly
                    "PrefabPath",        // internal id (tt_castle, tt_gungeon, base_camp, etc.)
                    "dungeonSceneName",  // sometimes present
                    "Name", "_name"      // generic
                };

                for (int i = 0; i < candidates.Length; i++)
                {
                    string val = TryStringMember(dungeon, candidates[i]);
                    val = MapDungeonIdToFriendly(val);
                    if (!IsLoadingish(val))
                    {
                        _cachedLevelMember = candidates[i];
                        if (val != _lastStableLevelName)
                            LogSrc.LogInfo("Level detected: " + val);
                        _lastStableLevelName = val;
                        return val;
                    }
                }

                // Try the Dungeon GameObject name
                string goName = MapDungeonIdToFriendly(TryDungeonGameObjectName(dungeon));
                if (!IsLoadingish(goName))
                {
                    _cachedLevelMember = "go.name";
                    if (goName != _lastStableLevelName)
                        LogSrc.LogInfo("Level detected: " + goName);
                    _lastStableLevelName = goName;
                    return goName;
                }

                // Fallback: active scene name
                string scene = MapDungeonIdToFriendly(SceneManager.GetActiveScene().name);
                if (!IsLoadingish(scene))
                {
                    _cachedLevelMember = "scene";
                    if (scene != _lastStableLevelName)
                        LogSrc.LogInfo("Level detected: " + scene);
                    _lastStableLevelName = scene;
                    return scene;
                }

                // If everything looks loading-ish, return last stable
                return _lastStableLevelName;
            }
            catch
            {
                return _lastStableLevelName;
            }
        }

        private static bool IsLoadingish(string s)
        {
            if (string.IsNullOrEmpty(s)) return true;
            string x = s.ToLowerInvariant();
            // Common EtG “loading” identifiers / scenes
            if (x.Contains("loading")) return true;          // "Loading Dungeon", "LoadingScreen", etc.
            if (x == "unknown") return true;
            return false;
        }

        private string GetLevelNameByMember(object dungeon, string member)
        {
            if (member == "scene")
                return SceneManager.GetActiveScene().name;
            if (member == "go.name")
                return TryDungeonGameObjectName(dungeon);
            return TryStringMember(dungeon, member);
        }

        private static string TryStringMember(object obj, string name)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.PropertyType == typeof(string))
                return p.GetValue(obj, null) as string;
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null && f.FieldType == typeof(string))
                return f.GetValue(obj) as string;
            return null;
        }

        private static string TryDungeonGameObjectName(object dungeon)
        {
            if (dungeon == null) return null;
            try
            {
                var t = dungeon.GetType();
                var pGo = t.GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pGo != null)
                {
                    var go = pGo.GetValue(dungeon, null) as UnityEngine.GameObject;
                    if (go != null) return go.name;
                }
            }
            catch { }
            return null;
        }

        // Map internal ids to friendly floor names (extend as you discover more)
        private static string MapDungeonIdToFriendly(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            string s = raw.ToLowerInvariant();

            // Hub
            if (s.Contains("breach") || s.Contains("base_camp")) return "Breach";

            // Main floors
            if (s.Contains("tt_castle") || s.Contains("keep"))     return "Keep of the Lead Lord";   // Floor 1
            if (s.Contains("tt_gungeon") || s.Contains("proper"))  return "Gungeon Proper";          // Floor 2
            if (s.Contains("tt_mines") || s.Contains("mines"))     return "Black Powder Mine";       // Floor 3
            if (s.Contains("tt_catacombs") || s.Contains("hollow"))return "Hollow";                  // Floor 4
            if (s.Contains("tt_forge") || s.Contains("forge"))     return "Forge";                   // Floor 5

            // Extras
            if (s.Contains("bullethell") || s.Contains("tt_bullethell")) return "Bullet Hell";
            if (s.Contains("tt_nakatomi") || s.Contains("tt_robot") || s.Contains("rat")) return "Resourceful Rat Lair";
            if (s.Contains("tt_tutorial") || s.Contains("tutorial")) return "Tutorial";

            // If it already looks nice, keep it
            return raw;
        }

        

        private void OnDestroy()
        {
            if (_harmony != null) _harmony.UnpatchSelf();
            if (_emit != null) _emit.Dispose();
            if (LogSrc != null) LogSrc.LogInfo(PluginName + " shutdown.");
        }
    }
}
#else
namespace EtG.Plugin
{
    public static class DevHelpers
    {
        public static string Hello()
        {
            return "Dev (net9.0) utilities loaded.";
        }
    }
}
#endif
