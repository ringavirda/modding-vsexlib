using System.Collections.Generic;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="LangCoverageCheck"/> guards <c>en</c> by default: a missing <c>en</c> key is the
/// in-game defect (a raw key on screen). <c>allLocales: true</c> extends the same rule to every
/// shipped locale - the repo build's translation-parity check, run through
/// <c>ExpandedLib.Testing.LangCoverage</c>.
/// </summary>
public class LangCoverageCheckTests {
  private const string Domain = "stub";
  private static readonly AssetLocation[] Codes = [new($"{Domain}:stubblock")];

  private sealed class StubSource(params (string Locale, JObject Json)[] lang)
    : ICheckSource {
    public IEnumerable<string> Domains => [Domain];
    public IEnumerable<AssetLocation> BlockCodes => Codes;
    public IEnumerable<AssetLocation> ItemCodes => [];

    public IEnumerable<(AssetLocation File, JObject Json)> Recipes(
      string domain
    ) => [];

    public IEnumerable<(string Locale, JObject Json)> Lang(string domain) =>
      domain == Domain ? lang : [];

    public IEnumerable<ExBlockDef> BlockDefinitions(string domain) => [];
  }

  [Fact]
  public void Missing_en_key_is_an_error() {
    var source = new StubSource(("en", new JObject()));

    CheckResult result = LangCoverageCheck.Run(source, Domain);

    Assert.Single(result.Errors);
    Assert.Contains("en: block-stubblock", result.Errors);
  }

  [Fact]
  public void Missing_key_in_a_non_en_locale_is_not_an_error() {
    var source = new StubSource(
      ("en", new JObject { ["block-stubblock"] = "Stub Block" }),
      ("de", new JObject())
    );

    CheckResult result = LangCoverageCheck.Run(source, Domain);

    Assert.Empty(result.Errors);
  }

  [Fact]
  public void Missing_key_in_a_non_en_locale_is_reported_only_with_allLocales() {
    var source = new StubSource(
      ("en", new JObject { ["block-stubblock"] = "Stub Block" }),
      ("de", new JObject())
    );

    Assert.Empty(LangCoverageCheck.Run(source, Domain, allLocales: false).Errors);

    CheckResult result = LangCoverageCheck.Run(source, Domain, allLocales: true);
    Assert.Single(result.Errors);
    Assert.Contains("de: block-stubblock", result.Errors);
  }
}
