using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Checks;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="ExCheckRegistry"/> discovery and its effect on <see cref="ExlibChecks.All(ICheckSource)"/>:
/// a registered check's own <see cref="CheckResult"/> comes back through the run list, a throwing
/// check is isolated rather than taking the rest of the pass down, and scanning the same type twice
/// registers it once. Both fixture checks below carry <c>[ExCheckRegister]</c>, so every
/// <see cref="ExCheckRegistry.RegisterAll"/> call in this file registers both of them at once - the
/// scan is assembly-wide, not per-type, so it also picks up every other <c>[ExCheckRegister]</c>
/// fixture in this test assembly (ExModSystemTests' and ExModuleHostTests' own), which is why a test
/// that cares about the exact run list filters by name rather than by count. Cleared on both ends:
/// whichever test in this class ran last left registrations behind that would otherwise leak into
/// every other test calling <see cref="ExlibChecks.All(ICheckSource)"/> over this same, process-wide
/// test assembly.
/// </summary>
[Collection("ExCheckRegistry")]
public class ExCheckRegistryTests : IDisposable {
  public ExCheckRegistryTests() => ExCheckRegistry.Clear();

  public void Dispose() => ExCheckRegistry.Clear();

  private const string Domain = "stub";

  private sealed class StubCheckSource : ICheckSource {
    public IEnumerable<string> Domains => [Domain];
    public IEnumerable<AssetLocation> BlockCodes => [];
    public IEnumerable<AssetLocation> ItemCodes => [];

    public IEnumerable<(AssetLocation File, Newtonsoft.Json.Linq.JObject Json)> Recipes(
      string domain
    ) => [];

    public IEnumerable<(string Locale, Newtonsoft.Json.Linq.JObject Json)> Lang(
      string domain
    ) => [];

    public IEnumerable<Definitions.ExBlockDef> BlockDefinitions(string domain) =>
      [];
  }

  // Non-static: a static class compiles to abstract sealed, which the assembly scan behind
  // RegisterAll skips (see the wiki's own note against writing one this way).
  [ExCheckRegister]
  private sealed class PassingCheck {
    public static CheckResult Run(ICheckSource source, string domain) =>
      new("Passing", domain, ["seeded error"]);
  }

  [ExCheckRegister]
  private sealed class ThrowingCheck {
    public static CheckResult Run(ICheckSource source, string domain) =>
      throw new System.InvalidOperationException("boom");
  }

  // A static class compiles to abstract sealed, so ReflectionScan.GetCandidateTypes drops it before
  // the attribute is ever read - no registration and no log line either.
  [ExCheckRegister]
  private static class StaticCheck {
    public static CheckResult Run(ICheckSource source, string domain) =>
      new(nameof(StaticCheck), domain, []);
  }

  // Carries the attribute but not the exact `static CheckResult Run(ICheckSource, string)` shape -
  // Register warns and skips it rather than registering nothing silently.
  [ExCheckRegister]
  private sealed class WrongSignatureCheck {
    public static void Run(ICheckSource source, string domain) { }
  }

  private static Mod FakeMod() {
    var mod = Substitute.For<Mod>();
    typeof(Mod)
      .GetProperty("Info")!
      .SetValue(mod, new ModInfo { ModID = "stubmod", Version = "1.0.0" });
    return mod;
  }

  [Fact]
  public void A_registered_checks_errors_appear_in_ExlibChecks_All() {
    var api = new TestWorld().Api;
    ExCheckRegistry.RegisterAll(api, FakeMod(), typeof(PassingCheck).Assembly);

    IReadOnlyList<CheckResult> results = ExlibChecks.All(new StubCheckSource());

    CheckResult passing = results.Single(r => r.Check == "Passing");
    Assert.Equal(["seeded error"], passing.Errors);
  }

  [Fact]
  public void A_throwing_check_is_reported_and_does_not_stop_the_others() {
    var api = new TestWorld().Api;
    ExCheckRegistry.RegisterAll(api, FakeMod(), typeof(ThrowingCheck).Assembly);

    IReadOnlyList<CheckResult> results = ExlibChecks.All(new StubCheckSource());

    // Eight shipped checks, plus every [ExCheckRegister] fixture in this test assembly (the two
    // below, and ExModSystemTests'/ExModuleHostTests' own) - the throw took down only its own entry.
    CheckResult thrown = results.Single(r => r.Check == nameof(ThrowingCheck));
    Assert.Single(thrown.Errors);
    Assert.Contains("InvalidOperationException", thrown.Errors[0]);
    Assert.Equal(["seeded error"], results.Single(r => r.Check == "Passing").Errors);
  }

  // The eight shipped checks, by the name their own CheckResult carries, in ExlibChecks._checks'
  // order.
  private static readonly string[] ShippedChecks =
  [
    "DefinitionCatalogue",
    "LateDefinition",
    "MultiblockCodes",
    "RecipeCodes",
    "LangCoverage",
    "NetworkNodeContract",
    "PinnedNetworkNodes",
    "CodePrefixCollision",
  ];

  [Fact]
  public void Registered_checks_run_after_the_eight_shipped_checks_in_registration_order() {
    var api = new TestWorld().Api;
    ExCheckRegistry.RegisterAll(api, FakeMod(), typeof(PassingCheck).Assembly);

    var results = ExlibChecks.All(new StubCheckSource()).ToList();

    Assert.Equal(ShippedChecks, results.Take(8).Select(r => r.Check));
    int passingAt = results.FindIndex(r => r.Check == "Passing");
    int throwingAt = results.FindIndex(r => r.Check == nameof(ThrowingCheck));
    Assert.True(passingAt >= 8);
    Assert.True(throwingAt >= 8);
    Assert.True(passingAt < throwingAt);
  }

  [Fact]
  public void A_static_class_is_dropped_before_the_attribute_is_ever_read() {
    var world = new TestWorld();
    ExCheckRegistry.RegisterAll(world.Api, FakeMod(), typeof(StaticCheck).Assembly);

    Assert.DoesNotContain(
      ExCheckRegistry.Registered,
      c => c.Type == typeof(StaticCheck)
    );
    Assert.DoesNotContain(
      world.Log.Warnings,
      w => w.Contains(nameof(StaticCheck))
    );
  }

  [Fact]
  public void A_wrong_signature_is_warned_about_and_skipped() {
    var world = new TestWorld();
    ExCheckRegistry.RegisterAll(world.Api, FakeMod(), typeof(WrongSignatureCheck).Assembly);

    Assert.DoesNotContain(
      ExCheckRegistry.Registered,
      c => c.Type == typeof(WrongSignatureCheck)
    );
    Assert.Contains(
      world.Log.Warnings,
      w => w.Contains(nameof(WrongSignatureCheck))
    );
  }

  [Fact]
  public void Scanning_the_same_assembly_twice_registers_each_type_once() {
    var api = new TestWorld().Api;
    ExCheckRegistry.RegisterAll(api, FakeMod(), typeof(PassingCheck).Assembly);
    ExCheckRegistry.RegisterAll(api, FakeMod(), typeof(PassingCheck).Assembly);

    IReadOnlyList<CheckResult> results = ExlibChecks.All(new StubCheckSource());

    Assert.Single(results, r => r.Check == "Passing");
    Assert.Single(results, r => r.Check == nameof(ThrowingCheck));
  }
}
