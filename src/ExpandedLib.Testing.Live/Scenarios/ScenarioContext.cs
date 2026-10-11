using System;
using System.Collections.Generic;
using ExpandedLib.Scenarios;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Testing.Live;

/// <summary>What the steps of one case act on: the world, the plot's origin, the server clock,
/// the log watch and the lines.</summary>
internal sealed class ScenarioContext(
  IWorldAccessor world,
  ScenarioWorld setup,
  Func<double> clock,
  ScenarioErrors errors,
  Action<string> output,
  string file
) {
  public IWorldAccessor World => world;

  public ScenarioWorld Setup => setup;

  /// <summary>The server clock in seconds.</summary>
  public double Now => clock();

  public ScenarioErrors Errors => errors;

  public string File => file;

  /// <summary>Plot-local cells a step placed; <c>complete</c> never replaces one.</summary>
  public HashSet<BlockPos> Placed { get; } = [];

  /// <summary>Plot-local cells a step touched, for a failure's detail.</summary>
  public HashSet<BlockPos> Touched { get; } = [];

  /// <summary>Plot-local cells an <c>expect</c> read, for a failure's detail.</summary>
  public HashSet<BlockPos> Expected { get; } = [];

  /// <summary>Every position the running step resolved, by the key that named it.</summary>
  public List<(string Key, BlockPos Local)> Resolved { get; } = [];

  public void Line(string line) => output(line);

  public SetupJson.Where Where(SetupStep step) =>
    new(file, $"{step.Path} ({step.Kind})");

  public bool IsAir(BlockPos local) => setup.BlockAt(local).Id == 0;

  /// <summary>The plot-local cell the step's <paramref name="key"/> names.</summary>
  /// <exception cref="StepFailure">The position names an id that placed nothing or a form the
  /// block does not have.</exception>
  public BlockPos Position(SetupStep step, JToken? token, string key) {
    try {
      BlockPos local = SetupPosition.Parse(token, Where(step).In(key)).Resolve(setup);
      Resolved.Add((key, local));
      return local;
    } catch (SetupFileException e) {
      throw new StepFailure(e.Message, Suspect.Scenario, e);
    }
  }

  /// <summary>Sets <paramref name="block"/> into the plot-local cell as the block accessor
  /// does a placed block, so its entity is created and a megablock fills its footprint.</summary>
  public void SetBlock(Block block, BlockPos local) {
    BlockPos at = setup.Absolute(local);
    world.BlockAccessor.SetBlock(block.BlockId, at, new ItemStack(block));
    if (block is BlockMPBase power)
      power.WasPlaced(world, at, null);
    Placed.Add(local.Copy());
    Touched.Add(local.Copy());
  }

  /// <exception cref="StepFailure">The code names no block.</exception>
  public Block BlockOf(string code) {
    var location = new AssetLocation(code);
    Block? block = world.GetBlock(location);
    if (block == null || (block.Id == 0 && location.Path != "air"))
      throw new StepFailure($"\"{code}\" is no block code.", Suspect.Scenario);
    return block;
  }

  /// <summary>Fails the running step when the log holds an error the case does not allow.</summary>
  /// <exception cref="StepFailure">A new failing entry reached the log.</exception>
  public void ThrowIfLogged() {
    if (errors.TakeNew() is { } entry)
      throw new StepFailure($"server log error: {entry}");
  }
}
