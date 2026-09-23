#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using ExpandedLib.Blocks;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Testing;

/// <summary>Stands up every block definition that reserves filler cells or carries construction
/// stages and breaks it as a survival player, through the engine's break and removal hooks
/// (<see cref="TestWorld.BreakRunsBlockHooks"/>, <see cref="TestWorld.RunsRemovalHooks"/>).</summary>
/// <remarks>A structure with construction stages is broken from every cell at each stage, partly
/// built or complete; one without is broken from every cell. A break passes
/// when nothing throws, no cell of the structure is left standing, and the drops are the
/// definition's: its resolved <c>drops</c> plus the materials of every paid stage at the configured
/// salvage ratio.</remarks>
public static class StructureBreaks
{
  /// <summary>What <see cref="Run"/> covered and every break that failed, one line each.</summary>
  /// <param name="Blocks">Definitions stood up.</param>
  /// <param name="Variants">Concrete block variants stood up across them.</param>
  /// <param name="Breaks">Breaks performed, one per structure broken from one cell.</param>
  public sealed record Result(
    int Blocks,
    int Variants,
    int Breaks,
    IReadOnlyList<string> Failures
  );

  private static readonly BlockPos At = new(64, 64, 64);

  /// <summary>
  /// Breaks each definition in <paramref name="defs"/> that declares <c>fillerOffsets</c> (plain or
  /// by type) or an <c>ExRightClickConstructable</c> behaviour, in every variant it registers.
  /// </summary>
  /// <param name="assemblies">The assemblies whose registered classes the definitions name, exlib's
  /// own included; exlib's structure filler is taken from them.</param>
  /// <param name="prepare">Runs on each fresh world before the structure is placed, to register the
  /// network types and mod systems its block entities need.</param>
  public static Result Run(
    IEnumerable<ExBlockDef> defs,
    IReadOnlyList<Assembly> assemblies,
    Action<TestWorld>? prepare = null
  )
  {
    ExBlockDef fillerDef = ExDefinitions
      .DefinitionsOf(typeof(BlockStructureFiller), "exlib")
      .Single();
    var failures = new List<string>();
    int blocks = 0,
      variants = 0,
      breaks = 0;

    foreach (ExBlockDef def in defs.Where(InScope))
    {
      blocks++;
      foreach (
        DefinitionCodes.Registered variant in DefinitionCodes.Expand(def)
      )
      {
        variants++;
        TestWorld Stand()
        {
          var world = new TestWorld();
          world.RegisterClasses([.. assemblies]);
          world.RegisterClass("Animatable", typeof(BEBehaviorAnimatable));
          world.RegisterClass(
            "BlockEntityInteract",
            typeof(BlockBehaviorBlockEntityInteract)
          );
          var power = new MechanicalPowerMod();
          world.Mods.Register(power);
          power.Start(world.Api);
          prepare?.Invoke(world);
          world.DefineBlock(
            fillerDef,
            DefinitionCodes.Expand(fillerDef).Single()
          );
          return world;
        }

        int stages = StageCount(Stand(), def, variant, failures);
        if (stages < 0)
          continue;
        for (int built = 0; built < Math.Max(stages, 1); built++)
        {
          for (int cell = -1; ; cell++)
          {
            string? failure = BreakOnce(
              Stand(),
              def,
              variant,
              stages == 0 ? null : built,
              cell,
              out bool noMoreCells
            );
            if (failure == null && noMoreCells)
              break;
            breaks++;
            if (failure != null)
              failures.Add(failure);
            if (noMoreCells)
              break;
          }
        }
      }
    }
    return new Result(blocks, variants, breaks, failures);
  }

  /// <summary>Whether <paramref name="def"/> reserves filler cells or carries construction
  /// stages.</summary>
  public static bool InScope(ExBlockDef def)
  {
    JObject json = def.ToJson();
    bool fillers =
      json["attributes"]?["fillerOffsets"] != null
      || (json["attributesByType"] as JObject)
        ?.Properties()
        .Any(p => p.Value["fillerOffsets"] != null) == true;
    return fillers || RccProperties(json) != null;
  }

  private static JToken? RccProperties(JToken json) =>
    (json["entityBehaviors"] as JArray)
      ?.FirstOrDefault(b => (string?)b["name"] == "ExRightClickConstructable")
      ?["properties"];

  /// <summary>The number of construction stages <paramref name="variant"/> declares (0 when it
  /// declares none), or -1 after recording why it could not be stood up.</summary>
  private static int StageCount(
    TestWorld world,
    ExBlockDef def,
    DefinitionCodes.Registered variant,
    List<string> failures
  )
  {
    try
    {
      Block block = world.DefineBlock(def, variant);
      Place(world, block);
      if (Construction(world) is { } rcc)
        return rcc.Stages.Length;
      if (RccProperties(def.ToJson()) == null)
        return 0;
      failures.Add(
        $"{variant.Code} declares construction stages but stands up without them"
      );
      return -1;
    }
    catch (Exception e)
    {
      failures.Add($"{variant.Code} could not be stood up: {Describe(e)}");
      return -1;
    }
  }

  private static void Place(TestWorld world, Block block)
  {
    world.Place(At, block);
    block.OnBlockPlaced(world.World, At, new ItemStack(block));
  }

  private static RightClickConstruction? Construction(TestWorld world) =>
    world.GetBlockEntity(At)?.GetBehavior<ExRightClickConstructable>()
      is { } behavior
      ? (RightClickConstruction?)ReflectionHelpers.GetField(behavior, "rcc")
      : null;

  /// <summary>
  /// Stands <paramref name="variant"/> up in <paramref name="world"/>, builds it to stage
  /// <paramref name="built"/> (null for a structure without stages) and breaks it from
  /// <paramref name="cell"/> (-1 the principal, else the index of a filler cell).
  /// </summary>
  /// <param name="noMoreCells">Set when no later cell can be broken: <paramref name="cell"/> is past
  /// the last filler (the return is then null), or the structure could not be stood up.</param>
  /// <returns>Why the break failed, or null when it passed.</returns>
  private static string? BreakOnce(
    TestWorld world,
    ExBlockDef def,
    DefinitionCodes.Registered variant,
    int? built,
    int cell,
    out bool noMoreCells
  )
  {
    noMoreCells = false;
    string where = cell < 0 ? "the principal" : $"filler cell {cell}";
    string stage = built is { } paid ? $" at stage {paid}" : "";
    Block block;
    BlockPos[] fillers;
    var expected = new Dictionary<string, (float Low, float High)>();
    try
    {
      block = world.DefineBlock(def, variant);
      Place(world, block);
      fillers =
      [
        .. world
          .BlockEntities.Where(e =>
            e.Value is BlockEntityStructureFiller { Principal: { } p }
            && p.Equals(At)
          )
          .Select(e => e.Key.Copy())
          .OrderBy(p => p.X)
          .ThenBy(p => p.Y)
          .ThenBy(p => p.Z),
      ];
      if (cell >= fillers.Length)
      {
        noMoreCells = true;
        return null;
      }
      AddDefinitionDrops(block, expected);
      if (built is { } k && Construction(world) is { } rcc)
        Build(world, block, rcc, k, expected);
    }
    catch (Exception e)
    {
      noMoreCells = true;
      return $"{variant.Code}{stage} could not be stood up: {Describe(e)}";
    }

    BlockPos target = cell < 0 ? At : fillers[cell];
    TestPlayer player = world.Player();
    player.Player.WorldData.CurrentGameMode.Returns(EnumGameMode.Survival);
    try
    {
      world.Accessor.BreakBlock(target, player.Player);
    }
    catch (Exception e)
    {
      return $"{variant.Code}{stage} broken from {where} threw {Describe(e)}";
    }

    var standing = fillers
      .Prepend(At)
      .Where(p => world.GetBlock(p).Id != 0)
      .ToList();
    if (standing.Count > 0)
      return $"{variant.Code}{stage} broken from {where} left "
        + string.Join(
          ", ",
          standing.Select(p => $"{p} ({world.GetBlock(p).Code})")
        );

    return DropMismatch(world.Drops, expected) is { } mismatch
      ? $"{variant.Code}{stage} broken from {where} {mismatch}"
      : null;
  }

  /// <summary>Adds the block's resolved <c>drops</c> (the block itself when the definition names
  /// none) to <paramref name="expected"/>.</summary>
  private static void AddDefinitionDrops(
    Block block,
    Dictionary<string, (float Low, float High)> expected
  )
  {
    foreach (BlockDropItemStack drop in block.Drops ?? [])
      if (drop.Code != null)
        Expect(
          expected,
          drop.Code.ToString(),
          drop.Quantity.avg - drop.Quantity.var,
          drop.Quantity.avg + drop.Quantity.var
        );
  }

  /// <summary>Builds <paramref name="rcc"/> to stage <paramref name="built"/> with each stored
  /// wildcard set to its ingredient's first allowed variant, registers every material its stages
  /// name, and adds to <paramref name="expected"/> what breaking it refunds: every stage up to the
  /// one built, at the salvage ratio, once at least one stage is paid.</summary>
  private static void Build(
    TestWorld world,
    Block block,
    RightClickConstruction rcc,
    int built,
    Dictionary<string, (float Low, float High)> expected
  )
  {
    rcc.CurrentCompletedStage = built;
    for (int i = 1; i <= built; i++)
      foreach (ConstructionIngredient ing in rcc.Stages[i].RequireStacks ?? [])
        if (ing.StoreWildCard is { } key)
          rcc.StoredWildCards[key] =
            ing.AllowedVariants?.FirstOrDefault()
            ?? throw new InvalidOperationException(
              $"stage {i} stores wildcard '{key}' for {ing.Code} but names no allowed variant"
            );

    float ratio =
      ExRccSettings.BrokenDropsRatio(block.Code.Domain)
      ?? block
        .BlockEntityBehaviors.First(b => b.Name == "ExRightClickConstructable")
        .properties["brokenDropsRatio"]
        .AsFloat(1f);
    for (int i = 0; i <= built; i++)
      foreach (ConstructionIngredient ing in rcc.Stages[i].RequireStacks ?? [])
      {
        string code = ing.Code.ToString();
        if (
          ing.StoreWildCard is { } key
          && rcc.StoredWildCards.TryGetValue(key, out string? value)
        )
          code = code.Replace("*", value);
        if (!code.Contains('*'))
          Resolvable(world, code, ing.Type);
        if (built >= 1)
          Expect(
            expected,
            code,
            MathF.Floor(ing.Quantity * ratio),
            MathF.Ceiling(ing.Quantity * ratio)
          );
      }
  }

  /// <summary>Registers <paramref name="code"/> in <paramref name="world"/> as an item or a block
  /// when nothing there answers to it yet.</summary>
  private static void Resolvable(
    TestWorld world,
    string code,
    EnumItemClass type
  )
  {
    var location = new AssetLocation(code);
    if (type == EnumItemClass.Item)
    {
      if (world.GetItem(location) == null)
        world.RegisterItem(code);
    }
    else if (world.World.GetBlock(location) == null)
      world.Register(
        TestBlocks.Configure(
          new Block(),
          code,
          Interlocked.Increment(ref _nextMaterialId)
        )
      );
  }

  private static int _nextMaterialId = 60000;

  private static void Expect(
    Dictionary<string, (float Low, float High)> expected,
    string code,
    float low,
    float high
  )
  {
    expected.TryGetValue(code, out var have);
    expected[code] = (have.Low + low, have.High + high);
  }

  /// <summary>How <paramref name="drops"/> differ from <paramref name="expected"/> per code, or
  /// null when every code's total falls inside its expected range.</summary>
  private static string? DropMismatch(
    IEnumerable<ItemStack> drops,
    Dictionary<string, (float Low, float High)> expected
  )
  {
    var actual = drops
      .GroupBy(s => s.Collectible?.Code?.ToString() ?? "?")
      .ToDictionary(g => g.Key, g => g.Sum(s => s.StackSize));
    var wrong = expected
      .Keys.Union(actual.Keys)
      .OrderBy(c => c)
      .Select(code =>
      {
        int got = actual.GetValueOrDefault(code);
        var (low, high) = expected.GetValueOrDefault(code);
        return got >= low && got <= high
          ? null
          : $"{code} x{got} (definition: {low}..{high})";
      })
      .OfType<string>()
      .ToList();
    return wrong.Count == 0 ? null : "dropped " + string.Join(", ", wrong);
  }

  private static string Describe(Exception e)
  {
    string? frame = e
      .StackTrace?.Split('\n')
      .Select(l => l.Trim())
      .FirstOrDefault();
    return $"{e.GetType().Name}: {e.Message} ({frame})";
  }
}
#endif
