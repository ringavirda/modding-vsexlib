using ExpandedLib.Industry.Machining;
using ExpandedLib.Registries;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using WasherPunch.Gui;

namespace WasherPunch.BlockEntities;

/// <summary>The drilling machine's station: a plate on the table, a bit in the spindle and a chips slot,
/// cut by <see cref="BlockEntityMachineTool"/> while its mpshaft node turns.</summary>
[BlockEntityRegister]
public class BlockEntityWasherDrill : BlockEntityMachineTool {
  public override string InventoryClassName => "washerdrill";

  protected override GuiDialogBlockEntity CreateDialog(ICoreClientAPI capi) =>
    new GuiDialogWasherDrill(
      Lang.Get("washerpunch:drill-title"),
      Inventory,
      Pos,
      capi
    );
}
