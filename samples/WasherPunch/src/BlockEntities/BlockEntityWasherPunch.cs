using System.Text;
using ExpandedLib.Blocks;
using ExpandedLib.Catalogues;
using ExpandedLib.Machines;
using ExpandedLib.Registries;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using WasherPunch.Gui;

namespace WasherPunch.BlockEntities;

/// <summary>The press's station: a plate slot and a washer slot, worked a stroke at a time from the
/// <see cref="ProcessJobRegistry"/> table filed under <see cref="WasherPunchModSystem.Machine"/>.</summary>
[BlockEntityRegister]
public class BlockEntityWasherPunch : BlockEntityMachineStation {
  /// <summary>Slot the player loads.</summary>
  public const int InputSlot = 0;

  /// <summary>Take-only slot the washers land in.</summary>
  public const int OutputSlot = 1;

  private int _strokes;

  protected override MachineSlotSpec[] SlotSpecs =>
    [MachineSlotSpec.AnyInput(), MachineSlotSpec.Output()];

  public override string InventoryClassName => "washerpunch";

  /// <summary>Strokes made on the plate now in the input slot.</summary>
  public int Strokes => _strokes;

  /// <summary>The job the input slot's item has at this press, or null when it is empty or no job
  /// takes it.</summary>
  public ProcessJob? CurrentJob =>
    ProcessJobRegistry.Shared.Job(
      WasherPunchModSystem.Machine,
      Inventory[InputSlot].Itemstack?.Collectible.Code.ToString(),
      null,
      null
    );

  protected override void DeclareState(ExBlockState state) =>
    state.Int("strokes", () => _strokes, v => _strokes = v);

  protected override GuiDialogBlockEntity CreateDialog(ICoreClientAPI capi) =>
    new GuiDialogWasherPunch(
      Lang.Get("washerpunch:press-title"),
      Inventory,
      Pos,
      capi
    );

  /// <summary>One fly stroke. With a job and room in the output for its count, the stroke is counted
  /// and the job's last stroke takes one plate and puts the washers out; otherwise the stroke plays
  /// and counts nothing. Server-side.</summary>
  /// <returns>Whether the stroke was counted.</returns>
  public bool Stroke() {
    Api.World.PlaySoundAt(
      new AssetLocation("game:sounds/effect/anvilhit1"),
      Pos,
      0
    );

    ProcessJob? job = CurrentJob;
    if (job == null)
      return false;

    ItemStack? product = Product(job);
    ItemSlot output = Inventory[OutputSlot];
    if (
      product == null
      || !(
        output.Empty
        || (
          output.Itemstack.Collectible == product.Collectible
          && output.StackSize + job.Count <= product.Collectible.MaxStackSize
        )
      )
    )
      return false;

    if (++_strokes < job.Strokes) {
      MarkDirty();
      return true;
    }

    _strokes = 0;
    Inventory[InputSlot].TakeOut(1);
    Inventory[InputSlot].MarkDirty();
    if (output.Empty)
      output.Itemstack = product;
    else
      output.Itemstack.StackSize += job.Count;
    output.MarkDirty();
    MarkDirty();
    return true;
  }

  private ItemStack? Product(ProcessJob job) {
    var code = new AssetLocation(job.Output);
    CollectibleObject? product =
      (CollectibleObject?)Api.World.GetItem(code) ?? Api.World.GetBlock(code);
    return product == null ? null : new ItemStack(product, job.Count);
  }

  /// <summary>The block-info line for the plate in the input slot: its stroke count against the
  /// job's, or that no job takes it.</summary>
  /// <returns>The line, or null when the input slot is empty.</returns>
  public string? InfoLine() {
    ItemStack? held = Inventory[InputSlot].Itemstack;
    if (held == null)
      return null;

    ProcessJob? job = CurrentJob;
    return job == null
      ? Lang.Get("washerpunch:press-nojob", held.GetName())
      : Lang.Get("washerpunch:press-strokes", _strokes, job.Strokes);
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    base.GetBlockInfo(forPlayer, dsc);
    string? line = InfoLine();
    if (line != null)
      dsc.AppendLine(line);
  }
}
