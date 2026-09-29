using System.Linq;
using ExpandedLib.Industry;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
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
    bool first = world.Networks.RegisterNetworkType(
      "mpenergy",
      () => new MpEnergyNetwork(world.Networks)
    );
    bool second = world.Networks.RegisterNetworkType(
      "mpenergy",
      () => new MpEnergyNetwork(world.Networks)
    );
    Assert.False(first);
    Assert.True(second);
  }

  // Fails when the re-registration line carries its own "[exlib]", which the mod logger already
  // prefixes.
  [Fact]
  public void A_re_registration_is_logged_once_without_its_own_prefix() {
    var world = new TestWorld();
    var logger = new RecordingLogger();
    var mod = Substitute.For<Mod>();
    ReflectionHelpers.SetProperty(mod, nameof(Mod.Logger), logger);
    ReflectionHelpers.SetProperty(world.Networks, nameof(ModSystem.Mod), mod);

    world.Networks.RegisterNetworkType(
      "mpenergy",
      () => new MpEnergyNetwork(world.Networks)
    );
    world.Networks.RegisterNetworkType(
      "mpenergy",
      () => new MpEnergyNetwork(world.Networks)
    );

    Assert.Equal(
      [
        (
          EnumLogType.Notification,
          "Block network type 'mpenergy' re-registered; the later factory wins."
        ),
      ],
      logger.Entries
    );
  }
}
