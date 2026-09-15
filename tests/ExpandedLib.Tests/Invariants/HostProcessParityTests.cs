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

  private static (string[] Body, int FirstLine) HostProcessBody(
    string relativePath,
    string ownerTypeName
  ) {
    string path = Path.Combine(RepoPaths.Src("exlib"), relativePath);
    string[] lines = File.ReadAllLines(path);

    int classLine = System.Array.FindIndex(
      lines,
      l => l.Contains("private sealed class HostProcess(")
    );
    Assert.True(
      classLine >= 0,
      $"{relativePath}: no 'HostProcess' nested class found."
    );

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
    Assert.True(
      end < lines.Length,
      $"{relativePath}: HostProcess class never closes."
    );

    var body = new string[end - start + 1];
    for (int i = 0; i <= end - start; i++)
      // Normalises the owner type name, the one thing the two shims may differ on.
      body[i] = lines[start + i].Replace(ownerTypeName, "TOwner");
    return (body, start + 1); // 1-based source line of body[0]
  }

  [Fact]
  public void The_two_HostProcess_shims_stay_identical_apart_from_the_owner_type() {
    (string[] production, int productionFirstLine) = HostProcessBody(
      ProductionMachinePath,
      "BlockEntityProductionMachine"
    );
    (string[] multiblock, int multiblockFirstLine) = HostProcessBody(
      MultiblockMachinePath,
      "BlockEntityMultiblockMachine"
    );

    int shorter = System.Math.Min(production.Length, multiblock.Length);
    for (int i = 0; i < shorter; i++) {
      Assert.True(
        production[i] == multiblock[i],
        "HostProcess shims diverge at "
          + $"{ProductionMachinePath}:{productionFirstLine + i} / "
          + $"{MultiblockMachinePath}:{multiblockFirstLine + i} (owner-normalised):\n"
          + $"  {ProductionMachinePath}: {production[i]}\n"
          + $"  {MultiblockMachinePath}: {multiblock[i]}"
      );
    }

    Assert.True(
      production.Length == multiblock.Length,
      $"HostProcess shims agree for {shorter} lines but then differ in length: "
        + $"{ProductionMachinePath} has {production.Length} lines, "
        + $"{MultiblockMachinePath} has {multiblock.Length}."
    );
  }
}
