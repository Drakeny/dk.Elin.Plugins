﻿using Cwl.API;
using Cwl.Helper.Unity;
using HarmonyLib;

namespace Cwl.Loader.Patches;

[HarmonyPatch]
internal class SafeSceneInitPatch
{
    internal static bool SafeToCreate;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Scene), nameof(Scene.Init))]
    internal static void OnSceneInit(Scene.Mode newMode)
    {
        if (newMode != Scene.Mode.StartGame) {
            return;
        }

        SafeToCreate = true;

        CoroutineHelper.Immediate(CustomAdventurer.AddDelayedChara());
        CoroutineHelper.Immediate(CustomElement.GainAbilityOnLoad());

        CoroutineHelper.Deferred(
            () => SafeToCreate = false,
            () => EMono.core?.game is null);
    }
}