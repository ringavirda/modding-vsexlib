using System.Linq;
using ExpandedLib.Industry;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests.Networks;

public class NetworkTypeRegistrationTests {
  [Fact]
  public void Industry_registers_pipe_molten_and_mpenergy() {
    var world = new TestWorld();
    IndustryModule.RegisterNetworkTypes(world.Networks);
    Assert.Equal(
      ["molten", "mpenergy", "pipe"],
      world.Networks.RegisteredNetworkTypes.Order()
    );
  }

  [Fact]
  public void A_later_registration_replaces_the_earlier_one_and_says_so() {
    var world = new TestWorld();
    bool first = world.Networks.RegisterNetworkType("mpenergy", () => new MpEnergyNetwork(world.Networks));
    bool second = world.Networks.RegisterNetworkType("mpenergy", () => new MpEnergyNetwork(world.Networks));
    Assert.False(first);
    Assert.True(second);
  }
}
