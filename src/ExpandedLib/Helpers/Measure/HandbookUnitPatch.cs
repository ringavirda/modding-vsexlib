using System.ComponentModel;
using ExpandedLib.Helpers;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace ExpandedLib.Helpers;

/// <summary>Makes handbook page text follow the active <see cref="ExMeasure.System"/>.</summary>
[HarmonyPatch(typeof(GuiHandbookTextPage), "Init")]
[EditorBrowsable(EditorBrowsableState.Never)]
public static class HandbookUnitPatch {
  /// <summary>Rebuilds the survival handbook's pages so their prose re-converts to the current <see cref="ExMeasure.System"/>.</summary>
  public static void Rebuild(ICoreClientAPI capi) {
    try {
      var handbook = capi.ModLoader.GetModSystem<ModSystemSurvivalHandbook>();
      if (handbook == null)
        return;

      object? dialog = Traverse.Create(handbook).Field("dialog").GetValue();
      if (dialog == null)
        return;

      Traverse.Create(dialog).Method("loadEntries").GetValue();
    } catch {
      // A display refresh must not break the measure command.
    }
  }

  public static void Prefix(GuiHandbookTextPage __instance) {
    if (ExMeasure.System != MeasurementSystem.Imperial)
      return;

    try {
      var field = Traverse.Create(__instance).Field("Text");
      string? raw = field.GetValue<string>();
      if (string.IsNullOrEmpty(raw))
        return;

      string resolved = Lang.Get(raw);
      string converted = ExMeasure.ConvertMetricText(resolved);
      if (converted != raw)
        field.SetValue(converted);
    } catch {
      // On failure the page keeps the authored metric text.
    }
  }
}
