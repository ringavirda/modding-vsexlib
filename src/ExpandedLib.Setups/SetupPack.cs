using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ExpandedLib.Machines;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ExpandedLib.Scenarios;

/// <summary>What a run did with its pack, as the recording's <c>pack</c> section keeps it: the stacks
/// it started with, the stacks left, the blocks standing and every draw in the order made.</summary>
internal sealed record SetupPackRecord(
  IReadOnlyDictionary<string, int> Start,
  IReadOnlyDictionary<string, int> End,
  IReadOnlyDictionary<string, int> Standing,
  IReadOnlyList<(string Where, string Code, int Count)> Draws
);

/// <summary>
/// The stacks, metal units and standing blocks a setup's <c>pack</c> holds (setups.md, The pack).
/// Codes are read as keys, a key holding a <c>*</c> matches every code the wildcard covers; a
/// drawn code holding a <c>*</c> draws from one key that covers its whole count.
/// </summary>
internal sealed class SetupPack {
  private readonly SetupPackSpec _spec;
  private readonly Dictionary<string, int> _held = [];
  private readonly List<string> _standing = [];
  private readonly HashSet<(int X, int Y, int Z)> _carried = [];
  private readonly List<(string Where, string Code, int Count)> _draws = [];
  private readonly Dictionary<(string Pin, string Code), string> _pins = [];
  private Dictionary<string, int> _start = [];

  private SetupPack(SetupPackSpec spec) {
    _spec = spec;
  }

  /// <summary>Starts the pack of <paramref name="file"/> from its <c>gathered</c> stacks, its
  /// <c>grants</c>, and the <c>ends</c> of its <c>after</c> stages times their runs.</summary>
  /// <param name="file">A setup file with a <c>pack</c>.</param>
  /// <param name="stageOf">The file an <c>after</c> id names, or null when no file has that id.</param>
  /// <returns>The pack, with nothing drawn or given yet.</returns>
  /// <exception cref="ArgumentException"><paramref name="file"/> has no <c>pack</c>.</exception>
  /// <exception cref="InvalidOperationException">An <c>after</c> names no file or a file with no
  /// <c>pack</c>, or an <c>after</c> chain returns to a stage it has passed.</exception>
  public static SetupPack Start(
    SetupFileModel file,
    Func<string, SetupFileModel?> stageOf
  ) {
    SetupPackSpec spec =
      file.Pack
      ?? throw new ArgumentException($"{file.File} has no pack.", nameof(file));
    var pack = new SetupPack(spec);
    foreach (var (code, count) in spec.Gathered)
      pack.Add(code, count);
    foreach (SetupGrant grant in spec.Grants)
      pack.Add(grant.Code, grant.Count);
    foreach (var (id, runs) in spec.After)
      foreach (var (code, count) in EndsOf(id, stageOf).EndStacks)
        pack.Add(code, count * runs);
    Standing(
      spec,
      stageOf,
      new List<string>(),
      new HashSet<string>(),
      pack._standing
    );
    pack._start = pack.Snapshot();
    return pack;
  }

  /// <summary>The stacks and units of <paramref name="code"/> the pack holds, summed over every key
  /// that matches it.</summary>
  /// <param name="code">A stack code, or a metal unit as <c>metal:&lt;code&gt;</c>; a <c>*</c> in
  /// it covers every key the wildcard reaches.</param>
  public int Held(string code) =>
    _held.Where(k => Reaches(k.Key, code)).Sum(k => k.Value);

  /// <summary>The concrete stack codes the pack holds stock of; a key holding a <c>*</c> is left
  /// out.</summary>
  public IEnumerable<string> HeldKeys =>
    _held.Where(k => k.Value > 0 && !k.Key.Contains('*')).Select(k => k.Key);

#if GAME_GE_1_22
  /// <summary>The stack that lays <paramref name="input"/> from what the pack holds: the first
  /// concrete key whose collectible satisfies the ingredient.</summary>
  /// <param name="world">Resolves each held key to an item or block.</param>
  /// <returns>A stack of the ingredient's quantity, or null when no held key satisfies it.</returns>
  public Vintagestory.API.Common.ItemStack? Laid(
    Vintagestory.API.Common.IWorldAccessor world,
    CraftInput input
  )
  {
    foreach (string key in HeldKeys)
    {
      var code = new Vintagestory.API.Common.AssetLocation(key);
      Vintagestory.API.Common.CollectibleObject? item =
        (Vintagestory.API.Common.CollectibleObject?)world.GetItem(code)
        ?? world.GetBlock(code);
      if (item == null)
        continue;
      var stack = new Vintagestory.API.Common.ItemStack(
        item,
        input.Ingredient.Quantity
      );
      if (input.Ingredient.SatisfiesAsIngredient(stack))
        return stack;
    }
    return null;
  }

  /// <summary>The code to draw for a block laid at <paramref name="pos"/>: <paramref name="code"/>
  /// when the pack holds it, otherwise the first block the game drops for <paramref name="code"/>
  /// that the pack holds, which is the item a player holds for a rotated or staged variant.</summary>
  /// <param name="world">Resolves <paramref name="code"/> to its block and asks it for its drops.</param>
  /// <param name="code">The block's code; one holding a <c>*</c> is returned as it is.</param>
  /// <param name="pos">The absolute cell the block is laid at, given to <c>Block.GetDrops</c>.</param>
  /// <returns>The code to draw; <paramref name="code"/> when the pack holds none of the block's
  /// drops, so the draw fails naming the code laid.</returns>
  public string HeldFor(
    Vintagestory.API.Common.IWorldAccessor world,
    string code,
    Vintagestory.API.MathTools.BlockPos pos
  )
  {
    if (code.Contains('*') || Held(code) > 0)
      return code;
    Vintagestory.API.Common.Block? block = world.GetBlock(
      new Vintagestory.API.Common.AssetLocation(code)
    );
    foreach (
      Vintagestory.API.Common.ItemStack drop in block?.GetDrops(
        world,
        pos,
        null
      ) ?? []
    )
      if (
        drop.Collectible is Vintagestory.API.Common.Block dropped
        && Held(dropped.Code.ToString()) > 0
      )
        return dropped.Code.ToString();
    return code;
  }
#endif

  /// <summary>The pack code of a molten cell's metal.</summary>
  /// <param name="metalType">The cell's full metal code, such as <c>game:ingot-iron</c>.</param>
  /// <returns>The metal as <c>metal:&lt;code&gt;</c>, <c>metal:iron</c> for the example.</returns>
  public static string MetalCode(string metalType) =>
    "metal:"
    + new Vintagestory.API.Common.AssetLocation(metalType).Path.Replace(
      "ingot-",
      ""
    );

  /// <summary>Takes <paramref name="count"/> of <paramref name="code"/> out of the pack. A concrete
  /// code takes from its own key first, then from other concrete keys, then from wildcard keys. A
  /// code holding a <c>*</c> takes all of it from one key: the first that holds the whole count, and
  /// the same key for every later draw of that code under the same <paramref name="pin"/>.</summary>
  /// <param name="code">A stack code, or a metal unit as <c>metal:&lt;code&gt;</c>.</param>
  /// <param name="count">The stacks or units taken, at least 1.</param>
  /// <param name="where">The step that takes them, kept in the draw log.</param>
  /// <param name="pin">The construction or structure a wildcard draw belongs to, as its position;
  /// null for none.</param>
  /// <exception cref="InvalidOperationException">The pack holds fewer than <paramref name="count"/>,
  /// or a wildcard code has no one key holding it; the message names the code and both counts, and
  /// nothing is taken.</exception>
  public void Draw(
    string code,
    int count,
    string where = "",
    string? pin = null
  ) {
    List<string> keys = code.Contains('*')
      ? [Single(code, count, pin)]
      : Reached(code);
    int held = keys.Sum(k => _held[k]);
    if (held < count)
      throw new InvalidOperationException(
        $"the pack holds {held} of {code}; the step takes {count}."
      );
    int left = count;
    foreach (string key in keys) {
      int take = Math.Min(left, _held[key]);
      _held[key] -= take;
      left -= take;
    }
    _draws.Add((where, code, count));
  }

  /// <summary>What the run did with the pack, for the recording.</summary>
  /// <param name="standing">The codes of the blocks standing at the end of the run, one per
  /// block.</param>
  public SetupPackRecord Record(IEnumerable<string> standing) =>
    new(
      _start,
      Snapshot(),
      standing
        .GroupBy(c => c, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.Count()),
      [.. _draws]
    );

  /// <summary>Adds <paramref name="count"/> of <paramref name="code"/> to the pack: a stack or metal
  /// the run made or took back.</summary>
  /// <param name="code">A stack code, or a metal unit as <c>metal:&lt;code&gt;</c>.</param>
  /// <param name="count">The stacks or units handed on, at least 1.</param>
  public void Give(string code, int count) => Add(code, count);

  /// <summary>Takes the first standing block that matches <paramref name="code"/> off the list.</summary>
  /// <param name="code">The block's code.</param>
  /// <param name="cell">The cell the block is placed at, or null when it is not a principal whose
  /// structure and construction stages the run may raise; a taken block is recorded against it for
  /// <see cref="TookStanding"/>.</param>
  /// <returns>True when a standing block was taken, so the place draws nothing; false when none
  /// matches.</returns>
  public bool TakeStanding(string code, BlockPos? cell = null) {
    int index = _standing.FindIndex(p => Matches(p, code));
    if (index < 0)
      return false;
    _standing.RemoveAt(index);
    if (cell != null)
      _carried.Add((cell.X, cell.Y, cell.Z));
    return true;
  }

  /// <summary>Whether the block at <paramref name="cell"/> was taken free from the standing list, so
  /// raising its structure and paying its construction stages draws nothing.</summary>
  /// <param name="cell">The cell <see cref="TakeStanding"/> was given for the block, in the same
  /// coordinates.</param>
  /// <returns>True once that cell has been taken; false for a block built from the pack, and for
  /// any other cell of the same code.</returns>
  public bool TookStanding(BlockPos cell) =>
    _carried.Contains((cell.X, cell.Y, cell.Z));

  /// <summary>Checks that the pack holds every stack and metal unit its <c>ends</c> declares, at
  /// least the declared count of each.</summary>
  /// <exception cref="InvalidOperationException">A declared stack or unit is held short; the
  /// message names the code and both counts.</exception>
  public void CheckEnds() {
    foreach (var (code, count) in _spec.EndStacks) {
      int held = Held(code);
      if (held < count)
        throw new InvalidOperationException(
          $"the stage ends with {held} of {code}; it declares {count}."
        );
    }
  }

  /// <summary>Checks that a block matching each <c>ends.standing</c> code stands.</summary>
  /// <param name="standing">The codes of the blocks standing at the end of the run.</param>
  /// <exception cref="InvalidOperationException">A declared code matches no standing block; the
  /// message names it.</exception>
  public void CheckStanding(IReadOnlyCollection<string> standing) {
    foreach (string code in _spec.EndStanding)
      if (!standing.Any(s => Matches(code, s)))
        throw new InvalidOperationException(
          $"the stage ends with no {code} standing."
        );
  }

  /// <summary>Whether placed blocks are drawn from the pack: true under <c>scope: "all"</c>, false
  /// under <c>"stacks"</c>.</summary>
  public bool DrawsBlocks => _spec.Scope == "all";

  private Dictionary<string, int> Snapshot() =>
    _held.Where(k => k.Value > 0).ToDictionary(k => k.Key, k => k.Value);

  private void Add(string code, int count) =>
    _held[code] = _held.GetValueOrDefault(code) + count;

  private static bool Reaches(string key, string code) =>
    Matches(key, code) || (code.Contains('*') && Matches(code, key));

  private List<string> Reached(string code) =>
    _held
      .Where(k => k.Value > 0 && Reaches(k.Key, code))
      .Select(k => k.Key)
      .OrderBy(k => k == code ? 0 : (k.Contains('*') ? 2 : 1))
      .ToList();

  private string Single(string code, int count, string? pin) {
    if (pin != null && _pins.TryGetValue((pin, code), out string? pinned))
      return pinned;
    string? key = Reached(code).FirstOrDefault(k => _held[k] >= count);
    if (key == null) {
      int total = Held(code);
      throw new InvalidOperationException(
        total < count
          ? $"the pack holds {total} of {code}; the step takes {count}."
          : $"the pack holds {total} of {code} under no one key; the step takes {count}."
      );
    }
    if (pin != null)
      _pins[(pin, code)] = key;
    return key;
  }

  // A key without a domain is a * glob over the whole code; one with a domain reads as the game reads
  // a wildcard AssetLocation: a * glob, an @ regex path, a * domain.
  private static bool Matches(string key, string code) =>
    key == code
    || (
      key.Contains(':')
        ? WildcardUtil.Match(
          new Vintagestory.API.Common.AssetLocation(key),
          new Vintagestory.API.Common.AssetLocation(code)
        )
        : Regex.IsMatch(
          code,
          "^" + Regex.Escape(key).Replace("\\*", ".*") + "$"
        )
    );

  private static void Standing(
    SetupPackSpec spec,
    Func<string, SetupFileModel?> stageOf,
    List<string> path,
    HashSet<string> visited,
    List<string> into
  ) {
    foreach (var (id, _) in spec.After) {
      if (path.Contains(id))
        throw new InvalidOperationException($"after cycles through \"{id}\".");
      if (!visited.Add(id))
        continue;
      SetupPackSpec stage = EndsOf(id, stageOf);
      into.AddRange(stage.EndStanding);
      path.Add(id);
      Standing(stage, stageOf, path, visited, into);
      path.RemoveAt(path.Count - 1);
    }
  }

  private static SetupPackSpec EndsOf(
    string id,
    Func<string, SetupFileModel?> stageOf
  ) {
    SetupFileModel stage =
      stageOf(id)
      ?? throw new InvalidOperationException(
        $"after names \"{id}\", which no file has."
      );
    return stage.Pack
      ?? throw new InvalidOperationException(
        $"after names \"{id}\", whose file has no pack."
      );
  }
}
