﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using Cwl.API;
using Cwl.LangMod;
using HarmonyLib;
using MethodTimer;
using NPOI.SS.UserModel;

namespace Cwl.Loader.Patches.Sources;

[HarmonyPatch]
internal class NamedImportPatch
{
    private static readonly Dictionary<Type, List<MigrateDetail.HeaderCell>> _expected = [];
    private static readonly Dictionary<ISheet, List<MigrateDetail.HeaderCell>> _cached = [];

    internal static bool Prepare()
    {
        return CwlConfig.NamedImport;
    }

    [HarmonyTargetMethods]
    internal static IEnumerable<MethodInfo> SourcesCreateRow()
    {
        return typeof(SourceManager)
            .GetFields(AccessTools.all)
            .Where(f => typeof(SourceData).IsAssignableFrom(f.FieldType))
            .Select(f => f.FieldType)
            .Where(s => AccessTools.GetMethodNames(s).Any(mi => mi.Contains("CreateRow")))
            .Select(s => AccessTools.Method(s, "CreateRow"));
    }

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> OnCreateSourceRowIl(IEnumerable<CodeInstruction> instructions,
        MethodBase rowCreator)
    {
        return new CodeMatcher(instructions)
            .MatchStartForward(
                new CodeMatch(o => o.opcode.ToString().Contains("ldc")),
                new CodeMatch(o => o.opcode == OpCodes.Call &&
                                   o.operand is MethodInfo mi &&
                                   mi.DeclaringType == typeof(SourceData)))
            .Repeat(cm => {
                var extraParse = false;
                // Core.ParseElements
                if ((MethodInfo)cm.InstructionAt(1).operand == AccessTools.Method(
                        typeof(SourceData),
                        nameof(SourceData.GetStr))) {
                    cm.RemoveInstruction();
                    cm.Advance(-1);
                    extraParse = true;
                }

                var ldc = cm.Instruction;
                cm.RemoveInstruction();

                var parser = cm.Instruction.operand as MethodInfo;
                cm.RemoveInstruction();
                if (extraParse) {
                    cm.RemoveInstruction();
                }

                var field = cm.Instruction.operand as FieldInfo;

                var id = ldc.opcode.Value switch {
                    >= 0x16 and <= 0x1E => ldc.opcode.Value - 0x16,
                    0x1F => (sbyte)ldc.operand,
                    _ => throw new SourceParseException($"invalid ID for {parser!.Name}:{field!.Name}"),
                };

                var rowType = rowCreator.DeclaringType!;

                cm.SetInstructionAndAdvance(Transpilers.EmitDelegate<Action<object>>(
                    row => RelaxedImport(row, id, field!, parser!, rowType, extraParse)));

                _expected.TryAdd(rowType, []);
                if (_expected[rowType].All(c => c.Index != id)) {
                    _expected[rowType].Add(new(id, field!.Name));
                }
            })
            .InstructionEnumeration();
    }

    [Time]
    private static void RelaxedImport(object row, int id, FieldInfo field, MethodInfo parser, Type rowCreator,
        bool extraParse)
    {
        if (!SourceInitPatch.SafeToCreate) {
            var parsed = extraParse
                ? Core.ParseElements((string)parser.Invoke(null, [id, false]))
                : parser.Invoke(null, [id]);
            field.SetValue(row, parsed);
            return;
        }

        var sheet = SourceData.row.Sheet;
        var migrate = MigrateDetail.GetOrAdd(sheet.Workbook);
        var expected = _expected[rowCreator];

        try {
            if (!_cached.TryGetValue(sheet, out var header)) {
                header = sheet.GetRow(sheet.FirstRowNum).Cells
                    .Where(c => !c.StringCellValue.IsNullOrWhiteSpace())
                    .Select(c => new MigrateDetail.HeaderCell(c.ColumnIndex, c.StringCellValue.Trim()))
                    .ToList();

                _cached[sheet] = header;
                migrate.StartNewSheet(sheet, expected);
            }

            var headerSet = new HashSet<MigrateDetail.HeaderCell>(header);
            var strategy = expected.All(headerSet.Contains)
                ? MigrateDetail.Strategy.Correct
                : MigrateDetail.Strategy.Missing;

            var readPos = id;
            if (strategy == MigrateDetail.Strategy.Missing) {
                var expectedFlat = expected.GroupBy(c => c.Name).ToDictionary(c => c.Key, c => c.Count());
                var headerFlat = header.GroupBy(c => c.Name).ToDictionary(c => c.Key, c => c.Count());

                if (header.Count == expected.Count &&
                    expectedFlat.All(c => headerFlat.TryGetValue(c.Key, out var count) && count == c.Value)) {
                    strategy = MigrateDetail.Strategy.Reorder;

                    var matched = header.FindAll(c => c.Name == field.Name);
                    if (matched.Count != 0 && matched.All(c => c.Index != id)) {
                        readPos = matched[0].Index;
                    }
                }

                migrate.SetStrategy(strategy).SetGiven(header);
            }

            var parsed = extraParse
                ? Core.ParseElements((string)parser.Invoke(null, [readPos, false]))
                : parser.Invoke(null, [readPos]);
            field.SetValue(row, parsed);

            var parseDetail = readPos == id ? "cwl_import_parse" : "cwl_import_reloc";
            CwlMod.Debug($"{parseDetail.Loc(id, readPos)}:{field.Name}:{parser.Name}");
        } finally {
            if (id == expected.Count - 1) {
                migrate.FinalizeMigration();
            }
        }
    }
}