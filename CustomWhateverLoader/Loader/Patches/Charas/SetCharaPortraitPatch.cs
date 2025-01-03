﻿using System.Linq;
using Cwl.API.Custom;
using Cwl.Helper.Unity;
using HarmonyLib;

namespace Cwl.Loader.Patches.Charas;

[HarmonyPatch]
internal class SetCharaPortraitPatch
{
    // calculated from all game npc average
    private const float AverageDistance = 54.5f;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Portrait), nameof(Portrait.SetChara))]
    internal static void OnSetCharaPortrait(Portrait __instance, Chara? c)
    {
        if (c?.IsPC is not false || c.source?.id is null) {
            return;
        }

        if (__instance.imageChara?.sprite == null) {
            return;
        }

        if (!CwlConfig.FixBaseGameAvatar &&
            CustomChara.All.All(adv => adv != c.source.id)) {
            return;
        }

        var sprite = __instance.imageChara.sprite;

        var dist = sprite.NearestPerceivableMulticast(4, 4,
            (int)sprite.rect.width / 2,
            (int)sprite.rect.height,
            directionY: -1);

        var pos = __instance.imageChara.transform.localPosition;
        if (dist > AverageDistance) {
            return;
        }

        __instance.imageChara.transform.localPosition = pos with { y = pos.y - (AverageDistance - dist) / 2 };
    }
}