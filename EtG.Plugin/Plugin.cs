#if BEPINEX
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Reflection;

namespace EtG.Plugin
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid    = "com.t3nfive.etg.test0";
        public const string PluginName    = "Test0";
        public const string PluginVersion = "0.1.0";

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
            LogSrc.LogInfo(PluginName + " " + PluginVersion + " initializing...");

            _harmony = new Harmony(PluginGuid);
            // _harmony.PatchAll();

            // File output; to also POST live, pass "http://127.0.0.1:5055/ingest/"
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
                    UnityEngine.Object[] arr = UnityEngine.Object.FindObjectsOfType(_playerType);
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

            // default looking angle
            float lookDeg = -1f;

            if (_player != null)
            {
                // Position
                try { pos = _player.transform.position; } catch { }

                // Velocity via specRigidbody.{Velocity|velocity}
                try
                {
                    object spec = GetFieldOrProp(_player, "specRigidbody");
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
                    object hh = GetFieldOrProp(_player, "healthHaver");
                    if (hh != null)
                    {
                        Type t = hh.GetType();
                        MethodInfo mGet = t.GetMethod("GetCurrentHealth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (mGet != null) hp = Convert.ToSingle(mGet.Invoke(hh, null));
                        MethodInfo mMax = t.GetMethod("GetMaxHealth", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (mMax != null) maxHp = Convert.ToSingle(mMax.Invoke(hh, null));

                        if (hp < 0f)
                        {
                            FieldInfo f = t.GetField("m_currentHealth", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            if (f != null) hp = Convert.ToSingle(f.GetValue(hh));
                        }
                        if (maxHp < 0f)
                        {
                            FieldInfo f = t.GetField("m_maxHealth", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                            if (f != null) maxHp = Convert.ToSingle(f.GetValue(hh));
                        }
                    }
                }
                catch { }

                // Looking angle via CurrentGun.CurrentAngle (degrees)
                try
                {
                    object gun = GetFieldOrProp(_player, "CurrentGun");
                    if (gun == null) gun = GetFieldOrProp(_player, "currentGun"); // field fallback

                    if (gun != null)
                    {
                        object angObj = GetFieldOrProp(gun, "CurrentAngle"); // property
                        if (angObj is float raw)
                        {
                            raw %= 360f;
                            if (raw < 0f) raw += 360f;
                            lookDeg = raw; // [0,360)
                        }
                    }
                }
                catch { }
            }

            // --- Consumables ---
            int blanks = -1, money = -1, keys = -1;

            if (_player != null)
            {
                try
                {
                    // 1) Blanks directly on PlayerController
                    if (TryIntFromMembers(_player, out var b, "Blanks", "blanks", "m_currentBlanks"))
                        blanks = b;

                    // 2) Money / Keys – try direct first
                    bool moneyDone = false, keysDone = false;
                    if (TryIntFromMembers(_player, out var m0, "Currency", "currency", "Money", "money"))
                    { money = m0; moneyDone = true; }
                    if (TryIntFromMembers(_player, out var k0, "KeyBullets", "keys", "Keys", "keyBullets"))
                    { keys = k0; keysDone = true; }

                    // 3) Fallback: carriedConsumables container
                    object cc = GetFieldOrProp(_player, "carriedConsumables");
                    if (cc == null) cc = GetFieldOrProp(_player, "CarriedConsumables");

                    if (cc != null)
                    {
                        if (!moneyDone && TryIntFromMembers(cc, out var m1, "Currency", "currency", "Money", "money"))
                            money = m1;

                        if (!keysDone && TryIntFromMembers(cc, out var k1, "KeyBullets", "keyBullets", "Keys", "keys"))
                            keys = k1;

                        // Some builds expose nested fields inside carriedConsumables; try common backing names too:
                        if (!moneyDone && money < 0 && TryIntFromMembers(cc, out var m2, "m_currency", "m_Money"))
                            money = m2;

                        if (!keysDone && keys < 0 && TryIntFromMembers(cc, out var k2, "m_keyBullets", "m_keys"))
                            keys = k2;
                    }
                }
                catch { /* swallow; leave as -1 */ }
            }




            string levelName = GetLevelNameSimple(); // maps to friendly proper names

            var tick = new PlayerTick
            {
                sequence = ++_seq,
                realtime = Time.realtimeSinceStartup,
                level_name = levelName,
                px = pos.x, py = pos.y,
                vx = vel.x, vy = vel.y,
                health = hp, max_health = maxHp,
                looking_angle = lookDeg,
                blanks = blanks,
                money = money,
                keys  = keys
            };

            _emit.Enqueue(tick);
        }

        // --- Simplified level name: map engine keys/ids/prefabs to proper names ---
        private string GetLevelNameSimple()
        {
            try
            {
                // GameManager.Instance
                Type gmType = TryGetType("GameManager");
                object gm = null;
                if (gmType != null)
                {
                    PropertyInfo instProp = gmType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (instProp != null) gm = instProp.GetValue(null, null);
                    if (gm == null)
                    {
                        FieldInfo instField = gmType.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        if (instField != null) gm = instField.GetValue(null);
                    }
                }

                // Dungeon object
                object dungeon = null;
                if (gm != null)
                {
                    dungeon = GetFieldOrProp(gm, "Dungeon");
                    if (dungeon == null) dungeon = GetFieldOrProp(gm, "m_Dungeon");
                    if (dungeon == null) dungeon = GetFieldOrProp(gm, "dungeon");
                }

                // Try members in order
                string[] members = new string[] {
                    "DungeonFloorName",   // often a localization key like #CASTLE_NAME
                    "DungeonShortName",
                    "PrefabPath",         // e.g., Base_Castle, tt_castle, etc.
                    "dungeonSceneName",
                    "Name", "_name"
                };

                for (int i = 0; i < members.Length; i++)
                {
                    string val = TryStringMember(dungeon, members[i]);
                    if (!string.IsNullOrEmpty(val)) return CanonicalLevelName(val);
                }

                // Try Dungeon's GameObject name
                string goName = TryDungeonGameObjectName(dungeon);
                if (!string.IsNullOrEmpty(goName)) return CanonicalLevelName(goName);

                // Fallback: active scene name
                string scene = SceneManager.GetActiveScene().name;
                if (!string.IsNullOrEmpty(scene)) return CanonicalLevelName(scene);

                return "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// Return the proper floor name, mapping:
        /// - Localization keys (#FOO_NAME / #FOO_SHORTNAME),
        /// - IDs (tt_*, fs_*, ss_*),
        /// - Prefab paths (Base_*, FinalScenario_*),
        /// to one of the canonical strings below.
        /// </summary>
        private static string CanonicalLevelName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "Unknown";
            string k = NormalizeKey(raw);

            // --- Hub / tutorial ---
            if (k == "#foyer_name" || k == "tt_foyer" || k == "base_foyer") return "Breach";
            if (k == "#tutorial_name" || k == "tt_tutorial" || k == "base_tutorial") return "Halls of Knowledge";

            // --- Main floors ---
            if (k == "#castle_name" || k == "tt_castle" || k == "base_castle") return "Keep of the Lead Lord"; // F1
            if (k == "#gungeon_name" || k == "tt5"        || k == "base_gungeon") return "Gungeon Proper";      // F2
            if (k == "#mines_name"   || k == "tt_mines"   || k == "base_mines")   return "Black Powder Mine";   // F3
            if (k == "#catacombs_name" || k == "tt_catacombs" || k == "base_catacombs") return "Hollow";       // F4
            if (k == "#forge_name"     || k == "tt_forge"     || k == "base_forge")     return "Forge";        // F5
            if (k == "#bullethell_shortname" || k == "#bullethell_name" || k == "tt_bullethell" || k == "base_bullethell")
                return "Bullet Hell";

            // --- Secret / special ---
            if (k == "#sewers_shortname" || k == "tt_sewer" || k == "base_sewer") return "Oubliette";
            if (k == "#abbey_name" || k == "tt_cathedral" || k == "base_cathedral") return "Abbey of the True Gun";
            if (k == "#resourceful_rat_lair_shortname" || k == "ss_resourcefulrat" || k == "base_resourcefulrat")
                return "Resourceful Rat Lair";
            if (k == "#nakatomi_shortname_v1" || k == "tt_nakatomi" || k == "base_nakatomi") return "R&G Dept.";
            if (k == "tt_belly" || k == "base_belly") return "Belly of the Beast";
            if (k == "tt_jungle" || k == "base_jungle") return "Jungle";
            if (k == "tt_future" || k == "base_future") return "The Future";
            if (k == "tt_phobos" || k == "base_phobos") return "Phobos";
            if (k == "tt_west"   || k == "base_west")   return "The West";

            // --- Bro What ---

            // --- The Past ---
            if (k == "fs_pilot"   || k == "finalscenario_pilot")   return "The Past - The Pilot";
            if (k == "fs_convict" || k == "finalscenario_convict") return "The Past - The Convict";
            if (k == "fs_soldier" || k == "finalscenario_soldier") return "The Past - The Marine";
            if (k == "fs_guide"   || k == "finalscenario_guide")   return "The Past - The Hunter";
            if (k == "fs_coop"    || k == "finalscenario_coop")    return "The Past - Co-op";
            if (k == "fs_robot"   || k == "finalscenario_robot")   return "The Past - The Robot";
            if (k == "#bulletpast_name" || k == "fs_bullet" || k == "finalscenario_bullet")
                return "The Past - The Bullet";

            // --- Loading screens ---
            if (k == "loadingdungeon" || k == "loadingscreen") return "Loading";

            // Last tidy fallback: strip known prefixes to make it readable
            if (k.StartsWith("base_"))          return CleanToken(raw.Substring(5));
            if (k.StartsWith("finalscenario_")) return "The Past - " + CleanToken(raw.Substring(14));
            if (k.StartsWith("tt_"))            return CleanToken(raw.Substring(3));
            if (k.StartsWith("fs_") || k.StartsWith("ss_")) return CleanToken(raw);

            // Otherwise, keep what we got
            return raw;
        }

        private static string NormalizeKey(string s)
        {
            string x = s.Trim();

            // Strip angle-bracket color tags if they ever leak through
            if (x.Length > 0 && x[0] == '<')
            {
                string lower = x.ToLowerInvariant();
                if (lower.StartsWith("<color"))
                {
                    int end = lower.IndexOf('>');
                    if (end >= 0 && end + 1 < x.Length) x = x.Substring(end + 1);
                    x = x.Replace("</color>", "");
                    x = x.Trim();
                }
            }

            return x.ToLowerInvariant();
        }

        private static string CleanToken(string token)
        {
            return token.Replace('_', ' ');
        }

        // Helpers
        private static Type TryGetType(string name)
        {
            try { return AccessTools.TypeByName(name); } catch { return null; }
        }

        private static object GetFieldOrProp(object obj, string name)
        {
            if (obj == null) return null;
            Type t = obj.GetType();
            PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) return p.GetValue(obj, null);
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return f.GetValue(obj);
            return null;
        }

        private static bool TryIntFromMembers(object obj, out int value, params string[] names)
        {
            value = 0;
            if (obj == null || names == null) return false;

            foreach (string n in names)
            {
                object v = GetFieldOrProp(obj, n);
                if (v == null) continue;

                // Handle common numeric types we might see via reflection
                try
                {
                    // Many EtG counters are int, but sometimes float/double
                    if (v is int)        { value = (int)v; return true; }
                    if (v is float)      { value = (int)(float)v; return true; }
                    if (v is double)     { value = (int)(double)v; return true; }
                    if (v is long)       { value = (int)(long)v; return true; }
                    if (v is short)      { value = (int)(short)v; return true; }
                    if (v is byte)       { value = (int)(byte)v; return true; }

                    // Strings occasionally show up; try parse
                    if (v is string)
                    {
                        int parsed;
                        if (int.TryParse((string)v, out parsed)) { value = parsed; return true; }
                        // Some counters can be serialized as floats in strings
                        float pf;
                        if (float.TryParse((string)v, out pf)) { value = (int)pf; return true; }
                    }

                    // Last-ditch: Convert handles many boxed primitives
                    value = Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    // ignore and try the next name
                }
            }
            return false;
        }
        private static string TryStringMember(object obj, string name)
        {
            if (obj == null) return null;
            Type t = obj.GetType();
            PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.PropertyType == typeof(string))
            {
                object v = p.GetValue(obj, null);
                return v as string;
            }
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null && f.FieldType == typeof(string))
            {
                object v = f.GetValue(obj);
                return v as string;
            }
            return null;
        }

        private static string TryDungeonGameObjectName(object dungeon)
        {
            if (dungeon == null) return null;
            try
            {
                Type t = dungeon.GetType();
                PropertyInfo pGo = t.GetProperty("gameObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pGo != null)
                {
                    GameObject go = pGo.GetValue(dungeon, null) as GameObject;
                    if (go != null) return go.name;
                }
            }
            catch { }
            return null;
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
