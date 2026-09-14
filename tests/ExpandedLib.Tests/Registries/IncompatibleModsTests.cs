using System.Linq;
using ExpandedLib.Registries;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Registries;

public class IncompatibleModsTests {
  private static IModLoader LoaderWith(params string[] enabled) {
    var loader = Substitute.For<IModLoader>();
    loader
      .IsModEnabled(Arg.Any<string>())
      .Returns(call => enabled.Contains(call.Arg<string>()));
    return loader;
  }

  [Fact]
  public void Nothing_enabled_means_no_message() =>
    Assert.Null(IncompatibleMods.Message(LoaderWith("iiex"), "0.8.0"));

  [Fact]
  public void Both_old_mods_are_named_with_the_fix() {
    string? msg = IncompatibleMods.Message(LoaderWith("smex", "ppex"), "0.8.0");
    Assert.NotNull(msg);
    Assert.Contains("Steelmaking Expanded", msg);
    Assert.Contains("Pipes and Power Expanded", msg);
    Assert.Contains("exlib 0.7.2", msg);
    Assert.Contains("Iron Industry Expanded", msg);
  }

  [Fact]
  public void One_old_mod_is_named_alone() {
    string? msg = IncompatibleMods.Message(LoaderWith("ppex"), "0.8.0");
    Assert.Contains("Pipes and Power Expanded", msg);
    Assert.DoesNotContain("Steelmaking", msg);
  }
}
