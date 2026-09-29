using System;
using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using HarmonyLib;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The zero-line registration rung. Each lifecycle hook runs the matching registries for that phase
/// before the mod's own hook: config and entities in <c>Start</c>, commands in each side hook,
/// preferences before commands on the client, Harmony patched only when <c>PatchHarmony</c> opts in.
/// </summary>
[Collection(ExHarmonyCollection.Name)]
public class ExModSystemTests : IDisposable {
  // EntityRegistry.RegisterAll records this assembly against the last mod id that registered it;
  // reset on dispose, along with this assembly's ExDefinitions.Contributors and ExCheckRegistry entries.
  public void Dispose() {
    var field = typeof(EntityRegistry).GetField(
      "_domainByAssembly",
      BindingFlags.NonPublic | BindingFlags.Static
    )!;
    var map = (IDictionary<Assembly, string>)field.GetValue(null)!;
    map.Remove(typeof(ExModSystemTests).Assembly);
    ExDefinitions.Clear();
    Checks.ExCheckRegistry.Clear();
  }

  [BlockRegister]
  private sealed class TestBlock : Block { }

  [Checks.ExCheckRegister]
  private sealed class TestSystemCheck {
    public static Checks.CheckResult Run(
      Checks.ICheckSource source,
      string domain
    ) => new(nameof(TestSystemCheck), domain, []);
  }

  [SubCommandRegister(Side = EnumAppSide.Server)]
  private sealed class TestServerSubCommand : IExSubCommand {
    public string ParentName => "exmod";

    public void Register(ICoreAPI api, Mod mod, IChatCommand parent) =>
      parent
        .BeginSubCommand("exmodsystemtest-server")
        .HandleWith(_ => TextCommandResult.Success())
        .EndSubCommand();
  }

  [SubCommandRegister(Side = EnumAppSide.Client)]
  private sealed class TestClientSubCommand : IExSubCommand {
    public string ParentName => "exmod";

    public void Register(ICoreAPI api, Mod mod, IChatCommand parent) =>
      parent
        .BeginSubCommand("exmodsystemtest-client")
        .HandleWith(_ => TextCommandResult.Success())
        .EndSubCommand();
  }

  [PreferenceRegister]
  private sealed class TestSystemPreference : IExPreference {
    public string Key => "exmodsystemtestpref";
    public IReadOnlyList<string> Options { get; } = ["a", "b"];
    public string Default => "a";

    public void Apply(string value) { }
  }

  // A private target of this test class only; other test files' uncategorised [HarmonyPatch]
  // classes target their own private types and never mix counts with this one.
  private static class HarmonyTarget {
    public static void Method() { }
  }

  [HarmonyPatch(typeof(HarmonyTarget), nameof(HarmonyTarget.Method))]
  private static class HarmonyTargetPatch {
    private static void Prefix() { }
  }

  // A module of "exlibtest.host" via the test assembly's own [assembly: ExModule].
  private sealed class RecordingModule : IExModule {
    public static readonly List<string> Phases = [];

    public void Start(ICoreAPI api) => Phases.Add("Start");

    public void Dispose() => Phases.Add("Dispose");
  }

  private sealed class RecordingModSystem(bool patchHarmony) : ExModSystem {
    public List<string> Order { get; } = [];
    protected override bool PatchHarmony => patchHarmony;

    // Thrown by both side hooks when set.
    public Exception? SideHookThrows { get; set; }

    protected override void OnStart(ICoreAPI api) => Order.Add("OnStart");

    protected override void OnStartServerSide(ICoreServerAPI api) {
      Order.Add("OnStartServerSide");
      if (SideHookThrows != null)
        throw SideHookThrows;
    }

    protected override void OnStartClientSide(ICoreClientAPI api) {
      Order.Add("OnStartClientSide");
      if (SideHookThrows != null)
        throw SideHookThrows;
    }

    protected override void OnAssetsFinalize(ICoreAPI api) =>
      Order.Add("OnAssetsFinalize");
  }

  private static RecordingModSystem NewSystem(
    Mod mod,
    bool patchHarmony = false
  ) {
    var system = new RecordingModSystem(patchHarmony);
    ReflectionHelpers.SetProperty(system, nameof(ModSystem.Mod), mod);
    return system;
  }

  private static Mod FakeMod(string modId) {
    var mod = Substitute.For<Mod>();
    ReflectionHelpers.SetProperty(
      mod,
      nameof(Mod.Info),
      new ModInfo { ModID = modId }
    );
    // The module host logs through this when a hosted module's entry point throws.
    ReflectionHelpers.SetProperty(
      mod,
      nameof(Mod.Logger),
      new RecordingLogger()
    );
    return mod;
  }

  #region Start

  [Fact]
  public void Start_registers_entities_loads_config_and_then_runs_OnStart() {
    var world = new TestWorld();
    var system = NewSystem(FakeMod("exlibtest.exmodsystem-start"));

    system.Start(world.Api);

    world
      .Api.Received(1)
      .RegisterBlockClass(
        Arg.Is<string>(k => k.EndsWith("TestBlock")),
        typeof(TestBlock)
      );
    Assert.Equal(42, ExModSystemTestValues.Tunable);
    Assert.Contains(
      Checks.ExCheckRegistry.Registered,
      c => c.Type == typeof(TestSystemCheck)
    );
    Assert.Equal(["OnStart"], system.Order);
  }

  [Fact]
  public void Hosting_a_module_runs_it_through_the_mods_phases() {
    RecordingModule.Phases.Clear();
    var world = new TestWorld();
    var system = NewSystem(FakeMod("exlibtest.host"));

    system.Start(world.Api);
    system.Dispose();

    Assert.Contains("Start", RecordingModule.Phases);
    Assert.Contains("Dispose", RecordingModule.Phases);
  }

  #endregion

  #region StartServerSide / StartClientSide

  [Fact]
  public void StartServerSide_registers_server_commands_then_runs_the_hook() {
    var world = new TestWorld();
    var system = NewSystem(FakeMod("exlibtest.exmodsystem-server"));

    system.StartServerSide(world.Api);

    Assert.Equal(["OnStartServerSide"], system.Order);
  }

  [Fact]
  public void StartClientSide_registers_the_preference_before_running_the_hook() {
    var world = new TestWorld();
    var system = NewSystem(FakeMod("exlibtest.exmodsystem-client"));

    system.StartClientSide(world.ClientApi);

    Assert.IsType<TestSystemPreference>(
      ExPreferences.Find("exmodsystemtestpref")
    );
    Assert.Equal(["OnStartClientSide"], system.Order);
  }

  #endregion

  #region AssetsFinalize

  [Fact]
  public void AssetsFinalize_runs_the_hook() {
    var world = new TestWorld();
    var system = NewSystem(FakeMod("exlibtest.exmodsystem-finalize"));

    system.AssetsFinalize(world.Api);

    Assert.Equal(["OnAssetsFinalize"], system.Order);
  }

  #endregion

  #region PatchHarmony

  [Fact]
  public void PatchHarmony_true_patches_on_Start_and_unpatches_on_Dispose() {
    var mod = FakeMod("exlibtest.exmodsystem-harmony");
    var system = NewSystem(mod, patchHarmony: true);
    var world = new TestWorld();
    MethodBase original = typeof(HarmonyTarget).GetMethod(
      nameof(HarmonyTarget.Method)
    )!;

    try {
      system.Start(world.Api);
      Assert.Equal(1, Harmony.GetPatchInfo(original)?.Prefixes.Count);

      system.Dispose();
      var info = Harmony.GetPatchInfo(original);
      Assert.True(info == null || info.Prefixes.Count == 0);
    } finally {
      ExHarmony.UnpatchAll(mod);
    }
  }

  // Fails when Dispose releases a hold on an instance whose Start never took one.
  [Fact]
  public void Dispose_without_Start_leaves_another_holders_patches() {
    var mod = FakeMod("exlibtest.exmodsystem-harmony-unstarted");
    MethodBase original = typeof(HarmonyTarget).GetMethod(
      nameof(HarmonyTarget.Method)
    )!;

    try {
      ExHarmony.PatchOnce(mod, typeof(ExModSystemTests).Assembly);

      NewSystem(mod, patchHarmony: true).Dispose();

      Assert.Equal(1, Harmony.GetPatchInfo(original)?.Prefixes.Count);
    } finally {
      ExHarmony.UnpatchAll(mod);
    }
  }

  // Fails when a second Dispose of one instance releases a hold again.
  [Fact]
  public void A_second_Dispose_leaves_another_holders_patches() {
    var mod = FakeMod("exlibtest.exmodsystem-harmony-twice");
    var system = NewSystem(mod, patchHarmony: true);
    var world = new TestWorld();
    MethodBase original = typeof(HarmonyTarget).GetMethod(
      nameof(HarmonyTarget.Method)
    )!;

    try {
      ExHarmony.PatchOnce(mod, typeof(ExModSystemTests).Assembly);
      system.Start(world.Api);

      system.Dispose();
      system.Dispose();

      Assert.Equal(1, Harmony.GetPatchInfo(original)?.Prefixes.Count);
    } finally {
      ExHarmony.UnpatchAll(mod);
    }
  }

  private static void SideStart(
    RecordingModSystem system,
    TestWorld world,
    EnumAppSide side
  ) {
    if (side == EnumAppSide.Server)
      system.StartServerSide(world.Api);
    else
      system.StartClientSide(world.ClientApi);
  }

  // Another holder of the id stands in for the other side. Fails when a throwing side start keeps
  // its hold (the last assert), swallows the hook's exception or gives it a new stack.
  [Theory]
  [InlineData(EnumAppSide.Server)]
  [InlineData(EnumAppSide.Client)]
  public void A_side_start_that_throws_releases_its_hold_and_rethrows_the_hooks_exception(
    EnumAppSide side
  ) {
    const string id = "exlibtest.exmodsystem-failedstart";
    var mod = FakeMod(id);
    var system = NewSystem(mod, patchHarmony: true);
    var boom = new InvalidOperationException("side hook");
    system.SideHookThrows = boom;
    var world = new TestWorld();
    MethodBase original = typeof(HarmonyTarget).GetMethod(
      nameof(HarmonyTarget.Method)
    )!;

    try {
      ExHarmony.PatchOnce(id, typeof(ExModSystemTests).Assembly);
      system.Start(world.Api);

      Exception thrown = Assert.ThrowsAny<Exception>(() =>
        SideStart(system, world, side)
      );

      Assert.Same(boom, thrown);
      Assert.Contains(
        side == EnumAppSide.Server ? "OnStartServerSide" : "OnStartClientSide",
        thrown.StackTrace
      );
      Assert.Contains(id, Harmony.GetPatchInfo(original)!.Owners);
    } finally {
      ExHarmony.UnpatchAll(id);
    }
    Assert.DoesNotContain(
      id,
      (IEnumerable<string>?)Harmony.GetPatchInfo(original)?.Owners ?? []
    );
  }

  // As above for a module the system hosts, with the system itself patching nothing. Fails when a
  // throwing side start keeps the module host's hold.
  [Theory]
  [InlineData(EnumAppSide.Server)]
  [InlineData(EnumAppSide.Client)]
  public void A_side_start_that_throws_releases_its_module_hosts_hold(
    EnumAppSide side
  ) {
    const string moduleId = "exlibtest.host.exmodsystem-failedstart";
    Assembly assembly = typeof(ExModSystemTests).Assembly;
    var mod = FakeMod("exlibtest.exmodsystem-failedstart");
    var system = NewSystem(mod);
    var boom = new InvalidOperationException("side hook");
    system.SideHookThrows = boom;
    ReflectionHelpers.SetField(
      system,
      "_modules",
      new ExModuleHost(
        mod,
        new ExModuleSet(
          [
            new ExModuleInfo
            {
              Id = "exmodsystem-failedstart",
              Host = "exlibtest.host",
              Mod = "exlibtest.host",
              Requires = [],
              Assembly = assembly,
              EntryPoints = [],
              PatchHarmony = true,
            },
          ],
          []
        )
      )
    );
    var world = new TestWorld();
    MethodBase original = typeof(HarmonyTarget).GetMethod(
      nameof(HarmonyTarget.Method)
    )!;

    try {
      ExHarmony.PatchOnce(moduleId, assembly);
      system.Start(world.Api);

      Exception thrown = Assert.ThrowsAny<Exception>(() =>
        SideStart(system, world, side)
      );

      Assert.Same(boom, thrown);
      Assert.Contains(moduleId, Harmony.GetPatchInfo(original)!.Owners);
    } finally {
      ExHarmony.UnpatchAll(moduleId);
    }
    Assert.DoesNotContain(
      moduleId,
      (IEnumerable<string>?)Harmony.GetPatchInfo(original)?.Owners ?? []
    );
  }

  [Fact]
  public void PatchHarmony_false_never_patches() {
    var mod = FakeMod("exlibtest.exmodsystem-no-harmony");
    var system = NewSystem(mod, patchHarmony: false);
    var world = new TestWorld();
    MethodBase original = typeof(HarmonyTarget).GetMethod(
      nameof(HarmonyTarget.Method)
    )!;

    system.Start(world.Api);
    system.Dispose();

    var info = Harmony.GetPatchInfo(original);
    Assert.True(info == null || info.Prefixes.Count == 0);
  }

  #endregion
}
