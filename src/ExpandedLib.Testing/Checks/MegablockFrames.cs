using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>A reach of a drawn element past a mega-block's declared cells that a test accepts up to
/// a size, read from a reaches file.</summary>
/// <param name="Element">The element's path from the top of the shape, names joined with <c>/</c>; it
/// covers the element and every element under it, and the nearest entry that nests holds.</param>
/// <param name="Cell">The cell the element reaches into, in the north frame relative to the principal;
/// it turns with the variant's structure angle.</param>
/// <param name="Max">The farthest reach accepted, in voxels.</param>
/// <param name="Reason">Why the drawing reaches there.</param>
public sealed record MegablockReach(
  string Element,
  Vec3i Cell,
  float Max,
  string Reason
);

/// <summary>Relates a mega-block's drawn mesh to the cells it declares, cell by cell.</summary>
public static class MegablockFrames {
  /// <summary>Blocks of overhang a drawn mesh may have past its declared cells.</summary>
  [CheckHelper("the overhang Misfit allows by default")]
  public const float DefaultOverhang = 0.25f;

  private const float Grid = 0.5f;

  /// <summary>
  /// Measures each leaf element of <paramref name="shapePath"/> at rest against the cells
  /// <paramref name="principal"/> declares: a half-voxel surface sample's reach is its largest
  /// per-axis distance in voxels to the nearest declared box (a cell's <c>collisionBox</c> or whole).
  /// </summary>
  /// <param name="spinDegrees">The shape's <c>rotateY</c>.</param>
  /// <param name="structureAngle">The footprint's turn.</param>
  /// <param name="overhang">Blocks a reach may go past the boxes; 16 voxels to the block.</param>
  /// <param name="reaches">Reaches accepted past <paramref name="overhang"/>, each up to its
  /// <see cref="MegablockReach.Max"/>; null accepts none.</param>
  /// <returns>Null when nothing reaches past its limit and every entry is used. Otherwise a line
  /// per cell and leaf element reaching too far (the element's path, its worst reach in voxels
  /// and the cell; the first ends with the spin and angle that must agree), then a line per
  /// entry naming no element or reached by none.</returns>
  /// <exception cref="System.IO.FileNotFoundException">The shape file is missing.</exception>
  public static string? Misfit(
    string shapePath,
    int spinDegrees,
    IFillerHost principal,
    int structureAngle,
    float overhang = DefaultOverhang,
    IReadOnlyList<MegablockReach>? reaches = null
  ) {
    reaches ??= [];
    Outcome outcome = Judge(
      shapePath,
      spinDegrees,
      principal,
      structureAngle,
      overhang,
      reaches
    );
    List<string> lines = [.. outcome.Past];
    lines.AddRange(outcome.Unknown.Select(i => UnknownLine(reaches[i])));
    lines.AddRange(
      Enumerable
        .Range(0, reaches.Count)
        .Where(i => !outcome.Used.Contains(i) && !outcome.Unknown.Contains(i))
        .Select(i => UnusedLine(reaches[i]))
    );
    if (outcome.Past.Count > 0)
      lines[0] += Hint(spinDegrees, structureAngle);
    return lines.Count == 0 ? null : string.Join("\n", lines);
  }

  /// <summary>
  /// Runs <see cref="Misfit"/>'s measure over every loaded variant of every
  /// <see cref="IFillerHost"/> block of <paramref name="domain"/> that declares cells and a shape,
  /// at its resolved shape base and <c>rotateY</c> and its own <c>StructureAngle</c>.
  /// </summary>
  /// <param name="defs">Block and item definitions; others, and exlib's filler, are skipped.</param>
  /// <param name="assemblies">Where the definitions' classes live, exlib's included.</param>
  /// <param name="reachesFile">A file <see cref="ReadReaches"/> reads; null or missing accepts no reach.</param>
  /// <param name="prepare">Registers on the world what the blocks need, before any is defined.</param>
  /// <param name="shapeFile">Resolves a shape base to its file; null uses <see cref="ShapeFile"/>.</param>
  /// <returns>The law <c>megablock frames</c>: a reach too far is keyed by the variant's code;
  /// an entry naming no element, one reached by no variant, and one for an undrawn shape by the
  /// shape base.</returns>
  /// <exception cref="System.IO.FileNotFoundException">A host's shape file is missing.</exception>
  /// <exception cref="InvalidCastException">An entry of <paramref name="reachesFile"/> lacks a field or
  /// has one of the wrong type.</exception>
  public static BlockLaws.Law Run(
    string domain,
    IEnumerable<IExDef> defs,
    IReadOnlyList<Assembly> assemblies,
    string? reachesFile = null,
    Action<TestWorld>? prepare = null,
    System.Func<string, string>? shapeFile = null
  ) {
    shapeFile ??= ShapeFile;
    IReadOnlyDictionary<string, IReadOnlyList<MegablockReach>> all =
      reachesFile != null && System.IO.File.Exists(reachesFile)
        ? ReadReaches(reachesFile)
        : new Dictionary<string, IReadOnlyList<MegablockReach>>();
    TestWorld world = BlockLaws.Stand(
      defs.OfType<ExBlockDef>(),
      assemblies,
      prepare
    );

    var findings = new List<string>();
    var used = new Dictionary<string, HashSet<int>>();
    var unknown = new Dictionary<string, HashSet<int>>();
    int blocks = 0,
      cases = 0;
    foreach (
      IGrouping<string, Block> type in BlockLaws.Blocktypes(world, domain)
    ) {
      Block[] hosts =
      [
        .. type.Where(b =>
          b is IFillerHost host
          && b.Shape?.Base != null
          && StructureFillers.ReadOffsets(host.FillerOffsets).Count > 0
        ),
      ];
      if (hosts.Length == 0)
        continue;
      blocks++;
      foreach (Block block in hosts) {
        cases++;
        string shapeBase = block.Shape.Base.ToString();
        IReadOnlyList<MegablockReach> reaches = all.GetValueOrDefault(
          shapeBase,
          []
        );
        int spin = (int)Math.Round(block.Shape.rotateY);
        int angle = ((IFillerHost)block).StructureAngle;
        Outcome outcome = Judge(
          shapeFile(shapeBase),
          spin,
          (IFillerHost)block,
          angle,
          DefaultOverhang,
          reaches
        );
        used.TryAdd(shapeBase, []);
        used[shapeBase].UnionWith(outcome.Used);
        unknown.TryAdd(shapeBase, []);
        unknown[shapeBase].UnionWith(outcome.Unknown);
        for (int i = 0; i < outcome.Past.Count; i++)
          findings.Add(
            $"{block.Code} {outcome.Past[i]}"
              + (i == 0 ? Hint(spin, angle) : "")
          );
      }
    }

    foreach ((string shapeBase, IReadOnlyList<MegablockReach> reaches) in all) {
      if (!used.TryGetValue(shapeBase, out HashSet<int>? seen)) {
        findings.Add(
          $"{shapeBase} has reach entries and no loaded variant draws it"
        );
        continue;
      }
      for (int i = 0; i < reaches.Count; i++)
        if (unknown[shapeBase].Contains(i))
          findings.Add($"{shapeBase} {UnknownLine(reaches[i])}");
        else if (!seen.Contains(i))
          findings.Add($"{shapeBase} {UnusedLine(reaches[i])}");
    }
    return new BlockLaws.Law("megablock frames", blocks, cases, findings);
  }

  /// <summary>Reads a reaches file.</summary>
  /// <param name="path">A JSON object keyed by shape base, each value an array of
  /// <c>{ "element": "a/b", "cell": [x, y, z], "max": voxels, "reason": "..." }</c>.</param>
  /// <returns>The entries of each shape base, in file order.</returns>
  /// <exception cref="System.IO.FileNotFoundException">The file is missing.</exception>
  /// <exception cref="InvalidCastException">An entry lacks a field or has one of the wrong
  /// type.</exception>
  public static IReadOnlyDictionary<
    string,
    IReadOnlyList<MegablockReach>
  > ReadReaches(string path) =>
    JObject
      .Parse(System.IO.File.ReadAllText(path))
      .Properties()
      .ToDictionary(
        p => p.Name,
        p =>
          (IReadOnlyList<MegablockReach>)
            [
              .. p.Value.Select(e => new MegablockReach(
                (string)e["element"]!,
                new Vec3i(
                  (int)e["cell"]![0]!,
                  (int)e["cell"]![1]!,
                  (int)e["cell"]![2]!
                ),
                (float)e["max"]!,
                (string)e["reason"]!
              )),
            ]
      );

  /// <summary>The repo-relative shape file a <c>domain:path</c> shape reference names.</summary>
  [CheckHelper("resolves a shape base to its file")]
  public static string ShapeFile(string shapeBase) {
    string[] parts = shapeBase.Split(':', 2);
    return System.IO.Path.Combine(
      RepoPaths.Assets(parts[0]),
      "shapes",
      (parts[1] + ".json").Replace('/', System.IO.Path.DirectorySeparatorChar)
    );
  }

  private sealed record Outcome(
    List<string> Past,
    HashSet<int> Used,
    HashSet<int> Unknown
  );

  private static string Hint(int spin, int angle) =>
    $" (the shape spins by {spin}deg and the footprint by {angle}deg; unless those "
    + "two agree the mesh and the fillers are placed in different frames)";

  private static string UnknownLine(MegablockReach r) =>
    $"reach '{r.Element}' for cell ({r.Cell.X}, {r.Cell.Y}, {r.Cell.Z}) names no element of the shape";

  private static string UnusedLine(MegablockReach r) =>
    $"reach '{r.Element}' for cell ({r.Cell.X}, {r.Cell.Y}, {r.Cell.Z}) is unused: "
    + "no element under it reaches that cell past the overhang";

  private static string Bare(string path) =>
    path.StartsWith("Root/", StringComparison.Ordinal) ? path[5..] : path;

  private static bool Under(string path, string element) {
    string p = Bare(path),
      e = Bare(element);
    return p == e || p.StartsWith(e + "/", StringComparison.Ordinal);
  }

  private static Outcome Judge(
    string shapePath,
    int spin,
    IFillerHost principal,
    int angle,
    float overhang,
    IReadOnlyList<MegablockReach> reaches
  ) {
    List<(float[] Lo, float[] Hi)> boxes = DeclaredBoxes(principal, angle);
    List<ShapeExtents.DrawnElement> elements = ShapeExtents.Surfaces(
      shapePath,
      Grid
    );
    float limit = overhang * 16f;

    var worst = new Dictionary<(Vec3i Cell, string Path), float>();
    foreach (ShapeExtents.DrawnElement element in elements.Where(e => e.Leaf)) {
      float[] mid = Spun(element.Middle, spin);
      foreach (float[] point in element.Points) {
        float[] p = Spun(point, spin);
        float gap = Gap(p, boxes);
        if (gap < 0.05f)
          continue;
        var cell = new Vec3i(
          CellOf(p[0], mid[0]),
          CellOf(p[1], mid[1]),
          CellOf(p[2], mid[2])
        );
        if (
          !worst.TryGetValue((cell, element.Path), out float known)
          || gap > known
        )
          worst[(cell, element.Path)] = gap;
      }
    }

    var unknown = new HashSet<int>(
      Enumerable
        .Range(0, reaches.Count)
        .Where(i =>
          !elements.Any(e => Bare(e.Path) == Bare(reaches[i].Element))
        )
    );
    var turned = reaches
      .Select(r => ExOrientation.RotateOffset(r.Cell, angle))
      .ToList();
    var used = new HashSet<int>();
    var found =
      new List<(float Reach, Vec3i Cell, string Path, string? Note)>();
    foreach (((Vec3i cell, string path), float reach) in worst) {
      if (reach <= limit + 1e-6f)
        continue;
      int hit = Enumerable
        .Range(0, reaches.Count)
        .Where(i => turned[i].Equals(cell) && Under(path, reaches[i].Element))
        .DefaultIfEmpty(-1)
        .MaxBy(i => i < 0 ? -1 : Bare(reaches[i].Element).Length);
      string? note = null;
      if (hit >= 0) {
        used.Add(hit);
        if (reach <= reaches[hit].Max + 1e-6f)
          continue;
        note = $", past its allowed {reaches[hit].Max:0.###}";
      }
      found.Add((reach, cell, path, note));
    }

    List<string> past =
    [
      .. found
        .OrderByDescending(f => f.Reach)
        .ThenBy(f => f.Path, StringComparer.Ordinal)
        .Select(f =>
          $"draws '{f.Path}' {f.Reach:0.##} voxels past cell "
          + $"({f.Cell.X}, {f.Cell.Y}, {f.Cell.Z}) from the principal, "
          + $"{limit:0.###} allowed{f.Note}"
        ),
    ];
    return new Outcome(past, used, unknown);
  }

  private static int CellOf(float value, float middle) =>
    (int)Math.Floor((value + 8d + 1e-6 * Math.Sign(middle - value)) / 16d);

  private static float[] Spun(float[] voxel, int spin) {
    (double x, double z) = ExOrientation.RotateXZ(
      voxel[0] - 8,
      voxel[2] - 8,
      spin
    );
    return [(float)x, voxel[1] - 8, (float)z];
  }

  private static float Gap(float[] p, List<(float[] Lo, float[] Hi)> boxes) {
    float best = float.PositiveInfinity;
    foreach ((float[] lo, float[] hi) in boxes) {
      float g = 0;
      for (int axis = 0; axis < 3; axis++)
        g = Math.Max(g, Math.Max(lo[axis] - p[axis], p[axis] - hi[axis]));
      best = Math.Min(best, g);
    }
    return best;
  }

  /// <summary>The declared boxes in voxels from the principal cell's centre: the principal's whole cell, then
  /// each declared cell turned by <paramref name="angle"/>, whole or as its collision boxes.</summary>
  private static List<(float[] Lo, float[] Hi)> DeclaredBoxes(
    IFillerHost principal,
    int angle
  ) {
    var boxes = new List<(float[] Lo, float[] Hi)>
    {
      Box(new Vec3i(0, 0, 0), null),
    };
    foreach (
      FillerOffset offset in StructureFillers.ReadOffsets(
        principal.FillerOffsets
      )
    ) {
      Vec3i cell = ExOrientation.RotateOffset(offset.Offset, angle);
      Cuboidf[]? own =
        offset.CollisionBoxes is not { Length: > 0 }
          ? null
          : ExOrientation.RotateBoxes(offset.CollisionBoxes, angle);
      if (own == null)
        boxes.Add(Box(cell, null));
      else
        boxes.AddRange(own.Select(b => Box(cell, b)));
    }
    return boxes;
  }

  private static (float[] Lo, float[] Hi) Box(Vec3i cell, Cuboidf? within) {
    Cuboidf b = within ?? new Cuboidf(0, 0, 0, 1, 1, 1);
    float[] at = [cell.X * 16f - 8, cell.Y * 16f - 8, cell.Z * 16f - 8];
    return (
      [at[0] + b.X1 * 16, at[1] + b.Y1 * 16, at[2] + b.Z1 * 16],
      [at[0] + b.X2 * 16, at[1] + b.Y2 * 16, at[2] + b.Z2 * 16]
    );
  }
}
