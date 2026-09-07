using System;
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
/// The fixture's mod id is fixed by its own <c>[ExConfigRegister]</c> attribute, so it cannot pick a
/// fresh code per test the way <c>RecipeProfilesTests</c> does; <see cref="Dispose"/> unregisters it
/// so it does not linger in <see cref="ExRecipeProfiles.ApplyAll"/> for other test classes, and
/// <see cref="RecipeProfileRegistryCollection"/> keeps this class from running at the same time as
/// one that calls <c>ApplyAll</c>.
/// </summary>
[Collection(nameof(RecipeProfileRegistryCollection))]
public class ExRecipeProfileGeneratorTests : IDisposable {
  private const string Code = "exlib-recipeprofile-test";

  public void Dispose() => ExRecipeProfiles.Unregister(Code);

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
    modLoader.GetMod(Code).Returns(mod);
    api.ModLoader.Returns(modLoader);
    return api;
  }

  [Fact]
  public void Load_registers_a_profile_under_the_ExConfigRegister_mod_id() {
    ExRecipeProfileTestValues.Load(FakeApi(EnumAppSide.Client));

    Assert.True(ExRecipeProfiles.TryGet(Code, out var profile));
    Assert.Equal(Code, profile.Code);
  }

  [Fact]
  public void The_registered_profile_reads_the_catalogue_and_defaults_properties() {
    ExRecipeProfileTestValues.Load(FakeApi(EnumAppSide.Client));
    ExRecipeProfiles.TryGet(Code, out var profile);

    Assert.Same(ExRecipeProfileTestValues.Recipes, profile.Catalogue());
    // Recipes starts empty and DefaultCatalogue() ships one entry, so this can only pass if
    // Defaults is wired to DefaultCatalogue rather than to the live (empty) catalogue.
    Assert.Single(profile.Defaults());
    Assert.True(profile.Defaults().ContainsKey("stub"));
  }

  [Fact]
  public void GetLevel_and_SetLevel_round_trip_through_the_RecipeLevel_property() {
    ExRecipeProfileTestValues.Load(FakeApi(EnumAppSide.Client));
    ExRecipeProfiles.TryGet(Code, out var profile);

    Assert.Equal("normal", profile.GetLevel());
    profile.SetLevel("cheap");

    Assert.Equal("cheap", ExRecipeProfileTestValues.RecipeLevel);
    Assert.Equal("cheap", profile.GetLevel());
  }

  [Fact]
  public void SaveCatalogue_persists_through_the_generated_Save() {
    var api = FakeApi(EnumAppSide.Server);
    ExRecipeProfileTestValues.Load(api);
    ExRecipeProfiles.TryGet(Code, out var profile);

    profile.SaveCatalogue(); // must not throw

    api.Received()
      .StoreModConfig(
        Arg.Any<Newtonsoft.Json.Linq.JObject>(),
        "exrecipeprofiletest.json"
      );
  }
}
