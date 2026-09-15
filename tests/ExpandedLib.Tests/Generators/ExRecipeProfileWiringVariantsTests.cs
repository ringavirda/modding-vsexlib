using System;
using ExpandedLib.Registries;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The two <c>[ExRecipeProfile]</c> wiring options <see cref="ExRecipeProfileGeneratorTests"/>
/// does not cover: <see cref="ExpandedLib.Config.ExRecipeProfileAttribute.RecipeLevelProperty"/> and
/// <see cref="ExpandedLib.Config.ExRecipeProfileAttribute.LevelConfig"/>.</summary>
[Collection(nameof(RecipeProfileRegistryCollection))]
public class ExRecipeProfileWiringVariantsTests : IDisposable {
  private const string CustomLevelCode = "exlib-recipeprofile-customlevel";
  private const string CrossClassCode = "exlib-recipeprofile-crossclass";

  public void Dispose() {
    ExRecipeProfiles.Unregister(CustomLevelCode);
    ExRecipeProfiles.Unregister(CrossClassCode);
  }

  private static ICoreAPI FakeApi(string modId) {
    var api = Substitute.For<ICoreAPI>();
    api.Logger.Returns(Substitute.For<ILogger>());
    api.Side.Returns(EnumAppSide.Client);
    api.LoadModConfig<Newtonsoft.Json.Linq.JObject>(Arg.Any<string>())
      .Returns((Newtonsoft.Json.Linq.JObject?)null);

    var mod = Substitute.For<Mod>();
    typeof(Mod)
      .GetProperty("Info")!
      .SetValue(mod, new ModInfo { Version = "1.0.0" });
    var modLoader = Substitute.For<IModLoader>();
    modLoader.GetMod(modId).Returns(mod);
    api.ModLoader.Returns(modLoader);
    return api;
  }

  [Fact]
  public void GetLevel_and_SetLevel_round_trip_through_a_custom_named_level_property() {
    ExRecipeProfileCustomLevelPropertyTestValues.Load(FakeApi(CustomLevelCode));
    ExRecipeProfiles.TryGet(CustomLevelCode, out var profile);

    Assert.Equal("normal", profile.GetLevel());
    profile.SetLevel("cheap");

    Assert.Equal(
      "cheap",
      ExRecipeProfileCustomLevelPropertyTestValues.CostLevel
    );
    Assert.Equal("cheap", profile.GetLevel());
  }

  [Fact]
  public void GetLevel_and_SetLevel_read_and_write_the_LevelConfig_named_by_the_attribute() {
    var api = FakeApi(CrossClassCode);
    ExRecipeProfileCrossClassLevelTestValues.Load(api);
    ExRecipeProfileCrossClassCatalogueTestValues.Load(api);
    ExRecipeProfiles.TryGet(CrossClassCode, out var profile);

    Assert.Equal("normal", profile.GetLevel());
    profile.SetLevel("cheap");

    Assert.Equal("cheap", ExRecipeProfileCrossClassLevelTestValues.RecipeLevel);
    Assert.Equal("cheap", profile.GetLevel());
  }
}
