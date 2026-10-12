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

/// <summary>The loader names each entry's mod (the domain of its file) as the registrant, so a key
/// two mods define warns, and a mod's own files and a reload do not.</summary>
[Collection("ExLiquids")]
public class LiquidCatalogueLoaderWarningTests : RegistryLogFixture {
  private const string Brine = """{ "liquids": [ { "code": "Brine" } ] }""";
  private const string Steam =
    """{ "liquids": [ { "code": "Steam", "phase": "Gas", "priority": 10 } ] }""";

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
    Assert.Contains("registered by iiex", warning);
    Assert.Contains("siex", warning);
  }

  // Fails when the loader takes the owner from the asset path instead of its domain.
  [Fact]
  public void A_key_one_mod_defines_in_two_files_logs_no_warning() {
    ICoreAPI api = FakeAssetApi.Create(
      "config/liquids.json",
      ("iiex:config/liquids.json", Brine),
      ("iiex:config/more/liquids.json", Brine)
    );

    LiquidCatalogueLoader.Load(api);

    Assert.Empty(Log.Warnings);
  }

  // Fails when the loader takes the owner from the asset path instead of its domain.
  [Fact]
  public void The_exlib_catalogue_repeating_the_built_ins_logs_no_warning() {
    ICoreAPI api = FakeAssetApi.Create(
      "config/liquids.json",
      ("exlib:config/liquids.json", Steam)
    );

    LiquidCatalogueLoader.Load(api);

    Assert.Empty(Log.Warnings);
  }

  // Fails when the built-in media are seeded without the exlib owner.
  [Fact]
  public void Another_mods_catalogue_replacing_a_built_in_warns_naming_exlib_and_the_mod() {
    ICoreAPI api = FakeAssetApi.Create(
      "config/liquids.json",
      ("iiex:config/liquids.json", Steam)
    );
    Log.Expect(EnumLogType.Warning, "");

    LiquidCatalogueLoader.Load(api);

    string warning = Only("'Steam'");
    Assert.Contains("registered by exlib", warning);
    Assert.Contains("iiex", warning);
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
