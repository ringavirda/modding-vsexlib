using System.Collections.Generic;
using ExpandedLib.Checks;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// <see cref="LateDefinitionCheck"/> against <see cref="ExDefinitions"/> directly, since the check
/// reads that static registry rather than the <see cref="ICheckSource"/> it is handed. Shares the
/// "ExDefinitions" collection with everything else that mutates it.
/// </summary>
[Collection("ExDefinitions")]
public class LateDefinitionCheckTests {
  public LateDefinitionCheckTests() => ExDefinitions.Clear();

  // LateDefinitionCheck never reads its ICheckSource argument (blocks come from ExDefinitions
  // directly, and it has no item/recipe accessor to speak of), so this stub answers nothing.
  private sealed class EmptyCheckSource : ICheckSource {
    public IEnumerable<string> Domains => [];
    public IEnumerable<AssetLocation> BlockCodes => [];
    public IEnumerable<AssetLocation> ItemCodes => [];

    public IEnumerable<(AssetLocation File, JObject Json)> Recipes(
      string domain
    ) => [];

    public IEnumerable<(string Locale, JObject Json)> Lang(string domain) =>
      [];

    public IEnumerable<ExBlockDef> BlockDefinitions(string domain) => [];
  }

  private static CheckResult Run() =>
    LateDefinitionCheck.Run(new EmptyCheckSource(), "stub");

  [Fact]
  public void Nothing_reported_when_injection_never_ran() {
    ExDefinitions.RegisterBlock(ExBlockDef.Create("stub", "late"));

    Assert.Empty(Run().Errors);
  }

  [Fact]
  public void An_injected_definition_is_not_reported() {
    var def = ExBlockDef.Create("stub", "ontime");
    ExDefinitions.RegisterBlock(def);
    ExDefinitions.RecordInjected([def.Location]);

    Assert.Empty(Run().Errors);
  }

  [Fact]
  public void A_late_block_is_reported_with_its_name_and_the_remedy() {
    ExDefinitions.RecordInjected([]);
    ExDefinitions.RegisterBlock(ExBlockDef.Create("stub", "late"));

    string error = Assert.Single(Run().Errors);
    Assert.Contains("stub:late", error);
    Assert.Contains("Start", error);
    Assert.Contains("IExDefinitionContributor", error);
  }

  [Fact]
  public void A_late_item_is_reported_with_its_name_and_the_remedy() {
    ExDefinitions.RecordInjected([]);
    ExDefinitions.RegisterItem(ExItemDef.Create("stub", "lateitem"));

    string error = Assert.Single(Run().Errors);
    Assert.Contains("stub:lateitem", error);
    Assert.Contains("Start", error);
    Assert.Contains("IExDefinitionContributor", error);
  }

  [Fact]
  public void A_late_recipe_is_reported_with_its_name_and_the_remedy() {
    ExDefinitions.RecordInjected([]);
    ExDefinitions.RegisterRecipe(ExRecipeDef.Create("stub", "grid", "late"));

    string error = Assert.Single(Run().Errors);
    Assert.Contains("stub:late", error);
    Assert.Contains("Start", error);
    Assert.Contains("IExDefinitionContributor", error);
  }

  [Fact]
  public void A_definition_in_another_domain_is_left_alone() {
    ExDefinitions.RecordInjected([]);
    ExDefinitions.RegisterBlock(ExBlockDef.Create("other", "late"));

    Assert.Empty(Run().Errors);
  }
}
