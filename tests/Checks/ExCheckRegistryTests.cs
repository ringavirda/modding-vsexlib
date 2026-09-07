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
/// scan is assembly-wide, not per-type. Cleared on both ends: whichever test in this class ran last
/// left registrations behind that would otherwise leak into every other test calling
/// <see cref="ExlibChecks.All(ICheckSource)"/> over this same, process-wide test assembly.
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

    // Eight shipped checks, plus the two fixtures above - the throw took down only its own entry.
    Assert.Equal(10, results.Count);
    CheckResult thrown = results.Single(r => r.Check == nameof(ThrowingCheck));
    Assert.Single(thrown.Errors);
    Assert.Contains("InvalidOperationException", thrown.Errors[0]);
    Assert.Equal(["seeded error"], results.Single(r => r.Check == "Passing").Errors);
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
