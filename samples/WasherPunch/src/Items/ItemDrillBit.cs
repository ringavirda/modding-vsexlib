using System.Collections.Generic;
using ExpandedLib.Catalogues;
using ExpandedLib.Definitions;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace WasherPunch.Items;

/// <summary>A twist drill for the drilling machine, one variant per rod metal; the steel one cuts what the
/// iron one will not.</summary>
[ItemRegister]
public partial class ItemDrillBit : Item, IExItemDefProvider {
  /// <summary>The tool kind the drilling machine's step asks for.</summary>
  public const string ToolKind = "bit";

  public static IEnumerable<ExItemDef> Definitions(string domain) =>
    [
      MachineTool
        .Itemtype(
          domain,
          "drillbit",
          ToolKind,
          new Dictionary<string, int> { ["iron"] = 1, ["steel"] = 2 },
          $"{domain}:item/drillbit",
          "metal",
          null,
          null,
          1
        )
        .Class<ItemDrillBit>()
        .Texture("metal", "game:block/metal/ingot/{metal}")
        .RootKey("durability", 40),
    ];
}
