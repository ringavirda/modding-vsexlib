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
/// <remarks>A structure with construction stages is paid stage by stage through its own
/// interaction, the game's <see cref="RightClickConstruction"/>, in each <see cref="Payment"/>, and
/// broken from every cell at each stage, partly built or complete; one without is broken from every
/// cell. A break passes when nothing throws, no cell of the structure is left standing, and the
/// drops are the block's: its resolved <c>drops</c> plus stage 0's materials and what every paid
/// stage took, at the configured salvage ratio.</remarks>
public static class StructureBreaks
{
  /// <summary>What a run covered, every break that failed, one line each, and what each break
  /// spawned.</summary>
  /// <param name="Blocks">Definitions or blocktypes stood up.</param>
  /// <param name="Variants">Concrete block variants stood up across them.</param>
  /// <param name="Breaks">Breaks performed, one per structure broken from one cell.</param>
  /// <param name="Spawned">Every break that ran, in run order, with the stacks it spawned.</param>
  /// <summary>How a player pays a construction stage.</summary>
  public enum Payment
  {
    /// <summary>A survival player, from the hotbar.</summary>
    Survival,

    /// <summary>A creative player holding Ctrl, whom the game charges nothing and records as
    /// having paid <c>wood</c> oak and <c>metal</c> iron.</summary>
    CreativeWithCtrl,

    /// <summary>A creative player without Ctrl, who pays from the hotbar as in survival.</summary>
    Creative,
  }

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
  /// <param name="Paid">How its stages were paid; null for a structure without stages or one built
  /// to stage 0.</param>
  public sealed record Spawn(
    string Code,
    int? Stage,
    int Cell,
    IReadOnlyList<ItemStack> Stacks,
    Payment? Paid
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

  /// <summary>Breaks the variant <paramref name="code"/> from every cell at every stage, each stage
  /// past 0 paid in every <see cref="Payment"/>, standing it up at a fresh
  /// <paramref name="stand"/> site for its stage count and for each break.</summary>
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
      Payment?[] payments =
        built == 0 ? [null] : [.. Enum.GetValues<Payment>()];
      foreach (Payment? payment in payments)
        for (int cell = -1; ; cell++)
        {
          string? failure = BreakOnce(
            stand(),
            code,
            stages == 0 ? null : built,
            payment,
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
      if (Construction(site.World, site.At) is { } behavior)
        return Rcc(behavior).Stages.Length;
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

  private static ExRightClickConstructable? Construction(
    TestWorld world,
    BlockPos at
  ) => world.GetBlockEntity(at)?.GetBehavior<ExRightClickConstructable>();

  private static RightClickConstruction Rcc(
    ExRightClickConstructable behavior
  ) => (RightClickConstruction)ReflectionHelpers.GetField(behavior, "rcc")!;

  /// <summary>Stands the variant <paramref name="code"/> up at <paramref name="site"/>, pays it to
  /// stage <paramref name="built"/> (null for a structure without stages) in
  /// <paramref name="payment"/>, breaks it from <paramref name="cell"/> (-1 the principal, else a
  /// filler cell's index) and adds what the break spawned to <paramref name="spawned"/>.</summary>
  /// <param name="noMoreCells">Set when no later cell can be broken: <paramref name="cell"/> is past
  /// the last filler (the return is then null), or the structure could not be stood up.</param>
  /// <returns>Why the break failed, or null when it passed.</returns>
  private static string? BreakOnce(
    Site site,
    string code,
    int? built,
    Payment? payment,
    int cell,
    List<Spawn> spawned,
    out bool noMoreCells
  )
  {
    noMoreCells = false;
    TestWorld world = site.World;
    BlockPos at = site.At;
    string where = cell < 0 ? "the principal" : $"filler cell {cell}";
    string stage =
      (built is { } k0 ? $" at stage {k0}" : "")
      + (payment is { } how ? " " + Describe(how) : "");
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
      if (built is { } k && Construction(world, at) is { } behavior)
        Pay(
          world,
          at,
          block,
          behavior,
          k,
          payment ?? Payment.Survival,
          expected
        );
    }
    catch (Exception e)
    {
      noMoreCells = true;
      return $"{code}{stage} could not be stood up: {Describe(e)}";
    }

    BlockPos target = cell < 0 ? at : fillers[cell];
    TestPlayer player = world.Player();
    player.GameMode = EnumGameMode.Survival;
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
      spawned.Add(new Spawn(code, built, cell, [.. world.Drops], payment));
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

  /// <summary>One ingredient as a stage offers it: its code with the stored keys filled and, for a
  /// wildcard, the allowed variant this stage pays it in.</summary>
  /// <param name="Pattern">The code with the stored keys filled, its wildcard kept.</param>
  /// <param name="Code">The code paid.</param>
  /// <param name="Key">The key the ingredient stores, or null.</param>
  /// <param name="Variant">The variant paid for the wildcard, or null for an exact code.</param>
  /// <param name="Allowed">The ingredient's allowed variants; empty for an exact code.</param>
  private sealed record Offer(
    string Pattern,
    string Code,
    EnumItemClass Type,
    int Quantity,
    string? Key,
    string? Variant,
    string[] Allowed
  )
  {
    public string In(string variant) => Pattern.Replace("*", variant);
  }

  /// <summary>Pays <paramref name="behavior"/>'s stages 1 to <paramref name="built"/> through its
  /// own interaction as a player paying in <paramref name="payment"/>, and adds to
  /// <paramref name="expected"/> what breaking it refunds: stage 0's materials and what each stage
  /// took, at the salvage ratio, once a stage is paid.</summary>
  /// <remarks>A wildcard ingredient is paid in its next allowed variant, one wildcard stage after
  /// another. Before paying a stage from the hotbar, a key the stage stores is offered in two
  /// variants, and the stage must refuse it. What a stage paid with Ctrl held took is what the game
  /// records: its codes with the stored keys filled.</remarks>
  /// <exception cref="InvalidOperationException">A wildcard ingredient names no allowed variant, a
  /// stage is not paid, or a stage takes a stored key in two variants.</exception>
  private static void Pay(
    TestWorld world,
    BlockPos at,
    Block block,
    ExRightClickConstructable behavior,
    int built,
    Payment payment,
    Dictionary<string, (float Low, float High)> expected
  )
  {
    RightClickConstruction rcc = Rcc(behavior);
    TestPlayer payer = world.Player("payer");
    payer.GameMode =
      payment == Payment.Survival
        ? EnumGameMode.Survival
        : EnumGameMode.Creative;
    payer.CtrlHeld = payment == Payment.CreativeWithCtrl;
    var paid = new List<(string Code, EnumItemClass Type, int Quantity)>();
    int turn = 0;
    for (int i = 1; i <= built; i++)
    {
      Offer[] offers = Offers(rcc.Stages[i], i, rcc.StoredWildCards, turn);
      foreach (Offer offer in offers)
        Resolvable(world, offer.Code, offer.Type, offer.Key, offer.Variant);
      var stores = new Dictionary<string, string>(rcc.StoredWildCards);
      foreach (Offer offer in offers.Where(o => o.Key != null))
        if (payment == Payment.CreativeWithCtrl)
        {
          stores["wood"] = "oak";
          stores["metal"] = "iron";
        }
        else
          stores[offer.Key!] = offer.Variant!;
      if (i + 1 < rcc.Stages.Length)
        foreach (
          ConstructionIngredient next in rcc.Stages[i + 1].RequireStacks ?? []
        )
          if (
            Filled(next.Code.ToString(), stores) is var code
            && !code.Contains('*')
            && !code.Contains('{')
          )
            Resolvable(world, code, next.Type);

      if (payment != Payment.CreativeWithCtrl)
        OfferMixed(world, behavior, rcc, payer, offers, i, at);

      Fill(payer.Hotbar, offers.Select(o => (o.Code, o.Type, o.Quantity)));
      int[] before = [.. payer.Hotbar.Select(s => s.StackSize)];
      Interact(world, behavior, payer, at);
      if (rcc.CurrentCompletedStage != i)
        throw new InvalidOperationException(
          $"stage {i} was not paid {Describe(payment)}"
        );
      for (int j = 0; j < offers.Length; j++)
        paid.Add(
          payment == Payment.CreativeWithCtrl
            ? (
              offers[j].Key is { } key
              && rcc.StoredWildCards.TryGetValue(key, out string? seeded)
                ? offers[j].In(seeded)
                : offers[j].Pattern,
              offers[j].Type,
              offers[j].Quantity
            )
            : (
              offers[j].Code,
              offers[j].Type,
              before[j] - payer.Hotbar[j].StackSize
            )
        );
      Fill(payer.Hotbar, []);
      if (offers.Any(o => o.Variant != null))
        turn++;
    }

    float ratio =
      ExRccSettings.BrokenDropsRatio(block.Code.Domain)
      ?? block
        .BlockEntityBehaviors.First(b => b.Name == "ExRightClickConstructable")
        .properties["brokenDropsRatio"]
        .AsFloat(1f);
    foreach (ConstructionIngredient ing in rcc.Stages[0].RequireStacks ?? [])
    {
      string code = Filled(ing.Code.ToString(), rcc.StoredWildCards);
      if (
        ing.StoreWildCard is { } key
        && rcc.StoredWildCards.TryGetValue(key, out string? value)
      )
        code = code.Replace("*", value);
      paid.Insert(0, (code, ing.Type, ing.Quantity));
    }
    foreach ((string code, EnumItemClass type, _) in paid)
      if (!code.Contains('*') && !code.Contains('{'))
        Resolvable(world, code, type);
    if (built >= 1)
      foreach ((string code, _, int quantity) in paid)
        Expect(
          expected,
          code,
          MathF.Floor(quantity * ratio),
          MathF.Ceiling(quantity * ratio)
        );
  }

  /// <summary>What stage <paramref name="index"/> offers, its stored keys filled from
  /// <paramref name="stored"/> and each wildcard paid in the allowed variant
  /// <paramref name="turn"/> steps on.</summary>
  /// <exception cref="InvalidOperationException">A wildcard ingredient names no allowed
  /// variant.</exception>
  private static Offer[] Offers(
    Vintagestory.GameContent.ConstructionStage stage,
    int index,
    IReadOnlyDictionary<string, string> stored,
    int turn
  ) =>
    [
      .. (stage.RequireStacks ?? []).Select(ing =>
      {
        string pattern = Filled(ing.Code.ToString(), stored);
        if (!pattern.Contains('*'))
          return new Offer(
            pattern,
            pattern,
            ing.Type,
            ing.Quantity,
            ing.StoreWildCard,
            null,
            []
          );
        string[] allowed = ing.AllowedVariants is { Length: > 0 } a
          ? a
          : throw new InvalidOperationException(
            ing.StoreWildCard is { } key
              ? $"stage {index} stores wildcard '{key}' for {ing.Code} but names no allowed variant"
              : $"stage {index} asks for {ing.Code} but names no allowed variant"
          );
        string variant = allowed[turn % allowed.Length];
        return new Offer(
          pattern,
          pattern.Replace("*", variant),
          ing.Type,
          ing.Quantity,
          ing.StoreWildCard,
          variant,
          allowed
        );
      }),
    ];

  /// <summary>Offers stage <paramref name="index"/> the first key it stores in two variants: one
  /// storing ingredient in another allowed variant than the rest, or a lone storing ingredient of
  /// two or more split one unit to the other variant; nothing when no key allows it.</summary>
  /// <exception cref="InvalidOperationException">The stage took the mixed payment.</exception>
  private static void OfferMixed(
    TestWorld world,
    ExRightClickConstructable behavior,
    RightClickConstruction rcc,
    TestPlayer payer,
    Offer[] offers,
    int index,
    BlockPos at
  )
  {
    foreach (
      IGrouping<string, Offer> storing in offers
        .Where(o => o.Key != null && o.Allowed.Distinct().Count() >= 2)
        .GroupBy(o => o.Key!)
    )
    {
      Offer first = storing.First();
      string other = first.Allowed.First(v => v != first.Variant);
      bool several = storing.Skip(1).Any(o => o.Variant != other);
      if (!several && first.Quantity < 2)
        continue;
      Resolvable(world, first.In(other), first.Type, first.Key, other);
      var stacks = new List<(string, EnumItemClass, int)>();
      foreach (Offer offer in offers)
        if (!ReferenceEquals(offer, first))
          stacks.Add((offer.Code, offer.Type, offer.Quantity));
        else if (several)
          stacks.Add((offer.In(other), offer.Type, offer.Quantity));
        else
        {
          stacks.Add((offer.In(other), offer.Type, 1));
          stacks.Add((offer.Code, offer.Type, offer.Quantity - 1));
        }
      Fill(payer.Hotbar, stacks);
      Interact(world, behavior, payer, at);
      Fill(payer.Hotbar, []);
      if (rcc.CurrentCompletedStage >= index)
        throw new InvalidOperationException(
          $"stage {index} took '{storing.Key}' in two variants, {other} and {first.Variant}"
        );
      return;
    }
  }

  private static string Filled(
    string code,
    IReadOnlyDictionary<string, string> stored
  )
  {
    foreach ((string key, string value) in stored)
      code = code.Replace("{" + key + "}", value);
    return code;
  }

  /// <summary>Empties <paramref name="hotbar"/> and puts each of <paramref name="stacks"/> in the
  /// next slot, in order.</summary>
  /// <exception cref="InvalidOperationException">More stacks than slots.</exception>
  private static void Fill(
    InventoryGeneric hotbar,
    IEnumerable<(string Code, EnumItemClass Type, int Quantity)> stacks
  )
  {
    foreach (ItemSlot slot in hotbar)
      slot.Itemstack = null;
    int next = 0;
    foreach ((string code, EnumItemClass type, int quantity) in stacks)
    {
      if (next >= hotbar.Count)
        throw new InvalidOperationException(
          $"a stage asks for more than the {hotbar.Count} hotbar slots"
        );
      var location = new AssetLocation(code);
      hotbar[next++].Itemstack =
        type == EnumItemClass.Item
          ? new ItemStack(hotbar.Api.World.GetItem(location), quantity)
          : new ItemStack(hotbar.Api.World.GetBlock(location), quantity);
    }
  }

  private static void Interact(
    TestWorld world,
    ExRightClickConstructable behavior,
    TestPlayer payer,
    BlockPos at
  )
  {
    EnumHandling handling = EnumHandling.PassThrough;
    behavior.OnBlockInteractStart(
      world.World,
      payer.Player,
      new BlockSelection { Position = at.Copy() },
      ref handling
    );
  }

  private static string Describe(Payment payment) =>
    payment switch
    {
      Payment.Survival => "paid in survival",
      Payment.CreativeWithCtrl => "paid in creative with Ctrl held",
      _ => "paid in creative",
    };

  /// <summary>Registers <paramref name="code"/> in <paramref name="world"/> as an item or a block
  /// when nothing there answers to it yet, and gives it <paramref name="variant"/> under
  /// <paramref name="key"/> when it names none there.</summary>
  private static void Resolvable(
    TestWorld world,
    string code,
    EnumItemClass type,
    string? key = null,
    string? variant = null
  )
  {
    var location = new AssetLocation(code);
    CollectibleObject collectible;
    if (type == EnumItemClass.Item)
      collectible = world.GetItem(location) ?? world.RegisterItem(code);
    else if (world.World.GetBlock(location) is { } known)
      collectible = known;
    else
    {
      Block standIn = TestBlocks.Configure(
        new Block(),
        code,
        Interlocked.Increment(ref _nextMaterialId)
      );
      ReflectionHelpers.SetField(standIn, "api", world.Api);
      world.Register(standIn);
      collectible = standIn;
    }
    if (key != null && variant != null && collectible.Variant[key] == null)
      collectible.VariantStrict[key] = variant;
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
          : $"{code} x{got} (expected: {low}..{high})";
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
