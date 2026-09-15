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
/// <see cref="ExModuleHost"/> tests: each case builds its own host. Joins
/// <see cref="ExHarmonyCollection"/>; the Harmony case patches a real, process-wide target.
/// </summary>
[Collection(ExHarmonyCollection.Name)]
public class ExModuleHostTests : IDisposable {
  public ExModuleHostTests() {
    RecordingModule.Phases.Clear();
    RecordingModule.Created.Clear();
    RecordingModule.ObservedKey = null;
  }

  // Clears the process-wide state RegisterAll leaves behind: domain map, ExDefinitions,
  // ExCheckRegistry.
  public void Dispose() {
    var field = typeof(EntityRegistry).GetField(
      "_domainByAssembly",
      BindingFlags.NonPublic | BindingFlags.Static
    )!;
    var map = (Dictionary<Assembly, string>)field.GetValue(null)!;
    map.Remove(typeof(ExModuleHostTests).Assembly);
    ExDefinitions.Clear();
    Checks.ExCheckRegistry.Clear();
  }

  [BlockRegister]
  private sealed class TestBlock : Block { }

  [Checks.ExCheckRegister]
  private sealed class TestHostCheck {
    public static Checks.CheckResult Run(
      Checks.ICheckSource source,
      string domain
    ) => new(nameof(TestHostCheck), domain, []);
  }

  [PreferenceRegister]
  private sealed class TestHostPreference : IExPreference {
    public string Key => "exmodulehosttestpref";
    public IReadOnlyList<string> Options { get; } = ["a", "b"];
    public string Default => "a";

    public void Apply(string value) { }
  }

  // A module of "exlibtest.host" via the test assembly's own [assembly: ExModule].
  private sealed class RecordingModule : IExModule {
    public static readonly List<string> Phases = [];
    public static readonly List<RecordingModule> Created = [];

    // Set by Start; an assertion failure raised here is swallowed by the host's isolation.
    public static string? ObservedKey;

    public RecordingModule() => Created.Add(this);

    public void StartPre(ICoreAPI api) => Phases.Add("StartPre");

    public void Start(ICoreAPI api) {
      // Resolves through the domain RegisterAll just recorded, not the unregistered fallback.
      ObservedKey = EntityRegistry.KeyFor(
        "not-exlibtest.host",
        typeof(TestBlock)
      );
      Phases.Add("Start");
    }

    public void StartServerSide(ICoreServerAPI api) =>
      Phases.Add("StartServerSide");

    public void StartClientSide(ICoreClientAPI api) =>
      Phases.Add("StartClientSide");

    public void AssetsLoaded(ICoreAPI api) => Phases.Add("AssetsLoaded");

    public void AssetsFinalize(ICoreAPI api) => Phases.Add("AssetsFinalize");

    public void Dispose() => Phases.Add("Dispose");
  }

  // A discovered entry point of "exlibtests"; every phase but Start is a no-op.
  private sealed class ThrowingModule : IExModule {
    public void Start(ICoreAPI api) =>
      throw new InvalidOperationException("ThrowingModule always throws.");
  }

  private static class HarmonyTarget {
    public static void Method() { }
  }

  [HarmonyPatch(typeof(HarmonyTarget), nameof(HarmonyTarget.Method))]
  private static class HarmonyTargetPatch {
    private static void Prefix() { }
  }

  private static Mod FakeMod(string modId, ILogger? logger = null) {
    var mod = Substitute.For<Mod>();
    ReflectionHelpers.SetProperty(
      mod,
      nameof(Mod.Info),
      new ModInfo { ModID = modId }
    );
    ReflectionHelpers.SetProperty(
      mod,
      nameof(Mod.Logger),
      logger ?? new RecordingLogger()
    );
    return mod;
  }

  [Fact]
  public void Two_hosts_get_distinct_entry_point_instances() {
    var world = new TestWorld();
    _ = new ExModuleHost(FakeMod("exlibtest.host"), world.Api);
    _ = new ExModuleHost(FakeMod("exlibtest.host"), world.Api);

    Assert.Equal(2, RecordingModule.Created.Count);
    Assert.NotSame(RecordingModule.Created[0], RecordingModule.Created[1]);
  }

  [Fact]
  public void Runs_every_phase() {
    var world = new TestWorld();
    var host = new ExModuleHost(FakeMod("exlibtest.host"), world.Api);

    host.StartPre(world.Api);
    host.Start(world.Api);
    host.AssetsLoaded(world.Api);
    host.AssetsFinalize(world.Api);
    host.StartServerSide(world.Api);
    host.StartClientSide(world.ClientApi);
    host.Dispose();

    // Matches the engine's own phase order.
    Assert.Equal(
      [
        "StartPre",
        "Start",
        "AssetsLoaded",
        "AssetsFinalize",
        "StartServerSide",
        "StartClientSide",
        "Dispose",
      ],
      RecordingModule.Phases
    );
    Assert.IsType<TestHostPreference>(
      ExPreferences.Find("exmodulehosttestpref")
    );
  }

  [Fact]
  public void Start_registers_the_module_assemblys_classes_before_its_entry_points() {
    var world = new TestWorld();
    var host = new ExModuleHost(FakeMod("exlibtest.host"), world.Api);

    host.Start(world.Api);

    world
      .Api.Received(1)
      .RegisterBlockClass(
        Arg.Is<string>(k => k.EndsWith("TestBlock")),
        typeof(TestBlock)
      );
    Assert.Contains("Start", RecordingModule.Phases);
    Assert.Equal("exlibtest.host.TestBlock", RecordingModule.ObservedKey);
    Assert.Contains(
      Checks.ExCheckRegistry.Registered,
      c => c.Type == typeof(TestHostCheck)
    );
  }

  [Fact]
  public void A_throwing_entry_point_is_logged_and_the_rest_continue() {
    var world = new TestWorld();
    var logger = new RecordingLogger();
    var host = new ExModuleHost(FakeMod("exlibtest.host", logger), world.Api);

    host.Start(world.Api);

    Assert.Contains("Start", RecordingModule.Phases);
    Assert.Contains(logger.Errors, e => e.Contains("ThrowingModule"));
  }

  [Fact]
  public void A_registration_only_module_still_registers_its_classes() {
    var world = new TestWorld();
    var set = new ExModuleSet(
      [
        new ExModuleInfo
        {
          Id = "registration-only",
          Host = "exlibtest.host",
          Mod = "exlibtest.host",
          Requires = [],
          Assembly = typeof(ExModuleHostTests).Assembly,
          EntryPoints = [],
        },
      ],
      []
    );
    var host = new ExModuleHost(FakeMod("exlibtest.host"), set);

    host.Start(world.Api);

    world
      .Api.Received(1)
      .RegisterBlockClass(
        Arg.Is<string>(k => k.EndsWith("TestBlock")),
        typeof(TestBlock)
      );
  }

  [Fact]
  public void Resolution_errors_are_logged_at_StartPre() {
    var world = new TestWorld();
    var logger = new RecordingLogger();
    var set = new ExModuleSet([], ["boom"]);
    var host = new ExModuleHost(FakeMod("exlibtest.host", logger), set);

    host.StartPre(world.Api);

    Assert.Contains(logger.Errors, e => e.Contains("boom"));
  }

  [Fact]
  public void PatchHarmony_patches_under_the_module_id_and_unpatches_on_Dispose() {
    var world = new TestWorld();
    var set = new ExModuleSet(
      [
        new ExModuleInfo
        {
          Id = "exlibtests",
          Host = "exlibtest.host",
          Mod = "exlibtest.host",
          Requires = [],
          Assembly = typeof(ExModuleHostTests).Assembly,
          EntryPoints = [],
          PatchHarmony = true,
        },
      ],
      []
    );
    var host = new ExModuleHost(FakeMod("exlibtest.host"), set);
    MethodBase original = typeof(HarmonyTarget).GetMethod(
      nameof(HarmonyTarget.Method)
    )!;

    try {
      host.Start(world.Api);
      Assert.Contains(
        "exlibtest.host.exlibtests",
        Harmony.GetPatchInfo(original)!.Owners
      );

      host.Dispose();
      var info = Harmony.GetPatchInfo(original);
      Assert.DoesNotContain(
        "exlibtest.host.exlibtests",
        (IEnumerable<string>?)info?.Owners ?? []
      );
    } finally {
      ExHarmony.UnpatchAll("exlibtest.host.exlibtests");
    }
  }
}
