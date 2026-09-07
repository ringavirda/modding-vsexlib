using ExpandedLib.Registries;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <c>ExConfigGenerator</c>'s <c>[ExRecipeProfile]</c> handling: a config carrying both
/// <c>[ExConfigRegister]</c> and <c>[ExRecipeProfile]</c> - <see cref="ExRecipeProfileTestConfig"/>,
/// generating <c>ExRecipeProfileTestValues</c> - registers its <see cref="RecipeProfile"/> from
/// <c>Load</c> alone, with no hand-written <see cref="ExRecipeProfiles.Register"/> call anywhere.
/// </summary>
public class ExRecipeProfileGeneratorTests {
  private static ICoreAPI FakeApi(EnumAppSide side) {
    var api = Substitute.For<ICoreAPI>();
    api.Logger.Returns(Substitute.For<ILogger>());
    api.Side.Returns(side);
    api.LoadModConfig<Newtonsoft.Json.Linq.JObject>(Arg.Any<string>())
      .Returns((Newtonsoft.Json.Linq.JObject?)null);

    var mod = Substitute.For<Mod>();
    typeof(Mod)
      .GetProperty("Info")!
      .SetValue(mod, new ModInfo { Version = "1.0.0" });
    var modLoader = Substitute.For<IModLoader>();
    modLoader.GetMod("exlib-recipeprofile-test").Returns(mod);
    api.ModLoader.Returns(modLoader);
    return api;
  }

  [Fact]
  public void Load_registers_a_profile_under_the_ExConfigRegister_mod_id() {
    ExRecipeProfileTestValues.Load(FakeApi(EnumAppSide.Client));

    Assert.True(
      ExRecipeProfiles.TryGet("exlib-recipeprofile-test", out var profile)
    );
    Assert.Equal("exlib-recipeprofile-test", profile.Code);
  }

  [Fact]
  public void The_registered_profile_reads_the_catalogue_and_defaults_properties() {
    ExRecipeProfileTestValues.Load(FakeApi(EnumAppSide.Client));
    ExRecipeProfiles.TryGet("exlib-recipeprofile-test", out var profile);

    Assert.Same(ExRecipeProfileTestValues.Recipes, profile.Catalogue());
    Assert.Empty(profile.Defaults()); // DefaultCatalogue() returns a fresh empty dictionary
  }

  [Fact]
  public void GetLevel_and_SetLevel_round_trip_through_the_RecipeLevel_property() {
    ExRecipeProfileTestValues.Load(FakeApi(EnumAppSide.Client));
    ExRecipeProfiles.TryGet("exlib-recipeprofile-test", out var profile);

    Assert.Equal("normal", profile.GetLevel());
    profile.SetLevel("cheap");

    Assert.Equal("cheap", ExRecipeProfileTestValues.RecipeLevel);
    Assert.Equal("cheap", profile.GetLevel());
  }

  [Fact]
  public void SaveCatalogue_persists_through_the_generated_Save() {
    var api = FakeApi(EnumAppSide.Server);
    ExRecipeProfileTestValues.Load(api);
    ExRecipeProfiles.TryGet("exlib-recipeprofile-test", out var profile);

    profile.SaveCatalogue(); // must not throw

    api.Received().StoreModConfig(
      Arg.Any<Newtonsoft.Json.Linq.JObject>(),
      "exrecipeprofiletest.json"
    );
  }
}
