#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Catalogues;
using ExpandedLib.Config;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.Metals;
using ExpandedLib.Machines;
using ExpandedLib.Scenarios;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Testing;

/// <summary>
/// A setup file built into a fresh world: mods and vanilla codes loaded, layout placed, every step
/// but run executed and the mechanical lines joined.
/// </summary>
public sealed partial class SetupScene : IDisposable
{
  private static readonly Dictionary<string, string> Lacking = new()
  {
    ["paste"] = "the fake world has no structure files to paste",
    ["power"] = "the fake world has no creative power source",
    ["source"] = "the fake world has no creative medium source",
    ["break"] = "the fake world has no player to break with",
    ["reload"] = "the fake world has no chunk save and load to reload through",
    ["timespeed"] =
      "the fake world has no calendar speed to run a span of server clock through",
    ["climate"] = "the fake world has no weather to hold a climate with",
  };

  /// <summary>The first verb of <paramref name="file"/> that the fake world lacks, which only the live
  /// runner runs.</summary>
  /// <returns>The verb (<c>climate</c> for a file that holds a climate), or null when the harness runs
  /// every step.</returns>
  internal static string? LackedVerb(SetupFile file) =>
    file.Model.HasClimate
      ? "climate"
      : SetupStepWalk
        .Of(file.Model)
        .Select(step => step.Kind)
        .FirstOrDefault(Lacking.ContainsKey);

  private readonly SetupFile _file;
  private readonly Dictionary<string, SetupPlacement> _placed = [];
  private readonly Dictionary<BlockPos, HashSet<long>> _listeners = [];
  private readonly HashSet<int> _ready = [];
  private readonly List<string> _standIns = [];
  private HashSet<long> _lineListeners = [];
  private readonly SetupConfig _config;
  private readonly bool _borrowed;
  private readonly HashSet<(int X, int Y, int Z)> _laid = [];
  private bool _joined;

  private SetupScene(SetupFile file, Scene? scene = null)
  {
    _file = file;
    _config = new SetupConfig(file.Name);
    _borrowed = scene != null;
    Scene = scene ?? new();
    View = new SetupWorldView(World, _placed);
    NetworkTick = () => World.Tick(1);
  }

  /// <summary>The scene the file was built into.</summary>
  public Scene Scene { get; }

  /// <summary>The scene's world.</summary>
  public TestWorld World => Scene.World;

  /// <summary>The file the scene was built from.</summary>
  public SetupFile File => _file;

  /// <summary>One <c>stand-in &lt;code&gt;: &lt;text&gt;</c> line for each code a placer placed in
  /// place of the game, in the order they were first placed.</summary>
  public IReadOnlyList<string> StandIns => _standIns;

  internal ISetupWorld View { get; }

  /// <summary>Releases the scene's world.</summary>
  public void Dispose()
  {
    _config.Restore();
    if (!_borrowed)
      World.Dispose();
  }

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

  internal IReadOnlyDictionary<string, SetupPlacement> Placements => _placed;

  /// <summary>Builds <paramref name="file"/> into a fresh world.</summary>
  /// <param name="file">The validated file.</param>
  /// <param name="modPaths">The folders of the file's <see cref="SetupFile.Mods"/>, in the same
  /// order, as <see cref="TestWorld.LoadAssets(IReadOnlyList{string}, string)"/> takes them.</param>
  /// <returns>The scene, its layout and steps placed.</returns>
  /// <exception cref="ArgumentException"><paramref name="modPaths"/> does not hold one folder for
  /// each of the file's mods.</exception>
  /// <exception cref="InvalidOperationException">The file uses a step kind the fake world cannot
  /// run or a key the reader refuses (<c>tree</c>, <c>materials</c>, a <c>complete</c> to a
  /// stage); or a step fails: a code the load did not register, a cell
  /// that is not air, a layout or run that does not fit, a step at a cell without the block entity,
  /// member, key, slot or method it names, a hold, <c>returns</c>, <c>wait</c> or <c>repeat</c> not
  /// met, or an <c>expect</c> that does not hold. The message names the file and where.</exception>
  public static SetupScene Build(
    SetupFile file,
    IReadOnlyList<string> modPaths
  ) => Build(file, modPaths, null);

  internal static SetupScene Build(
    SetupFile file,
    IReadOnlyList<string> modPaths,
    Action<TestWorld>? afterLoad,
    Action<TestWorld>? beforeLoad = null,
    System.Func<string, SetupFileModel?>? stages = null
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
    scene.World.SpawnInitializesBeforeRegistering = true;
    try
    {
      scene.Refuse();
      beforeLoad?.Invoke(scene.World);
      scene.Load(modPaths);
      afterLoad?.Invoke(scene.World);
      scene._config.Apply(scene.Model.Config);
      scene.StartPack(stages);
      scene.PlaceLayout();
      scene.RunSteps();
      scene.JoinLines();
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

  /// <summary>A scene over a world that is already loaded, for code that places and crafts in it
  /// as a setup does. Disposing the scene leaves the world alone.</summary>
  /// <param name="world">The loaded world.</param>
  /// <param name="name">What failure messages call the scene.</param>
  internal static SetupScene Over(TestWorld world, string name) =>
    new(
      SetupFile.Parse(
        "{\"setup\": \""
          + name
          + "\", \"description\": \""
          + name
          + "\", \"mods\": [], \"steps\": [], \"seconds\": 10, \"record\": {}}",
        name
      ),
      new Scene(world)
    );

  /// <summary>Places <paramref name="block"/> at <paramref name="at"/> as a <c>place</c> step does
  /// and records it under <paramref name="id"/>.</summary>
  /// <exception cref="InvalidOperationException">The cell is not air or a placer refuses the
  /// block.</exception>
  internal void PlaceAt(string id, Block block, BlockPos at)
  {
    Put(block, at, id);
    Record(id, at, block);
  }

  private SetupFileModel Model => _file.Model;

  private InvalidOperationException Fail(string where, string message) =>
    new($"{_file.Name}: {where}: {message}");

  private static string Describe(BlockPos pos) =>
    $"({pos.X}, {pos.Y}, {pos.Z})";

  #region Refusals

  private void Refuse()
  {
    if (Model.HasClimate)
      throw Fail("climate", $"\"climate\" is refused: {Lacking["climate"]}.");
    foreach (SetupStep step in SetupStepWalk.Of(Model))
    {
      JToken body = step.Body;
      if (Lacking.TryGetValue(step.Kind, out string? lacks))
        throw Fail(step.Path, $"\"{step.Kind}\" is refused: {lacks}.");
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
        case "expect":
          if (body["drops"] != null)
            throw Fail(
              step.Path,
              "\"expect drops\" is refused: the fake world has no item entities, a spawned drop is a stack in TestWorld.Drops."
            );
          if (body["player"] != null)
            throw Fail(
              step.Path,
              "\"expect player\" is refused: the fake world's player has no health."
            );
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
          : $"\"{key}\" is not built; the harness reader refuses it."
      );
    }
  }

  #endregion

  #region Loading

  private void Load(IReadOnlyList<string> modPaths)
  {
    bool crafts =
      Model.Recipes
      || EveryStep()
        .Any(s =>
          s.Kind == "craft"
          || (
            s.Kind == "interact"
            && (s.Body["offHand"] != null || s.Body["control"] != null)
          )
        );
    World.LoadAssets(modPaths, null, VanillaCodes(), crafts);
    MetalCatalogueLoader.Load(World.Api);
    if (World.Mods.GetModSystem<SurvivalCoreSystem>() == null)
    {
      var core = new SurvivalCoreSystem();
      World.Mods.Register(core);
      core.AssetsLoaded(World.Api);
    }
    ResolveSmeltedStacks();
    if (World.Mods.GetModSystem<WeatherSystemBase>() == null)
      World.StartModSystem<SetupDryWeather>();
    if (crafts)
    {
      World.LoadRecipes();
      CraftSequenceLoader.Load(World.Api);
      CraftSequenceGate.Register(World.Api.Event);
    }
    if (World.Mods.GetModSystem<MechanicalPowerMod>() == null)
    {
      HashSet<long> before = World.TickListenerIds();
      var power = new MechanicalPowerMod();
      World.Mods.Register(power);
      power.Start(World.Api);
      _lineListeners = [.. World.TickListenerIds().Except(before)];
    }
    BlockLaws.StartRooms(World);
    foreach (Block block in World.World.Blocks.ToArray())
      if (block is { Id: not 0 } && _file.Mods.Contains(block.Code.Domain))
        Ready(block);
  }

  private void ResolveSmeltedStacks()
  {
    foreach (
      CollectibleObject collectible in World
        .World.Items.Where(i => i?.Code != null)
        .Cast<CollectibleObject>()
        .Concat(World.World.Blocks.Where(b => b is { Id: not 0 }))
    )
      if (
        collectible.CombustibleProps?.SmeltedStack is
        { ResolvedItemstack: null } smelted
      )
        smelted.Resolve(World.World, $"smelted stack of {collectible.Code}");
  }

  private IEnumerable<SetupStep> EveryStep() =>
    Model
      .AllSteps.Concat(Model.During.SelectMany(d => d.Steps))
      .SelectMany(s => s.All());

  private string[] VanillaCodes()
  {
    IEnumerable<JToken> roots =
    [
      .. Model.Layout == null ? [] : (JToken[])[Model.Layout],
      .. EveryStep().Select(s => s.Body),
    ];
    return
    [
      .. roots
        .SelectMany(r => r is JContainer c ? c.DescendantsAndSelf() : [r])
        .OfType<JValue>()
        .Select(v => v.Value as string)
        .OfType<string>()
        .Where(v => v.StartsWith("game:"))
        .Concat(Model.Load)
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

  private void Put(
    Block block,
    BlockPos pos,
    string where,
    string? kind = null,
    bool draws = true
  )
  {
    string code = block.Code.ToString();
    if (draws)
      DrawBlock(where, kind ?? code, pos, Describe(pos), pos);
    BlockPos at = pos.Copy();
    _laid.Add((at.X, at.Y, at.Z));
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
          FillerCell cell in SetupStructureCheck.Footprint(
            World.World,
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
        SetupStructureCheck.Footprint(World.World, host, pos, angle),
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

  /// <summary>Brings the <see cref="ExRightClickConstructable"/> of the entity at
  /// <paramref name="pos"/> to its last stage, drawing the stages' requirements from the pack, and
  /// plants a completed construction in the entity as <see cref="RccFake.Complete"/> does when the
  /// entity caches its construction in an <c>_animator</c> or <c>_rcc</c> field.</summary>
  /// <param name="be">The block entity at <paramref name="pos"/>.</param>
  /// <param name="pos">The entity's cell.</param>
  /// <param name="where">The step path a pack shortfall is reported under.</param>
  /// <returns>Whether the entity carries a construction; false leaves it untouched.</returns>
  /// <exception cref="InvalidOperationException">The pack lacks a stage's requirement.</exception>
  internal bool CompleteConstruction(BlockEntity be, BlockPos pos, string where)
  {
    if (be.GetBehavior<ExRightClickConstructable>() is not { } construction)
      return false;
    DrawStages(where, construction.Construction, Describe(pos), pos);
    if (
      ReflectionHelpers.TryGetField(be, "_animator", out _)
      || ReflectionHelpers.TryGetField(be, "_rcc", out _)
    )
      RccFake.Complete(be);
    if (
      ReflectionHelpers.GetField(construction, "rcc")
      is ExRightClickConstruction built
    )
      built.CurrentCompletedStage = Math.Max(0, built.Stages.Length - 1);
    return true;
  }

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
      bool carried = _pack?.TookStanding(pos) ?? false;
      foreach (var (at, raised) in rig.Raised)
      {
        _laid.Add((at.X, at.Y, at.Z));
        if (!carried)
          DrawBlock(where, raised, at, Describe(pos));
      }
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
    if (CompleteConstruction(be, pos, where))
      completed = true;
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

  private void RunSteps() => RunSteps(Model.Steps);

  private void JoinLines()
  {
    if (_joined)
      return;
    SetupMechanical.Join(World);
    _joined = true;
  }

  private void RunSteps(IEnumerable<SetupStep> steps)
  {
    foreach (SetupStep step in steps)
    {
      if (step.Kind is "place" or "complete" or "fill" or "run")
        _joined = false;
      else
        JoinLines();
      foreach (var (code, count) in step.Draws)
        Draw(step.Path, code, count);
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
        case "run":
          RunStep(step);
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
        case "interact":
          InteractStep(step);
          break;
        case "use":
          UseStep(step);
          break;
        case "window":
          WindowStep(step);
          break;
        case "ignite":
          IgniteStep(step);
          break;
        case "craft":
          CraftStep(step);
          break;
        case "expect":
          ExpectStep(step);
          break;
        case "wait":
          WaitStep(step);
          break;
        case "advance":
          AdvanceStep(step);
          break;
        case "fillInventory":
          FillInventoryStep(step);
          break;
        case "select":
          SelectStep(step);
          break;
        case "stand":
          StandStep(step);
          break;
        case "repeat":
          RepeatStep(step);
          break;
      }
      foreach (var (code, count) in step.Gives)
        _pack?.Give(code, count);
      GiveSpawns();
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
    var put = new List<BlockPos>();
    for (int x = Math.Min(from.X, to.X); x <= Math.Max(from.X, to.X); x++)
    for (int y = Math.Min(from.Y, to.Y); y <= Math.Max(from.Y, to.Y); y++)
    for (int z = Math.Min(from.Z, to.Z); z <= Math.Max(from.Z, to.Z); z++)
    {
      var cell = new BlockPos(x, y, z);
      cells.Add(cell);
      if (World.GetBlock(cell).Code?.Equals(block.Code) != true)
      {
        Put(block, cell, step.Path, draws: false);
        put.Add(cell);
      }
    }
    DrawMetal(step.Path, put);
    if (step.Id != null)
      _placed[step.Id] = new SetupPlacement(cells[0], cells);
  }

  #endregion
}
#endif
