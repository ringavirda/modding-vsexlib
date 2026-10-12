#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Scenarios;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Testing;

/// <summary>
/// A setup file built into a fresh world: its mods and the vanilla blocktypes its <c>game:</c> codes
/// name loaded, its layout and its <c>place</c>, <c>complete</c>, <c>fill</c>, <c>insert</c>,
/// <c>set</c> and <c>call</c> steps run, and the mechanical lines joined. Running, sampling and
/// checking are not built yet.
/// </summary>
public sealed partial class SetupScene : IDisposable
{
  private static readonly string[] Unbuilt =
  [
    "run",
    "interact",
    "window",
    "ignite",
    "expect",
    "wait",
    "repeat",
  ];

  private static readonly Dictionary<string, string> Lacking = new()
  {
    ["paste"] = "the fake world has no structure files to paste",
    ["power"] = "the fake world has no creative power source",
    ["source"] = "the fake world has no creative medium source",
    ["break"] = "the fake world has no player to break with",
    ["reload"] = "the fake world has no chunk save and load to reload through",
    ["advance"] = "the fake world has no game calendar to advance",
  };

  private readonly SetupFile _file;
  private readonly Dictionary<string, SetupPlacement> _placed = [];
  private readonly Dictionary<BlockPos, HashSet<long>> _listeners = [];
  private readonly HashSet<int> _ready = [];
  private readonly List<string> _standIns = [];

  private SetupScene(SetupFile file)
  {
    _file = file;
    View = new SetupWorldView(World, _placed);
  }

  /// <summary>The scene the file was built into.</summary>
  public Scene Scene { get; } = new();

  /// <summary>The scene's world.</summary>
  public TestWorld World => Scene.World;

  /// <summary>The file the scene was built from.</summary>
  public SetupFile File => _file;

  /// <summary>One <c>stand-in &lt;code&gt;: &lt;text&gt;</c> line for each code a placer placed in
  /// place of the game, in the order they were first placed.</summary>
  public IReadOnlyList<string> StandIns => _standIns;

  internal ISetupWorld View { get; }

  /// <summary>Releases the scene's world.</summary>
  public void Dispose() => World.Dispose();

  /// <summary>The cell the layout entry or step with <paramref name="id"/> placed; a run's first
  /// cell.</summary>
  /// <param name="id">An entry <c>id</c>, a run id or a step <c>id</c>.</param>
  /// <exception cref="InvalidOperationException">The id has placed nothing.</exception>
  public BlockPos PositionOf(string id) =>
    _placed.TryGetValue(id, out SetupPlacement? placed)
      ? placed.Cell.Copy()
      : throw new InvalidOperationException(
        $"{_file.Name}: \"{id}\" has placed nothing."
      );

  /// <summary>Every cell the entry or step with <paramref name="id"/> placed; a run's cells in path
  /// order.</summary>
  /// <param name="id">An entry <c>id</c>, a run id or a step <c>id</c>.</param>
  /// <exception cref="InvalidOperationException">The id has placed nothing.</exception>
  public IReadOnlyList<BlockPos> CellsOf(string id) =>
    _placed.TryGetValue(id, out SetupPlacement? placed)
      ? [.. placed.Cells.Select(c => c.Copy())]
      : throw new InvalidOperationException(
        $"{_file.Name}: \"{id}\" has placed nothing."
      );

  /// <summary>Builds <paramref name="file"/> into a fresh world.</summary>
  /// <param name="file">The validated file.</param>
  /// <param name="modPaths">The folders of the file's <see cref="SetupFile.Mods"/>, in the same
  /// order, as <see cref="TestWorld.LoadAssets(IReadOnlyList{string}, string)"/> takes them.</param>
  /// <returns>The scene, its layout and steps placed.</returns>
  /// <exception cref="ArgumentException"><paramref name="modPaths"/> does not hold one folder for
  /// each of the file's mods.</exception>
  /// <exception cref="InvalidOperationException">The file uses a step kind the fake world cannot
  /// run, or one not built yet; a code the load did not register; a cell that is not air; a run cell
  /// no block of its family fits, or two fit; a drawn structure that disagrees with its cells; a
  /// <c>complete</c> that does not complete; a step at a cell with no block entity; an <c>insert</c>
  /// into an entity with no inventory, to a slot past the end, or of a stack not taken whole; a
  /// <c>set</c> of a member or tree key the entity lacks, of a tree key whose attribute type it
  /// cannot write, or of an enum name the type lacks; a <c>call</c> of a behaviour the entity lacks
  /// or a method of no public name and argument count, of a method that threw, or whose result is
  /// not the step's <c>returns</c>; the message names the file and where.</exception>
  public static SetupScene Build(
    SetupFile file,
    IReadOnlyList<string> modPaths
  ) => Build(file, modPaths, null);

  internal static SetupScene Build(
    SetupFile file,
    IReadOnlyList<string> modPaths,
    Action<TestWorld>? afterLoad,
    Action<TestWorld>? beforeLoad = null
  )
  {
    ArgumentNullException.ThrowIfNull(file);
    ArgumentNullException.ThrowIfNull(modPaths);
    if (modPaths.Count != file.Mods.Count)
      throw new ArgumentException(
        $"{file.Name} lists {file.Mods.Count} mods, {modPaths.Count} folders given.",
        nameof(modPaths)
      );
    var scene = new SetupScene(file);
    try
    {
      scene.Refuse();
      beforeLoad?.Invoke(scene.World);
      scene.Load(modPaths);
      afterLoad?.Invoke(scene.World);
      scene.PlaceLayout();
      scene.RunSteps();
      SetupMechanical.Join(scene.World);
      scene.Scene.Build();
    }
    catch (SetupFileException e)
    {
      scene.Dispose();
      throw new InvalidOperationException(e.Message, e);
    }
    catch
    {
      scene.Dispose();
      throw;
    }
    return scene;
  }

  private SetupFileModel Model => _file.Model;

  private InvalidOperationException Fail(string where, string message) =>
    new($"{_file.Name}: {where}: {message}");

  private static string Describe(BlockPos pos) =>
    $"({pos.X}, {pos.Y}, {pos.Z})";

  #region Refusals

  private void Refuse()
  {
    foreach (SetupStep step in Model.Steps.SelectMany(s => s.All()))
    {
      JToken body = step.Body;
      if (Lacking.TryGetValue(step.Kind, out string? lacks))
        throw Fail(step.Path, $"\"{step.Kind}\" is refused: {lacks}.");
      if (Unbuilt.Contains(step.Kind))
        throw Fail(
          step.Path,
          $"\"{step.Kind}\" is not built by the harness reader yet."
        );
      switch (step.Kind)
      {
        case "place":
          if ((string?)body["by"] == "player")
            throw Fail(
              step.Path,
              "\"place\" by the player is refused: the fake world has no player to place with."
            );
          RefuseKeys(step.Path, body, "complete", "materials", "tree");
          break;
        case "complete":
          if (body["to"] != null && (string?)body["to"] != "all")
            throw Fail(
              step.Path,
              "\"complete\" to a stage is refused: the fake world completes every stage or none."
            );
          RefuseKeys(step.Path, body, "materials");
          break;
      }
    }
    if (Model.Drawn is { } layout)
      foreach (LayoutEntry entry in layout.Entries)
        if (entry.Kind == LayoutEntryKind.Block)
          RefuseKeys(
            $"legend \"{entry.Glyph}\"",
            entry.Body,
            "complete",
            "materials",
            "tree"
          );
  }

  private void RefuseKeys(string where, JToken body, params string[] keys)
  {
    foreach (string key in keys)
    {
      JToken? value = body[key];
      if (value == null)
        continue;
      if (key == "complete" && (string?)value == "all")
        continue;
      throw Fail(
        where,
        key == "complete"
          ? "\"complete\" to a stage is refused: the fake world completes every stage or none."
          : $"\"{key}\" is not built by the harness reader yet."
      );
    }
  }

  #endregion

  #region Loading

  private void Load(IReadOnlyList<string> modPaths)
  {
    World.LoadAssets(modPaths, null, VanillaCodes());
    if (World.Mods.GetModSystem<MechanicalPowerMod>() == null)
    {
      var power = new MechanicalPowerMod();
      World.Mods.Register(power);
      power.Start(World.Api);
    }
    foreach (Block block in World.World.Blocks.ToArray())
      if (block is { Id: not 0 } && _file.Mods.Contains(block.Code.Domain))
        Ready(block);
  }

  private string[] VanillaCodes()
  {
    IEnumerable<JToken> roots =
    [
      .. Model.Layout == null ? [] : (JToken[])[Model.Layout],
      .. Model.Steps.SelectMany(s => s.All()).Select(s => s.Body),
    ];
    return
    [
      .. roots
        .SelectMany(r => r is JContainer c ? c.DescendantsAndSelf() : [r])
        .OfType<JValue>()
        .Select(v => v.Value as string)
        .OfType<string>()
        .Where(v => v.StartsWith("game:"))
        .Distinct(),
    ];
  }

  private void Ready(Block block)
  {
    if (_ready.Add(block.BlockId))
      StructureBreaks.Ready(World, block);
  }

  private Block Resolve(string code, string where)
  {
    Block? block = World.World.GetBlock(new AssetLocation(code));
    if (block == null)
      throw Fail(where, $"\"{code}\" is not registered by the load.");
    return block;
  }

  #endregion

  #region Placing

  private void Put(Block block, BlockPos pos, string where)
  {
    string code = block.Code.ToString();
    BlockPos at = pos.Copy();
    HashSet<long> before = World.TickListenerIds();
    if (SetupPlacers.For(code) is { } placer)
    {
      if (
        placer.StandIn(code) is { } text
        && !_standIns.Any(s => s.StartsWith($"stand-in {code}: "))
      )
        _standIns.Add($"stand-in {code}: {text}");
      placer.Place(this, block, at);
    }
    else
    {
      Ready(block);
      Block held = World.GetBlock(at);
      if (held.Id != 0)
        throw Fail(where, $"cell {Describe(at)} holds {held.Code}, not air.");
      if (block is IFillerHost host)
        foreach (
          FillerCell cell in StructureFillers.FootprintCells(
            host,
            at,
            AngleOf(block)
          )
        )
          if (World.GetBlock(cell.Pos).Id != 0)
            throw Fail(
              where,
              $"footprint cell {Describe(cell.Pos)} of {code} holds {World.GetBlock(cell.Pos).Code}, not air."
            );
      World.Accessor.SetBlock(block.BlockId, at);
      if (block is IFillerHost)
        block.OnBlockPlaced(World.World, at, new ItemStack(block));
    }
    _listeners[at] = [.. World.TickListenerIds().Except(before)];
  }

  private static int AngleOf(Block block) =>
    block switch
    {
      BlockFilledMegastructure mega => mega.StructureAngle,
      IFillerHost => ExOrientation.AngleFromSide(
        block.Variant?["side"] ?? block.Variant?["orientation"]
      ),
      _ => 0,
    };

  private SetupFrame? FrameOf(Block block, BlockPos pos)
  {
    BlockEntity? be = World.GetBlockEntity(pos);
    if (block is IFillerHost host)
    {
      int angle = AngleOf(block);
      return new SetupFrame(
        pos.Copy(),
        angle,
        StructureFillers.FootprintCells(host, pos, angle),
        SetupStructureCheck.IsMultiblock(block, be)
          ? o => ExOrientation.GlobalPos(pos, o.X, o.Y, o.Z, angle)
          : null
      );
    }
    if (be is BlockEntityMultiblockStructure mb)
      return new SetupFrame(
        pos.Copy(),
        mb.LayoutAngle,
        [],
        o => ExOrientation.GlobalPos(pos, o.X, o.Y, o.Z, mb.LayoutAngle)
      );
    return null;
  }

  private void Record(string id, BlockPos pos, Block block) =>
    _placed[id] = new SetupPlacement(
      pos.Copy(),
      [pos.Copy()],
      FrameOf(block, pos)
    );

  private void Complete(BlockPos pos, string where)
  {
    Block block = World.GetBlock(pos);
    BlockEntity be =
      World.GetBlockEntity(pos)
      ?? throw Fail(
        where,
        $"cell {Describe(pos)} has no block entity to complete."
      );
    bool completed = false;
    if (SetupStructureCheck.IsMultiblock(block, be))
    {
      var multiblock = (BlockEntityMultiblockStructure)be;
      StructureRig rig = StructureRig
        .Around(World, multiblock, multiblock.LayoutAngle)
        .Raise();
      HashSet<long> own = _listeners.GetValueOrDefault(pos) ?? [];
      for (int i = 0; i < 2 && !multiblock.StructureComplete; i++)
        World.AdvanceBlockEntityTime(3000, own);
      if (!multiblock.StructureComplete)
        throw Fail(
          where,
          $"{block.Code} at {Describe(pos)} did not complete: {rig.Missing} cells unsatisfied.{rig.MissingReport}"
        );
      completed = true;
    }
    if (be.GetBehavior<ExRightClickConstructable>() != null)
    {
      RccFake.Complete(be);
      completed = true;
    }
    if (!completed)
      throw Fail(
        where,
        $"{block.Code} at {Describe(pos)} is neither a multiblock nor a construction, so it has nothing to complete."
      );
  }

  #endregion

  #region Steps

  private BlockPos At(SetupStep step, string key) =>
    SetupPosition
      .Parse(step.Body[key], new SetupJson.Where(_file.Name, step.Path).In(key))
      .Resolve(View);

  private void RunSteps()
  {
    foreach (SetupStep step in Model.Steps)
      switch (step.Kind)
      {
        case "place":
          PlaceStep(step);
          break;
        case "complete":
          Complete(PositionOf((string)step.Body["of"]!), step.Path);
          break;
        case "fill":
          FillStep(step);
          break;
        case "insert":
          InsertStep(step);
          break;
        case "set":
          SetStep(step);
          break;
        case "call":
          CallStep(step);
          break;
      }
  }

  private void PlaceStep(SetupStep step)
  {
    Block block = Resolve((string)step.Body["code"]!, step.Path);
    BlockPos at = At(step, "at");
    Put(block, at, step.Path);
    if (step.Id != null)
      Record(step.Id, at, block);
    if ((string?)step.Body["complete"] == "all")
      Complete(at, step.Path);
  }

  private void FillStep(SetupStep step)
  {
    Block block = Resolve((string)step.Body["code"]!, step.Path);
    BlockPos from = At(step, "from");
    BlockPos to = At(step, "to");
    var cells = new List<BlockPos>();
    for (int x = Math.Min(from.X, to.X); x <= Math.Max(from.X, to.X); x++)
    for (int y = Math.Min(from.Y, to.Y); y <= Math.Max(from.Y, to.Y); y++)
    for (int z = Math.Min(from.Z, to.Z); z <= Math.Max(from.Z, to.Z); z++)
    {
      var cell = new BlockPos(x, y, z);
      cells.Add(cell);
      if (World.GetBlock(cell).Code?.Equals(block.Code) != true)
        Put(block, cell, step.Path);
    }
    if (step.Id != null)
      _placed[step.Id] = new SetupPlacement(cells[0], cells);
  }

  #endregion
}
#endif
