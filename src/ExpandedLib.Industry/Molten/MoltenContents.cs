using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.Molten;

/// <summary>
/// Round-trips the metal a carried barrel or tool-mold item holds through the stack's
/// <c>blockEntityAttributes</c> tree, matching the shape the vanilla tool mold and this mod's barrel
/// block entity persist.
/// </summary>
public static class MoltenContents {
  /// <summary>Tree key for a barrel's stored unit count.</summary>
  public const string BarrelUnitsKey = "currentUnitAmount";

  /// <summary>Tree key for a mold's stored unit count (vanilla tool-mold convention).</summary>
  public const string MoldUnitsKey = "fillLevel";

  /// <summary>Reads the metal content and unit count carried by <paramref name="itemStack"/> under
  /// <paramref name="unitsKey"/>, or (null, 0) when it carries nothing.</summary>
  public static (ItemStack? content, int units) Read(
    ItemStack itemStack,
    string unitsKey,
    IWorldAccessor worldForResolve
  ) {
    if (
      itemStack.Attributes?["blockEntityAttributes"]
      is not ITreeAttribute beData
    )
      return (null, 0);

    ItemStack? content = beData.GetItemstack("contents");
    content?.ResolveBlockOrItem(worldForResolve);
    return (content, beData.GetInt(unitsKey));
  }

  /// <summary>Writes <paramref name="content"/> and <paramref name="units"/> onto
  /// <paramref name="itemStack"/> under <paramref name="unitsKey"/>; leaves the stack untouched when
  /// there is nothing to carry.</summary>
  public static void Write(
    ItemStack itemStack,
    string unitsKey,
    ItemStack? content,
    int units
  ) {
    if (content == null || units <= 0)
      return;

    var beData = new TreeAttribute();
    beData.SetItemstack("contents", content.Clone());
    beData.SetInt(unitsKey, units);
    if (unitsKey == MoldUnitsKey) {
      beData.SetBool("shattered", false);
      beData.SetFloat("meshAngle", 0f);
    }
    itemStack.Attributes["blockEntityAttributes"] = beData;
  }
}
