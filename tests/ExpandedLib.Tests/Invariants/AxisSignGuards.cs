using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;
using Xunit;
using Xunit.Abstractions;

namespace ExpandedLib.Tests;

/// <summary>exlib's mechanical-power behaviours under <see cref="AxisSigns"/>: each placed in the
/// four facings gives one <c>AxisSign</c> per world axis.</summary>
[GuardOf(typeof(AxisSigns), nameof(AxisSigns.Check))]
public class AxisSignGuards(ITestOutputHelper output) {
  private static Dictionary<
    Type,
    Func<BlockFacing, BEBehaviorMPBase>
  > Placements() =>
    new() {
      [typeof(BEBehaviorMPFillerPort)] = facing => {
        var port = new BEBehaviorMPFillerPort(new BlockEntityStructureFiller());
        port.ConfigureFromFiller(null, facing, null);
        return port;
      },
    };

  [Fact]
  public void Every_behaviour_gives_one_sign_per_axis() {
    Premise.NotEmpty(
      AxisSigns.MechanicalTypes(typeof(BEBehaviorMPFillerPort).Assembly),
      "mechanical-power behaviours"
    );
    Assert.Empty(
      AxisSigns.MechanicalTypes(typeof(ExpandedLibModSystem).Assembly)
    );

    AxisSigns.Result result = AxisSigns.Check(
      typeof(BEBehaviorMPFillerPort).Assembly,
      Placements()
    );
    foreach (string line in result.Placements.Concat(result.Findings))
      output.WriteLine(line);

    FindingLists.Assert(
      result.Findings,
      new Dictionary<string, string>(),
      new Dictionary<string, string>()
    );
  }
}
