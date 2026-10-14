#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>Runs every setup file under <c>tests/live/setups</c> in the harness and compares its
/// recording with the one under <c>Setups/Recordings</c>.</summary>
[Collection("Setups")]
public class ExlibSetupTests : IDisposable
{
  private static string Tests => Path.Combine(RepoPaths.Root, "tests");

  private static readonly SetupRunOptions Options = new()
  {
    ModFolder = id =>
      id switch
      {
        "platedpipes" => Path.Combine(RepoPaths.Root, "samples", "PlatedPipes"),
        "twintubblower" => Path.Combine(
          RepoPaths.Root,
          "samples",
          "TwinTubBlower"
        ),
        _ => throw new KeyNotFoundException(id),
      },
    Recordings = Path.Combine(
      RepoPaths.Root,
      "tests",
      "ExpandedLib.Tests",
      "Setups",
      "Recordings"
    ),
    ExlibVersion = "0.8.4",
  };

  public static IEnumerable<object[]> Rows() =>
    SetupCases.In(Path.Combine(Tests, "live", "setups"));

  [SetupTheory, MemberData(nameof(Rows))]
  public void Case(SetupCaseRow row) => SetupCases.Run(row, Options);

  // Fails when the enumeration skips tests/live/setups.
  [Fact]
  public void The_setups_folder_yields_the_reader_setup()
  {
    Assert.Contains(Rows(), r => ((SetupCaseRow)r[0]).Setup == "live-reader");
  }

  public void Dispose() => SetupPlacers.Clear();
}
#endif
