using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Industry;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// exlib's own driver at execute order 0.03: wires framework loggers, tunables and per-module
/// world-config flags, then hosts every framework module (currently exlib.industry.dll).
/// </summary>
public class ExModuleModSystemTests {
  private static Mod FakeMod(string modId) {
    var mod = Substitute.For<Mod>();
    ReflectionHelpers.SetProperty(
      mod,
      nameof(Mod.Info),
      new ModInfo { ModID = modId }
    );
    return mod;
  }

  private static ExModuleModSystem NewSystem(Mod mod) {
    var system = new ExModuleModSystem();
    ReflectionHelpers.SetProperty(system, nameof(ModSystem.Mod), mod);
    return system;
  }

  [Fact]
  public void StartPre_wires_loggers_and_sets_module_flags() {
    _ = typeof(IndustryModule);
    var world = new TestWorld();
    var system = NewSystem(FakeMod("exlib"));

    system.StartPre(world.Api);

    Assert.Same(world.Api.Logger, ExDefinitions.Logger);
    Assert.Same(world.Api.Logger, EntityRegistry.Logger);
    Assert.True(world.Api.World.Config.GetBool(ExModules.FlagKey("industry")));
  }

  [Fact]
  public void Phases_drive_the_framework_modules() {
    _ = typeof(IndustryModule);
    var world = new TestWorld();
    var system = NewSystem(FakeMod("exlib"));

    system.StartPre(world.Api);

    // IndustryModule.StartPre registers the "refractory" variant qualifier.
    Assert.Contains(ExBlockNames.Qualifiers, q => q.Group == "refractory");
  }
}
