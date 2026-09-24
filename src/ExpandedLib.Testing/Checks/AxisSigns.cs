using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Testing;

/// <summary>
/// Guards the spin sense of mechanical-power behaviours: <c>AxisSign</c> belongs to the axle's
/// axis, not to the facing, so opposite facings on one axis share one sign. A signed normal flips
/// half the orientations and the axle runs backwards.
/// </summary>
public static class AxisSigns {
  private static readonly BlockFacing[] Facings =
  [
    BlockFacing.NORTH,
    BlockFacing.EAST,
    BlockFacing.SOUTH,
    BlockFacing.WEST,
  ];

  /// <summary>What one run placed and found.</summary>
  /// <param name="Placements">One line per type and facing,
  /// <c>{type} placed {facing}: discovery {face}, AxisSign [x,y,z]</c>.</param>
  /// <param name="Findings">One line per fault; empty when clean.</param>
  public sealed record Result(
    IReadOnlyList<string> Placements,
    IReadOnlyList<string> Findings
  );

  /// <summary>Places every concrete <see cref="BEBehaviorMPBase"/> type of
  /// <paramref name="assembly"/> in the four horizontal facings, runs its
  /// <c>SetOrientations</c>, and reports the <c>AxisSign</c> it gives per world axis.</summary>
  /// <remarks>A finding is a type with no placement, a placement with no discovery face, an
  /// <c>AxisSign</c> that is not a unit on its discovery face's axis, or two placements on one
  /// axis that disagree.</remarks>
  /// <param name="assembly">The assembly whose behaviour types must all be placed.</param>
  /// <param name="placements">Per type, the behaviour built headless for a declared horizontal
  /// facing; types outside <paramref name="assembly"/> are placed too.</param>
  /// <exception cref="Exception">Whatever a placement throws, unwrapped.</exception>
  public static Result Check(
    Assembly assembly,
    IReadOnlyDictionary<Type, Func<BlockFacing, BEBehaviorMPBase>> placements
  ) {
    var lines = new List<string>();
    var findings = new List<string>();
    foreach (Type type in MechanicalTypes(assembly))
      if (!placements.ContainsKey(type))
        findings.Add($"{type.Name}: no placement given");

    foreach (
      (Type type, Func<BlockFacing, BEBehaviorMPBase> place) in placements
    ) {
      var byAxis = new Dictionary<EnumAxis, List<(BlockFacing, int[])>>();
      foreach (BlockFacing facing in Facings) {
        BEBehaviorMPBase behavior = place(facing);
        behavior.SetOrientations();
        BlockFacing? face = behavior.OutFacingForNetworkDiscovery;
        int[] sign = behavior.AxisSign ?? [];
        string where = $"{type.Name} placed {facing.Code}";
        lines.Add(
          $"{where}: discovery {face?.Code ?? "none"}, AxisSign {Show(sign)}"
        );
        if (face == null) {
          findings.Add($"{where}: no discovery face");
          continue;
        }
        if (!OnAxis(sign, face.Axis)) {
          findings.Add(
            $"{where}: AxisSign {Show(sign)} is not a unit on the {face.Axis} axis of "
              + $"its discovery face {face.Code}"
          );
          continue;
        }
        if (
          !byAxis.TryGetValue(face.Axis, out List<(BlockFacing, int[])>? seen)
        )
          byAxis[face.Axis] = seen = [];
        seen.Add((facing, sign));
      }
      foreach (
        (EnumAxis axis, List<(BlockFacing Facing, int[] Sign)> seen) in byAxis
      )
        if (seen.Select(s => Show(s.Sign)).Distinct().Count() > 1)
          findings.Add(
            $"{type.Name}: AxisSign differs along the {axis} axis ("
              + string.Join(
                ", ",
                seen.Select(s => $"{Show(s.Sign)} placed {s.Facing.Code}")
              )
              + "); opposite facings share one sign per axis"
          );
    }
    return new Result(lines, findings);
  }

  /// <summary>Every concrete type in <paramref name="assembly"/> deriving from
  /// <see cref="BEBehaviorMPBase"/>, by full name.</summary>
  [CheckHelper("lists the types a guard must place")]
  public static IReadOnlyList<Type> MechanicalTypes(Assembly assembly) {
    Type[] types;
    try {
      types = assembly.GetTypes();
    } catch (ReflectionTypeLoadException e) {
      types = [.. e.Types.Where(t => t != null)!];
    }
    return
    [
      .. types
        .Where(t =>
          !t.IsAbstract && typeof(BEBehaviorMPBase).IsAssignableFrom(t)
        )
        .OrderBy(t => t.FullName, StringComparer.Ordinal),
    ];
  }

  private static bool OnAxis(int[] sign, EnumAxis axis) {
    int index = axis switch {
      EnumAxis.X => 0,
      EnumAxis.Y => 1,
      _ => 2,
    };
    return sign.Length == 3
      && Math.Abs(sign[index]) == 1
      && sign.Where((_, i) => i != index).All(v => v == 0);
  }

  private static string Show(int[] sign) => $"[{string.Join(",", sign)}]";
}
