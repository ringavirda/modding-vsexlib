using System.Collections.Generic;
using ExpandedLib.Catalogues;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.Machining;

/// <summary>The jobs one catalogue file or one C# contribution declares.</summary>
/// <param name="Schema">Schema version of the declaration.</param>
/// <param name="Jobs">The declared jobs, in order.</param>
public sealed record MachiningJobSet(
  int Schema,
  IReadOnlyList<MachiningJob> Jobs
) {
  /// <summary>The schema this parser writes and reads up to.</summary>
  public const int CurrentSchema = SpecSchema.First;

  /// <summary>Parses one <c>config/machiningjobs</c> file. Returns false with a human-readable
  /// <paramref name="error"/> naming the malformed field; <paramref name="set"/> is then null.</summary>
  internal static bool TryParse(
    JsonObject? node,
    out MachiningJobSet? set,
    out string? error
  ) {
    set = null;
    if (node is not { Exists: true }) {
      error = "missing machining job declaration";
      return false;
    }
    if (!SpecSchema.TryRead(node, CurrentSchema, out int schema, out error))
      return false;

    var jobs = new List<MachiningJob>();
    foreach (JsonObject jobNode in node["jobs"].AsArray() ?? []) {
      string input = jobNode["input"].AsString("");
      if (string.IsNullOrWhiteSpace(input)) {
        error = "a machining job names no 'input'";
        return false;
      }
      string step = jobNode["step"].AsString("");
      if (string.IsNullOrWhiteSpace(step)) {
        error = $"the job on '{input}' names no 'step'";
        return false;
      }
      string output = jobNode["output"].AsString("");
      if (string.IsNullOrWhiteSpace(output)) {
        error = $"{step}: the job on '{input}' names no 'output'";
        return false;
      }

      int count = jobNode["count"].AsInt(1);
      if (count < 1) {
        error =
          $"{step}: the job on '{input}' yields a 'count' of {count}, which would destroy the piece";
        return false;
      }
      int wear = jobNode["wear"].AsInt(MachiningJob.DefaultWear);
      if (wear < 0) {
        error = $"{step}: the job on '{input}' declares a 'wear' of {wear}";
        return false;
      }
      int minTier = jobNode["minTier"].AsInt(0);
      if (minTier < 0) {
        error =
          $"{step}: the job on '{input}' declares a 'minTier' of {minTier}";
        return false;
      }

      float? removed = Positive(
        jobNode["removed"],
        "removed",
        step,
        input,
        out error
      );
      if (error != null)
        return false;
      float? energy = Positive(
        jobNode["energy"],
        "energy",
        step,
        input,
        out error
      );
      if (error != null)
        return false;

      var byproducts = new List<MachiningByproduct>();
      foreach (JsonObject byNode in jobNode["byproducts"].AsArray() ?? []) {
        string byCode = byNode["code"].AsString("");
        int byCount = byNode["count"].AsInt(1);
        if (string.IsNullOrWhiteSpace(byCode) || byCount < 1) {
          error =
            $"{step}: a byproduct of the job on '{input}' needs a 'code' and a 'count' of at least 1";
          return false;
        }
        byproducts.Add(new MachiningByproduct(byCode, byCount));
      }

      MachiningHand? hand = null;
      if (jobNode["hand"].Exists) {
        string machine = jobNode["hand"]["machine"].AsString("");
        string recipe = jobNode["hand"]["recipe"].AsString("");
        if (
          string.IsNullOrWhiteSpace(machine)
          || string.IsNullOrWhiteSpace(recipe)
        ) {
          error =
            $"{step}: the 'hand' of the job on '{input}' needs a 'machine' and a 'recipe'";
          return false;
        }
        hand = new MachiningHand(machine, recipe);
      }

      var machines = new List<string>();
      foreach (JsonObject machineNode in jobNode["machines"].AsArray() ?? [])
        machines.Add(machineNode.AsString(""));

      jobs.Add(
        new MachiningJob(
          step,
          input,
          output,
          count,
          Blank(jobNode["material"].AsString("")),
          removed,
          energy,
          minTier,
          wear,
          Blank(jobNode["fixture"].AsString("")),
          machines,
          byproducts,
          hand
        )
      );
    }

    error = null;
    set = new MachiningJobSet(schema, jobs);
    return true;
  }

  private static float? Positive(
    JsonObject node,
    string key,
    string step,
    string input,
    out string? error
  ) {
    error = null;
    if (!node.Exists)
      return null;
    float value = node.AsFloat(0f);
    if (value <= 0f)
      error =
        $"{step}: the job on '{input}' declares '{key}' of {value}, which is not above 0";
    return value;
  }

  private static string? Blank(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value;
}
