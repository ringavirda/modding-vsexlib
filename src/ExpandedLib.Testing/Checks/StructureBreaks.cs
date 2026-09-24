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

/// <summary>Stands up every block that reserves filler cells or carries construction stages, from a
/// mod's code-first definitions or from the JSON blocks <see cref="TestWorld.LoadAssets"/> loaded,
/// and breaks it as a survival player, through the engine's break and removal hooks
/// (<see cref="TestWorld.BreakRunsBlockHooks"/>, <see cref="TestWorld.RunsRemovalHooks"/>).</summary>
/// <remarks>A structure with construction stages is broken from every cell at each stage, partly
/// built or complete; one without is broken from every cell. A break passes
/// when nothing throws, no cell of the structure is left standing, and the drops are the
/// block's: its resolved <c>drops</c> plus the materials of every paid stage at the configured
/// salvage ratio.</remarks>
public static class StructureBreaks
{
  /// <summary>What a run covered, every break that failed, one line each, and what each break
  /// spawned.</summary>
  /// <param name="Blocks">Definitions or blocktypes stood up.</param>
  /// <param name="Variants">Concrete block variants stood up across them.</param>
  /// <param name="Breaks">Breaks performed, one per structure broken from one cell.</param>
  /// <param name="Spawned">Every break that ran, in run order, with the stacks it spawned.</param>
  public sealed record Result(
    int Blocks,
    int Variants,
    int Breaks,
    IReadOnlyList<string> Failures,
    IReadOnlyList<Spawn> Spawned
  );

  /// <summary>The stacks one break spawned into the world through <c>SpawnItemEntity</c>, whatever
  /// spawned them: the block's drops, its block entity's contents, or its construction
  /// refund.</summary>
  /// <param name="Code">The variant broken.</param>
  /// <param name="Stage">The construction stage it was built to; null for a structure without
  /// stages.</param>
  /// <param name="Cell">The cell it was broken from: -1 the principal, else the index of a filler
  /// cell, ordered by X, then Y, then Z.</param>
  /// <param name="Stacks">Every stack spawned from the break on, those of a break that threw
  /// included.</param>
  public sealed record Spawn(
    string Code,
    int? Stage,
    int Cell,
    IReadOnlyList<ItemStack> Stacks
  );

  private static readonly BlockPos At = new(64, 64, 64);

  /// <summary>Blocks along X between the principals a run over one world places.</summary>
  private const int Stride = 32;

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
    var tally = new Tally();
    int blocks = 0,
      variants = 0;

    foreach (ExBlockDef def in defs.Where(InScope))
    {
      blocks++;
      bool declaresStages = RccProperties(def.ToJson()) != null;
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

        BreakVariant(
          variant.Code,
          declaresStages,
          () =>
          {
            TestWorld world = Stand();
            return new Site(world, At, () => world.DefineBlock(def, variant));
          },
          tally
        );
      }
    }
    return new Result(
      blocks,
      variants,
      tally.Breaks,
      tally.Failures,
      tally.Spawned
    );
  }

  /// <summary>Breaks each block of <paramref name="world"/> whose attributes carry
  /// <c>fillerOffsets</c> or whose entity behaviours include <c>ExRightClickConstructable</c>, one
  /// variant per block, each at its own principal 32 blocks along X from the last, a later run on
  /// the same world continuing past the earlier run's.</summary>
  /// <remarks>Registers and starts a <see cref="MechanicalPowerMod"/> when the world holds none;
  /// gives each block it breaks, and each <see cref="BlockStructureFiller"/>, the world's api, its
  /// resolved <c>drops</c> and one <see cref="Block.OnLoaded"/>; registers a stand-in for every drop
  /// and material the world lacks; clears <see cref="TestWorld.Drops"/> before each break and keeps
  /// what a break leaves.</remarks>
  /// <param name="world">A world <see cref="TestWorld.LoadAssets"/> loaded, holding the network
  /// types and mod systems the block entities need.</param>
  /// <param name="include">Which in-scope blocks to break; all of them when null.</param>
  /// <returns>The blocktypes (codes less their variant parts) and variants covered, as on the
  /// code-first path.</returns>
  /// <exception cref="Exception">What a structure filler's <see cref="Block.OnLoaded"/> throws; a
  /// block that cannot be stood up or broken is a failure line.</exception>
  public static Result Run(
    TestWorld world,
    System.Func<Block, bool>? include = null
  )
  {
    if (world.Mods.GetModSystem<MechanicalPowerMod>() == null)
    {
      var power = new MechanicalPowerMod();
      world.Mods.Register(power);
      power.Start(world.Api);
    }
    foreach (Block filler in world.World.Blocks.OfType<BlockStructureFiller>())
      Ready(world, filler);

    List<Block> scoped =
    [
      .. world
        .World.Blocks.Where(b => InScope(b) && (include?.Invoke(b) ?? true))
        .OrderBy(b => b.Code.ToString(), StringComparer.Ordinal),
    ];
    var tally = new Tally();
    foreach (Block block in scoped)
    {
      bool ready = false;
      BreakVariant(
        block.Code.ToString(),
        HasConstruction(block),
        () =>
          new Site(
            world,
            new BlockPos(At.X + Stride * world.BreakSites++, At.Y, At.Z),
            () =>
            {
              if (!ready)
                Ready(world, block);
              ready = true;
              return block;
            }
          ),
        tally
      );
    }
    int blocks = scoped
      .Select(b => $"{b.Code.Domain}:{b.CodeWithoutParts(b.Variant.Count)}")
      .Distinct()
      .Count();
    return new Result(
      blocks,
      scoped.Count,
      tally.Breaks,
      tally.Failures,
      tally.Spawned
    );
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

  private static bool InScope(Block block) =>
    block.Attributes?["fillerOffsets"].Exists == true || HasConstruction(block);

  private static bool HasConstruction(Block block) =>
    block.BlockEntityBehaviors?.Any(b => b.Name == "ExRightClickConstructable")
    == true;

  private static JToken? RccProperties(JToken json) =>
    (json["entityBehaviors"] as JArray)
      ?.FirstOrDefault(b => (string?)b["name"] == "ExRightClickConstructable")
      ?["properties"];

  /// <summary>Gives <paramref name="block"/> <paramref name="world"/>'s api, resolves its
  /// <c>drops</c> there, registering a stand-in for each it does not hold, and runs its
  /// <see cref="Block.OnLoaded"/>, as the engine does once the object loader is done.</summary>
  private static void Ready(TestWorld world, Block block)
  {
    ReflectionHelpers.SetField(block, "api", world.Api);
    foreach (BlockDropItemStack drop in block.Drops ?? [])
    {
      Resolvable(world, drop.Code.ToString(), drop.Type);
      drop.Resolve(world.World, "StructureBreaks", block.Code);
    }
    block.OnLoaded(world.Api);
  }

  /// <summary>Where one break stands its structure up: the world, the principal's position, and the
  /// block to place there, built or readied on each call.</summary>
  private sealed record Site(
    TestWorld World,
    BlockPos At,
    System.Func<Block> Block
  );

  private sealed class Tally
  {
    public List<string> Failures { get; } = [];
    public List<Spawn> Spawned { get; } = [];
    public int Breaks { get; set; }
  }

  /// <summary>Breaks the variant <paramref name="code"/> from every cell at every stage, standing it
  /// up at a fresh <paramref name="stand"/> site for its stage count and for each break.</summary>
  private static void BreakVariant(
    string code,
    bool declaresStages,
    System.Func<Site> stand,
    Tally tally
  )
  {
    int stages = StageCount(stand(), code, declaresStages, tally.Failures);
    if (stages < 0)
      return;
    for (int built = 0; built < Math.Max(stages, 1); built++)
    {
      for (int cell = -1; ; cell++)
      {
        string? failure = BreakOnce(
          stand(),
          code,
          stages == 0 ? null : built,
          cell,
          tally.Spawned,
          out bool noMoreCells
        );
        if (failure == null && noMoreCells)
          break;
        tally.Breaks++;
        if (failure != null)
          tally.Failures.Add(failure);
        if (noMoreCells)
          break;
      }
    }
  }

  /// <summary>The number of construction stages the variant at <paramref name="site"/> stands up
  /// with (0 when it declares none), or -1 after recording why it could not be stood up.</summary>
  private static int StageCount(
    Site site,
    string code,
    bool declaresStages,
    List<string> failures
  )
  {
    try
    {
      Block block = site.Block();
      Place(site.World, site.At, block);
      if (Construction(site.World, site.At) is { } rcc)
        return rcc.Stages.Length;
      if (!declaresStages)
        return 0;
      failures.Add(
        $"{code} declares construction stages but stands up without them"
      );
      return -1;
    }
    catch (Exception e)
    {
      failures.Add($"{code} could not be stood up: {Describe(e)}");
      return -1;
    }
  }

  private static void Place(TestWorld world, BlockPos at, Block block)
  {
    world.Place(at, block);
    block.OnBlockPlaced(world.World, at, new ItemStack(block));
  }

  private static RightClickConstruction? Construction(
    TestWorld world,
    BlockPos at
  ) =>
    world.GetBlockEntity(at)?.GetBehavior<ExRightClickConstructable>()
      is { } behavior
      ? (RightClickConstruction?)ReflectionHelpers.GetField(behavior, "rcc")
      : null;

  /// <summary>Stands the variant <paramref name="code"/> up at <paramref name="site"/>, builds it to
  /// stage <paramref name="built"/> (null for a structure without stages), breaks it from
  /// <paramref name="cell"/> (-1 the principal, else a filler cell's index) and adds what the break
  /// spawned to <paramref name="spawned"/>.</summary>
  /// <param name="noMoreCells">Set when no later cell can be broken: <paramref name="cell"/> is past
  /// the last filler (the return is then null), or the structure could not be stood up.</param>
  /// <returns>Why the break failed, or null when it passed.</returns>
  private static string? BreakOnce(
    Site site,
    string code,
    int? built,
    int cell,
    List<Spawn> spawned,
    out bool noMoreCells
  )
  {
    noMoreCells = false;
    TestWorld world = site.World;
    BlockPos at = site.At;
    string where = cell < 0 ? "the principal" : $"filler cell {cell}";
    string stage = built is { } paid ? $" at stage {paid}" : "";
    Block block;
    BlockPos[] fillers;
    var expected = new Dictionary<string, (float Low, float High)>();
    try
    {
      block = site.Block();
      Place(world, at, block);
      fillers =
      [
        .. world
          .BlockEntities.Where(e =>
            e.Value is BlockEntityStructureFiller { Principal: { } p }
            && p.Equals(at)
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
      if (built is { } k && Construction(world, at) is { } rcc)
        Build(world, block, rcc, k, expected);
    }
    catch (Exception e)
    {
      noMoreCells = true;
      return $"{code}{stage} could not be stood up: {Describe(e)}";
    }

    BlockPos target = cell < 0 ? at : fillers[cell];
    TestPlayer player = world.Player();
    player.Player.WorldData.CurrentGameMode.Returns(EnumGameMode.Survival);
    world.Drops.Clear();
    try
    {
      world.Accessor.BreakBlock(target, player.Player);
    }
    catch (Exception e)
    {
      return $"{code}{stage} broken from {where} threw {Describe(e)}";
    }
    finally
    {
      spawned.Add(new Spawn(code, built, cell, [.. world.Drops]));
    }

    var standing = fillers
      .Prepend(at)
      .Where(p => world.GetBlock(p).Id != 0)
      .ToList();
    if (standing.Count > 0)
      return $"{code}{stage} broken from {where} left "
        + string.Join(
          ", ",
          standing.Select(p => $"{p} ({world.GetBlock(p).Code})")
        );

    return DropMismatch(world.Drops, expected) is { } mismatch
      ? $"{code}{stage} broken from {where} {mismatch}"
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
  /// name, its <c>{key}</c>s filled from them, and adds to <paramref name="expected"/> what breaking
  /// it refunds: every stage up to the one built, at the salvage ratio, once one is paid.</summary>
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
        foreach ((string stored, string state) in rcc.StoredWildCards)
          code = code.Replace("{" + stored + "}", state);
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
