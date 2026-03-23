using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace AutoSLCut;

[ModInitializer(nameof(Initialize))]
public static class AutoSLCutInitializer
{
    private const string HarmonyId = "sts2.u1x.autoslcut";

    private static bool _isInitialized;

    public static void Initialize()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

        Harmony harmony = new Harmony(HarmonyId);
        harmony.PatchAll(Assembly.GetExecutingAssembly());

        Obs.ObsWebSocketRecordingWatcher.Start();
        Log.Info("[AutoSLCut] Initialized");
    }
}
