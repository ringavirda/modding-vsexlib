#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Catalogues;
using ExpandedLib.Industry.Materials;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>Runs every setup file under <c>tests/live/setups/parity</c> in the harness and compares its
/// recording with the one under <c>Setups/Recordings</c>. The same files run live, where their figures
/// are read against the harness's.</summary>
[Collection("Setups")]
public class ExlibParitySetupTests : IDisposable
{
  private static string Parity =>
    Path.Combine(RepoPaths.Root, "tests", "live", "setups", "parity");

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
        "burdenmaker" => Path.Combine(RepoPaths.Root, "samples", "BurdenMaker"),
        "smokestack" => Path.Combine(RepoPaths.Root, "samples", "SmokeStack"),
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
    AfterLoad = _ =>
    {
      MaterialRoleRegistry.Register(
        new MaterialRoleDef { Role = Roles.Flux, Code = "game:lime" }
      );
      MaterialRoleRegistry.Register(
        new MaterialRoleDef
        {
          Role = Roles.IronOre,
          PathPrefix = "crushed-iron",
        }
      );
    },
  };

  public static IEnumerable<object[]> Rows() => SetupCases.In(Parity);

  [SetupTheory, MemberData(nameof(Rows))]
  public void Case(SetupCaseRow row) => SetupCases.Run(row, Options);

  // Fails when the enumeration skips tests/live/setups/parity or drops a case.
  [Fact]
  public void The_parity_folder_yields_its_two_cases()
  {
    Assert.Equal(
      ["parity-drain-250", "parity-wait-slice"],
      Rows().Select(r => ((SetupCaseRow)r[0]).Setup).Distinct().Order()
    );
  }

  public void Dispose() => SetupPlacers.Clear();
}
#endif
