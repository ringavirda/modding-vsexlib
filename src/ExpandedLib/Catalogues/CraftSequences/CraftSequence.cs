using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Catalogues;

/// <summary>One gesture of a <see cref="CraftSequence"/>: a tool held for a time while a hand
/// animation plays.</summary>
/// <param name="Tool">Item code the active-hand stack must match, wildcards allowed; null for bare hands.</param>
/// <param name="Offhand">Item code the off-hand stack must match; null when the off hand is not checked.</param>
/// <param name="Seconds">How long the step is held. Above 0 for a timed step; 0 for a step counted in
/// <paramref name="Hits"/>.</param>
/// <param name="Animation">Entity animation code played on the holder at each stroke; null for the held
/// tool's <see cref="CraftToolDefault"/>, and none when it has none.</param>
/// <param name="Help">Lang key of the interaction-help line shown beside the step's tool; null for none.</param>
/// <param name="At">Where the step is worked: <see cref="CraftStep.Vice"/>, which waits while the vice is
/// open, or null for the top.</param>
/// <param name="Sound">Asset path of the sound played at each stroke; null for the held tool's
/// <see cref="CraftToolDefault"/>, and none when it has none.</param>
/// <param name="Hits">Finished tool swings the step takes; 0 for a timed step. A hit counts at the end of
/// a swing, and the hits counted survive the player letting go.</param>
public sealed record CraftStep(
  string? Tool,
  string? Offhand,
  float Seconds,
  string? Animation,
  string? Help,
  string? At,
  string? Sound = null,
  int Hits = 0
) {
  /// <summary>What a step takes when it declares no time of its own.</summary>
  public const float DefaultSeconds = 1f;

  /// <summary>The <see cref="At"/> value of a step worked in the face vice.</summary>
  public const string Vice = "vice";

  /// <summary>How long holding the step takes.</summary>
  /// <param name="stroke">Seconds of one swing of the held tool; above 0.</param>
  /// <returns><see cref="Seconds"/> for a timed step, <see cref="Hits"/> swings of
  /// <paramref name="stroke"/> for a counted one.</returns>
  public float Duration(float stroke) => Hits > 0 ? Hits * stroke : Seconds;
}

/// <summary>What a held tool does at each stroke of a step that names no animation or sound of its own.</summary>
/// <param name="Tool">Item code the held stack must match, wildcards allowed.</param>
/// <param name="Animation">Entity animation code started on the holder at each stroke; null for none.</param>
/// <param name="Sound">Asset path of the sound played at each stroke; null for none.</param>
/// <param name="Stroke">Seconds from one stroke to the next. Above 0.</param>
/// <param name="Strike">Seconds into a stroke at which the sound plays. From 0 up to <paramref name="Stroke"/>.</param>
public sealed record CraftToolDefault(
  string Tool,
  string? Animation,
  string? Sound,
  float Stroke = CraftToolDefault.DefaultStroke,
  float Strike = CraftToolDefault.DefaultStrike
) {
  /// <summary>The stroke of vanilla's anvil swing: its 30 frames at 1.33 times speed end at 0.73 s and
  /// the clip stays active through its ease-out, and the engine drops a start while the clip is
  /// active.</summary>
  public const float DefaultStroke = 1.05f;

  /// <summary>The strike of vanilla's anvil hit: soundAtFrame 16 over the 1.33 speed, frame 12 of 30, as
  /// ItemHammer reads it.</summary>
  public const float DefaultStrike = 0.3f;

  /// <summary>The hammer's default, vanilla's anvil swing and hit: the default of every registry before
  /// any declaration.</summary>
  public static CraftToolDefault Hammer { get; } =
    new("game:hammer-*", "hammerhit", "sounds/effect/anvilhit");
}

/// <summary>A stack a craft sheds beside its product.</summary>
/// <param name="Type"><c>item</c> or <c>block</c>.</param>
/// <param name="Code">Collectible code.</param>
/// <param name="Quantity">How many. At least one.</param>
public sealed record CraftByproduct(string Type, string Code, int Quantity);

/// <summary>The labour for one grid recipe: an ordered list of steps.</summary>
/// <param name="Recipe">The grid recipe's asset name, the key the sequence is filed under.</param>
/// <param name="Steps">The gestures, in order. At least one.</param>
/// <param name="Byproducts">Stacks shed beside the product; empty for none.</param>
public sealed record CraftSequence(
  string Recipe,
  CraftStep[] Steps,
  CraftByproduct[] Byproducts
);

/// <summary>Every sequence one machine works, as a mod declares them. Merged into
/// <see cref="CraftSequenceRegistry"/>.</summary>
/// <param name="Schema">Schema version of the declaration.</param>
/// <param name="Machine">The machine these sequences belong to, e.g. <c>workbench</c>.</param>
/// <param name="Sequences">The sequences, in declaration order.</param>
/// <param name="Tools">The per-tool stroke defaults the file declares; null for none.</param>
public sealed record CraftSequenceSet(
  int Schema,
  string Machine,
  CraftSequence[] Sequences,
  CraftToolDefault[]? Tools = null
) {
  /// <summary>The schema this parser writes and reads up to.</summary>
  public const int CurrentSchema = SpecSchema.First;

  /// <summary>Parses and validates one sequence-set declaration. Returns false with a human-readable
  /// <paramref name="error"/> on any malformed field.</summary>
  public static bool TryParse(
    JsonObject? node,
    out CraftSequenceSet? set,
    out string? error
  ) {
    set = null;
    error = null;

    if (node is not { Exists: true }) {
      error = "missing sequence-set declaration";
      return false;
    }
    if (!SpecSchema.TryRead(node, CurrentSchema, out int schema, out error))
      return false;

    string machine = node["machine"].AsString("");
    if (string.IsNullOrWhiteSpace(machine)) {
      error =
        "missing 'machine' (the key the registry files these sequences under)";
      return false;
    }

    var sequences = new List<CraftSequence>();
    foreach (JsonObject sequenceNode in node["sequences"].AsArray() ?? []) {
      string recipe = sequenceNode["recipe"].AsString("");
      if (string.IsNullOrWhiteSpace(recipe)) {
        error = $"{machine}: a sequence names no 'recipe'";
        return false;
      }

      var steps = new List<CraftStep>();
      foreach (JsonObject stepNode in sequenceNode["steps"].AsArray() ?? []) {
        bool timed = stepNode["seconds"].Exists;
        bool counted = stepNode["hits"].Exists;
        if (timed && counted) {
          error =
            $"{machine}: a step of '{recipe}' names both 'seconds' and 'hits'";
          return false;
        }
        int hits = counted ? stepNode["hits"].AsInt(0) : 0;
        if (counted && hits < 1) {
          error =
            $"{machine}: a step of '{recipe}' takes {hits} hits, which is not a count";
          return false;
        }
        float seconds = counted
          ? 0f
          : stepNode["seconds"].AsFloat(CraftStep.DefaultSeconds);
        if (!counted && seconds <= 0f) {
          error =
            $"{machine}: a step of '{recipe}' holds for {seconds} seconds, which is not a time";
          return false;
        }
        string? at = Blank(stepNode["at"].AsString(""));
        if (at != null && at != CraftStep.Vice) {
          error =
            $"{machine}: a step of '{recipe}' works at '{at}', not '{CraftStep.Vice}'";
          return false;
        }
        steps.Add(
          new CraftStep(
            Blank(stepNode["tool"].AsString("")),
            Blank(stepNode["offhand"].AsString("")),
            seconds,
            Blank(stepNode["animation"].AsString("")),
            Blank(stepNode["help"].AsString("")),
            at,
            Blank(stepNode["sound"].AsString("")),
            hits
          )
        );
      }
      if (steps.Count == 0) {
        error = $"{machine}: the sequence of '{recipe}' has no steps";
        return false;
      }

      var byproducts = new List<CraftByproduct>();
      foreach (JsonObject byNode in sequenceNode["byproducts"].AsArray() ?? []) {
        string code = byNode["code"].AsString("");
        if (string.IsNullOrWhiteSpace(code)) {
          error = $"{machine}: a byproduct of '{recipe}' names no 'code'";
          return false;
        }
        int quantity = byNode["quantity"].AsInt(1);
        if (quantity < 1) {
          error =
            $"{machine}: the byproduct '{code}' of '{recipe}' has a 'quantity' of {quantity}";
          return false;
        }
        byproducts.Add(
          new CraftByproduct(byNode["type"].AsString("item"), code, quantity)
        );
      }

      sequences.Add(new CraftSequence(recipe, [.. steps], [.. byproducts]));
    }

    var tools = new List<CraftToolDefault>();
    foreach (JsonObject toolNode in node["tools"].AsArray() ?? []) {
      string tool = toolNode["tool"].AsString("");
      if (string.IsNullOrWhiteSpace(tool)) {
        error = $"{machine}: a tool default names no 'tool'";
        return false;
      }
      string? animation = Blank(toolNode["animation"].AsString(""));
      string? sound = Blank(toolNode["sound"].AsString(""));
      if (animation == null && sound == null) {
        error =
          $"{machine}: the tool default of '{tool}' names no animation or sound";
        return false;
      }
      float stroke = toolNode["stroke"].AsFloat(CraftToolDefault.DefaultStroke);
      float strike = toolNode["strike"].AsFloat(CraftToolDefault.DefaultStrike);
      if (stroke <= 0f || strike < 0f || strike >= stroke) {
        error =
          $"{machine}: the tool default of '{tool}' strikes at {strike} seconds of a {stroke} second stroke";
        return false;
      }
      tools.Add(new CraftToolDefault(tool, animation, sound, stroke, strike));
    }

    set = new CraftSequenceSet(schema, machine, [.. sequences], [.. tools]);
    return true;
  }

  private static string? Blank(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value;
}
