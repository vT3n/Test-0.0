#if BEPINEX
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
#endif

namespace EtG.Plugin
{
#if BEPINEX
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid    = "com.t3nfive.etg.test0";
        public const string PluginName    = "Test0";
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource LogSrc = null!;
        private Harmony _harmony;

        private void Awake()
        {
            LogSrc = Logger;
            LogSrc.LogInfo($"{PluginName} {PluginVersion} initializing…");

            _harmony = new Harmony(PluginGuid);
            // _harmony.PatchAll(); // keep ready for future patches

            LogSrc.LogInfo($"{PluginName} initialized.");
        }

        private void Update()
        {
            // Per-frame logic here (avoid heavy work).
            // Example debug key:
            if (Input.GetKeyDown(KeyCode.F6))
                LogSrc.LogInfo("F6 pressed (debug hook).");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchAll(PluginGuid);
            LogSrc.LogInfo($"{PluginName} shutdown.");
        }
    }
#else
    // net9.0 build: keep something to compile against
    // You can add modern helpers, records, Span<> utils, etc. here.
    public static class DevHelpers
    {
        public static string Hello() => "Dev (net9.0) utilities loaded.";
    }
#endif
}
