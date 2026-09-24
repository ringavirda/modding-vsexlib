#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>Every block that reserves filler cells or carries construction stages breaks as
/// <see cref="StructureBreaks"/> requires; a break that refunds paid stages does not also drop the
/// block's own code, and no break drops a structure filler.</summary>
public static class BreakLaw
{
  internal const string Name = "break";

  /// <summary>Runs <see cref="StructureBreaks.Run(IEnumerable{ExBlockDef}, IReadOnlyList{Assembly}, Action{TestWorld}?)"/>
  /// over <paramref name="defs"/> and judges what each break spawned.</summary>
  /// <param name="defs">The definitions judged.</param>
  /// <param name="assemblies">The assemblies whose registered classes the definitions name,
  /// exlib's own included.</param>
  /// <param name="prepare">Runs on each fresh world before the structure is placed.</param>
  /// <returns>The law's blocktypes, breaks and findings: every <see cref="StructureBreaks"/>
  /// failure, then one line per variant and rule broken, keyed by the variant's code.</returns>
  public static BlockLaws.Law Run(
    IEnumerable<ExBlockDef> defs,
    IReadOnlyList<Assembly> assemblies,
    Action<TestWorld>? prepare = null
  )
  {
    ExBlockDef[] all = [.. defs];
    var typeOf = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (ExBlockDef def in all)
    foreach (DefinitionCodes.Registered variant in DefinitionCodes.Expand(def))
      typeOf[variant.Code] = $"{def.Domain}:{def.Code}";

    StructureBreaks.Result result = StructureBreaks.Run(
      all,
      assemblies,
      prepare
    );
    var findings = new List<string>(result.Failures);
    foreach (
      IGrouping<string, StructureBreaks.Spawn> broken in result.Spawned.GroupBy(
        s => s.Code
      )
    )
    {
      string? type = typeOf.GetValueOrDefault(broken.Key);
      StructureBreaks.Spawn? itself = broken.FirstOrDefault(s =>
        s.Stage >= 1
        && s.Stacks.Any(stack =>
          stack.Collectible?.Code?.ToString() is { } code
          && (code == broken.Key || typeOf.GetValueOrDefault(code) == type)
        )
      );
      if (itself != null)
        findings.Add(
          $"{broken.Key} broken at stage {itself.Stage} refunds its paid stages and also drops "
            + "its own code"
        );
      StructureBreaks.Spawn? filler = broken.FirstOrDefault(s =>
        s.Stacks.Any(stack => stack.Collectible is BlockStructureFiller)
      );
      if (filler != null)
        findings.Add(
          $"{broken.Key} broken from {(filler.Cell < 0 ? "the principal" : $"filler cell {filler.Cell}")} "
            + "drops a structure filler"
        );
    }
    return new BlockLaws.Law(Name, result.Blocks, result.Breaks, findings);
  }
}
#endif
