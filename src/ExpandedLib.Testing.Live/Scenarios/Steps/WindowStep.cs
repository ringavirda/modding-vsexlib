using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Scenarios;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Server;

namespace ExpandedLib.Testing.Live;

/// <summary><c>window</c>: works a block entity's dialog through the entity's own client packet handler, as
/// the dialog sends it: open, a slot click for each put or take with the stack on the cursor, a button,
/// then close. The hand is cleared when the step ends.</summary>
internal sealed class WindowStep : ScenarioStep {
  private const int OpenPacketId = 1000;
  private const int ClosePacketId = 1001;

  public override string Kind => "window";

  public override string What(SetupStep step) =>
    step.Body["at"]?.ToString(Formatting.None) ?? "";

  /// <summary>The slot indices a put or take names: one index, or each index of the inclusive range
  /// <c>[first, last]</c> in order.</summary>
  /// <exception cref="StepFailure">The range runs backward.</exception>
  public static int[] Slots(JToken token) {
    if (token is not JArray range)
      return [(int)token];
    int first = (int)range[0]!;
    int last = (int)range[1]!;
    if (last < first)
      throw new StepFailure(
        $"the slot range [{first}, {last}] runs backward.",
        Suspect.Scenario
      );
    return [.. Enumerable.Range(first, last - first + 1)];
  }

  /// <summary>The payload a press sends: each value a byte.</summary>
  /// <exception cref="StepFailure">A value is outside 0 to 255.</exception>
  public static byte[] Data(JArray values) {
    var bytes = new List<byte>();
    foreach (JToken value in values) {
      int b = (int)value;
      if (b < 0 || b > 255)
        throw new StepFailure(
          $"{b} is not a byte value, 0 to 255.",
          Suspect.Scenario
        );
      bytes.Add((byte)b);
    }
    return [.. bytes];
  }

  /// <summary>The packet id a button sends, which lies above the close packet.</summary>
  /// <exception cref="StepFailure">The id is the open, close or a slot packet.</exception>
  public static int ButtonId(int id) {
    if (id <= ClosePacketId)
      throw new StepFailure(
        $"press {id}: a button's packet id is above {ClosePacketId}.",
        Suspect.Scenario
      );
    return id;
  }

  public override IEnumerable<bool> Run(
    ScenarioContext ctx,
    SetupStep step,
    string label,
    StepReport report
  ) {
    var o = (JObject)step.Body;
    ScenarioPlayer player =
      ctx.Player
      ?? throw new StepFailure(
        "\"window\" needs the scenario player.",
        Suspect.Scenario
      );
    BlockPos local = ctx.Position(step, o["at"], "at");
    BlockEntity be = SetStep.EntityAt(ctx, local);
    ServerPlayer who = player.Player;
    ctx.Touched.Add(local.Copy());
    var done = new List<string>();
    try {
      if (o["open"] is { } open && (bool)open) {
        be.OnReceivedClientPacket(who, OpenPacketId, null);
        done.Add("open");
      }
      if (o["put"] is JObject put)
        done.Add(Put(ctx, who, be, put));
      if (o["take"] is JObject take)
        done.Add(Take(who, be, (int)take["slot"]!));
      if (o["press"] is JObject press) {
        int id = ButtonId((int)press["id"]!);
        byte[]? data = press["data"] is JArray values ? Data(values) : null;
        be.OnReceivedClientPacket(who, id, data);
        done.Add($"press {id}");
      }
      if (o["close"] is { } close && (bool)close) {
        be.OnReceivedClientPacket(who, ClosePacketId, null);
        done.Add("close");
      }
      report.Value = string.Join("; ", done);
    } finally {
      who.InventoryManager.ActiveHotbarSlot.Itemstack = null;
    }
    yield break;
  }

  private static string Put(
    ScenarioContext ctx,
    ServerPlayer who,
    BlockEntity be,
    JObject put
  ) {
    ItemStack stack = StepValues.Stack(
      ctx,
      (string)put["stack"]!,
      (int?)put["count"] ?? 1
    );
    int wanted = stack.StackSize;
    ItemSlot hand = who.InventoryManager.ActiveHotbarSlot;
    hand.Itemstack = stack;
    int moved = 0;
    foreach (
      int slot in Slots(
        put["slot"] ?? throw new StepFailure("\"put\" takes \"slot\".")
      )
    ) {
      if (hand.Empty)
        break;
      moved += PutOnce(who, be, slot);
    }
    if (moved < wanted)
      throw new StepFailure(
        $"the window took {moved} of {wanted} {stack.Collectible.Code}."
      );
    return $"{moved} x {stack.Collectible.Code}";
  }

  private static int PutOnce(ServerPlayer who, BlockEntity be, int slot) {
    ItemSlot hand = who.InventoryManager.ActiveHotbarSlot;
    ItemSlot cursor = who.InventoryManager.MouseItemSlot;
    ItemStack held = hand.Itemstack!;
    int count = held.StackSize;
    cursor.Itemstack = held;
    hand.Itemstack = null;
    Click(who, be, slot);
    ItemStack? left = cursor.Itemstack;
    hand.Itemstack = left;
    cursor.Itemstack = null;
    return left != null && left.Collectible == held.Collectible
      ? count - left.StackSize
      : count;
  }

  private static string Take(ServerPlayer who, BlockEntity be, int slot) {
    ItemSlot hand = who.InventoryManager.ActiveHotbarSlot;
    ItemSlot cursor = who.InventoryManager.MouseItemSlot;
    if (!hand.Empty)
      throw new StepFailure("the hand must be empty to take.");
    Click(who, be, slot);
    ItemStack? taken = cursor.Itemstack;
    cursor.Itemstack = null;
    hand.Itemstack = taken;
    if (taken == null)
      throw new StepFailure($"slot {slot} gave nothing to take.");
    return $"took {taken.StackSize} x {taken.Collectible.Code}";
  }

  private static void Click(ServerPlayer who, BlockEntity be, int slot) {
    if (be is not IBlockEntityContainer { Inventory: InventoryBase inventory })
      throw new StepFailure(
        $"{be.GetType().Name} has no inventory to click.",
        Suspect.Scenario
      );
    var op = new ItemStackMoveOperation(
      who.Entity.World,
      EnumMouseButton.Left,
      0,
      EnumMergePriority.DirectMerge
    );
    var packet = (Packet_Client)
      inventory.InvNetworkUtil.GetActivateSlotPacket(slot, op);
    be.OnReceivedClientPacket(
      who,
      packet.Id,
      Packet_ClientSerializer.SerializeToBytes(packet)
    );
  }
}
