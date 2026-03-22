using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using aibot.Scripts.Core;

namespace aibot.Scripts.Harmony;

[HarmonyPatch]
public static class AiBotPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(NGame), nameof(NGame.StartNewSingleplayerRun))]
    public static void StartNewSingleplayerRun_Postfix(Task<RunState> __result)
    {
        AiBotRuntime.Instance.NotifyNewRunTask(__result);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NGame), nameof(NGame.StartNewMultiplayerRun))]
    public static void StartNewMultiplayerRun_Postfix(Task<RunState> __result)
    {
        AiBotRuntime.Instance.NotifyNewRunTask(__result);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NGame), nameof(NGame.LoadRun))]
    public static void LoadRun_Postfix(Task __result)
    {
        AiBotRuntime.Instance.NotifyLoadRunTask(__result);
    }
}
