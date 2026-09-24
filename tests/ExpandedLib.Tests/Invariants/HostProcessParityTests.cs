using System.IO;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Checks the two nested <c>HostProcess</c> forwarding shims in
/// <c>BlockEntityProductionMachine</c> and <c>BlockEntityMultiblockMachine</c> stay identical apart
/// from the owner type they close over.</summary>
public class HostProcessParityTests {
  private const string ProductionMachinePath =
    "Machines/BlockEntityProductionMachine.cs";
  private const string MultiblockMachinePath =
    "Structures/BlockEntityMultiblockMachine.cs";

  /// <summary>One side of the comparison: a source file's path as reported, its lines, and the
  /// owner type name its shim closes over.</summary>
  public sealed record Shim(string Path, string[] Lines, string Owner);

  // The shim's lines with the owner normalised, and the 1-based line of the first; or the reason
  // there is none.
  private static (string[]? Body, int FirstLine, string? Error) HostProcessBody(
    Shim shim
  ) {
    string[] lines = shim.Lines;
    int classLine = System.Array.FindIndex(
      lines,
      l => l.Contains("private sealed class HostProcess(")
    );
    if (classLine < 0)
      return (null, 0, $"{shim.Path}: no 'HostProcess' nested class found.");

    // Includes the doc comment: a contiguous run of "  /// " lines directly above the class.
    int start = classLine;
    while (start > 0 && lines[start - 1].TrimStart().StartsWith("///"))
      start--;

    // The class body ends at the first line at the class's own two-space indent.
    int end = classLine + 1;
    while (
      end < lines.Length
      && !(lines[end].StartsWith("  }") && lines[end].Trim() == "}")
    )
      end++;
    if (end >= lines.Length)
      return (null, 0, $"{shim.Path}: HostProcess class never closes.");

    var body = new string[end - start + 1];
    for (int i = 0; i <= end - start; i++)
      // Normalises the owner type name, the one thing the two shims may differ on.
      body[i] = lines[start + i].Replace(shim.Owner, "TOwner");
    return (body, start + 1, null);
  }

  /// <summary>How the <c>HostProcess</c> shims of <paramref name="production"/> and
  /// <paramref name="multiblock"/> differ once each owner name reads <c>TOwner</c>, the doc comment
  /// above each class included.</summary>
  /// <returns>The first differing line pair, a length difference, or a missing or unclosed
  /// class, as a message naming the paths; null when the shims agree.</returns>
  public static string? ShimDivergence(Shim production, Shim multiblock) {
    var (productionBody, productionFirstLine, productionError) =
      HostProcessBody(production);
    if (productionError != null)
      return productionError;
    var (multiblockBody, multiblockFirstLine, multiblockError) =
      HostProcessBody(multiblock);
    if (multiblockError != null)
      return multiblockError;

    int shorter = System.Math.Min(
      productionBody!.Length,
      multiblockBody!.Length
    );
    for (int i = 0; i < shorter; i++)
      if (productionBody[i] != multiblockBody[i])
        return "HostProcess shims diverge at "
          + $"{production.Path}:{productionFirstLine + i} / "
          + $"{multiblock.Path}:{multiblockFirstLine + i} (owner-normalised):\n"
          + $"  {production.Path}: {productionBody[i]}\n"
          + $"  {multiblock.Path}: {multiblockBody[i]}";

    if (productionBody.Length != multiblockBody.Length)
      return $"HostProcess shims agree for {shorter} lines but then differ in length: "
        + $"{production.Path} has {productionBody.Length} lines, "
        + $"{multiblock.Path} has {multiblockBody.Length}.";
    return null;
  }

  private static Shim Read(string relativePath, string owner) =>
    new(
      relativePath,
      File.ReadAllLines(Path.Combine(RepoPaths.Src("exlib"), relativePath)),
      owner
    );

  [Fact]
  public void The_two_HostProcess_shims_stay_identical_apart_from_the_owner_type() {
    string? divergence = ShimDivergence(
      Read(ProductionMachinePath, "BlockEntityProductionMachine"),
      Read(MultiblockMachinePath, "BlockEntityMultiblockMachine")
    );

    Assert.True(divergence == null, divergence);
  }

  private static Shim Planted(string owner, params string[] members) =>
    new(
      owner + ".cs",
      [
        $"public class {owner} {{",
        "  /// <summary>Forwards.</summary>",
        $"  private sealed class HostProcess({owner} owner) {{",
        .. members,
        "  }",
        "}",
      ],
      owner
    );

  // Fails when ShimDivergence passes a differing line, a missing or an unclosed class, or reads
  // the owner name as a difference.
  [Fact]
  [PlantedDefect(typeof(HostProcessParityTests), nameof(ShimDivergence))]
  public void Shims_that_differ_beyond_the_owner_are_named() {
    Shim a = Planted(
      "Kiln",
      "    Kiln Owner => owner;",
      "    int Tick() => 1;"
    );

    Assert.Null(
      ShimDivergence(
        a,
        Planted("Oven", "    Oven Owner => owner;", "    int Tick() => 1;")
      )
    );
    Assert.Equal(
      "HostProcess shims diverge at Kiln.cs:5 / Oven.cs:5 (owner-normalised):\n"
        + "  Kiln.cs:     int Tick() => 1;\n"
        + "  Oven.cs:     int Tick() => 2;",
      ShimDivergence(
        a,
        Planted("Oven", "    Oven Owner => owner;", "    int Tick() => 2;")
      )
    );
    Assert.Equal(
      "Oven.cs: HostProcess class never closes.",
      ShimDivergence(
        a,
        new Shim("Oven.cs", ["  private sealed class HostProcess(Oven o) {"], "Oven")
      )
    );
    Assert.Equal(
      "Oven.cs: no 'HostProcess' nested class found.",
      ShimDivergence(a, new Shim("Oven.cs", ["class Oven { }"], "Oven"))
    );
  }
}
