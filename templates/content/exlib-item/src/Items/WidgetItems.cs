using System.Collections.Generic;
using ExpandedLib.Definitions;

namespace WidgetNamespace.Items;

/// <summary>A code-first item, the linen sack shape. Pure: no asset reads, so it is unit-testable
/// without a <see cref="Vintagestory.API.Common.ICoreAPI"/>.</summary>
public class WidgetItems : IExItemDefProvider {
  public static IEnumerable<ExItemDef> Definitions(string domain) =>
    [
      ExItemDef
        .Create(domain, "widget")
        .Shape("game:item/bag/linensack")
        .MaxStackSize(16)
        .CreativeCommon("*"),
    ];
}
