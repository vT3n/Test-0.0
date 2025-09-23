#if BEPINEX
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace EtG.Plugin
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid    = "com.t3nfive.etg.test0";
        public const string PluginName    = "Test0";
        public const string PluginVersion = "0.1.0";

        // C# 7.3 / .NET 3.5 friendly: no nullable features
        internal static ManualLogSource LogSrc;
        private Harmony _harmony;

        private void Awake()
        {
            LogSrc = Logger;
            LogSrc.LogInfo(PluginName + " " + PluginVersion + " initializing…");

            _harmony = new Harmony(PluginGuid);
            // _harmony.PatchAll(); // keep ready for future patches

            LogSrc.LogInfo(PluginName + " initialized.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F6))
                LogSrc.LogInfo("F6 pressed (debug hook).");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchSelf(); // instead of UnpatchAll(PluginGuid)

            if (LogSrc != null)
                LogSrc.LogInfo(PluginName + " shutdown.");
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
