using System.Linq;
using System.Text;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="AssetCheckSource.Domains"/> against a <see cref="TestModLoader"/>: only exlib and its
/// declared dependents are in scope, never a mod with no exlib dependency at all.
/// </summary>
public class AssetCheckSourceTests {
  [Fact]
  public void Domains_is_exlib_plus_its_dependents_only() {
    using var world = new TestWorld();
    world.Mods.Add("dependent", "1.0.0", dependencies: "exlib");
    world.Mods.Add("bystander", "1.0.0");

    var source = new AssetCheckSource(world.Api);

    Assert.Equal(["dependent", "exlib"], source.Domains.OrderBy(d => d));
  }

  // Fails when BlockTypes stops reading the domain's blocktype assets or keeps an unparsable one.
  [Fact]
  public void BlockTypes_reads_each_parsable_blocktype_of_the_domain() {
    using var world = new TestWorld();
    IAsset Asset(string path, string text) =>
      ExSyntheticAsset.Create(
        new AssetLocation("dependent", path),
        Encoding.UTF8.GetBytes(text),
        new ExDefinitionOrigin()
      );
    world
      .Api.Assets.GetMany("blocktypes/", "dependent")
      .Returns([
        Asset("blocktypes/crate.json", "{ \"code\": \"crate\" }"),
        Asset("blocktypes/broken.json", "{ \"code\": "),
      ]);

    var (file, json) = Assert.Single(
      new AssetCheckSource(world.Api).BlockTypes("dependent")
    );

    Assert.Equal("dependent:blocktypes/crate.json", file.ToString());
    Assert.Equal("crate", (string?)json["code"]);
  }
}
