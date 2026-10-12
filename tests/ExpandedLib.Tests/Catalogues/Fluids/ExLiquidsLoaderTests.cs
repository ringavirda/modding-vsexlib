using ExpandedLib.Catalogues;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Reading <c>config/liquids.json</c> off the asset manager: strict binding against
/// <see cref="LiquidDef"/> and the <see cref="CatalogueLoadReport"/> the loader hands back.
/// </summary>
[Collection("ExLiquids")] // shares the process-wide ExLiquids registry with MediumTaxonomyTests
public class ExLiquidsLoaderTests {
  [Fact]
  public void An_unknown_key_is_reported_by_file_and_key() {
    ICoreAPI api = FakeAssetApi.Create(
      "config/liquids.json",
      (
        "iiex:config/liquids.json",
        """{ "liquids": [ { "code": "Brine", "prority": 5 } ] }"""
      )
    );

    CatalogueLoadReport report = LiquidCatalogueLoader.Load(api);

    Assert.Equal("liquids", report.Catalogue);
    string error = Assert.Single(report.Errors);
    Assert.Contains("iiex:config/liquids.json", error);
    Assert.Contains("prority", error);
  }

  [Fact]
  public void A_valid_catalogue_reports_its_file_and_entry_counts_with_no_errors() {
    ICoreAPI api = FakeAssetApi.Create(
      "config/liquids.json",
      (
        "iiex:config/liquids.json",
        """{ "liquids": [ { "code": "Brine" }, { "code": "Oil" } ] }"""
      )
    );

    CatalogueLoadReport report = LiquidCatalogueLoader.Load(api);

    Assert.Equal(1, report.Files);
    Assert.Equal(2, report.Entries);
    Assert.Empty(report.Errors);
  }
}

/// <summary>The loader names each catalogue file as the registrant of its liquids, so a key two
/// files define warns and a file loaded twice does not.</summary>
[Collection("ExLiquids")]
public class LiquidCatalogueLoaderWarningTests : RegistryLogFixture {
  private const string Brine = """{ "liquids": [ { "code": "Brine" } ] }""";

  [Fact]
  public void A_key_two_sources_define_warns_once_naming_both_sources() {
    ICoreAPI api = FakeAssetApi.Create(
      "config/liquids.json",
      ("iiex:config/liquids.json", Brine),
      ("siex:config/liquids.json", Brine)
    );
    Log.Expect(EnumLogType.Warning, "");

    LiquidCatalogueLoader.Load(api);

    string warning = Only("'Brine'");
    Assert.Contains("iiex:config/liquids.json", warning);
    Assert.Contains("siex:config/liquids.json", warning);
  }

  [Fact]
  public void The_same_source_loaded_twice_logs_no_warning() {
    ICoreAPI api = FakeAssetApi.Create(
      "config/liquids.json",
      ("iiex:config/liquids.json", Brine)
    );

    LiquidCatalogueLoader.Load(api);
    LiquidCatalogueLoader.Load(api);

    Assert.Empty(Log.Warnings);
  }
}
