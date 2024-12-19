﻿using System;
using System.Linq;
using System.Text.RegularExpressions;
using Cwl.Helper.Unity;
using Cwl.LangMod;
using Cwl.Loader.Patches.Sources;
using HarmonyLib;
using MethodTimer;
using UnityEngine;

namespace Cwl.Loader.Patches.Materials;

[HarmonyPatch]
internal class SetMaterialRowPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SourceMaterial), nameof(SourceMaterial.SetRow))]
    internal static void OnSetRow(SourceMaterial.Row r)
    {
        if (!SourceInitPatch.SafeToCreate) {
            return;
        }

        var matColors = Core.Instance.Colors.matColors;
        if (matColors.ContainsKey(r.alias)) {
            return;
        }

        AddNewMaterial(r);
    }

    [Time]
    private static void AddNewMaterial(SourceMaterial.Row r)
    {
        var matColors = Core.Instance.Colors.matColors;
        Color main = default;
        Color alt = default;

        var tags = r.tag
            .Where(t => t.StartsWith("addCol"))
            .Select(t => t[6..])
            .ToArray();
        try {
            foreach (var tag in tags) {
                if (tag.StartsWith("_Main")) {
                    main = Regex.Replace(tag, @"_Main|\(|\)", "").ParseColorHex();
                } else if (tag.StartsWith("_Alt")) {
                    alt = Regex.Replace(tag, @"_Alt|\(|\)", "").ParseColorHex();
                }
            }
        } catch (Exception ex) {
            CwlMod.Warn("cwl_warn_mat_color".Loc(r.alias, tags.Join(), ex));
            return;
        }

        matColors[r.alias] = new() {
            main = main,
            alt = alt,
        };
        CwlMod.Log("cwl_log_mat_color".Loc(r.alias, main, alt));
    }
}