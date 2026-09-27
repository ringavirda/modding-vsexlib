using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>A stack a block entity accepts stays with it: still held after a reload, and dropped
/// when the block is broken.</summary>
public static class ContainerLaw {
  internal const string Name = "container";

  internal const string FormedName = "formed container";

  /// <summary>Stands every variant with an entity of each block of <paramref name="domain"/> as
  /// <see cref="InteractionLaw.Run"/> does, offers it stacks until it accepts one, by the clicks
  /// its help names and then into its inventory, reloads it and breaks it.</summary>
  /// <remarks>A click accepts when the hand gives up items and the entity's tree then holds more of
  /// the item; the inventory, when the put moves the stack. A finding is another count after the
  /// reload, fewer dropped by the break than held, and a reload or break that throws or
  /// logs.</remarks>
  /// <param name="world">A world holding every variant of the blocks judged and the items their
  /// help names (<see cref="BlockLaws.Run"/> stands one).</param>
  /// <param name="domain">The domain whose blocks are judged.</param>
  /// <returns>The law's blocktypes with a variant that accepted a stack, the variants that did, and
  /// findings, each keyed by the variant's code.</returns>
  public static BlockLaws.Law Run(TestWorld world, string domain) {
    var findings = new List<string>();
    var sites = new BlockLaws.Sites();
    int blocks = 0,
      cases = 0;
    Block solid = BlockLaws.Solid(world);
    TestPlayer player = InteractionLaw.User(world);
    CollectibleObject[] palette = Palette(world);
    foreach (
      IGrouping<string, Block> type in BlockLaws.Blocktypes(world, domain)
    ) {
      bool judged = false;
      foreach (
        Block block in type.Where(b =>
          b.EntityClass != null && !InteractionLaw.Unplaceable(b)
        )
      ) {
        if (
          InteractionLaw.StandUp(world, block, sites.Next(), solid, null, null)
          is not { } step
        )
          continue;
        (CollectibleObject Item, int Held, string How)? accepted =
          ByClick(world, player, step) ?? ByInventory(world, step, palette);
        if (accepted is not { } stack)
          continue;
        judged = true;
        cases++;
        Follow(
          world,
          player,
          step,
          stack,
          findings,
          step.Block.Code.ToString()
        );
      }
      if (judged)
        blocks++;
    }
    return new BlockLaws.Law(Name, blocks, cases, findings);
  }

  /// <summary>Stands each structure of <paramref name="domain"/> formed, in one facing, as
  /// <see cref="InteractionLaw.RunFormed"/> does and, on a fresh structure for each of its cells
  /// holding a block outside <c>game</c> with an entity, offers that entity stacks as
  /// <see cref="Run"/> does, reloads it and breaks its cell.</summary>
  /// <remarks>A finding is what <see cref="Run"/> finds. A count lost over the reload or the break
  /// is keyed by the anchor's code and names the cell's block; a reload or break that throws or logs,
  /// by the cell's block.</remarks>
  /// <param name="world">A world holding the blocks judged, the blocks their layouts name and the
  /// items their help names (<see cref="BlockLaws.Run"/> stands one).</param>
  /// <param name="domain">The domain whose structures are formed.</param>
  /// <returns>The blocktypes with a formed structure a cell of which accepted a stack, the cells
  /// that did, and findings, keyed by the anchor's variant code.</returns>
  public static BlockLaws.Law RunFormed(TestWorld world, string domain) {
    var findings = new List<string>();
    var sites = new BlockLaws.Sites();
    int blocks = 0,
      cases = 0;
    TestPlayer player = InteractionLaw.User(world);
    CollectibleObject[] palette = Palette(world);
    Block[] registered = MultiblockLaw.Registered(world);
    foreach (
      IGrouping<string, Block> type in BlockLaws.Blocktypes(world, domain)
    ) {
      bool judged = false;
      foreach (Block block in MultiblockLaw.OneFacing(world, type)) {
        if (
          MultiblockLaw.Formed(world, block, sites.Next(), registered, null)
          is not { } probe
        )
          continue;
        foreach (
          BlockPos cell in MultiblockLaw
            .FormedCells(world, probe)
            .Where(c => world.GetBlockEntity(c) != null)
        ) {
          var offset = new Vec3i(
            cell.X - probe.At.X,
            cell.Y - probe.At.Y,
            cell.Z - probe.At.Z
          );
          if (
            MultiblockLaw.Formed(world, block, sites.Next(), registered, null)
            is not { } formed
          )
            break;
          BlockPos at = formed.At.AddCopy(offset.X, offset.Y, offset.Z);
          var step = new BlockLaws.EntityCase(
            world,
            world.GetBlock(at),
            at,
            findings
          );
          if (
            (ByClick(world, player, step) ?? ByInventory(world, step, palette))
            is not { } stack
          )
            continue;
          judged = true;
          cases++;
          Follow(
            world,
            player,
            step,
            stack,
            findings,
            $"{block.Code} formed, {step.Block.Code} on "
              + $"{BlockLaws.CellName(offset)},"
          );
        }
      }
      if (judged)
        blocks++;
    }
    return new BlockLaws.Law(FormedName, blocks, cases, findings);
  }

  /// <summary>Every item, then every block but air, of <paramref name="world"/>.</summary>
  private static CollectibleObject[] Palette(TestWorld world) =>
    [
      .. world.World.Items.Where(i => i?.Code != null),
      .. world.World.Blocks.Where(b => b?.Code != null && b.Id != 0),
    ];

  private static (CollectibleObject, int, string)? ByClick(
    TestWorld world,
    TestPlayer player,
    BlockLaws.EntityCase step
  ) {
    foreach (
      InteractionLaw.Click click in InteractionLaw
        .Clicks(world, player.Player, step)
        .Where(c => c.Item != null)
    ) {
      int before = Held(step, click.Item!);
      if (
        InteractionLaw.Press(world, player, step, click, judged: false) == null
      )
        continue;
      int left =
        InteractionLaw.Hand(player).Itemstack is { } hand
        && hand.Collectible == click.Item
          ? hand.StackSize
          : 0;
      int after = Held(step, click.Item!);
      if (left < click.Count && after > before)
        return (click.Item!, after, click.Describe());
    }
    InteractionLaw.Hand(player).Itemstack = null;
    return null;
  }

  private static (CollectibleObject, int, string)? ByInventory(
    TestWorld world,
    BlockLaws.EntityCase step,
    CollectibleObject[] palette
  ) {
    if (step.Entity is not IBlockEntityContainer { Inventory: { } inventory })
      return null;
    foreach (CollectibleObject item in palette) {
      var source = new DummySlot(InteractionLaw.Fresh(item, 1));
      if (inventory.GetBestSuitedSlot(source)?.slot is not { } slot)
        continue;
      if (source.TryPutInto(world.World, slot, 1) > 0)
        return (item, Held(step, item), "put into its inventory");
    }
    return null;
  }

  private static void Follow(
    TestWorld world,
    TestPlayer player,
    BlockLaws.EntityCase step,
    (CollectibleObject Item, int Held, string How) accepted,
    List<string> findings,
    string who
  ) {
    string what =
      $"{who} held {accepted.Held} {accepted.Item.Code} {accepted.How}";
    if (!step.Reload(judged: true))
      return;
    int kept = Held(step, accepted.Item);
    if (kept != accepted.Held) {
      findings.Add($"{what}, and {kept} after the reload");
      return;
    }
    int from = world.Drops.Count;
    if (!step.Break(player.Player, judged: true))
      return;
    int dropped = world
      .Drops.Skip(from)
      .Where(d => d.Collectible?.Code?.Equals(accepted.Item.Code) == true)
      .Sum(d => d.StackSize);
    if (dropped < accepted.Held)
      findings.Add($"{what}, and broken dropped {dropped}");
  }

  /// <summary>How many of <paramref name="item"/> the tree of <paramref name="step"/>'s entity
  /// holds in item stacks, at any depth; 0 without an entity.</summary>
  internal static int Held(BlockLaws.EntityCase step, CollectibleObject item) =>
    step.Entity is { } entity
    && step.Tree(entity, "held", judged: false) is { } tree
      ? Count(tree, item)
      : 0;

  private static int Count(ITreeAttribute tree, CollectibleObject item) {
    int held = 0;
    foreach (KeyValuePair<string, IAttribute> entry in tree)
      held += entry.Value switch {
        ItemstackAttribute { value: { } stack }
          when stack.Collectible?.Code?.Equals(item.Code) == true =>
          stack.StackSize,
        ITreeAttribute inner => Count(inner, item),
        TreeArrayAttribute { value: { } trees } => trees.Sum(t =>
          Count(t, item)
        ),
        _ => 0,
      };
    return held;
  }
}
