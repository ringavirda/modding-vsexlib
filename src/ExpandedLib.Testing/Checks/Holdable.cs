#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ExpandedLib.Catalogues;
using ExpandedLib.Checks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ExpandedLib.Testing;

/// <summary>Which codes of a domain a survival player can hold as a stack, and why.</summary>
/// <remarks>A code is held when something yields it: the output of any recipe the craft walk reads
/// (fired or not), a byproduct of a craft sequence or machining job, a smelted, crushed, ground,
/// fired, harvested, squeezed or juiced stack, the product of a transition, a code a gathered
/// declaration, a produced declaration of an item or the kit names, or a drop of a block. A drop is read from
/// <see cref="Block.Drops"/>, and from <see cref="Block.GetDrops"/> itself for a block whose class
/// or behaviours override it. A drop of the block's own type counts only where the block can stand
/// in the world: a code of its kind is held. A code made in place, by a produced declaration or a
/// machine product, is held only when the drops of its own block (<see cref="Block.Drops"/>, or
/// <see cref="Block.GetDrops"/> for an overriding class or behaviour) include that code: a hooped
/// brick course that drops brick and hoop is not held, and one whose drops cannot be read is
/// <see cref="State.Unsure"/>. A gathered code, a construction stage's block and a kit row are not
/// made in place: gathered and kit codes are held as stacks, and construction edges credit
/// nothing. Knapping, tool modes, traders and loot lists are not
/// read. A code whose answer is not certain is <see cref="State.Unsure"/>, and a view gate keeps
/// judging it.</remarks>
public static class Holdable
{
  /// <summary>Variable naming the file <see cref="Write"/> writes the census to.</summary>
  public const string DefaultVariable = "EX_HOLDABLE";

  /// <summary>Whether a stack of a code can be held.</summary>
  public enum State
  {
    /// <summary>Something yields it.</summary>
    Yes,

    /// <summary>Nothing the census reads yields it.</summary>
    No,

    /// <summary>The census cannot tell; a gate judges it.</summary>
    Unsure,
  }

  /// <summary>One collectible of the domain.</summary>
  /// <param name="Code">The domain-qualified code.</param>
  /// <param name="Type">Block or item.</param>
  /// <param name="State">Whether a stack of it can be held.</param>
  /// <param name="Why">One line: what yields it; <c>creative only</c> or <c>none</c> when
  /// <paramref name="State"/> is <see cref="State.No"/>; the reason it is unsure
  /// otherwise.</param>
  /// <param name="Creative">Whether a creative tab lists it.</param>
  public sealed record Entry(
    string Code,
    EnumItemClass Type,
    State State,
    string Why,
    bool Creative
  );

  /// <summary>The census of one domain.</summary>
  public sealed class Census
  {
    private readonly Dictionary<string, Entry> _byCode;

    internal Census(IReadOnlyList<Entry> entries)
    {
      Entries = entries;
      _byCode = entries.ToDictionary(e => e.Code, StringComparer.Ordinal);
    }

    /// <summary>One entry per collectible of the domain, sorted by code.</summary>
    public IReadOnlyList<Entry> Entries { get; }

    /// <summary>Why a view gate may skip <paramref name="code"/>.</summary>
    /// <param name="code">A domain-qualified code.</param>
    /// <returns><c>not holdable</c> for a code nothing yields, <c>creative only</c> for one
    /// only a creative tab lists; null for a held or unsure code and for a code the census has no
    /// entry for.</returns>
    public string? SkipOf(string code) =>
      _byCode.TryGetValue(code, out Entry? entry) && entry.State == State.No
        ? (entry.Creative ? "creative only" : "not holdable")
        : null;
  }

  private const string NotProbed = "drops not probed: ";
  private const string AskedFor = "asked for by ";
  private const string OwnDrop = "only its own drop yields it: ";
  private const string Placed = "stands where it is made, not held: ";
  private const string ProducedBy = "produced by ";

  /// <summary>Builds the census of <paramref name="domain"/>.</summary>
  /// <param name="world">A world from <see cref="TestWorld.LoadAssets(IReadOnlyList{string}, string?, string[])"/>
  /// then <see cref="TestWorld.LoadRecipes"/>.</param>
  /// <param name="domain">The domain whose collectibles are listed.</param>
  /// <param name="kitFile">The craft walk's kit file; null or a missing file is empty.</param>
  /// <returns>The census, one entry for each block and item of <paramref name="domain"/>.</returns>
  /// <exception cref="InvalidOperationException">The kit file is not a JSON object or a row has no
  /// route.</exception>
  /// <exception cref="Newtonsoft.Json.JsonException">The kit file is not JSON.</exception>
  public static Census Run(TestWorld world, string domain, string? kitFile)
  {
    CollectibleObject[] all =
    [
      .. world
        .World.Items.Where(i => i?.Code != null)
        .Cast<CollectibleObject>(),
      .. world.World.Blocks.Where(b => b is { Id: not 0 }),
    ];
    CollectibleObject[] mine = [.. all.Where(c => c.Code.Domain == domain)];
    var yes = new Dictionary<string, string>(StringComparer.Ordinal);
    var placed = new Dictionary<string, string>(StringComparer.Ordinal);
    HashSet<string> blocks =
    [
      .. all.OfType<Block>().Select(b => b.Code.ToString()),
    ];

    List<WalkEdge> edges = CraftWalkEdges.Read(world, domain, all);
    foreach (WalkEdge edge in edges.Where(e => e.Kind != "construction"))
    {
      string output = edge.Output.StartsWith("core:", StringComparison.Ordinal)
        ? edge.Output[5..]
        : edge.Output;
      if (edge.Kind == "machine product" && blocks.Contains(output))
        placed.TryAdd(output, edge.Name);
      else
        yes.TryAdd(output, edge.Name);
      foreach (CraftByproduct by in edge.Sequence?.Byproducts ?? [])
        yes.TryAdd(by.Code, $"{edge.Name} byproduct");
    }
    foreach (
      KeyValuePair<string, string> seeded in CraftWalk.Seeded(
        all,
        CraftWalkFiles.Kit(kitFile)
      )
    )
      yes.TryAdd(seeded.Key, seeded.Value);

    var made = new ObtainabilityCheck.Made(all, []);
    foreach (ExlibChecks.Declaration declared in ExlibChecks.Produced())
      made.Declare(all, declared.Code, ProducedBy + declared.Source);
    foreach ((bool isBlock, string code, string source) in made.Held())
    {
      Dictionary<string, string> into =
        isBlock && source.StartsWith(ProducedBy, StringComparison.Ordinal)
          ? placed
          : yes;
      if (code.IndexOfAny(['*', '@', '{']) < 0)
        into.TryAdd(code, source);
      else
        foreach (
          CollectibleObject c in mine.Where(c =>
            (c is Block) == isBlock
            && WildcardUtil.Match(new AssetLocation(code), c.Code)
          )
        )
          into.TryAdd(c.Code.ToString(), source);
    }

    var own = new List<(Block Block, string Dropped)>();
    var notProbed = new Dictionary<string, string>(StringComparer.Ordinal);
    var droppedBy = new Dictionary<string, List<string>>(
      StringComparer.Ordinal
    );
    foreach (Block block in mine.OfType<Block>())
    {
      string type = ObtainabilityCheck.TypeOf(block);
      List<string> read = [];
      foreach (BlockDropItemStack drop in block.Drops ?? [])
        if (drop?.Code != null)
        {
          read.Add(drop.Code.ToString());
          if (IsOwn(drop.Code.ToString(), type))
            own.Add((block, drop.Code.ToString()));
        }
      if (!Overrides(block))
      {
        droppedBy[block.Code.ToString()] = read;
        continue;
      }
      try
      {
        ItemStack[]? drops = Probe(world, block);
        if (drops == null)
        {
          notProbed[block.Code.ToString()] = NotProbed + "null";
          continue;
        }
        read = [];
        foreach (ItemStack stack in drops.Where(d => d?.Collectible != null))
        {
          string dropped = stack.Collectible.Code.ToString();
          read.Add(dropped);
          if (IsOwn(dropped, type))
            own.Add((block, dropped));
          else
            yes.TryAdd(dropped, $"drop of {block.Code}");
        }
        droppedBy[block.Code.ToString()] = read;
      }
      catch (Exception e)
      {
        notProbed[block.Code.ToString()] = NotProbed + e.GetType().Name;
      }
    }

    var notHeld = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach ((string code, string source) in placed)
    {
      if (yes.ContainsKey(code) || !droppedBy.TryGetValue(code, out var read))
        continue;
      if (read.Contains(code))
        yes[code] = source.StartsWith(ProducedBy, StringComparison.Ordinal)
          ? source
          : ProducedBy + source;
      else
        notHeld[code] =
          $"{Placed}{source}; its drop yields "
          + (read.Count == 0 ? "nothing" : string.Join(", ", read.Distinct()));
    }

    bool grew = true;
    while (grew)
    {
      grew = false;
      HashSet<string> standing =
      [
        .. mine.Where(c => yes.ContainsKey(c.Code.ToString()))
          .Select(ObtainabilityCheck.KindOf),
      ];
      foreach ((Block block, string dropped) in own)
        if (
          !yes.ContainsKey(dropped)
          && standing.Contains(ObtainabilityCheck.KindOf(block))
        )
        {
          yes[dropped] = $"drop of {block.Code}";
          grew = true;
        }
    }

    var unsure = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (WalkEdge edge in edges)
    foreach (WalkNeed need in edge.Needs)
      if (!need.Codes.Any(yes.ContainsKey))
        foreach (string code in need.Codes)
          unsure.TryAdd(code, $"{AskedFor}{edge.Name}, which nothing yields");
    HashSet<string> heldTypes =
    [
      .. mine.Where(c => yes.ContainsKey(c.Code.ToString()))
        .Select(ObtainabilityCheck.TypeOf),
    ];
    foreach ((Block block, string dropped) in own)
      if (
        !yes.ContainsKey(dropped)
        && mine.FirstOrDefault(m => m.Code.ToString() == dropped) is { } target
        && heldTypes.Contains(ObtainabilityCheck.TypeOf(target))
      )
        unsure.TryAdd(dropped, OwnDrop + block.Code);

    var entries = new List<Entry>();
    foreach (
      CollectibleObject c in mine.OrderBy(
        c => c.Code.ToString(),
        StringComparer.Ordinal
      )
    )
    {
      string code = c.Code.ToString();
      bool creative = c.CreativeInventoryTabs is { Length: > 0 };
      (State state, string why) =
        yes.TryGetValue(code, out string? source) ? (State.Yes, source)
        : notHeld.TryGetValue(code, out string? placedWhy)
          ? (State.No, placedWhy)
        : notProbed.TryGetValue(code, out string? failed)
          ? (State.Unsure, failed)
        : unsure.TryGetValue(code, out string? reason) ? (State.Unsure, reason)
        : (State.No, creative ? "creative only" : "none");
      entries.Add(new Entry(code, c.ItemClass, state, why, creative));
    }
    return new Census(entries);
  }

  /// <summary>Writes <paramref name="census"/> as a tab-separated file, then its counts.</summary>
  /// <param name="census">The census to write.</param>
  /// <param name="variable">The environment variable that names the file; unset or empty writes
  /// nothing.</param>
  /// <returns>The path written; null when <paramref name="variable"/> is not set.</returns>
  /// <remarks>Columns are <c>code</c>, <c>type</c>, <c>holdable</c> (<c>yes</c>, <c>no</c> or
  /// <c>unsure</c>), <c>why</c> and <c>creative</c> (<c>yes</c> or <c>no</c>), sorted by code. The
  /// <c>#</c> lines after them count the codes, the creative-only ones, and the unsure ones by
  /// reason.</remarks>
  /// <exception cref="IOException">The file cannot be written.</exception>
  [CheckHelper("writes the census to the file a variable names")]
  public static string? Write(Census census, string variable = DefaultVariable)
  {
    if (
      Environment.GetEnvironmentVariable(variable) is not { Length: > 0 } path
    )
      return null;
    var lines = new List<string> { "code\ttype\tholdable\twhy\tcreative" };
    lines.AddRange(
      census.Entries.Select(e =>
        string.Join(
          "\t",
          e.Code,
          e.Type.ToString().ToLowerInvariant(),
          e.State.ToString().ToLowerInvariant(),
          e.Why.ReplaceLineEndings(" ").Replace('\t', ' '),
          e.Creative ? "yes" : "no"
        )
      )
    );
    int Count(System.Func<Entry, bool> where) => census.Entries.Count(where);
    lines.Add($"#total\t{census.Entries.Count}");
    lines.Add($"#yes\t{Count(e => e.State == State.Yes)}");
    lines.Add($"#no\t{Count(e => e.State == State.No)}");
    lines.Add(
      $"#creative only\t{Count(e => e.State == State.No && e.Creative)}"
    );
    lines.Add($"#unsure\t{Count(e => e.State == State.Unsure)}");
    lines.Add(
      $"#unsure drops not probed\t{Count(e => e.State == State.Unsure && e.Why.StartsWith(NotProbed, StringComparison.Ordinal))}"
    );
    lines.Add(
      $"#unsure asked for\t{Count(e => e.State == State.Unsure && e.Why.StartsWith(AskedFor, StringComparison.Ordinal))}"
    );
    lines.Add(
      $"#unsure own drop only\t{Count(e => e.State == State.Unsure && e.Why.StartsWith(OwnDrop, StringComparison.Ordinal))}"
    );
    string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
    if (folder != null)
      Directory.CreateDirectory(folder);
    File.WriteAllLines(path, lines);
    return path;
  }

  private static bool IsOwn(string dropped, string type) =>
    dropped == type || dropped.StartsWith(type + "-", StringComparison.Ordinal);

  // Whether the block's class or one of its behaviours declares its own GetDrops.
  private static bool Overrides(Block block) =>
    Declares(block.GetType(), typeof(Block))
    || (block.BlockBehaviors ?? []).Any(b =>
      Declares(b.GetType(), typeof(BlockBehavior))
    );

  private static bool Declares(Type type, Type stop)
  {
    for (Type? t = type; t != null && t != stop; t = t.BaseType)
      if (
        t.GetMethods(
            BindingFlags.Public
              | BindingFlags.NonPublic
              | BindingFlags.Instance
              | BindingFlags.DeclaredOnly
          )
          .Any(m => m.Name == "GetDrops")
      )
        return true;
    return false;
  }

  private static ItemStack[]? Probe(TestWorld world, Block block)
  {
    StructureBreaks.Ready(world, block);
    var at = new BlockPos(16, 8, 16);
    world.Place(at, block);
    return block.GetDrops(world.World, at, null);
  }
}
#endif
