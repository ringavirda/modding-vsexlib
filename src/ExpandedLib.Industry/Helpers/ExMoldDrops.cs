using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ExpandedLib.Industry.Helpers;

/// <summary>Reads a tool mold's cast-product templates off its block attributes, matching
/// <c>BlockEntityToolMold.GetMoldedStacks</c>. Freshly deserialized on every call and safe to
/// mutate.</summary>
public static class ExMoldDrops {
  /// <summary>The mold's drop templates, empty when it declares none.</summary>
  public static List<JsonItemStack> Templates(Block? mold) {
    var templates = new List<JsonItemStack>();
    if (mold?.Attributes == null)
      return templates;

    // Vanilla treats the two keys as exclusive: a mold with `drop` never consults `drops`.
    if (mold.Attributes["drop"].Exists) {
      JsonItemStack? one = mold.Attributes["drop"]
        .AsObject<JsonItemStack>(null, mold.Code.Domain);
      if (one != null)
        templates.Add(one);
      return templates;
    }

    JsonItemStack[]? many = mold.Attributes["drops"]
      .AsObject<JsonItemStack[]>(null, mold.Code.Domain);
    if (many != null)
      templates.AddRange(many);
    return templates;
  }
}
