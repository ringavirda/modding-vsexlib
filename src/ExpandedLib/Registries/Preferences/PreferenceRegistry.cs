using System;
using System.Reflection;
using Vintagestory.API.Common;

namespace ExpandedLib.Registries;

/// <summary>Reflection-driven preference registration for mods built on ExpandedLib. Scans an
/// assembly for <see cref="IExPreference"/> classes and adds each one to
/// <see cref="ExPreferences"/>.</summary>
public static class PreferenceRegistry {
  /// <summary>
  /// Registers every <see cref="PreferenceRegisterAttribute"/>-decorated
  /// <see cref="IExPreference"/> in <paramref name="asm"/> (default: the calling assembly) with
  /// <see cref="ExPreferences"/>.
  /// </summary>
  public static void RegisterAll(ICoreAPI api, Mod mod, Assembly? asm = null) {
    asm ??= Assembly.GetCallingAssembly();
    string modId = mod.Info.ModID;

    ReflectionScan.ForEachAttributed<
      PreferenceRegisterAttribute,
      IExPreference
    >(api, modId, asm, (attr, pref) => ExPreferences.Register(pref));
  }
}
