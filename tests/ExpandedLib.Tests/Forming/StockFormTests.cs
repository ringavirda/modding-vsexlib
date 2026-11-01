using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Industry.Forming;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests.Forming;

/// <summary>Stock forms and mill feedstock: the registry from JSON and C#, the former names, the clash
/// rules, the spread formula. Shares the process-wide <see cref="StockFormRegistry.Shared"/>.</summary>
[Collection("WorldState")]
public class StockFormTests : IDisposable {
  public void Dispose() => StockFormRegistry.ResetForWorld();

  private const string Bar = """
    { "schema": 1, "forms": [ {
      "name": "testbar", "baseWidth": 3, "baseThickness": 3, "maxWidth": 8,
      "baseLength": 18, "spreadExponent": 0.846,
      "shape": "test:forming/testbar", "baseElement": "TestBar1",
      "formerNames": ["oldbar"] } ] }
    """;

  private static StockFormRegistry Registry(params string[] files) {
    var registry = new StockFormRegistry();
    var sources = new List<(string, string)>();
    foreach (string json in files)
      sources.Add(($"test:config/stockforms/f{sources.Count}.json", json));
    StockFormLoader.Load(sources, registry);
    return registry;
  }

  private static List<string> Errors(
    StockFormRegistry registry,
    params string[] files
  ) {
    var sources = new List<(string, string)>();
    foreach (string json in files)
      sources.Add(($"test:config/stockforms/f{sources.Count}.json", json));
    return StockFormLoader.Load(sources, registry);
  }

  private static string Form(string name, float maxWidth) =>
    $$"""
      { "schema": 1, "forms": [ {
        "name": "{{name}}", "baseWidth": 3, "baseThickness": 3,
        "maxWidth": {{maxWidth}}, "baseLength": 16, "spreadExponent": 0.8 } ] }
      """;

  private static TestWorld WorldWithNoAssets() {
    var world = new TestWorld();
    var assets = Substitute.For<IAssetManager>();
    assets
      .GetMany(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
      .Returns(new List<IAsset>());
    ((ICoreAPI)world.Api).Assets.Returns(assets);
    return world;
  }

  [Fact]
  public void A_form_from_json_is_found_by_name() {
    StockFormRegistry registry = Registry(Bar);

    Assert.True(registry.TryGet("testbar", out StockForm? form));
    Assert.Equal(8f, form!.MaxWidth);
    Assert.Equal(18f, form.BaseLength);
    Assert.Equal("TestBar1", form.BaseElement);
    Assert.Contains("testbar", registry.All.Keys);
  }

  [Fact]
  public void An_unregistered_form_is_absent_rather_than_a_throw() {
    StockFormRegistry registry = Registry(Bar);

    Assert.False(registry.TryGet("nosuchform", out _));
    Assert.False(registry.TryGet(null, out _));
  }

  // Fails when former names are put in All.
  [Fact]
  public void A_form_resolves_by_a_former_name_and_stays_out_of_All() {
    StockFormRegistry registry = Registry(Bar);

    Assert.True(registry.TryGet("oldbar", out StockForm? form));
    Assert.Equal("testbar", form!.Name);
    Assert.DoesNotContain("oldbar", registry.All.Keys);
  }

  // Fails when a redeclaration replaces the held form.
  [Fact]
  public void A_form_redeclared_with_another_width_is_reported_and_the_first_stands() {
    var registry = new StockFormRegistry();

    List<string> errors = Errors(
      registry,
      Form("testbar", 8f),
      Form("testbar", 12f)
    );

    Assert.Contains("testbar", Assert.Single(errors));
    Assert.Equal(8f, registry.All["testbar"].MaxWidth);
  }

  [Fact]
  public void An_identical_redeclaration_is_a_no_op() {
    var registry = new StockFormRegistry();

    List<string> errors = Errors(registry, Bar, Bar);

    Assert.Empty(errors);
    Assert.Single(registry.All);
  }

  [Fact]
  public void A_former_name_another_form_holds_is_reported() {
    var registry = new StockFormRegistry();
    string other = Bar.Replace("\"testbar\"", "\"otherbar\"");

    List<string> errors = Errors(registry, Bar, other);

    Assert.Contains("oldbar", Assert.Single(errors));
    Assert.True(registry.TryGet("oldbar", out StockForm? form));
    Assert.Equal("testbar", form!.Name);
  }

  [Fact]
  public void Feedstock_enters_as_its_stock_item() {
    StockFormRegistry registry = Registry(
      """{ "feedstock": [ { "offered": "game:rod-iron", "entersAs": "test:stock-rod" } ] }"""
    );

    Assert.Equal("test:stock-rod", registry.EntersAs("game:rod-iron"));
    Assert.Null(registry.EntersAs("game:ingot-iron"));
    Assert.Null(registry.EntersAs(null));
  }

  // Fails when a second declaration replaces the first.
  [Fact]
  public void A_feedstock_clash_is_reported_and_the_first_stands() {
    var registry = new StockFormRegistry();

    List<string> errors = Errors(
      registry,
      """{ "feedstock": [ { "offered": "game:rod-iron", "entersAs": "test:a" } ] }""",
      """{ "feedstock": [ { "offered": "game:rod-iron", "entersAs": "test:b" } ] }"""
    );

    Assert.Contains("game:rod-iron", Assert.Single(errors));
    Assert.Equal("test:a", registry.EntersAs("game:rod-iron"));
  }

  [Fact]
  public void Stock_is_never_feedstock_for_itself() {
    StockFormRegistry registry = Registry(
      """{ "feedstock": [ { "offered": "game:rod-iron", "entersAs": "test:stock-rod" } ] }"""
    );

    foreach (string entersAs in registry.Feedstock.Values)
      Assert.Null(registry.EntersAs(entersAs));
  }

  [Theory]
  [InlineData(
    """{ "forms": [ { "name": "Bar", "baseWidth": 1, "baseThickness": 1, "maxWidth": 1, "baseLength": 1, "spreadExponent": 1 } ] }""",
    "bare lowercase"
  )]
  [InlineData(
    """{ "forms": [ { "name": "bar", "baseWidth": 0, "baseThickness": 1, "maxWidth": 1, "baseLength": 1, "spreadExponent": 1 } ] }""",
    "baseWidth"
  )]
  [InlineData(
    """{ "feedstock": [ { "offered": "rod", "entersAs": "test:x" } ] }""",
    "full item codes"
  )]
  [InlineData("""{ "formz": [] }""", "formz")]
  public void A_malformed_file_is_reported_and_skipped(
    string json,
    string fragment
  ) {
    var registry = new StockFormRegistry();

    List<string> errors = Errors(registry, json);

    Assert.Contains(fragment, Assert.Single(errors));
    Assert.Empty(registry.All);
    Assert.Empty(registry.Feedstock);
  }

  // Fails when the exponent's sign is flipped.
  [Fact]
  public void Squeezed_metal_spreads_sideways_as_it_thins() {
    Assert.Equal(3f, StockForm.SpreadWidth(3f, 3f, 3f), 4);
    Assert.Equal(4.243f, StockForm.SpreadWidth(3f, 3f, 1.5f), 3);
    Assert.Equal(7.348f, StockForm.SpreadWidth(3f, 3f, 0.5f), 3);
  }

  [Fact]
  public void Degenerate_inputs_leave_the_width_alone() {
    Assert.Equal(3f, StockForm.SpreadWidth(3f, 3f, 0f), 4);
    Assert.Equal(3f, StockForm.SpreadWidth(3f, 0f, 1f), 4);
    Assert.Equal(0f, StockForm.SpreadWidth(-1f, 3f, 1f), 4);
  }

  [Fact]
  public void A_form_widens_to_its_cap_and_no_further() {
    var form = new StockForm("capbar", 3f, 3f, 5f, 16f, 0.5f);

    Assert.Equal(4.243f, form.WidthAt(1.5f), 3);
    Assert.Equal(5f, form.WidthAt(0.5f), 3);
  }

  // Fails when the reset body stops emptying the registry.
  [Fact]
  public void A_reset_empties_forms_feedstock_and_contributors() {
    StockFormRegistry.Shared.Contribute(
      StockFormSetFor("leftover", "game:rod-iron", "test:stock")
    );
    StockFormRegistry.Contributors.Register(_ => { });

    StockFormRegistry.ResetForWorld();

    Assert.Empty(StockFormRegistry.Shared.All);
    Assert.Empty(StockFormRegistry.Shared.Feedstock);
    Assert.Equal(0, StockFormRegistry.Contributors.Count);
  }

  // Fails when IndustryModule.StartPre stops calling StockFormRegistry.ResetForWorld.
  [Fact]
  public void A_server_load_start_empties_the_form_registry_and_its_contributors() {
    StockFormRegistry.Shared.Contribute(
      StockFormSetFor("leftover", "game:rod-iron", "test:stock")
    );
    StockFormRegistry.Contributors.Register(_ => { });
    var world = new TestWorld();
    var system = new ExModuleModSystem();
    ReflectionHelpers.SetProperty(
      system,
      nameof(ModSystem.Mod),
      world.Mods.GetMod("exlib")!
    );

    system.StartPre(world.Api);

    Assert.Empty(StockFormRegistry.Shared.All);
    Assert.Equal(0, StockFormRegistry.Contributors.Count);
  }

  // Fails when a C# contribution is not re-applied by the load that follows a clear.
  [Fact]
  public void A_C_sharp_contributor_adds_a_form_on_every_load() {
    StockFormRegistry.Contributors.Register(_ =>
      StockFormRegistry.Shared.Contribute(
        StockFormSetFor("csbar", "game:rod-iron", "test:stock")
      )
    );
    TestWorld world = WorldWithNoAssets();

    StockFormLoader.Load(world.Api);
    StockFormRegistry.Shared.Clear();
    StockFormLoader.Load(world.Api);

    Assert.True(StockFormRegistry.Shared.TryGet("csbar", out _));
  }

  // Fails when the counts are taken before the contributors run.
  [Fact]
  public void The_logged_counts_include_what_the_contributors_added() {
    StockFormRegistry.Contributors.Register(_ =>
      StockFormRegistry.Shared.Contribute(
        StockFormSetFor("csbar", "game:rod-iron", "test:stock")
      )
    );
    TestWorld world = WorldWithNoAssets();

    StockFormLoader.Load(world.Api);

    Assert.Contains(
      world.Log.Entries,
      e =>
        e.Message
        == "[exlib] stockforms: 1 form(s), 1 feedstock after contributors"
    );
  }

  private static StockFormSet StockFormSetFor(
    string name,
    string offered,
    string entersAs
  ) =>
    new(
      1,
      [new StockForm(name, 3f, 3f, 8f, 16f, 0.8f)],
      [new FeedstockEntry(offered, entersAs)]
    );
}
