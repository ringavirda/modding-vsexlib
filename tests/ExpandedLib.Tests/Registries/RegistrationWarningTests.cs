using System;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Catalogues;
using ExpandedLib.Config;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.MechanicalPower;
using ExpandedLib.Industry.Metals;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Holds <see cref="RegistryLog.Logger"/> to a recording logger for a test and puts the
/// previous one back.</summary>
public abstract class RegistryLogFixture : IDisposable {
  private readonly ILogger? _was = RegistryLog.Logger;
  protected readonly RecordingLogger Log = new();

  protected RegistryLogFixture() {
    RegistryLog.Logger = Log;
  }

  public void Dispose() => RegistryLog.Logger = _was;

  protected string Only(string fragment) =>
    Assert.Single(Log.Warnings, w => w.Contains(fragment));

  protected void None(string fragment) =>
    Assert.DoesNotContain(Log.Warnings, w => w.Contains(fragment));
}

/// <summary>Each registration table that takes a key twice logs a Warning naming both registrants.</summary>
[Collection("WorldState")]
public class RegistrationWarningTests : RegistryLogFixture {
  private sealed class PrefA : IExPreference {
    public string Key => "plantedpref";
    public System.Collections.Generic.IReadOnlyList<string> Options => ["a"];
    public string Default => "a";

    public void Apply(string value) { }
  }

  private sealed class PrefB : IExPreference {
    public string Key => "plantedpref";
    public System.Collections.Generic.IReadOnlyList<string> Options => ["a"];
    public string Default => "a";

    public void Apply(string value) { }
  }

  private sealed class ConfigA : StubConfig { }

  private sealed class ConfigB : StubConfig { }

  private abstract class StubConfig : IExConfigAccess {
    public string ModId => "plantedconfig";
    public string FileName => "";
    public System.Collections.Generic.IReadOnlyList<string> ValueNames => [];

    public bool TryGet(string name, out string canonicalName, out string value) {
      canonicalName = value = "";
      return false;
    }

    public ExConfigEditResult Set(string name, string raw) =>
      throw new NotSupportedException();

    public string ExportJson() => "";

    public void ImportJson(string json) { }
  }

  private static float One() => 1f;

  private static readonly Func<float> Other = Random.Shared.NextSingle;
  private const string OtherAssembly = "System.Private.CoreLib";

  [Fact]
  public void ExPreferences_names_both_types_of_a_key_taken_twice() {
    Log.Expect(EnumLogType.Warning, "");
    ExPreferences.Register(new PrefA());
    ExPreferences.Register(new PrefB());

    string warning = Only("ExPreferences");
    Assert.Contains("'plantedpref'", warning);
    Assert.Contains(nameof(PrefA), warning);
    Assert.Contains(nameof(PrefB), warning);
    ExPreferences.ResetForWorld();
  }

  [Fact]
  public void ExPreferences_is_silent_for_one_type_registered_twice() {
    ExPreferences.Register(new PrefA());
    ExPreferences.Register(new PrefA());

    None("ExPreferences");
    ExPreferences.ResetForWorld();
  }

  [Fact]
  public void ExRccSettings_names_the_assembly_of_a_domain_taken_twice() {
    Log.Expect(EnumLogType.Warning, "");
    ExRccSettings.RegisterBrokenDropsRatio("plantedrcc", One);
    ExRccSettings.RegisterBrokenDropsRatio("plantedrcc", Other);

    string warning = Only("ExRccSettings");
    Assert.Contains("'plantedrcc'", warning);
    Assert.Contains(
      typeof(RegistrationWarningTests).Assembly.GetName().Name!,
      warning
    );
    Assert.Contains(OtherAssembly, warning);
    ExRccSettings.ResetForWorld();
  }

  [Fact]
  public void ExRccSettings_is_silent_for_one_assembly_twice() {
    ExRccSettings.RegisterBrokenDropsRatio("plantedrcc", One);
    ExRccSettings.RegisterBrokenDropsRatio("plantedrcc", () => 2f);

    None("ExRccSettings");
    ExRccSettings.ResetForWorld();
  }

  [Fact]
  public void BlockPipe_burst_names_the_assembly_of_a_tier_taken_twice() {
    Log.Expect(EnumLogType.Warning, "");
    BlockPipe.RegisterBurst("plantedburst", One);
    BlockPipe.RegisterBurst("plantedburst", Other);

    string warning = Only("BlockPipe burst");
    Assert.Contains("'plantedburst'", warning);
    Assert.Contains(
      typeof(RegistrationWarningTests).Assembly.GetName().Name!,
      warning
    );
  }

  [Fact]
  public void BlockPipe_throughput_names_the_assembly_of_a_tier_taken_twice() {
    Log.Expect(EnumLogType.Warning, "");
    BlockPipe.RegisterThroughput("plantedflow", One);
    BlockPipe.RegisterThroughput("plantedflow", Other);

    string warning = Only("BlockPipe throughput");
    Assert.Contains("'plantedflow'", warning);
    Assert.Contains(
      typeof(RegistrationWarningTests).Assembly.GetName().Name!,
      warning
    );
  }

  [Fact]
  public void BlockPipe_burst_is_silent_for_one_assembly_twice() {
    BlockPipe.RegisterBurst("plantedsame", One);
    BlockPipe.RegisterBurst("plantedsame", () => 2f);

    None("BlockPipe burst");
  }

  [Fact]
  public void BlockPipe_joint_names_both_families_of_a_tier_taken_twice() {
    Log.Expect(EnumLogType.Warning, "");
    BlockPipe.RegisterJoint("plantedjoint", BlockPipe.FlangedJoint);
    BlockPipe.RegisterJoint("plantedjoint", BlockPipe.WeldedJoint);

    string warning = Only("BlockPipe joint");
    Assert.Contains("'plantedjoint'", warning);
    Assert.Contains(BlockPipe.FlangedJoint, warning);
    Assert.Contains(BlockPipe.WeldedJoint, warning);
  }

  [Fact]
  public void BlockPipe_joint_is_silent_for_the_same_family_twice() {
    BlockPipe.RegisterJoint("plantedsame", BlockPipe.FlangedJoint);
    BlockPipe.RegisterJoint("plantedsame", BlockPipe.FlangedJoint);

    None("BlockPipe joint");
  }

  [Fact]
  public void ExConfigProfiles_names_both_types_of_a_mod_id_taken_twice() {
    Log.Expect(EnumLogType.Warning, "");
    ExConfigProfiles.Register(new ConfigA());
    ExConfigProfiles.Register(new ConfigB());

    string warning = Only("ExConfigProfiles");
    Assert.Contains("'plantedconfig'", warning);
    Assert.Contains(nameof(ConfigA), warning);
    Assert.Contains(nameof(ConfigB), warning);
    ExConfigProfiles.ResetForWorld();
  }
}

/// <summary><see cref="ExLiquids"/> warns when a code is taken twice.</summary>
[Collection("ExLiquids")]
public class LiquidRegistrationWarningTests : RegistryLogFixture {
  public LiquidRegistrationWarningTests() {
    ExLiquids.Clear();
    ExLiquids.SeedDefaults();
  }

  private static LiquidDef Steam(float below, string code = "Steam") =>
    new() {
      Code = code,
      Phase = LiquidPhase.Gas,
      Priority = 10,
      CondenseBelowC = below,
    };

  [Fact]
  public void ExLiquids_names_both_sources_of_a_code_redefined_with_alike_phase_and_priority() {
    Log.Expect(EnumLogType.Warning, "");
    ExLiquids.Register(Steam(90f, "Vapour"), "modone/config/liquids.json");
    ExLiquids.Register(Steam(80f, "Vapour"), "modtwo/config/liquids.json");

    string warning = Only("ExLiquids");
    Assert.Contains("'Vapour'", warning);
    Assert.Contains("modone/config/liquids.json", warning);
    Assert.Contains("modtwo/config/liquids.json", warning);
  }

  [Fact]
  public void ExLiquids_is_silent_when_an_overlay_redefines_a_built_in() {
    ExLiquids.Register(Steam(90f), "modone/config/liquids.json");

    None("ExLiquids");
  }

  [Fact]
  public void ExLiquids_is_silent_for_one_source_twice() {
    var planted = new LiquidDef { Code = "Planted", Phase = LiquidPhase.Gas };
    ExLiquids.Register(planted, "modone/config/liquids.json");
    ExLiquids.Register(planted, "modone/config/liquids.json");

    None("ExLiquids");
  }

  [Fact]
  public void ExLiquids_public_register_names_the_calling_assembly() {
    Log.Expect(EnumLogType.Warning, "");
    ExLiquids.Register(Steam(90f, "Vapour"), "modone/config/liquids.json");
    ExLiquids.Register(Steam(80f, "Vapour"));

    string warning = Only("ExLiquids");
    Assert.Contains("modone/config/liquids.json", warning);
    Assert.Contains("ExpandedLib.Tests", warning);
  }
}

/// <summary><see cref="ExRecipeProfiles"/> warns when a code is taken twice.</summary>
[Collection(nameof(RecipeProfileRegistryCollection))]
public class RecipeProfileRegistrationWarningTests : RegistryLogFixture {
  private static class FromA {
    internal static RecipeProfile Profile(string code) =>
      new() {
        Code = code,
        Catalogue = () => throw new NotSupportedException(),
        Defaults = () => throw new NotSupportedException(),
        GetLevel = () => "normal",
        SetLevel = _ => { },
        SaveCatalogue = () => { },
      };
  }

  private static class FromB {
    internal static RecipeProfile Profile(string code) =>
      new() {
        Code = code,
        Catalogue = () => throw new NotSupportedException(),
        Defaults = () => throw new NotSupportedException(),
        GetLevel = () => "cheap",
        SetLevel = _ => { },
        SaveCatalogue = () => { },
      };
  }

  [Fact]
  public void ExRecipeProfiles_names_the_code_taken_twice() {
    Log.Expect(EnumLogType.Warning, "");
    ExRecipeProfiles.Register(FromA.Profile("plantedprofile"));
    ExRecipeProfiles.Register(FromB.Profile("plantedprofile"));

    string warning = Only("ExRecipeProfiles");
    Assert.Contains("'plantedprofile'", warning);
    Assert.Contains(nameof(FromA), warning);
    Assert.Contains(nameof(FromB), warning);
    ExRecipeProfiles.Unregister("plantedprofile");
  }
}

/// <summary><see cref="MetalRegistry"/> warns when a molten item or a code is taken twice.</summary>
[Collection("MetalRegistry")]
public class MetalRegistrationWarningTests : RegistryLogFixture {
  public MetalRegistrationWarningTests() => MetalRegistry.Clear();

  [Fact]
  public void MetalRegistry_names_both_sources_of_a_metal_redefined_with_a_different_glow() {
    Log.Expect(EnumLogType.Warning, "");
    MetalRegistry.Register(
      new MetalDef {
        Code = "plantedmetal",
        MoltenItem = "planted:one",
        GlowMinTemp = 500f,
      },
      "modone/config/metals/a.json"
    );
    MetalRegistry.Register(
      new MetalDef {
        Code = "plantedmetal",
        MoltenItem = "planted:one",
        GlowMinTemp = 600f,
      },
      "modtwo/config/metals/a.json"
    );

    string warning = Only("MetalRegistry codes");
    Assert.Contains("'plantedmetal'", warning);
    Assert.Contains("modone/config/metals/a.json", warning);
    Assert.Contains("modtwo/config/metals/a.json", warning);
    Only("MetalRegistry molten items");
  }

  [Fact]
  public void MetalRegistry_is_silent_for_one_source_twice() {
    var def = new MetalDef {
      Code = "plantedmetal",
      MoltenItem = "planted:one",
    };
    MetalRegistry.Register(def, "modone/config/metals/a.json");
    MetalRegistry.Register(def, "modone/config/metals/a.json");

    None("MetalRegistry");
  }

  [Fact]
  public void MetalCatalogueLoader_overlay_over_the_baseline_is_silent_and_over_an_overlay_warns() {
    Log.Expect(EnumLogType.Warning, "");
    MetalCatalogueLoader.Populate(
      ["iron"],
      [
        new MetalDef
        {
          Code = "iron",
          MoltenItem = "game:ingot-iron",
          GlowMinTemp = 500f,
        },
        new MetalDef
        {
          Code = "iron",
          MoltenItem = "game:ingot-iron",
          GlowMinTemp = 600f,
        },
      ],
      null,
      ["modone/config/metals/a.json", "modtwo/config/metals/a.json"]
    );

    string warning = Only("MetalRegistry codes");
    Assert.Contains("modone/config/metals/a.json", warning);
    Assert.Contains("modtwo/config/metals/a.json", warning);
  }

  [Fact]
  public void MetalRegistry_public_register_names_the_calling_assembly() {
    Log.Expect(EnumLogType.Warning, "");
    MetalRegistry.Register(
      new MetalDef { Code = "plantedmetal", MoltenItem = "planted:one" },
      "modone/config/metals/a.json"
    );
    MetalRegistry.Register(
      new MetalDef { Code = "plantedmetal", MoltenItem = "planted:one" }
    );

    Assert.Contains("ExpandedLib.Tests", Only("MetalRegistry codes"));
  }

  private static BlockNetwork Net(BlockNetworkModSystem networks) =>
    new MpEnergyNetwork(networks);

  [Fact]
  public void BlockNetworkModSystem_names_both_owners_of_a_type_taken_twice() {
    Log.Expect(EnumLogType.Warning, "");
    var networks = new BlockNetworkModSystem();
    networks.RegisterNetworkType("plantednet", () => Net(networks), "modone");
    bool replaced = networks.RegisterNetworkType(
      "plantednet",
      () => Net(networks),
      "modtwo"
    );

    string warning = Only("BlockNetworkModSystem");
    Assert.Contains("'plantednet'", warning);
    Assert.Contains("modone", warning);
    Assert.Contains("modtwo", warning);
    Assert.True(replaced);
  }

  [Fact]
  public void BlockNetworkModSystem_is_silent_for_one_owner_twice() {
    var networks = new BlockNetworkModSystem();
    networks.RegisterNetworkType("plantednet", () => Net(networks), "modone");
    networks.RegisterNetworkType("plantednet", () => Net(networks), "modone");

    None("BlockNetworkModSystem");
  }

  [Fact]
  public void BlockNetworkModSystem_public_register_names_the_calling_assembly() {
    Log.Expect(EnumLogType.Warning, "");
    var networks = new BlockNetworkModSystem();
    networks.RegisterNetworkType("plantednet", () => Net(networks), "modone");
    networks.RegisterNetworkType("plantednet", () => Net(networks));

    string warning = Only("BlockNetworkModSystem");
    Assert.Contains("modone", warning);
    Assert.Contains("ExpandedLib.Tests", warning);
  }

  // Fails when ReplaceNetworkType does not store the factory, or does not hand the type's owner to
  // the replacing assembly.
  [Fact]
  public void BlockNetworkModSystem_replace_call_is_silent_and_wins() {
    Log.Expect(EnumLogType.Warning, "");
    var world = new TestWorld();
    var networks = world.Networks;
    bool replacementUsed = false;
    networks.RegisterNetworkType(
      "plantednet",
      () => new StubNetwork(networks),
      "modone"
    );
    bool replaced = networks.ReplaceNetworkType(
      "plantednet",
      () => {
        replacementUsed = true;
        return new StubNetwork(networks);
      }
    );

    None("BlockNetworkModSystem");
    Assert.True(replaced);
    Assert.False(
      networks.ReplaceNetworkType(
        "plantednet2",
        () => new StubNetwork(networks)
      )
    );
    world.PlaceNode(new BlockPos(1, 1, 1), "plantednet", "we");
    Assert.True(replacementUsed);

    networks.RegisterNetworkType("plantednet", () => new StubNetwork(networks));
    None("BlockNetworkModSystem");
    networks.RegisterNetworkType(
      "plantednet",
      () => new StubNetwork(networks),
      "modthree"
    );
    string warning = Only("BlockNetworkModSystem");
    Assert.Contains("registered by ExpandedLib.Tests", warning);
    Assert.Contains("modthree", warning);
  }

  // Fails when the harness registers under its own assembly's name instead of the caller's.
  [Fact]
  public void TestWorld_and_Scene_register_networks_under_the_calling_assembly() {
    Log.Expect(EnumLogType.Warning, "");
    var world = new TestWorld();
    world.RegisterNetwork("plantednet", sys => new StubNetwork(sys));
    world.Networks.RegisterNetworkType(
      "plantednet",
      () => new StubNetwork(world.Networks),
      "modone"
    );
    Assert.Contains(
      "registered by ExpandedLib.Tests",
      Only("BlockNetworkModSystem")
    );

    var scene = new Scene();
    scene.Network("plantednet2", sys => new StubNetwork(sys));
    scene.World.Networks.RegisterNetworkType(
      "plantednet2",
      () => new StubNetwork(scene.World.Networks),
      "modone"
    );
    Assert.Contains(
      Log.Warnings,
      w =>
        w.Contains("'plantednet2'")
        && w.Contains("registered by ExpandedLib.Tests")
    );
  }

  [Fact]
  public void ExBlockNames_names_both_owners_of_a_group_taken_twice() {
    Log.Expect(EnumLogType.Warning, "");
    ExBlockNames.AddVariantQualifier("plantedgroup", "a:", "modone");
    ExBlockNames.AddVariantQualifier("plantedgroup", "b:", "modtwo");

    string warning = Only("ExBlockNames");
    Assert.Contains("'plantedgroup'", warning);
    Assert.Contains("modone", warning);
    Assert.Contains("modtwo", warning);
    ExBlockNames.RemoveVariantQualifier("plantedgroup");
  }

  [Fact]
  public void ExBlockNames_is_silent_for_one_owner_twice() {
    ExBlockNames.AddVariantQualifier("plantedgroup", "a:", "modone");
    ExBlockNames.AddVariantQualifier("plantedgroup", "b:", "modone");

    None("ExBlockNames");
    ExBlockNames.RemoveVariantQualifier("plantedgroup");
  }

  [Fact]
  public void CellRole_names_both_owners_of_a_key_declared_twice() {
    Log.Expect(EnumLogType.Warning, "");
    CellRole.Of("plantedrole", false, "modone");
    CellRole.Of("plantedrole", true, "modtwo");

    string warning = Only("CellRole");
    Assert.Contains("'plantedrole'", warning);
    Assert.Contains("modone", warning);
    Assert.Contains("modtwo", warning);
    Assert.True(new CellRole("plantedrole").IsSingle);
  }

  [Fact]
  public void CellRole_is_silent_for_one_owner_twice() {
    CellRole.Of("plantedrole2", false, "modone");
    CellRole.Of("plantedrole2", true, "modone");

    None("CellRole");
  }

  [Fact]
  public void CellRole_public_of_names_the_calling_assembly() {
    Log.Expect(EnumLogType.Warning, "");
    CellRole.Of("plantedrole3", false, "modone");
    CellRole.Of("plantedrole3");

    Assert.Contains("ExpandedLib.Tests", Only("CellRole"));
  }
}
