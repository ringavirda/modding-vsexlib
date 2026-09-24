using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;
using Xunit;
using Xunit.Abstractions;
using BlockEntity = Vintagestory.API.Common.BlockEntity;

namespace ExpandedLib.Tests;

/// <summary><see cref="AxisSigns"/> over planted behaviours, one fault each.</summary>
public class AxisSignsTests(ITestOutputHelper output) {
  private static Dictionary<Type, Func<BlockFacing, BEBehaviorMPBase>> Placed(
    Type type
  ) =>
    new() {
      [type] = facing =>
        (BEBehaviorMPBase)
          Activator.CreateInstance(
            type,
            new BlockEntityStructureFiller(),
            facing
          )!,
    };

  // The findings of a run over the placed type alone, printed with its placements.
  private IReadOnlyList<string> Findings(Type type) {
    AxisSigns.Result result = AxisSigns.Check(
      typeof(object).Assembly,
      Placed(type)
    );
    foreach (string line in result.Placements.Concat(result.Findings))
      output.WriteLine(line);
    return result.Findings;
  }

  // Fails when Check stops comparing the placements that share an axis.
  [Fact]
  [PlantedDefect(typeof(AxisSigns), nameof(AxisSigns.Check))]
  public void A_signed_normal_is_named_on_both_axes() {
    IReadOnlyList<string> findings = Findings(typeof(SignedNormalAxisFixture));

    Assert.Equal(2, findings.Count);
    Assert.Contains(
      "SignedNormalAxisFixture: AxisSign differs along the Z axis ([0,0,-1] placed north, "
        + "[0,0,1] placed south); opposite facings share one sign per axis",
      findings
    );
    Assert.Contains(
      findings,
      f =>
        f.StartsWith(
          "SignedNormalAxisFixture: AxisSign differs along the X axis"
        )
    );
  }

  // Fails when Check stops holding the sign to its discovery face's axis.
  [Fact]
  [PlantedDefect(typeof(AxisSigns), nameof(AxisSigns.Check))]
  public void A_sign_off_the_faces_axis_is_named() =>
    Assert.Equal(
      [
        "OffAxisFixture placed east: AxisSign [0,0,-1] is not a unit on the X axis of its "
          + "discovery face east",
        "OffAxisFixture placed west: AxisSign [0,0,-1] is not a unit on the X axis of its "
          + "discovery face west",
      ],
      Findings(typeof(OffAxisFixture))
    );

  // Fails when Check reads a placement with no discovery face as clean.
  [Fact]
  [PlantedDefect(typeof(AxisSigns), nameof(AxisSigns.Check))]
  public void A_placement_with_no_discovery_face_is_named() =>
    Assert.Equal(
      4,
      Findings(typeof(NoFaceAxisFixture))
        .Count(f => f.EndsWith(": no discovery face"))
    );

  // Fails when Check stops requiring a placement for every behaviour type of the assembly.
  [Fact]
  [PlantedDefect(typeof(AxisSigns), nameof(AxisSigns.Check))]
  public void A_type_with_no_placement_is_named() =>
    Assert.Contains(
      "SignedNormalAxisFixture: no placement given",
      AxisSigns
        .Check(typeof(AxisSignsTests).Assembly, Placed(typeof(PerAxisFixture)))
        .Findings
    );

  // Fails when Check names a sign that is one unit per axis, the same for opposite facings.
  [Fact]
  public void One_sign_per_axis_passes_and_every_facing_is_reported() {
    AxisSigns.Result result = AxisSigns.Check(
      typeof(object).Assembly,
      Placed(typeof(PerAxisFixture))
    );

    Assert.Empty(result.Findings);
    Assert.Equal(
      [
        "PerAxisFixture placed north: discovery north, AxisSign [0,0,-1]",
        "PerAxisFixture placed east: discovery east, AxisSign [-1,0,0]",
        "PerAxisFixture placed south: discovery south, AxisSign [0,0,-1]",
        "PerAxisFixture placed west: discovery west, AxisSign [-1,0,0]",
      ],
      result.Placements
    );
  }

  [Fact]
  public void MechanicalTypes_lists_the_concrete_behaviours() {
    IReadOnlyList<Type> types = AxisSigns.MechanicalTypes(
      typeof(AxisSignsTests).Assembly
    );

    Assert.Contains(typeof(PerAxisFixture), types);
    Assert.DoesNotContain(typeof(AxisFixtureBase), types);
  }
}

internal abstract class AxisFixtureBase(BlockEntity be, BlockFacing facing)
  : BEBehaviorMPBase(be) {
  protected readonly BlockFacing Facing = facing;

  public override float GetResistance() => 0f;
}

internal sealed class PerAxisFixture(BlockEntity be, BlockFacing facing)
  : AxisFixtureBase(be, facing) {
  public override void SetOrientations() {
    OutFacingForNetworkDiscovery = Facing;
    AxisSign = Facing.Axis == EnumAxis.X ? [-1, 0, 0] : [0, 0, -1];
  }
}

internal sealed class SignedNormalAxisFixture(
  BlockEntity be,
  BlockFacing facing
) : AxisFixtureBase(be, facing) {
  public override void SetOrientations() {
    OutFacingForNetworkDiscovery = Facing;
    AxisSign = [Facing.Normali.X, Facing.Normali.Y, Facing.Normali.Z];
  }
}

internal sealed class OffAxisFixture(BlockEntity be, BlockFacing facing)
  : AxisFixtureBase(be, facing) {
  public override void SetOrientations() {
    OutFacingForNetworkDiscovery = Facing;
    AxisSign = [0, 0, -1];
  }
}

internal sealed class NoFaceAxisFixture(BlockEntity be, BlockFacing facing)
  : AxisFixtureBase(be, facing) {
  public override void SetOrientations() => AxisSign = [0, 0, -1];
}
