using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;

namespace ExpandedLib.Registries;

/// <summary>Applies an assembly's Harmony patches once per process, gates a named category on a
/// required mod being loaded, and keeps them applied until the last side that asked for them lets
/// go.</summary>
/// <remarks>Thread-safe: both sides of a singleplayer process start and dispose on their own
/// threads.</remarks>
public static class ExHarmony {
  // Tracked as "<harmony id>::<category>"; PatchCategory is not idempotent.
  private static readonly HashSet<string> AppliedCategories = [];

  // Per "<id>::<assembly full name>", the PatchOnce calls no UnpatchAll has released yet; a key is
  // never held at zero. Harmony.HasAnyPatches(id) cannot tell categories apart.
  private static readonly Dictionary<string, int> UncategorizedPatched = [];

  /// <summary>
  /// Applies every uncategorised <c>[HarmonyPatch]</c> class in <paramref name="assembly"/> under
  /// <paramref name="mod"/>'s id, once per process. Returns the <see cref="Harmony"/> instance.
  /// </summary>
  /// <remarks>Every call takes one hold that one <see cref="UnpatchAll(Mod)"/> releases; a repeat
  /// call applies nothing new. In singleplayer the server's and the client's mod systems both call
  /// it, so the patches stay until both have disposed.</remarks>
  public static Harmony PatchOnce(Mod mod, Assembly assembly) =>
    PatchOnce(mod.Info.ModID, assembly);

  /// <summary>As <see cref="PatchOnce(Mod, Assembly)"/>, under an explicit <paramref name="id"/>.</summary>
  public static Harmony PatchOnce(string id, Assembly assembly) {
    var harmony = new Harmony(id);
    string key = id + "::" + assembly.FullName;
    lock (UncategorizedPatched) {
      UncategorizedPatched.TryGetValue(key, out int holds);
      if (holds == 0)
        harmony.PatchAllUncategorized(assembly);
      UncategorizedPatched[key] = holds + 1;
    }
    return harmony;
  }

  /// <summary>
  /// Applies the <c>[HarmonyPatchCategory(category)]</c> classes in <paramref name="assembly"/> when
  /// <paramref name="requiredModId"/> is loaded; does nothing and returns false otherwise.
  /// </summary>
  /// <remarks>Takes no hold: the category comes off with its id's last
  /// <see cref="UnpatchAll(Mod)"/>.</remarks>
  public static bool PatchCategoryWhenLoaded(
    ICoreAPI api,
    Harmony harmony,
    Assembly assembly,
    string category,
    string requiredModId
  ) {
    if (!ExMods.IsLoaded(api, requiredModId))
      return false;
    lock (UncategorizedPatched)
      if (AppliedCategories.Add(harmony.Id + "::" + category))
        harmony.PatchCategory(assembly, category);
    return true;
  }

  /// <summary>Releases one <see cref="PatchOnce(Mod, Assembly)"/> hold on each assembly patched
  /// under <paramref name="mod"/>'s id, and unpatches everything under the id, categories included,
  /// once none is still held.</summary>
  /// <remarks>With nothing held it unpatches at once, and a repeat call is harmless. In singleplayer
  /// the side that disposes first leaves the patches applied for the other.</remarks>
  public static void UnpatchAll(Mod mod) => UnpatchAll(mod.Info.ModID);

  /// <summary>As <see cref="UnpatchAll(Mod)"/>, under an explicit <paramref name="id"/>.</summary>
  public static void UnpatchAll(string id) {
    string prefix = id + "::";
    lock (UncategorizedPatched) {
      foreach (
        string key in UncategorizedPatched
          .Keys.Where(k => k.StartsWith(prefix))
          .ToList()
      )
        if (--UncategorizedPatched[key] == 0)
          UncategorizedPatched.Remove(key);
      if (UncategorizedPatched.Keys.Any(k => k.StartsWith(prefix)))
        return;
      new Harmony(id).UnpatchAll(id);
      AppliedCategories.RemoveWhere(key => key.StartsWith(prefix));
    }
  }
}
