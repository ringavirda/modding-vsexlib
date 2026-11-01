using System.Collections.Generic;
using ExpandedLib.Definitions;
using ExpandedLib.Registries;
using Vintagestory.API.Common;

namespace WasherPunch.Items;

/// <summary>A flat washer punched from plate, one variant per plate metal.</summary>
[ItemRegister]
public partial class ItemWasher : Item, IExItemDefProvider {
  public static IEnumerable<ExItemDef> Definitions(string domain) =>
    [
      ExItemDef
        .Create(domain, "washer")
        .Class<ItemWasher>()
        .MaxStackSize(128)
        .VariantGroup("metal", "iron", "copper")
        .Shape("washerpunch:item/washer")
        .Texture("metal", "game:block/metal/ingot/{metal}")
        .CreativeTab("general", "*")
        .CreativeTab("items", "*")
        .CreativeTab(domain, "*"),
    ];
}
