#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Blocks;
using ExpandedLib.Catalogues;
using ExpandedLib.Machines;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

public sealed partial class SetupScene
{
  /// <summary>Walks the file's steps against a copy of its pack and the load's registrations
  /// before anything is placed and refuses with every problem no earlier step can change
  /// (setups.md, The walk before the steps).</summary>
  private void Preflight(System.Func<string, SetupFileModel?>? stages)
  {
    var walk = new PreflightWalk(this, stages);
    walk.Run();
  }

  private sealed class PreflightWalk(
    SetupScene scene,
    System.Func<string, SetupFileModel?>? stages
  )
  {
    private static readonly Regex LedgerShort = new(
      @"the pack holds (no |\d+ of )",
      RegexOptions.Compiled
    );

    private readonly List<string> _problems = [];
    private readonly Dictionary<(int X, int Y, int Z), Block> _blocks = [];
    private readonly Dictionary<string, BlockPos> _ids = [];
    private readonly Dictionary<string, IReadOnlyList<CraftOrder>?> _orders =
    [];
    private readonly Dictionary<string, SetupPlacement> _placed = [];
    private SetupWorldView? _view;
    private IReadOnlyList<CraftOrder>? _handOrders;
    private ItemStack? _hand;
    private bool _unknown;

    private SetupFileModel Model => scene.Model;

    public void Run()
    {
      SetupPack? real = scene._pack;
      scene._pack =
        Model.Pack == null
          ? null
          : SetupPack.Start(Model, stages ?? (_ => null));
      try
      {
        Layout();
        Steps(Model.Steps);
      }
      finally
      {
        scene._pack = real;
      }
      if (_problems.Count == 1)
        throw new InvalidOperationException(
          $"{scene._file.Name}: {_problems[0]}"
        );
      if (_problems.Count > 1)
        throw new InvalidOperationException(
          $"{scene._file.Name}: {_problems.Count} problems before any step ran:\n"
            + string.Join("\n", _problems.Select(p => "  " + p))
        );
    }

    private bool Note(Action check)
    {
      try
      {
        check();
        return true;
      }
      catch (InvalidOperationException e)
      {
        string prefix = $"{scene._file.Name}: ";
        string text = e.Message.StartsWith(prefix, StringComparison.Ordinal)
          ? e.Message[prefix.Length..]
          : e.Message;
        if (!(_unknown && LedgerShort.IsMatch(text)))
          _problems.Add(text);
        return false;
      }
    }

    private BlockPos? Resolved(SetupStep step, string key)
    {
      try
      {
        return SetupPosition
          .Parse(
            step.Body[key],
            new SetupJson.Where(scene._file.Name, step.Path).In(key)
          )
          .Resolve(_view ??= new SetupWorldView(scene.World, _placed));
      }
      catch (SetupFileException)
      {
        return null;
      }
    }

    private void Stand(string? id, BlockPos at, Block block)
    {
      _blocks[(at.X, at.Y, at.Z)] = block;
      if (id == null)
        return;
      _ids[id] = at;
      _placed[id] = new SetupPlacement(at, [at]);
    }

    private void Layout()
    {
      if (Model.Drawn is not { } layout)
        return;
      foreach (LayoutEntry entry in layout.Entries)
      {
        switch (entry.Kind)
        {
          case LayoutEntryKind.Block:
            string where = $"legend \"{entry.Glyph}\"";
            Block? block = null;
            Note(() => block = scene.Resolve(entry.Code!, where));
            if (block == null)
              break;
            Block laid = block;
            foreach (
              var cell in layout.Cells.Where(c => c.Symbol == entry.Glyph)
            )
            {
              var pos = new BlockPos(cell.X, cell.Y, cell.Z);
              Note(() => scene.DrawBlock(where, laid.Code.ToString(), pos));
              Stand(entry.Id, pos, laid);
              if ((string?)entry.Body["complete"] == "all")
                CompleteAt(where, laid, pos);
            }
            break;
          case LayoutEntryKind.Run:
            if (
              layout.Runs.FirstOrDefault(r => r.Entries[0] == entry) is { } run
              && run.Id != entry.Id
            )
              AfterEntry(run.Id);
            break;
        }
        if (entry.Id != null)
          AfterEntry(entry.Id);
      }
    }

    private void AfterEntry(string id)
    {
      foreach (var (target, steps) in Model.AfterEntry)
        if (target == id)
          Steps(steps);
    }

    private void Steps(IEnumerable<SetupStep> steps)
    {
      foreach (SetupStep step in steps)
      {
        foreach (var (code, count) in step.Draws)
          Note(() => scene.Draw(step.Path, code, count));
        switch (step.Kind)
        {
          case "place":
            Place(step);
            break;
          case "fill":
            Fill(step);
            break;
          case "complete":
            if (
              _ids.TryGetValue((string)step.Body["of"]!, out BlockPos? at)
              && _blocks.TryGetValue((at.X, at.Y, at.Z), out Block? done)
            )
              CompleteAt(step.Path, done, at);
            break;
          case "insert":
            Note(() =>
              scene.Draw(step.Path, scene.StackOf(step.Body, step.Path))
            );
            break;
          case "interact":
          case "use":
            Hold(step);
            break;
          case "window":
            Window(step);
            break;
          case "craft":
            Craft(step);
            break;
          case "wait":
          case "advance":
          case "call":
          case "run":
          case "ignite":
          case "repeat":
            _unknown = true;
            break;
        }
        foreach (var (code, count) in step.Gives)
          scene._pack?.Give(code, count);
      }
    }

    private void Place(SetupStep step)
    {
      Block? block = null;
      Note(() => block = scene.Resolve((string)step.Body["code"]!, step.Path));
      if (block == null)
        return;
      Block placed = block;
      BlockPos? at = Resolved(step, "at");
      Note(() =>
        scene.DrawBlock(
          step.Path,
          placed.Code.ToString(),
          at ?? new BlockPos(0, 0, 0)
        )
      );
      if (at == null)
        return;
      Stand(step.Id, at, placed);
      if ((string?)step.Body["complete"] == "all")
        CompleteAt(step.Path, placed, at);
    }

    private void Fill(SetupStep step)
    {
      Block? block = null;
      Note(() => block = scene.Resolve((string)step.Body["code"]!, step.Path));
      if (
        block != null
        && Resolved(step, "from") is { } from
        && Resolved(step, "to") is { } to
      )
        Stand(
          step.Id,
          new BlockPos(
            Math.Min(from.X, to.X),
            Math.Min(from.Y, to.Y),
            Math.Min(from.Z, to.Z)
          ),
          block
        );
    }

    private void CompleteAt(string where, Block block, BlockPos at)
    {
      if (SetupPlacers.For(block.Code.ToString()) != null)
        return;
      BlockEntity? be = scene.World.NewBlockEntityFor(block);
      if (be == null)
        return;
      if (SetupStructureCheck.IsMultiblock(block, be))
        _unknown = true;
      else if (be.GetBehavior<ExRightClickConstructable>() == null)
        Note(() =>
          throw scene.Fail(
            where,
            $"{block.Code} at {Describe(at)} is neither a multiblock nor a construction, so it has nothing to complete."
          )
        );
    }

    private void Hold(SetupStep step)
    {
      JToken body = step.Body;
      bool keep = (bool?)body["keep"] ?? false;
      float seconds = (float?)body["seconds"] ?? 0f;
      if (seconds > 0f || body["control"] != null)
        _unknown = true;
      ItemStack? with = null;
      ItemStack? offHand = null;
      Note(() =>
      {
        ItemStack? laid = body["with"] is { } held
          ? scene.StackOf(held, step.Path)
          : null;
        if (laid != null)
          scene.Draw(step.Path, laid);
        with = laid;
        offHand = body["offHand"] is { } off
          ? scene.StackOf(off, step.Path)
          : null;
        if (offHand != null)
          scene.Draw(step.Path, offHand);
      });
      if (with != null)
        _hand = with;
      if (!keep)
      {
        scene.Give(_hand);
        _hand = null;
      }
      scene.Give(offHand);
    }

    private void Window(SetupStep step)
    {
      if (step.Body["put"] is JObject put)
      {
        Note(() =>
        {
          int slots = scene
            .SlotsOf(
              put["slot"] ?? throw scene.Fail(step.Path, "put needs a slot."),
              step.Path
            )
            .Count;
          for (int i = 0; i < slots; i++)
            scene.Draw(step.Path, scene.StackOf(put, step.Path));
        });
      }
      if (step.Body["take"] != null || step.Body["press"] != null)
        _unknown = true;
    }

    private void Craft(SetupStep step)
    {
      JToken body = step.Body;
      var wanted = new AssetLocation((string)body["output"]!);
      string? recipe = (string?)body["recipe"];
      int times = (int?)body["times"] ?? 1;
      var with = body["with"] as JObject;
      string station = "the hand grid";
      IReadOnlyList<CraftOrder>? orders;
      bool atStation = body["at"] != null;
      if (atStation)
      {
        BlockPos? at = Resolved(step, "at");
        if (
          at == null
          || !_blocks.TryGetValue((at.X, at.Y, at.Z), out Block? block)
          || SetupPlacers.For(block.Code.ToString()) != null
        )
        {
          _unknown = true;
          return;
        }
        station = $"{block.Code} at {Describe(at)}";
        if (!OrdersOf(block, out orders))
        {
          _unknown = true;
          return;
        }
        if (orders == null)
        {
          Note(() =>
            throw scene.Fail(step.Path, $"{station} is not a craft station.")
          );
          _unknown = true;
          return;
        }
      }
      else
        orders = _handOrders ??= HandGrid.Orders(scene.World.World);

      CraftOrder[] makes =
      [
        .. orders.Where(o => CraftBills.Makes(o, wanted, recipe)),
      ];
      if (makes.Length == 0)
      {
        Note(() =>
          throw scene.Fail(
            step.Path,
            $"{station} has no order that makes {wanted}"
              + (recipe == null ? "" : $" by {recipe}")
              + $"; made at: {scene.MadeAt(wanted, recipe, Stations())}."
          )
        );
        _unknown = true;
        return;
      }
      CraftOrder order = makes[0];
      CraftSequence? sequence = CraftSequenceRegistry.Shared.Sequence(
        order.Recipe
      );
      if (Note(() => scene.RefuseStrayWith(step.Path, order, with)))
        Note(() => scene.DrawBill(step.Path, order, with, times));
      if (atStation && sequence != null)
      {
        Tools(step.Path, sequence);
        _unknown = true;
      }
      foreach (var (code, count) in CraftBills.Yield(order, sequence, times))
        scene._pack?.Give(code, count);
    }

    private void Tools(string where, CraftSequence sequence)
    {
      for (int n = 0; n < sequence.Steps.Length; n++)
      {
        CraftStep step = sequence.Steps[n];
        string label = $"step {n + 1} of {sequence.Recipe}";
        ItemStack? tool = null;
        ItemStack? offhand = null;
        Note(() =>
        {
          tool = scene.Gesture(where, label, step.Tool);
          offhand = scene.Gesture(where, label, step.Offhand);
        });
        scene.Give(tool);
        scene.Give(offhand);
      }
    }

    private IEnumerable<(
      string Id,
      string Code,
      IReadOnlyList<CraftOrder>? Orders
    )> Stations() =>
      _ids.Select(p =>
      {
        Block block = _blocks[(p.Value.X, p.Value.Y, p.Value.Z)];
        OrdersOf(block, out IReadOnlyList<CraftOrder>? orders);
        return (p.Key, block.Code.ToString(), orders);
      });

    // False when the world cannot say what the block crafts; null orders when its entity is no
    // station. An entity that is not placed may not answer, which leaves the step to the run.
    private bool OrdersOf(Block block, out IReadOnlyList<CraftOrder>? orders)
    {
      string code = block.Code.ToString();
      if (_orders.TryGetValue(code, out orders))
        return true;
      try
      {
        BlockEntity? be = scene.World.NewBlockEntityFor(block);
        if (be == null)
          return false;
        orders = be is ICraftStation craft
          ? craft.Orders(scene.World.World)
          : null;
      }
      catch (Exception)
      {
        orders = null;
        return false;
      }
      _orders[code] = orders;
      return true;
    }
  }
}
#endif
