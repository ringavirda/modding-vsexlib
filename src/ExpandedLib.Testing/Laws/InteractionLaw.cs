using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using NSubstitute;
using NSubstitute.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.Testing;

/// <summary>A block answers the hand: a right click on any of its cells with an empty hand, or
/// with any item its interaction help names, throws nothing and logs nothing; a click with a named
/// item does something; and a block entity placed from a stack carrying
/// <c>blockEntityAttributes</c> stays at its own cell, through the clicks and a reload.</summary>
public static class InteractionLaw {
  internal const string Name = "interaction";

  internal const string FormedName = "formed interaction";

  /// <summary>Stands every variant of each block of <paramref name="domain"/> as
  /// <see cref="NeighbourLaw.Run"/> does and, on a fresh placement each, clicks every cell with an
  /// empty hand and with a fresh stack of each item the cell's right-click help names, keys
  /// held.</summary>
  /// <remarks>A click goes to the block, then to the held item. An entity is placed from a stack
  /// carrying its tree, less its position. A finding is a throw or log, an entity off its cell after
  /// the placement, a click or the first reload, and a named click both refuse or that changes
  /// nothing, or a right-click help line carrying both items and a <c>ShouldApply</c>, which the
  /// engine ignores on such a line; a variant stops at its first. <see cref="BlockBehaviorUnplaceable"/> blocks are
  /// skipped.</remarks>
  /// <param name="world">A world holding the blocks judged and the items their help names
  /// (<see cref="BlockLaws.Run"/> stands one).</param>
  /// <param name="domain">The domain whose blocks are judged.</param>
  /// <returns>Blocktypes, clicks made and findings, keyed by variant code.</returns>
  public static BlockLaws.Law Run(TestWorld world, string domain) {
    var findings = new List<string>();
    var sites = new BlockLaws.Sites();
    int blocks = 0,
      cases = 0;
    Block solid = BlockLaws.Solid(world);
    TestPlayer player = User(world);
    foreach (
      IGrouping<string, Block> type in BlockLaws.Blocktypes(world, domain)
    ) {
      blocks++;
      foreach (Block block in type) {
        if (
          Unplaceable(block)
          || StandUp(world, block, sites.Next(), solid, null, null)
            is not { } probe
        )
          continue;
        ItemStack? carrying = Carrying(probe, sites.Next());
        int flagged = findings.Count;
        Click[] clicks = Clicks(world, player.Player, probe, findings);
        if (findings.Count > flagged)
          continue;
        for (int i = 0; i < clicks.Length; i++) {
          cases++;
          int earlier = findings.Count;
          bool clean =
            StandUp(world, block, sites.Next(), solid, carrying, findings)
              is { } step
            && KeepsItsCell(step, "placed", findings)
            && Make(
              world,
              player,
              step,
              clicks[i],
              findings,
              Cells(world, step),
              step.Block.Code.ToString()
            )
            && (i > 0 || Reloads(step, findings));
          if (!clean || findings.Count > earlier)
            break;
        }
      }
    }
    return new BlockLaws.Law(Name, blocks, cases, findings);
  }

  /// <summary>Stands each structure of <paramref name="domain"/> formed, in one facing, as
  /// <see cref="MultiblockLaw.Run"/> completes it, and clicks each of its cells holding a block
  /// outside <c>game</c> as <see cref="Run"/> clicks a block's cells, on a fresh structure
  /// each.</summary>
  /// <remarks>A finding is a placement or click that throws or logs, the anchor's entity off its
  /// cell after a click, a named click both refuse or that changes nothing, and a help line
  /// carrying both items and a <c>ShouldApply</c>; a cell is judged no further after its first. A
  /// structure that does not form is left to the multiblock law. A click's findings are keyed by the
  /// anchor's code and name the block clicked; a placement's, by the block placed.</remarks>
  /// <param name="world">A world holding the blocks judged, the blocks their layouts name and the
  /// items their help names (<see cref="BlockLaws.Run"/> stands one).</param>
  /// <param name="domain">The domain whose structures are formed.</param>
  /// <returns>The blocktypes with a structure that formed, clicks made and findings, keyed by the
  /// anchor's variant code.</returns>
  public static BlockLaws.Law RunFormed(TestWorld world, string domain) {
    var findings = new List<string>();
    var sites = new BlockLaws.Sites();
    int blocks = 0,
      cases = 0;
    TestPlayer player = User(world);
    Block[] registered = MultiblockLaw.Registered(world);
    foreach (
      IGrouping<string, Block> type in BlockLaws.Blocktypes(world, domain)
    ) {
      bool formed = false;
      foreach (Block block in MultiblockLaw.OneFacing(world, type)) {
        if (
          MultiblockLaw.Formed(world, block, sites.Next(), registered, null)
          is not { } probe
        )
          continue;
        formed = true;
        int flagged = findings.Count;
        Click[] clicks = Clicks(
          world,
          player.Player,
          probe,
          findings,
          MultiblockLaw.FormedCells(world, probe)
        );
        if (findings.Count > flagged)
          continue;
        var stopped = new HashSet<(int, int, int)>();
        foreach (Click click in clicks) {
          if (stopped.Contains((click.Offset.X, click.Offset.Y, click.Offset.Z)))
            continue;
          cases++;
          int earlier = findings.Count;
          if (
            MultiblockLaw.Formed(
              world,
              block,
              sites.Next(),
              registered,
              findings
            )
            is not { } step
          )
            break;
          if (
            !Make(
              world,
              player,
              step,
              click,
              findings,
              MultiblockLaw.FormedCells(world, step),
              $"{block.Code} formed"
            )
            || findings.Count > earlier
          )
            stopped.Add((click.Offset.X, click.Offset.Y, click.Offset.Z));
        }
      }
      if (formed)
        blocks++;
    }
    return new BlockLaws.Law(FormedName, blocks, cases, findings);
  }

  /// <summary>One click a case makes: on the cell at <see cref="Offset"/> from the principal,
  /// with <see cref="Keys"/> held and a fresh stack of <see cref="Item"/> in hand (an empty hand
  /// for null), advertised under <see cref="Action"/> when it names an item.</summary>
  internal sealed record Click(
    Vec3i Offset,
    Keys Keys,
    CollectibleObject? Item,
    int Count,
    string? Action
  ) {
    /// <summary>Items carried in the hotbar's next slots beside the hand: the first item of each
    /// other interaction the cell's help lists under the same action and keys, as a construction
    /// stage lists each ingredient it takes together.</summary>
    public (CollectibleObject Item, int Count)[] Carried { get; init; } = [];

    /// <summary>The code of the block clicked, named on a cell of a formed structure; null
    /// elsewhere.</summary>
    public AssetLocation? On { get; init; }

    /// <summary>What the click is, for a finding.</summary>
    public string Describe() {
      string hand = Item == null ? "an empty hand" : $"{Item.Code}";
      if (Carried.Length > 0)
        hand +=
          " and "
          + string.Join(", ", Carried.Select(c => c.Item.Code.ToString()))
          + " carried";
      string keys =
        Keys == Keys.None
          ? ""
          : $" and {Keys.ToString().ToLowerInvariant()} held";
      return $"clicked on {Where(Offset, On)} with {hand}{keys}";
    }
  }

  /// <summary>A cell named by its offset from the principal, and by the block it holds when
  /// <paramref name="on"/> is given.</summary>
  private static string Where(Vec3i offset, AssetLocation? on) =>
    on == null
      ? BlockLaws.CellName(offset)
      : $"{BlockLaws.CellName(offset)}, {on},";

  /// <summary>The keys a click holds.</summary>
  [Flags]
  internal enum Keys {
    None = 0,
    Sneak = 1,
    Ctrl = 2,
  }

  /// <summary>Whether the game never sets <paramref name="block"/> in the world: it carries
  /// vanilla's <see cref="BlockBehaviorUnplaceable"/>, as a ground-stored vessel does.</summary>
  internal static bool Unplaceable(Block block) =>
    block.BlockBehaviors?.Any(b => b is BlockBehaviorUnplaceable) == true;

  /// <summary>A survival test player whose active slot is the first of its hotbar, where the
  /// game's construction pays from.</summary>
  internal static TestPlayer User(TestWorld world) {
    TestPlayer player = world.Player("user");
    player.GameMode = EnumGameMode.Survival;
    player.Player.InventoryManager.ActiveHotbarSlot.Returns(Hand(player));
    player.Entity.RightHandItemSlot.Returns(Hand(player));
    return player;
  }

  /// <summary>The slot a <see cref="User"/> holds its stack in.</summary>
  internal static ItemSlot Hand(TestPlayer player) => player.Hotbar[0];

  /// <summary>Places <paramref name="block"/> at <paramref name="site"/> on the solid stand-in,
  /// which also goes under each filler cell with air below, as <see cref="NeighbourLaw"/> stands a
  /// block; a footprint holding the cell below the principal stands on air.</summary>
  /// <param name="from">The stack placed from; a plain stack of the block for null.</param>
  /// <param name="faults">Where a placement's throw or log goes; null leaves it unjudged.</param>
  /// <returns>The case; null when the placement threw or logged.</returns>
  internal static BlockLaws.EntityCase? StandUp(
    TestWorld world,
    Block block,
    BlockPos site,
    Block solid,
    ItemStack? from,
    List<string>? faults
  ) {
    if (
      !BlockLaws
        .Signals(world, block)
        .Footprint.Any(f => f.Offset is { X: 0, Y: -1, Z: 0 })
    )
      world.Accessor.SetBlock(solid.BlockId, site.DownCopy());
    var step = new BlockLaws.EntityCase(world, block, site, faults);
    if (!step.Place(judged: faults != null, from))
      return null;
    foreach (BlockPos cell in Cells(world, step))
      if (world.GetBlock(cell.DownCopy()).Id == 0)
        world.Accessor.SetBlock(solid.BlockId, cell.DownCopy());
    return step;
  }

  /// <summary>The principal's cell, then each of its fillers'.</summary>
  internal static BlockPos[] Cells(
    TestWorld world,
    BlockLaws.EntityCase step
  ) => [step.At.Copy(), .. BlockLaws.FillersOf(world, step.At)];

  /// <summary>The selection a click and a help read use: the cell's top face at its
  /// centre.</summary>
  internal static BlockSelection Selection(BlockPos cell) =>
    new() {
      Position = cell.Copy(),
      Face = BlockFacing.UP,
      HitPosition = new Vec3d(0.5, 1, 0.5),
    };

  /// <summary>The clicks <paramref name="probe"/>'s help calls for: an empty hand on each cell, then
  /// each right-click interaction each cell's help lists, once per distinct item it names and with
  /// an empty hand when it names none, keys as listed; duplicates dropped. A line carrying both
  /// items and a <c>ShouldApply</c> goes to <paramref name="findings"/> when given.</summary>
  /// <param name="cells">The cells clicked, each click naming the block it holds; the principal's
  /// and its fillers' (<see cref="Cells"/>) for null.</param>
  internal static Click[] Clicks(
    TestWorld world,
    IPlayer player,
    BlockLaws.EntityCase probe,
    List<string>? findings = null,
    BlockPos[]? cells = null
  ) {
    var clicks = new List<Click>();
    foreach (BlockPos cell in cells ?? Cells(world, probe)) {
      var offset = new Vec3i(
        cell.X - probe.At.X,
        cell.Y - probe.At.Y,
        cell.Z - probe.At.Z
      );
      AssetLocation? on = cells == null ? null : world.GetBlock(cell).Code;
      clicks.Add(new Click(offset, Keys.None, null, 0, null) { On = on });
      BlockSelection selection = Selection(cell);
      WorldInteraction[] helps =
      [
        .. probe
          .Help(cell, selection, player)
          .Where(h => h.MouseButton == EnumMouseButton.Right),
      ];
      foreach (WorldInteraction help in helps) {
        if (
          findings != null
          && help.Itemstacks != null
          && help.ShouldApply != null
        )
          findings.Add(
            $"{probe.Block.Code} help on {Where(offset, on)} for "
              + $"\"{help.ActionLangCode}\" carries items and a ShouldApply, which the "
              + "engine ignores on a line with items"
          );
        Keys keys = KeysOf(help);
        ItemStack[] named = Named(help, selection);
        if (named.Length == 0)
          clicks.Add(new Click(offset, keys, null, 0, null) { On = on });
        WorldInteraction[] alongside =
        [
          .. helps.Where(h =>
            h != help
            && h.ActionLangCode == help.ActionLangCode
            && KeysOf(h) == keys
          ),
        ];
        foreach (ItemStack stack in named)
          clicks.Add(
            new Click(
              offset,
              keys,
              stack.Collectible,
              Math.Max(1, stack.StackSize),
              help.ActionLangCode
            ) {
              On = on,
              Carried =
              [
                .. alongside
                  .Select(h => Companion(Named(h, selection), stack))
                  .OfType<ItemStack>()
                  .Select(s => (s.Collectible, Math.Max(1, s.StackSize))),
              ],
            }
          );
      }
    }
    return
    [
      .. clicks.DistinctBy(c =>
        (c.Offset.X, c.Offset.Y, c.Offset.Z, c.Keys, c.Item?.Code?.ToString())
      ),
    ];
  }

  /// <summary>The first of <paramref name="named"/> whose variant agrees with
  /// <paramref name="hand"/>'s on every group both carry, as a stage storing a metal takes one
  /// metal throughout; the first of them when none agrees, null when there are none.</summary>
  private static ItemStack? Companion(ItemStack[] named, ItemStack hand) =>
    named.FirstOrDefault(s =>
      s.Collectible.Variant?.All(v =>
        hand.Collectible.Variant?[v.Key] is not { } mine || mine == v.Value
      ) != false
    ) ?? named.FirstOrDefault();

  /// <summary>The stacks <paramref name="help"/> names, from its list and its matching-stacks
  /// callback, each with a collectible, one per code.</summary>
  private static ItemStack[] Named(
    WorldInteraction help,
    BlockSelection selection
  ) {
    IEnumerable<ItemStack> listed = help.Itemstacks ?? [];
    try {
      listed = listed.Concat(
        help.GetMatchingStacks?.Invoke(help, selection, null) ?? []
      );
    } catch (Exception) {
      // The callback is the client's; one that needs a client names nothing here.
    }
    return
    [
      .. listed
        .Where(s => s?.Collectible?.Code != null)
        .DistinctBy(s => s.Collectible.Code.ToString()),
    ];
  }

  private static Keys KeysOf(WorldInteraction help) {
    Keys keys = Keys.None;
    foreach (string code in (help.HotKeyCodes ?? []).Append(help.HotKeyCode))
      keys |= code switch {
        "sneak" or "shift" => Keys.Sneak,
        "sprint" or "ctrl" => Keys.Ctrl,
        _ => Keys.None,
      };
    return keys;
  }

  /// <summary>A stack of <paramref name="probe"/>'s block carrying the tree an entity of it writes
  /// at <paramref name="spare"/> as <c>blockEntityAttributes</c>, less its position; null for a
  /// block without an entity or when the spawn throws or logs.</summary>
  private static ItemStack? Carrying(BlockLaws.EntityCase probe, BlockPos spare) {
    if (
      probe.Block.EntityClass == null
      || probe.FreshTree(spare, judged: false) is not { } tree
    )
      return null;
    foreach (string key in new[] { "posx", "posy", "posz" })
      tree.RemoveAttribute(key);
    var stack = new ItemStack(probe.Block);
    stack.Attributes["blockEntityAttributes"] = tree;
    return stack;
  }

  /// <summary>A fresh stack of <paramref name="item"/>, as the game makes one: its code and
  /// <paramref name="count"/>, no attributes.</summary>
  internal static ItemStack Fresh(CollectibleObject item, int count) =>
    item is Block block
      ? new ItemStack(block, count)
      : new ItemStack((Item)item, count);

  /// <summary>Makes <paramref name="click"/> on <paramref name="step"/> and judges it.</summary>
  /// <param name="cells">The cells whose codes and trees the click may change.</param>
  /// <param name="who">What a finding starts with.</param>
  /// <returns>False when the click threw or logged, or moved the entity off its cell.</returns>
  private static bool Make(
    TestWorld world,
    TestPlayer player,
    BlockLaws.EntityCase step,
    Click click,
    List<string> findings,
    BlockPos[] cells,
    string who
  ) {
    Snapshot before = Snapshot.Of(world, step, cells);
    bool? taken = Press(world, player, step, click, judged: true);
    if (taken == null || !KeepsItsCell(step, click.Describe(), findings))
      return false;
    if (click.Item == null)
      return true;
    string what =
      $"{who} {click.Describe()}, which its help names for "
      + $"\"{click.Action}\",";
    if (taken == false)
      findings.Add($"{what} and the click was refused");
    else if (!before.Changed(world, player, step, cells, click))
      findings.Add($"{what} and the click changed nothing");
    return true;
  }

  /// <summary>Holds a fresh stack of the click's item and its keys, clears the recorded calls the
  /// click's feedback is read from, clicks, and lets go.</summary>
  /// <returns>Whether the block or the held item took the click; null when a step threw or
  /// logged.</returns>
  internal static bool? Press(
    TestWorld world,
    TestPlayer player,
    BlockLaws.EntityCase step,
    Click click,
    bool judged
  ) {
    Hand(player).Itemstack =
      click.Item == null ? null : Fresh(click.Item, click.Count);
    for (int i = 1; i < player.Hotbar.Count; i++)
      player.Hotbar[i].Itemstack =
        i <= click.Carried.Length
          ? Fresh(click.Carried[i - 1].Item, click.Carried[i - 1].Count)
          : null;
    Hold(player, click.Keys);
    foreach (object heard in Listeners(world, player))
      heard.ClearReceivedCalls();
    BlockPos cell = step.At.AddCopy(
      click.Offset.X,
      click.Offset.Y,
      click.Offset.Z
    );
    bool? taken = step.Use(
      Selection(cell),
      player.Player,
      click.Describe(),
      judged
    );
    Hold(player, Keys.None);
    return taken;
  }

  private static void Hold(TestPlayer player, Keys keys) {
    EntityControls controls = player.Entity.Controls;
    controls.Sneak = controls.ShiftKey = keys.HasFlag(Keys.Sneak);
    controls.Sprint = controls.CtrlKey = keys.HasFlag(Keys.Ctrl);
  }

  /// <summary>The substitutes a click's feedback reaches: the world, the api, its network, the
  /// player and the player's inventory manager.</summary>
  private static object[] Listeners(TestWorld world, TestPlayer player) =>
    [
      world.World,
      world.Api,
      world.Api.Network,
      player.Player,
      player.Player.InventoryManager,
    ];

  /// <summary>Calls through which a block tells or gives the player something.</summary>
  private static readonly HashSet<string> Feedback =
  [
    "SendIngameError",
    "SendMessage",
    "SendLocalisedMessage",
    "SendIngameDiscovery",
    "PlaySoundAt",
    "PlaySoundFor",
    "SpawnParticles",
    "SpawnCubeParticles",
    "TryGiveItemstack",
    "SendBlockEntityPacket",
  ];

  /// <summary>Whether the entity at <paramref name="step"/>'s cell, if it has one, stands at that
  /// cell; a finding names where it stands instead.</summary>
  internal static bool KeepsItsCell(
    BlockLaws.EntityCase step,
    string when,
    List<string> findings
  ) {
    if (step.Entity is not { } entity || entity.Pos.Equals(step.At))
      return true;
    findings.Add(
      $"{step.Block.Code} {when} left its block entity at {entity.Pos.X}, {entity.Pos.Y}, "
        + $"{entity.Pos.Z}, not at its cell"
    );
    return false;
  }

  private static bool Reloads(
    BlockLaws.EntityCase step,
    List<string> findings
  ) =>
    step.Block.EntityClass == null
    || step.Entity == null
    || step.Reload(judged: true) && KeepsItsCell(step, "reloaded", findings);

  /// <summary>What a click can change: the hand, each cell's code and entity tree, the drops
  /// spawned.</summary>
  private sealed class Snapshot {
    private string[] _codes = [];
    private TreeAttribute?[] _trees = [];
    private int _drops;

    internal static Snapshot Of(
      TestWorld world,
      BlockLaws.EntityCase step,
      BlockPos[] cells
    ) =>
      new() {
        _codes = [.. cells.Select(c => Code(world, c))],
        _trees = [.. cells.Select(c => Tree(world, step, c))],
        _drops = world.Drops.Count,
      };

    /// <summary>Whether the click changed anything this snapshot holds, or told, gave or played the
    /// player anything.</summary>
    internal bool Changed(
      TestWorld world,
      TestPlayer player,
      BlockLaws.EntityCase step,
      BlockPos[] cells,
      Click click
    ) {
      if (world.Drops.Count != _drops)
        return true;
      if (
        Listeners(world, player)
          .SelectMany(l => l.ReceivedCalls())
          .Any(c => Feedback.Contains(c.GetMethodInfo().Name))
      )
        return true;
      if (!Holds(player, click))
        return true;
      for (int i = 0; i < cells.Length; i++) {
        if (Code(world, cells[i]) != _codes[i])
          return true;
        TreeAttribute? tree = Tree(world, step, cells[i]);
        if ((tree == null) != (_trees[i] == null))
          return true;
        if (
          tree != null
          && (
            ExTree.Differences(_trees[i]!, tree, tree).Any()
            || ExTree.Differences(tree, _trees[i]!, new TreeAttribute()).Any()
          )
        )
          return true;
      }
      return false;
    }

    /// <summary>Whether the hotbar still holds the fresh stacks the click put there.</summary>
    private static bool Holds(TestPlayer player, Click click) =>
      player
        .Hotbar.Select(
          (slot, i) =>
            Same(
              slot.Itemstack,
              i == 0 ? (click.Item, click.Count)
                : i <= click.Carried.Length ? click.Carried[i - 1]
                : (null, 0)
            )
        )
        .All(same => same);

    private static bool Same(
      ItemStack? held,
      (CollectibleObject? Item, int Count) put
    ) =>
      put.Item == null
        ? held == null
        : held != null
          && held.Collectible == put.Item
          && held.StackSize == put.Count
          && held.Attributes.Count == 0;

    private static string Code(TestWorld world, BlockPos cell) =>
      world.GetBlock(cell).Code?.ToString() ?? "";

    private static TreeAttribute? Tree(
      TestWorld world,
      BlockLaws.EntityCase step,
      BlockPos cell
    ) =>
      world.GetBlockEntity(cell) is { } entity
        ? step.Tree(entity, "clicked", judged: false)
        : null;
  }
}
