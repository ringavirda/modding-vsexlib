using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ExpandedLib.Scenarios;

/// <summary>
/// The stacks, metal units and standing blocks a setup's <c>pack</c> holds (setups.md, The pack).
/// Codes are read as keys, a key holding a <c>*</c> matches every code the wildcard covers.
/// </summary>
internal sealed class SetupPack {
  private readonly SetupPackSpec _spec;
  private readonly Dictionary<string, int> _held = [];
  private readonly List<string> _standing = [];

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
    Standing(spec, stageOf, new List<string>(), pack._standing);
    return pack;
  }

  /// <summary>The stacks and units of <paramref name="code"/> the pack holds, summed over every key
  /// that matches it.</summary>
  /// <param name="code">A stack code, or a metal unit as <c>metal:&lt;code&gt;</c>.</param>
  public int Held(string code) =>
    _held.Where(k => Matches(k.Key, code)).Sum(k => k.Value);

  /// <summary>The pack code of a molten cell's metal.</summary>
  /// <param name="metalType">The cell's full metal code, such as <c>game:ingot-iron</c>.</param>
  /// <returns>The metal as <c>metal:&lt;code&gt;</c>, <c>metal:iron</c> for the example.</returns>
  public static string MetalCode(string metalType) =>
    "metal:"
    + new Vintagestory.API.Common.AssetLocation(metalType).Path.Replace(
      "ingot-",
      ""
    );

  /// <summary>Takes <paramref name="count"/> of <paramref name="code"/> out of the pack.</summary>
  /// <param name="code">A stack code, or a metal unit as <c>metal:&lt;code&gt;</c>.</param>
  /// <param name="count">The stacks or units taken, at least 1.</param>
  /// <exception cref="InvalidOperationException">The pack holds fewer than <paramref name="count"/>;
  /// the message names the code and both counts, and nothing is taken.</exception>
  public void Draw(string code, int count) {
    int held = Held(code);
    if (held < count)
      throw new InvalidOperationException(
        $"the pack holds {held} of {code}; the step takes {count}."
      );
    int left = count;
    foreach (string key in _held.Keys.Where(k => Matches(k, code)).ToList()) {
      int take = Math.Min(left, _held[key]);
      _held[key] -= take;
      left -= take;
    }
  }

  /// <summary>Adds <paramref name="count"/> of <paramref name="code"/> to the pack: a stack or metal
  /// the run made or took back.</summary>
  /// <param name="code">A stack code, or a metal unit as <c>metal:&lt;code&gt;</c>.</param>
  /// <param name="count">The stacks or units handed on, at least 1.</param>
  public void Give(string code, int count) => Add(code, count);

  /// <summary>Takes the first standing block that matches <paramref name="code"/> off the list.</summary>
  /// <param name="code">The block's code.</param>
  /// <returns>True when a standing block was taken, so the place draws nothing; false when none
  /// matches.</returns>
  public bool TakeStanding(string code) {
    int index = _standing.FindIndex(p => Matches(p, code));
    if (index < 0)
      return false;
    _standing.RemoveAt(index);
    return true;
  }

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

  private void Add(string code, int count) =>
    _held[code] = _held.GetValueOrDefault(code) + count;

  // A * in a key stands for any run of characters.
  private static bool Matches(string key, string code) =>
    key == code
    || Regex.IsMatch(code, "^" + Regex.Escape(key).Replace("\\*", ".*") + "$");

  private static void Standing(
    SetupPackSpec spec,
    Func<string, SetupFileModel?> stageOf,
    List<string> path,
    List<string> into
  ) {
    foreach (var (id, _) in spec.After) {
      if (path.Contains(id))
        throw new InvalidOperationException($"after cycles through \"{id}\".");
      SetupPackSpec stage = EndsOf(id, stageOf);
      into.AddRange(stage.EndStanding);
      path.Add(id);
      Standing(stage, stageOf, path, into);
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
