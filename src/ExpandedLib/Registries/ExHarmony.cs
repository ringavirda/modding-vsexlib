using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;

namespace ExpandedLib.Registries;

/// <summary>Applies an assembly's Harmony patches once per process, gates a named category on a
/// required mod being loaded, and unpatches cleanly on dispose.</summary>
public static class ExHarmony {
  // Tracked as "<harmony id>::<category>"; PatchCategory is not idempotent.
  private static readonly HashSet<string> AppliedCategories = [];

  // Tracked as "<id>::<assembly full name>"; Harmony.HasAnyPatches(id) cannot tell categories apart.
  private static readonly HashSet<string> UncategorizedPatched = [];

  /// <summary>
  /// Applies every uncategorised <c>[HarmonyPatch]</c> class in <paramref name="assembly"/> under
  /// <paramref name="mod"/>'s id, once per process. Returns the <see cref="Harmony"/> instance.
  /// </summary>
  public static Harmony PatchOnce(Mod mod, Assembly assembly) =>
    PatchOnce(mod.Info.ModID, assembly);

  /// <summary>As <see cref="PatchOnce(Mod, Assembly)"/>, under an explicit <paramref name="id"/>.</summary>
  public static Harmony PatchOnce(string id, Assembly assembly) {
    var harmony = new Harmony(id);
    if (UncategorizedPatched.Add(id + "::" + assembly.FullName))
      harmony.PatchAllUncategorized(assembly);
    return harmony;
  }

  /// <summary>
  /// Applies the <c>[HarmonyPatchCategory(category)]</c> classes in <paramref name="assembly"/> when
  /// <paramref name="requiredModId"/> is loaded; does nothing and returns false otherwise.
  /// </summary>
  public static bool PatchCategoryWhenLoaded(
    ICoreAPI api,
    Harmony harmony,
    Assembly assembly,
    string category,
    string requiredModId
  ) {
    if (!ExMods.IsLoaded(api, requiredModId))
      return false;
    if (AppliedCategories.Add(harmony.Id + "::" + category))
      harmony.PatchCategory(assembly, category);
    return true;
  }

  /// <summary>Unpatches everything registered under <paramref name="mod"/>'s id.</summary>
  public static void UnpatchAll(Mod mod) => UnpatchAll(mod.Info.ModID);

  /// <summary>As <see cref="UnpatchAll(Mod)"/>, under an explicit <paramref name="id"/>.</summary>
  public static void UnpatchAll(string id) {
    new Harmony(id).UnpatchAll(id);
    AppliedCategories.RemoveWhere(key => key.StartsWith(id + "::"));
    UncategorizedPatched.RemoveWhere(key => key.StartsWith(id + "::"));
  }
}
