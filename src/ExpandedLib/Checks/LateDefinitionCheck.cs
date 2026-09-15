using System.Collections.Generic;
using ExpandedLib.Definitions;

namespace ExpandedLib.Checks;

/// <summary>
/// Catches a code-first definition whose location was never injected by
/// <see cref="ExDefinitionModSystem.AssetsLoaded"/>. Reports nothing until injection has run once
/// in this process.
/// </summary>
public static class LateDefinitionCheck {
  /// <summary>Every registered block, item or recipe def in <paramref name="domain"/> that missed
  /// the injection window, as the check's <see cref="CheckResult"/>.</summary>
  public static CheckResult Run(ICheckSource source, string domain) {
    var errors = new List<string>();
    if (ExDefinitions.InjectionRan) {
      foreach (ExBlockDef def in ExDefinitions.Blocks)
        if (def.Domain == domain && !ExDefinitions.WasInjected(def.Location))
          errors.Add(Message("block", def.QualifiedCode));
      foreach (ExItemDef def in ExDefinitions.Items)
        if (def.Domain == domain && !ExDefinitions.WasInjected(def.Location))
          errors.Add(Message("item", def.Domain + ":" + def.Code));
      foreach (ExRecipeDef def in ExDefinitions.Recipes)
        if (def.Domain == domain && !ExDefinitions.WasInjected(def.Location))
          // The full location, not the bare code, is what the injected set keys on.
          errors.Add(Message("recipe", def.Location.ToString()));
    }
    return new CheckResult("LateDefinition", domain, errors);
  }

  private static string Message(string kind, string name) =>
    $"{name} - {kind} definition registered too late to be built; register it from "
    + "Start, or from an IExDefinitionContributor. Injection happens once, at AssetsLoaded 0.04.";
}
