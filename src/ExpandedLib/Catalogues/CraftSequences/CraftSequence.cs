using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Catalogues;

/// <summary>One gesture of a <see cref="CraftSequence"/>: a tool held for a time while a hand
/// animation plays.</summary>
/// <param name="Tool">Item code the active-hand stack must match, wildcards allowed; null for bare hands.</param>
/// <param name="Offhand">Item code the off-hand stack must match; null when the off hand is not checked.</param>
/// <param name="Seconds">How long the step is held. Above 0.</param>
/// <param name="Animation">Hand animation to play; null for the tool's own.</param>
/// <param name="Help">Lang key of the interaction-help line shown beside the step's tool; null for none.</param>
/// <param name="At">Where the step is worked: <see cref="CraftStep.Vice"/>, which waits while the vice is
/// open, or null for the top.</param>
public sealed record CraftStep(
  string? Tool,
  string? Offhand,
  float Seconds,
  string? Animation,
  string? Help,
  string? At
) {
  /// <summary>What a step takes when it declares no time of its own.</summary>
  public const float DefaultSeconds = 1f;

  /// <summary>The <see cref="At"/> value of a step worked in the face vice.</summary>
  public const string Vice = "vice";
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
public sealed record CraftSequenceSet(
  int Schema,
  string Machine,
  CraftSequence[] Sequences
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
        float seconds = stepNode["seconds"].AsFloat(CraftStep.DefaultSeconds);
        if (seconds <= 0f) {
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
            at
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

    set = new CraftSequenceSet(schema, machine, [.. sequences]);
    return true;
  }

  private static string? Blank(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value;
}
