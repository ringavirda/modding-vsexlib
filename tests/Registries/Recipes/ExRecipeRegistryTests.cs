using System.Collections.Generic;
using System.IO;
using ExpandedLib.Registries;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The recipe-registry rung, mirroring <c>RecipeRegistrySystem</c> - one
/// <c>RegisterRecipeRegistry</c> call per recipe type, then a server-side asset load into the list it
/// returned.
/// </summary>
public class ExRecipeRegistryTests {
  public sealed class TestRecipe : IByteSerializable {
    public string Code = "";

    public void ToBytes(BinaryWriter writer) => writer.Write(Code);

    public void FromBytes(BinaryReader reader, IWorldAccessor resolver) =>
      Code = reader.ReadString();
  }

  [Fact]
  public void Register_returns_the_recipe_list_RegisterRecipeRegistry_holds() {
    var recipes = new List<TestRecipe>();
    var registry = new RecipeRegistryGeneric<TestRecipe>(recipes);
    var api = Substitute.For<ICoreAPI>();
    api.RegisterRecipeRegistry<RecipeRegistryGeneric<TestRecipe>>("testrecipes")
      .Returns(registry);

    List<TestRecipe> result = ExRecipeRegistry.Register<TestRecipe>(
      api,
      "testrecipes"
    );

    Assert.Same(recipes, result);
  }

  [Fact]
  public void LoadRecipes_reads_a_single_object_and_an_array_under_the_folder() {
    var sapi = Substitute.For<ICoreServerAPI>();
    sapi.Server.Logger.Returns(Substitute.For<ILogger>());
    var assets = new Dictionary<AssetLocation, JToken> {
      [new AssetLocation("test:recipes/widgets/a.json")] = JToken.Parse(
        """{ "code": "a" }"""
      ),
      [new AssetLocation("test:recipes/widgets/b.json")] = JToken.Parse(
        """[{ "code": "b1" }, { "code": "b2" }]"""
      ),
    };
    sapi
      .Assets.GetMany<JToken>(Arg.Any<ILogger>(), "recipes/widgets")
      .Returns(assets);

    var into = new List<TestRecipe>();
    ExRecipeRegistry.LoadRecipes(sapi, "widgets", into);

    Assert.Equal(3, into.Count);
    Assert.Contains(into, r => r.Code == "a");
    Assert.Contains(into, r => r.Code == "b1");
    Assert.Contains(into, r => r.Code == "b2");
  }

  [Fact]
  public void LoadRecipes_runs_resolve_on_every_loaded_recipe() {
    var sapi = Substitute.For<ICoreServerAPI>();
    sapi.Server.Logger.Returns(Substitute.For<ILogger>());
    var assets = new Dictionary<AssetLocation, JToken> {
      [new AssetLocation("test:recipes/widgets/a.json")] = JToken.Parse(
        """{ "code": "a" }"""
      ),
    };
    sapi
      .Assets.GetMany<JToken>(Arg.Any<ILogger>(), "recipes/widgets")
      .Returns(assets);

    var resolved = new List<string>();
    ExRecipeRegistry.LoadRecipes(
      sapi,
      "widgets",
      new List<TestRecipe>(),
      r => {
        resolved.Add(r.Code);
        return true;
      }
    );

    Assert.Equal(["a"], resolved);
  }

  [Fact]
  public void LoadRecipes_drops_a_recipe_resolve_rejects() {
    var sapi = Substitute.For<ICoreServerAPI>();
    sapi.Server.Logger.Returns(Substitute.For<ILogger>());
    var assets = new Dictionary<AssetLocation, JToken> {
      [new AssetLocation("test:recipes/widgets/a.json")] = JToken.Parse(
        """{ "code": "a" }"""
      ),
      [new AssetLocation("test:recipes/widgets/b.json")] = JToken.Parse(
        """{ "code": "b" }"""
      ),
    };
    sapi
      .Assets.GetMany<JToken>(Arg.Any<ILogger>(), "recipes/widgets")
      .Returns(assets);

    var into = new List<TestRecipe>();
    ExRecipeRegistry.LoadRecipes(sapi, "widgets", into, r => r.Code != "b");

    Assert.Equal(["a"], into.ConvertAll(r => r.Code));
  }
}
