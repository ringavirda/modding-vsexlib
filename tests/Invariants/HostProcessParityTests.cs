using System.IO;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The two nested <c>HostProcess</c> forwarding shims - <c>BlockEntityProductionMachine.HostProcess</c>
/// and <c>BlockEntityMultiblockMachine.HostProcess</c> - stay identical apart from the owner type
/// name they close over. A shim edited on one side and not the other silently forks the process's
/// forwarding surface; deliberately not merged (an internal host interface would cost a forwarder per
/// member for no fewer lines), so this test is what keeps them in step instead.
/// </summary>
public class HostProcessParityTests {
  private const string ProductionMachinePath =
    "Machines/BlockEntityProductionMachine.cs";
  private const string MultiblockMachinePath =
    "Structures/BlockEntityMultiblockMachine.cs";

  private static string[] HostProcessBody(string relativePath, string ownerTypeName) {
    string path = Path.Combine(RepoPaths.Src("exlib"), relativePath);
    string[] lines = File.ReadAllLines(path);

    int start = System.Array.FindIndex(
      lines,
      l => l.Contains("private sealed class HostProcess(")
    );
    Assert.True(start >= 0, $"{relativePath}: no 'HostProcess' nested class found.");

    // The class body ends at the first line back at the class's own two-space indent that closes
    // it - every member inside is indented four spaces or more.
    int end = start + 1;
    while (end < lines.Length && !(lines[end].StartsWith("  }") && lines[end].Trim() == "}"))
      end++;
    Assert.True(end < lines.Length, $"{relativePath}: HostProcess class never closes.");

    var body = new string[end - start + 1];
    for (int i = 0; i <= end - start; i++)
      // Normalise the owner type name so the comparison is blind to it - the one thing the two
      // shims are allowed to differ on - and leave everything else, including whitespace, exact.
      body[i] = lines[start + i].Replace(ownerTypeName, "TOwner");
    return body;
  }

  [Fact]
  public void The_two_HostProcess_shims_stay_identical_apart_from_the_owner_type() {
    string[] production = HostProcessBody(
      ProductionMachinePath,
      "BlockEntityProductionMachine"
    );
    string[] multiblock = HostProcessBody(
      MultiblockMachinePath,
      "BlockEntityMultiblockMachine"
    );

    int shorter = System.Math.Min(production.Length, multiblock.Length);
    for (int i = 0; i < shorter; i++) {
      Assert.True(
        production[i] == multiblock[i],
        $"HostProcess shims diverge at line {i + 1} (owner-normalised):\n"
          + $"  {ProductionMachinePath}: {production[i]}\n"
          + $"  {MultiblockMachinePath}: {multiblock[i]}"
      );
    }

    Assert.True(
      production.Length == multiblock.Length,
      $"HostProcess shims differ in length: {ProductionMachinePath} has "
        + $"{production.Length} lines, {MultiblockMachinePath} has {multiblock.Length}."
    );
  }
}
