using System.Linq;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A pipe's ambience ticks only on the client.</summary>
public class PipeAmbienceTests {
  private static (TestWorld world, BlockEntityPipe be) Placed() {
    var w = new TestWorld();
    w.RegisterNetwork("pipe", sys => new StubNetwork(sys, "pipe"));
    var be = new BlockEntityPipe();
    w.Place(
      new BlockPos(0, 0, 0),
      TestNetworkBlock.Create("pipe", "ns", id: 974),
      be
    );
    return (w, be);
  }

  // Read off the received calls rather than one overload, which differs between game versions.
  private static int AmbienceTicks(IEventAPI events) =>
    events
      .ReceivedCalls()
      .Count(c =>
        c.GetMethodInfo().Name == nameof(IEventAPI.RegisterGameTickListener)
        && c.GetArguments()[0]
          is System.Action<float> { Method.Name: "OnAmbientTick" }
      );

  [Fact]
  public void A_client_pipe_registers_its_ambience_tick() {
    var (w, be) = Placed();
    be.Api = w.ClientApi;

    be.Initialize(w.ClientApi);

    // BlockEntity registers through ICoreAPI.Event, which ICoreClientAPI re-declares as another property.
    Assert.Equal(1, AmbienceTicks(((ICoreAPI)w.ClientApi).Event));
  }
}
