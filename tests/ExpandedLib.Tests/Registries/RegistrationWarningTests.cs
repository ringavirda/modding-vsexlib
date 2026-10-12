using System;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Catalogues;
using ExpandedLib.Config;
using ExpandedLib.Industry.Metals;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
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

  [Fact]
  public void ExLiquids_names_both_media_of_a_code_taken_twice() {
    Log.Expect(EnumLogType.Warning, "");
    ExLiquids.Register(
      new LiquidDef {
        Code = "Planted",
        Phase = LiquidPhase.Gas,
        Priority = 3,
      }
    );
    ExLiquids.Register(
      new LiquidDef {
        Code = "Planted",
        Phase = LiquidPhase.Liquid,
        Priority = 5,
      }
    );

    string warning = Only("ExLiquids");
    Assert.Contains("'Planted'", warning);
    Assert.Contains("Gas medium of priority 3", warning);
    Assert.Contains("Liquid medium of priority 5", warning);
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
  public void MetalRegistry_warns_for_a_code_taken_by_two_molten_items() {
    Log.Expect(EnumLogType.Warning, "");
    MetalRegistry.Register(
      new MetalDef { Code = "plantedmetal", MoltenItem = "planted:one" }
    );
    MetalRegistry.Register(
      new MetalDef { Code = "plantedmetal", MoltenItem = "planted:two" }
    );

    string warning = Only("MetalRegistry codes");
    Assert.Contains("'plantedmetal'", warning);
    Assert.Contains("planted:one", warning);
    Assert.Contains("planted:two", warning);
    None("MetalRegistry molten items");
  }

  [Fact]
  public void MetalRegistry_warns_for_a_molten_item_taken_by_two_codes() {
    Log.Expect(EnumLogType.Warning, "");
    MetalRegistry.Register(
      new MetalDef { Code = "plantedone", MoltenItem = "planted:same" }
    );
    MetalRegistry.Register(
      new MetalDef { Code = "plantedtwo", MoltenItem = "planted:same" }
    );

    string warning = Only("MetalRegistry molten items");
    Assert.Contains("the plantedone metal", warning);
    Assert.Contains("the plantedtwo metal", warning);
    None("MetalRegistry codes");
  }
}
