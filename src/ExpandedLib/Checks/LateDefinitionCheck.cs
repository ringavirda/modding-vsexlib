using System.Collections.Generic;
using ExpandedLib.Definitions;

namespace ExpandedLib.Checks;

/// <summary>
/// Catches a code-first definition whose location is not in the set
/// <see cref="ExDefinitionModSystem.AssetsLoaded"/> already injected: the object loader never sees
/// it and the world looks fine, since nothing else in the pipeline says so. Tests location membership
/// only, not content - a definition re-registered under an already-injected location, replacing what
/// was built, is not caught; <see cref="ExDefinitions"/> keys on location for the same reason (see its
/// own doc). Reads <see cref="ExDefinitions"/> directly rather than through
/// <see cref="ICheckSource"/> - the interface has no item or recipe accessor, and the question here
/// is about the live process registry, not whatever a source projects from it. Reports nothing until
/// injection has actually run once in this process: a dedicated multiplayer client, where
/// <see cref="ExDefinitionModSystem"/> never loads, and a harness path that never replays it. Not
/// singleplayer - the integrated server and the client share one process and this same static state,
/// so once the server's <c>AssetsLoaded</c> has recorded a pass the client's own check call sees it too
/// and reports against the same registry.
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
          // The bare code drops the category (ExRecipeDef.Code is the file's base name only); the
          // full location is what the injected set is keyed on, and what a modder can grep for.
          errors.Add(Message("recipe", def.Location.ToString()));
    }
    return new CheckResult("LateDefinition", domain, errors);
  }

  private static string Message(string kind, string name) =>
    $"{name} - {kind} definition registered too late to be built; register it from "
    + "Start, or from an IExDefinitionContributor. Injection happens once, at AssetsLoaded 0.04.";
}
