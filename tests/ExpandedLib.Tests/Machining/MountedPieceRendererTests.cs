using ExpandedLib.Industry.Machining;
using Xunit;

namespace ExpandedLib.Tests.Machining;

/// <summary>The turn a mounted piece's renderer holds between frames.</summary>
public sealed class MountedPieceRendererTests {
  // Fails when the angle advances while the machine is not cutting, as it would on IsRunning.
  [Fact]
  public void The_piece_turns_only_while_cutting() {
    float cutting = MountedPieceRenderer.Step(1f, 90f, 0.5f, true);
    float idle = MountedPieceRenderer.Step(1f, 90f, 0.5f, false);

    Assert.Equal(1f + System.MathF.PI / 4f, cutting, 4);
    Assert.Equal(1f, idle);
  }
}
