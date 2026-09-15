using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Tests <see cref="ExlibChecks.All"/> over a hand-built <see cref="ICheckSource"/>: each
/// of the eight checks reports its own seeded violation and nothing else.</summary>
[Collection("ExDefinitions")] // process-wide static; shared with other classes that mutate it
public class ExlibChecksTests {
  public ExlibChecksTests() {
    ExDefinitions.Clear();
    ExDefinitions.RecordInjected([]);
  }

  private const string Domain = "stub";

  // Base code, a one-state `type` group, and the four-way `orientation` group NetworkOriented() reads.
  private static ExBlockDef Node() =>
    ExBlockDef
      .Create(Domain, "stubnode")
      .VariantGroup("type", "normal")
      .VariantGroup("orientation", "n", "e", "s", "w")
      .NetworkOriented();

  // A multiblock principal: one dangling blockNumbers entry, one multiblockFacings entry pinning a
  // concrete orientation code.
  private static ExBlockDef Wall() =>
    ExBlockDef
      .Create(Domain, "stubwall")
      .Attribute(
        "multiblockStructure",
        new JObject {
          ["blockNumbers"] = new JObject {
            ["stub:stubwall"] = 1,
            ["stub:missingblock"] = 2,
          },
        }
      )
      .Attribute(
        "multiblockFacings",
        new JObject { ["stub:stubnode-normal-n"] = new JArray(2) }
      );

  // A base code and a variant of it under a different def, colliding with a wildcard built from the
  // shorter code.
  private static ExBlockDef Family() => ExBlockDef.Create(Domain, "stubfam");

  private static ExBlockDef FamilyVariant() =>
    ExBlockDef.Create(Domain, "stubfam-big");

  // Registered, but the loader produced no block for it.
  private static ExBlockDef Ghost() => ExBlockDef.Create(Domain, "stubghost");

  // A network node with no `type` variant group; AllowedOrientations has nothing to contribute.
  private static ExBlockDef NodeMissingTypeGroup() =>
    ExBlockDef
      .Create(Domain, "stubnodenotype")
      .VariantGroup("orientation", "n", "e", "s", "w")
      .NetworkOriented();

  // A network node whose declared `scheme` does not match its `orientation` states, built with
  // Behavior directly.
  private static ExBlockDef NodeMisspelledScheme() =>
    ExBlockDef
      .Create(Domain, "stubnodebadscheme")
      .VariantGroup("type", "normal")
      .VariantGroup("orientation", "n", "e", "s", "w")
      .Behavior("ExOrientable", new { mode = "network", scheme = "FaceAll" });

  // A declared network membership with no `networkType`.
  private static ExBlockDef NodeUntypedMembership() =>
    ExBlockDef
      .Create(Domain, "stubmembership")
      .EntityBehavior("BEBehaviorNetworkMember");

  // The concrete codes these seven defs register, matched by hand with no game to ask. "stubghost"
  // is deliberately absent.
  private static readonly AssetLocation[] RegisteredCodes =
  [
    new("stub:stubnode-normal-n"),
    new("stub:stubnode-normal-e"),
    new("stub:stubnode-normal-s"),
    new("stub:stubnode-normal-w"),
    new("stub:stubwall"),
    new("stub:stubfam"),
    new("stub:stubfam-big"),
    new("stub:stubnodenotype-n"),
    new("stub:stubnodenotype-e"),
    new("stub:stubnodenotype-s"),
    new("stub:stubnodenotype-w"),
    new("stub:stubnodebadscheme-normal-n"),
    new("stub:stubnodebadscheme-normal-e"),
    new("stub:stubnodebadscheme-normal-s"),
    new("stub:stubnodebadscheme-normal-w"),
    new("stub:stubmembership"),
  ];

  // Every registered code gets an English name key except "stubfam".
  private static readonly JObject EnglishLang = new(
    RegisteredCodes
      .Where(c => c.Path != "stubfam")
      .Select(c => new JProperty("block-" + c.Path, "Stub Block"))
  );

  // A method, not a static field: a tuple-typed field forces early ValueTuple layout, which races
  // the harness's assembly resolver and fails test discovery.
  private static (AssetLocation File, JObject Json) DanglingRecipe() =>
    (
      new AssetLocation("stub", "recipes/grid/stubrecipe.json"),
      new JObject {
        ["output"] = new JObject {
          ["type"] = "block",
          ["code"] = "stub:missingrecipeoutput",
        },
      }
    );

  private sealed class StubCheckSource : ICheckSource {
    public IEnumerable<string> Domains => [Domain];
    public IEnumerable<AssetLocation> BlockCodes => RegisteredCodes;
    public IEnumerable<AssetLocation> ItemCodes => [];

    public IEnumerable<(AssetLocation File, JObject Json)> Recipes(
      string domain
    ) => domain == Domain ? [DanglingRecipe()] : [];

    public IEnumerable<(string Locale, JObject Json)> Lang(string domain) =>
      domain == Domain ? [("en", EnglishLang)] : [];

    public IEnumerable<ExBlockDef> BlockDefinitions(string domain) =>
      domain == Domain
        ?
        [
          Node(),
          Wall(),
          Family(),
          FamilyVariant(),
          Ghost(),
          NodeMissingTypeGroup(),
          NodeMisspelledScheme(),
          NodeUntypedMembership(),
        ]
        : [];
  }

  private static IReadOnlyList<CheckResult> Results() =>
    ExlibChecks.All(new StubCheckSource());

  private static IReadOnlyList<string> ErrorsOf(string check) =>
    Results().Single(r => r.Check == check).Errors;

  [Fact]
  public void Multiblock_codes_reports_only_the_dangling_cell() {
    IReadOnlyList<string> errors = ErrorsOf("MultiblockCodes");
    Assert.Single(errors);
    Assert.Contains("missingblock", errors[0]);
  }

  [Fact]
  public void Recipe_codes_reports_only_the_dangling_output() {
    IReadOnlyList<string> errors = ErrorsOf("RecipeCodes");
    Assert.Single(errors);
    Assert.Contains("missingrecipeoutput", errors[0]);
  }

  [Fact]
  public void Lang_coverage_reports_only_the_missing_key() {
    IReadOnlyList<string> errors = ErrorsOf("LangCoverage");
    Assert.Single(errors);
    Assert.Contains("block-stubfam", errors[0]);
  }

  [Fact]
  public void Pinned_network_nodes_reports_only_the_pinned_node() {
    IReadOnlyList<string> errors = ErrorsOf("PinnedNetworkNodes");
    Assert.Single(errors);
    Assert.Contains("stubnode-normal-n", errors[0]);
  }

  [Fact]
  public void Code_prefix_collision_reports_only_the_family_pair() {
    IReadOnlyList<string> errors = ErrorsOf("CodePrefixCollision");
    Assert.Single(errors);
    Assert.Contains("stubfam", errors[0]);
    Assert.Contains("stubfam-big", errors[0]);
  }

  [Fact]
  public void Clean_checks_report_nothing() {
    Assert.Empty(ErrorsOf("LateDefinition"));
  }

  [Fact]
  public void Definition_catalogue_reports_only_the_unregistered_def() {
    IReadOnlyList<string> errors = ErrorsOf("DefinitionCatalogue");
    Assert.Single(errors);
    Assert.Contains("stubghost", errors[0]);
  }

  [Fact]
  public void Network_node_contract_reports_the_missing_type_group_the_misspelled_scheme_and_the_untyped_membership() {
    IReadOnlyList<string> errors = ErrorsOf("NetworkNodeContract");
    Assert.Equal(3, errors.Count);
    Assert.Contains(
      errors,
      e => e.Contains("stubnodenotype") && e.Contains("`type`")
    );
    Assert.Contains(
      errors,
      e => e.Contains("stubnodebadscheme") && e.Contains("scheme")
    );
    Assert.Contains(
      errors,
      e => e.Contains("stubmembership") && e.Contains("networkType")
    );
  }

  [Fact]
  public void Late_definition_reports_through_ExlibChecks_once_injection_has_run() {
    ExDefinitions.RegisterBlock(ExBlockDef.Create(Domain, "toolate"));

    IReadOnlyList<string> errors = ErrorsOf("LateDefinition");

    Assert.Single(errors);
    Assert.Contains("stub:toolate", errors[0]);
  }

  [Fact]
  public void All_examines_every_check_for_every_domain_and_nothing_more() {
    IReadOnlyList<CheckResult> results = Results();
    // One CheckResult per (check, domain) pair - eight checks, one domain here.
    Assert.Equal(8, results.Count);
    Assert.All(results, r => Assert.Equal(Domain, r.Domain));
    Assert.Equal(9, results.Sum(r => r.Errors.Count));
  }

  [Fact]
  public void Log_writes_one_summary_line_per_check() {
    ILogger logger = Substitute.For<ILogger>();
    IReadOnlyList<CheckResult> results = Results();

    ExlibChecks.Log(logger, results);

    int summaryLines = logger
      .ReceivedCalls()
      .Count(call =>
        call.GetMethodInfo().Name == nameof(ILogger.Notification)
        && call.GetArguments() is [string fmt, object[] args]
        && fmt == "[exlib] check {0} ({1}): {2} error(s)"
        && args.Length == 3
      );
    Assert.Equal(results.Count, summaryLines);
  }

  [Fact]
  public void Log_writes_each_error_line_at_Error_not_Notification() {
    ILogger logger = Substitute.For<ILogger>();
    IReadOnlyList<CheckResult> results = Results();

    ExlibChecks.Log(logger, results);

    int errorLines = logger
      .ReceivedCalls()
      .Count(call =>
        call.GetMethodInfo().Name == nameof(ILogger.Error)
        && call.GetArguments() is [string fmt, object[] args]
        && fmt == "[exlib]   {0}"
        && args.Length == 1
      );
    Assert.Equal(results.Sum(r => r.Errors.Count), errorLines);

    int summaryLines = logger
      .ReceivedCalls()
      .Count(call => call.GetMethodInfo().Name == nameof(ILogger.Notification));
    Assert.Equal(results.Count, summaryLines);
  }

  [Fact]
  public void AssetCheckSource_over_a_TestWorld_enumerates_its_registered_blocks_and_items() {
    var world = new TestWorld();
    world.Register(TestBlocks.Configure(new Block(), "test:stubblock", 42));
    world.RegisterItem("test:stubitem");

    var source = new AssetCheckSource(world.Api);

    Assert.Contains(source.BlockCodes, c => c.ToString() == "test:stubblock");
    Assert.Contains(source.ItemCodes, c => c.ToString() == "test:stubitem");
  }
}
