using AutoSLCut.Timeline;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace AutoSLCut.Patches;

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRun))]
internal static class SaveManagerSaveRunPatch
{
    private static void Prefix()
    {
        SLCutTimelineTracker.RecordSave("SaveManager.SaveRun");
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedSinglePlayer))]
internal static class RunManagerSetUpSavedSinglePlayerPatch
{
    private static void Prefix()
    {
        SLCutTimelineTracker.RecordLoad("RunManager.SetUpSavedSinglePlayer");
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedMultiPlayer))]
internal static class RunManagerSetUpSavedMultiPlayerPatch
{
    private static void Prefix()
    {
        SLCutTimelineTracker.RecordLoad("RunManager.SetUpSavedMultiPlayer");
    }
}

[HarmonyPatch(typeof(NGame), nameof(NGame.LoadRun))]
internal static class NGameLoadRunFallbackPatch
{
    private static void Prefix()
    {
        // Fallback for future fast-SL mods that bypass SetUpSaved*.
        SLCutTimelineTracker.RecordLoad("NGame.LoadRun", isFallback: true);
    }
}
