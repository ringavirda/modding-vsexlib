using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Industry;
using ExpandedLib.Industry.Metals;
using ExpandedLib.Registries;
using ExpandedLib.Storage;
using ExpandedLib.Testing;
using Newtonsoft.Json;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="IExItemDefDecorator"/>: runs on every path a def reaches, and a decorator that skips a def
/// already carrying GroundStorable leaves it as it was.
/// </summary>
[Collection("ExDefinitions")]
public class ItemDefDecoratorTests : IDisposable {
  public ItemDefDecoratorTests() {
    TestModDomain.Register();
    ExDefinitions.Clear();
  }

  public void Dispose() => ExDefinitions.Clear();

  private sealed class Provider : IExItemDefProvider {
    public static IEnumerable<ExItemDef> Definitions(string domain) =>
      [
        ExItemDef.Create(domain, "gsdec-provided"),
        ExItemDef
          .Create(domain, "gsstore-kept")
          .Behavior("GroundStorable", new { layout = "Quadrants" }),
        ExItemDef.Create(domain, "gsstore-bare"),
      ];
  }

  private sealed class Decorator : IExItemDefDecorator {
    public void Decorate(ExItemDef def) {
      if (
        def.Code.StartsWith("gsdec-", StringComparison.Ordinal)
        || def.Domain == "gsdomain"
      )
        def.Attribute("decorated", true);
      if (
        def.Code.StartsWith("gsstore-", StringComparison.Ordinal)
        && !ExStorage.HasGroundStorable(def)
      )
        def.Behavior("GroundStorable", new { layout = "SingleCenter" });
    }
  }

  private static bool Decorated(ExItemDef def) =>
    (bool?)def.ToJson()["attributes"]?["decorated"] == true;

  private static string[] Layouts(ExItemDef def) =>
    def.ToJson()["behaviors"]!
      .Where(b => (string?)b["name"] == "GroundStorable")
      .Select(b => (string)b["properties"]!["layout"]!)
      .ToArray();

  // Fails when a def left by a decorator it does not own changes, or the skip reads a def with
  // GroundStorable as one without.
  [Fact]
  public void Decorator_skips_a_stored_def() {
    ExDefinitions.DiscoverDecorators(typeof(Decorator).Assembly);
    ExDefinitions.DiscoverAndRegisterItems("test", typeof(Provider).Assembly);

    Assert.Equal(
      ["Quadrants"],
      Layouts(ExDefinitions.Items.Single(d => d.Code == "gsstore-kept"))
    );
    Assert.Equal(
      ["SingleCenter"],
      Layouts(ExDefinitions.Items.Single(d => d.Code == "gsstore-bare"))
    );
  }

  // Fails when a def reaches the registry or the golden set past the decorators: through discovery, a
  // generated metal family, a def registered before the decorator was found, or Collect.
  [Fact]
  public void Decorator_runs_on_every_path() {
    ExDefinitions.DiscoverDecorators(typeof(Decorator).Assembly);
    ExDefinitions.DiscoverAndRegisterItems("test", typeof(Provider).Assembly);
    Assert.True(
      Decorated(ExDefinitions.Items.Single(d => d.Code == "gsdec-provided"))
    );

    ExDefinitions.Clear();
    ExDefinitions.RegisterItem(ExItemDef.Create("test", "gsdec-early"));
    ExDefinitions.DiscoverDecorators(typeof(Decorator).Assembly);
    Assert.True(
      Decorated(ExDefinitions.Items.Single(d => d.Code == "gsdec-early"))
    );

    ExDefinitions.Clear();
    ExDefinitions.DiscoverDecorators(typeof(Decorator).Assembly);
    new IndustryModule().Contribute(CatalogueApi());
    Assert.NotEmpty(ExDefinitions.Items);
    Assert.All(ExDefinitions.Items, def => Assert.True(Decorated(def)));

    ExDefinitions.Clear();
    IReadOnlyList<IExDef> collected = DefinitionGoldens.Collect(
      "test",
      typeof(Provider).Assembly
    );
    Assert.True(
      Decorated(
        collected.OfType<ExItemDef>().Single(d => d.Code == "gsdec-provided")
      )
    );
  }

  // The metal catalogue the Industry module reads, as one asset.
  private static ICoreAPI CatalogueApi() {
    MetalDef metal = new() {
      Code = "gsmetal",
      MoltenItem = "gsdomain:ingot-gsmetal",
      GenerateItemFamily = true,
      ItemForms = ["ingot"],
    };
    IAsset asset = Substitute.For<IAsset>();
    asset.Location.Returns(
      new AssetLocation("test:config/metals/gsmetal.json")
    );
    asset.ToObject<MetalDef>(Arg.Any<JsonSerializerSettings>()).Returns(metal);
    ICoreAPI api = Substitute.For<ICoreAPI>();
    api.Assets.GetMany("config/metals/").Returns([asset]);
    return api;
  }
}
