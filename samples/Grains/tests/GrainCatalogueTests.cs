using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Testing;
using Newtonsoft.Json;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace Grains.Tests;

/// <summary>
/// The loaded catalogue: <see cref="GrainCatalogue.ForItem"/> matches a grain code and its sack
/// code, and <see cref="GrainCatalogue.Load"/> reads every shipped <c>config/grains/</c> entry.
/// </summary>
public class GrainCatalogueTests {
  // An ICoreAPI whose Assets.GetMany("config/grains/") answers with one asset per shipped file,
  // so Load is exercised against the real shipped catalogue rather than a stand-in.
  private static ICoreAPI FakeApi() {
    string[] files = System.IO.Directory.GetFiles(
      System.IO.Path.Combine(RepoPaths.Assets("grains"), "config", "grains"),
      "*.json"
    );
    var assets = files
      .Select(path => {
        var def = JsonConvert.DeserializeObject<GrainDef>(
          System.IO.File.ReadAllText(path)
        )!;
        var asset = Substitute.For<IAsset>();
        asset
          .ToObject<GrainDef>(Arg.Any<JsonSerializerSettings>())
          .Returns(def);
        asset.Location.Returns(
          new AssetLocation(
            "grains",
            "config/grains/" + System.IO.Path.GetFileName(path)
          )
        );
        return asset;
      })
      .ToList();

    var manager = Substitute.For<IAssetManager>();
    manager
      .GetMany(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
      .Returns(call => (string)call[0] == "config/grains/" ? assets : []);

    var api = Substitute.For<ICoreAPI>();
    api.Assets.Returns(manager);
    return api;
  }

  [Fact]
  public void Load_reads_every_shipped_entry() {
    GrainCatalogue.Load(FakeApi());

    Assert.Equal(6, GrainCatalogue.All.Count);

    // Pins the shipped content itself, not just the count: a mistyped key, a wrong grain/flour
    // code or an out-of-range seconds value in a shipped file must fail here.
    var byCode = GrainCatalogue.All.ToDictionary(g => g.Code);
    Assert.Equal("game:grain-spelt", byCode["spelt"].Grain);
    Assert.Equal("game:flour-spelt", byCode["spelt"].Flour);
    Assert.Equal(6, byCode["spelt"].Seconds);
    Assert.Equal("game:grain-rice", byCode["rice"].Grain);
    Assert.Equal("game:flour-rice", byCode["rice"].Flour);
    Assert.Equal(6, byCode["rice"].Seconds);
    Assert.Equal("game:grain-flax", byCode["flax"].Grain);
    Assert.Equal("game:flour-flax", byCode["flax"].Flour);
    Assert.Equal(6, byCode["flax"].Seconds);
    Assert.Equal("game:grain-rye", byCode["rye"].Grain);
    Assert.Equal("game:flour-rye", byCode["rye"].Flour);
    Assert.Equal(8, byCode["rye"].Seconds);
    Assert.Equal("game:grain-amaranth", byCode["amaranth"].Grain);
    Assert.Equal("game:flour-amaranth", byCode["amaranth"].Flour);
    Assert.Equal(6, byCode["amaranth"].Seconds);
    Assert.Equal("game:grain-sunflower", byCode["sunflower"].Grain);
    Assert.Equal("game:flour-sunflower", byCode["sunflower"].Flour);
    Assert.Equal(6, byCode["sunflower"].Seconds);
  }

  [Fact]
  public void ForItem_finds_by_grain_code() {
    GrainCatalogue.Set(
      [new GrainDef { Code = "spelt", Grain = "game:grain-spelt", Flour = "game:flour-spelt" }]
    );

    Assert.Equal("spelt", GrainCatalogue.ForItem("game:grain-spelt")!.Code);
  }

  [Fact]
  public void ForItem_finds_by_sack_code() {
    GrainCatalogue.Set(
      [new GrainDef { Code = "spelt", Grain = "game:grain-spelt", Flour = "game:flour-spelt" }]
    );

    Assert.Equal("spelt", GrainCatalogue.ForItem("grains:sack-spelt")!.Code);
  }

  [Fact]
  public void ForItem_returns_null_for_anything_else() {
    GrainCatalogue.Set(
      [new GrainDef { Code = "spelt", Grain = "game:grain-spelt", Flour = "game:flour-spelt" }]
    );

    Assert.Null(GrainCatalogue.ForItem("game:grain-rye"));
    Assert.Null(GrainCatalogue.ForItem(null));
  }
}
