using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Catalogues;
using Xunit;

namespace WasherPunch.Tests;

/// <summary>
/// The press's job table after one game load: the iron row from <c>config/processjobs</c> and the
/// copper row from the mod system's contributor.
/// </summary>
[Collection(WorldCollection.Name)]
public class JobTableTests(LoadedWorld loaded) {
  private static IReadOnlyList<ProcessJob> Loaded() =>
    [.. ProcessJobRegistry.Shared.Jobs(WasherPunchModSystem.Machine)];

  [Fact]
  public void The_iron_row_comes_from_the_json_table() {
    ProcessJob job = Assert.Single(
      Loaded(),
      j => j.Input == "game:metalplate-iron"
    );

    Assert.Equal("washerpunch:washer-iron", job.Output);
    Assert.Equal(40, job.Count);
    Assert.Equal(40, job.Strokes);
  }

  [Fact]
  public void The_copper_row_comes_from_the_csharp_contributor() {
    ProcessJob job = Assert.Single(
      Loaded(),
      j => j.Input == "game:metalplate-copper"
    );

    Assert.Equal("washerpunch:washer-copper", job.Output);
    Assert.Equal(WasherPunchModSystem.WashersPerPlate, job.Count);
    Assert.Equal(WasherPunchModSystem.WashersPerPlate, job.Strokes);
  }

  [Fact]
  public void Only_those_two_rows_are_filed_under_the_press() {
    Assert.Equal(2, Loaded().Count);
  }
}
