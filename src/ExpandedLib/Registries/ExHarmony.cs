using System;
using System.Collections.Generic;
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

  // Per id, the assembly it patches (full name) and the PatchOnce calls no UnpatchAll has released
  // yet; an id is never held at zero. Harmony.HasAnyPatches(id) cannot tell categories apart.
  private static readonly Dictionary<
    string,
    (string Assembly, int Holds)
  > UncategorizedPatched = [];

  /// <summary>
  /// Applies every uncategorised <c>[HarmonyPatch]</c> class in <paramref name="assembly"/> under
  /// <paramref name="mod"/>'s id, once per process. Returns the <see cref="Harmony"/> instance.
  /// </summary>
  /// <remarks>Every call takes one hold that one <see cref="UnpatchAll(Mod)"/> releases; a repeat
  /// call applies nothing new. In singleplayer the server's and the client's mod systems both call
  /// it, so the patches stay until both have disposed. One id patches one assembly and one assembly
  /// is patched under one id while any hold is left on the pair.</remarks>
  /// <exception cref="InvalidOperationException">The id holds another assembly, or
  /// <paramref name="assembly"/> is held under another id; the message names both ids or both
  /// assemblies, and nothing is patched or held.</exception>
  public static Harmony PatchOnce(Mod mod, Assembly assembly) =>
    PatchOnce(mod.Info.ModID, assembly);

  /// <summary>As <see cref="PatchOnce(Mod, Assembly)"/>, under an explicit <paramref name="id"/>.</summary>
  /// <exception cref="InvalidOperationException">As <see cref="PatchOnce(Mod, Assembly)"/>.</exception>
  public static Harmony PatchOnce(string id, Assembly assembly) {
    var harmony = new Harmony(id);
    string name = assembly.FullName!;
    lock (UncategorizedPatched) {
      if (UncategorizedPatched.TryGetValue(id, out var held)) {
        if (held.Assembly != name)
          throw new InvalidOperationException(
            $"Harmony id {id} cannot patch {assembly.GetName().Name}: it already holds "
              + $"{new AssemblyName(held.Assembly).Name}, and one id patches one assembly"
          );
        UncategorizedPatched[id] = (name, held.Holds + 1);
        return harmony;
      }
      foreach ((string other, var hold) in UncategorizedPatched)
        if (hold.Assembly == name)
          throw new InvalidOperationException(
            $"Harmony id {id} cannot patch {assembly.GetName().Name}: id {other} already holds "
              + "it, and one assembly is patched under one id"
          );
      harmony.PatchAllUncategorized(assembly);
      UncategorizedPatched[id] = (name, 1);
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

  /// <summary>Releases one <see cref="PatchOnce(Mod, Assembly)"/> hold on the assembly patched
  /// under <paramref name="mod"/>'s id, and unpatches everything under the id, categories included,
  /// once none is still held.</summary>
  /// <remarks>Each call releases one hold on the id, whoever took it: call it once per
  /// <see cref="PatchOnce(Mod, Assembly)"/>, from the code that made that call and only after it
  /// returned. With nothing held it unpatches at once. In singleplayer the side that disposes first
  /// leaves the patches applied for the other. Once the last hold is released the id and its
  /// assembly are free to pair with others.</remarks>
  public static void UnpatchAll(Mod mod) => UnpatchAll(mod.Info.ModID);

  /// <summary>As <see cref="UnpatchAll(Mod)"/>, under an explicit <paramref name="id"/>.</summary>
  public static void UnpatchAll(string id) {
    lock (UncategorizedPatched) {
      if (UncategorizedPatched.TryGetValue(id, out var held)) {
        if (held.Holds == 1)
          UncategorizedPatched.Remove(id);
        else
          UncategorizedPatched[id] = (held.Assembly, held.Holds - 1);
      }
      UnpatchUnheldLocked(id);
    }
  }

  /// <summary>Unpatches everything under <paramref name="id"/>, categories included, unless a
  /// <see cref="PatchOnce(string, Assembly)"/> hold is left on it; releases no hold. For a caller
  /// that applied only categories and so holds nothing.</summary>
  internal static void UnpatchUnheld(string id) {
    lock (UncategorizedPatched)
      UnpatchUnheldLocked(id);
  }

  // The caller holds the lock on UncategorizedPatched.
  private static void UnpatchUnheldLocked(string id) {
    if (UncategorizedPatched.ContainsKey(id))
      return;
    new Harmony(id).UnpatchAll(id);
    AppliedCategories.RemoveWhere(key => key.StartsWith(id + "::"));
  }
}
