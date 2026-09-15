using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Registries;
using HarmonyLib;
using NSubstitute;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>
/// Applies a mod's Harmony patches once per test assembly and reverts them on dispose. Patches are
/// process-wide; a second fixture for the same <c>modId</c> does not double-patch.
/// </summary>
public sealed class HarmonyFixture : IDisposable {
  private readonly Mod _mod;

  /// <summary>
  /// Applies <paramref name="patches"/>' <c>[HarmonyPatch]</c> classes under <paramref name="modId"/>,
  /// or, when <paramref name="category"/> is given, only its <c>[HarmonyPatchCategory(category)]</c>
  /// classes.
  /// </summary>
  public HarmonyFixture(string modId, Assembly patches, string? category = null) {
    _mod = Substitute.For<Mod>();
    ReflectionHelpers.SetProperty(
      _mod,
      nameof(Mod.Info),
      new ModInfo { ModID = modId }
    );

    if (category == null) {
      Harmony = ExHarmony.PatchOnce(_mod, patches);
    } else {
      Harmony = new Harmony(modId);
      var loader = Substitute.For<IModLoader>();
      loader.IsModEnabled(modId).Returns(true);
      var api = Substitute.For<ICoreAPI>();
      api.ModLoader.Returns(loader);
      ExHarmony.PatchCategoryWhenLoaded(api, Harmony, patches, category, modId);
    }
  }

  /// <summary>The Harmony instance patches were applied through.</summary>
  public Harmony Harmony { get; }

  /// <summary>True when <paramref name="original"/> carries a patch owned by this fixture's mod id
  /// (through <see cref="Harmony.GetPatchInfo(MethodBase)"/>).</summary>
  public bool IsPatched(MethodBase original) =>
    HarmonyLib.Harmony.GetPatchInfo(original)?.Owners.Contains(Harmony.Id)
    ?? false;

  /// <summary>Every original method this fixture's Harmony instance has patched.</summary>
  public IReadOnlyList<MethodBase> PatchedMethods =>
    Harmony.GetPatchedMethods().ToList();

  /// <summary>Reverts every patch registered under this fixture's mod id. Safe to call twice.</summary>
  public void Dispose() => ExHarmony.UnpatchAll(_mod);
}
