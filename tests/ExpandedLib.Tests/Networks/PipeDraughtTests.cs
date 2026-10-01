using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Covers <see cref="PipeNetwork.HasDraught"/>: a run has draught through a chimney on a
/// chimney-ventable node or through a node whose entity draws it up a stack, and none through open
/// ends to air. Each run is one node open on top.
/// </summary>
public class PipeDraughtTests {
  #region Fixtures

  private static readonly BlockPos RunPos = new(0, 0, 0);

  private static readonly Block Chimney = TestBlocks.Configure(
    new Block(),
    "game:chimney",
    50
  );

  private static TestWorld NewWorld() {
    var w = new TestWorld();
    w.RegisterNetwork(
      "pipe",
      sys => new PipeNetwork(sys, new ChimneyVent(() => 16f))
    );
    return w;
  }

  private static PipeNetwork Run(
    TestWorld w,
    Block node,
    BlockEntity? be = null
  ) {
    w.Place(RunPos, node, be);
    w.AddNode(RunPos, "pipe");
    return Assert.IsType<PipeNetwork>(w.NetworkAt(RunPos));
  }

  #endregion

  #region Draught

  // Fails when a chimney face gives no draught.
  [Fact]
  public void A_chimney_on_a_ventable_node_gives_the_run_draught() {
    var w = NewWorld();
    w.Place(RunPos.UpCopy(), Chimney);
    PipeNetwork run = Run(w, OpenNode.Make(1, ventable: true));

    Assert.True(run.HasDraught(w.Accessor));
  }

  // Fails when a node's draught entity is ignored.
  [Fact]
  public void A_node_drawing_its_run_up_a_stack_gives_draught() {
    var w = NewWorld();
    PipeNetwork run = Run(
      w,
      OpenNode.Make(2, ventable: false),
      new StackEntity { Draws = true }
    );

    Assert.True(run.HasDraught(w.Accessor));
  }

  // Fails when an open end to air counts as draught.
  [Fact]
  public void An_open_end_to_air_gives_no_draught() {
    var w = NewWorld();
    PipeNetwork run = Run(w, OpenNode.Make(3, ventable: true));

    Assert.False(run.HasDraught(w.Accessor));
  }

  // Fails when a stack that does not draw still gives draught.
  [Fact]
  public void A_stack_that_does_not_draw_gives_no_draught() {
    var w = NewWorld();
    PipeNetwork run = Run(
      w,
      OpenNode.Make(4, ventable: false),
      new StackEntity { Draws = false }
    );

    Assert.False(run.HasDraught(w.Accessor));
  }

  #endregion

  #region Stand-ins

  /// <summary>A pipe node with one connector, on top.</summary>
  private class OpenNode : BlockNetworkNode {
    public override string NetworkType => "pipe";

    public override bool HasConnectorAt(BlockFacing face) =>
      face == BlockFacing.UP;

    public static OpenNode Make(int id, bool ventable) =>
      TestBlocks.Configure(
        ventable ? new VentableNode() : new OpenNode(),
        $"test:opennode-{id}",
        id
      );
  }

  private sealed class VentableNode : OpenNode, IChimneyVentable { }

  /// <summary>A stack's entity that draws while <see cref="Draws"/> is set.</summary>
  private sealed class StackEntity : BlockEntity, IPipeDraught {
    public bool Draws { get; init; }

    public bool GivesDraught => Draws;
  }

  #endregion
}
