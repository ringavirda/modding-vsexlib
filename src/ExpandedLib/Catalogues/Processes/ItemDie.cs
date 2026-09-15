using System.Collections.Generic;
using ExpandedLib.Definitions;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Catalogues;

/// <summary>
/// A die: the swappable tooling a heading, nail or rivet bench works with, carrying the job it
/// does in a <c>machinejob</c> attribute. Recognised by carrying a job that parses, never by its
/// code.
/// </summary>
public static class ItemDie {
  /// <summary>The attribute key a die carries its job under.</summary>
  public const string AttributeKey = "machinejob";

  /// <summary>Parses a die's <c>machinejob</c> attribute, by the same rules a declared job table meets.</summary>
  public static bool TryParse(
    JsonObject? node,
    out ProcessJobSet? set,
    out string? error
  ) {
    set = null;
    if (node is not { Exists: true }) {
      error = $"missing '{AttributeKey}' attribute";
      return false;
    }
    return ProcessJobSet.TryParse(node, out set, out error);
  }

  /// <summary>Whether <paramref name="stack"/> is a die, i.e. carries a job that parses.</summary>
  public static bool IsDie(ItemStack? stack) =>
    TryParse(stack?.Collectible?.Attributes?[AttributeKey], out _, out _);

  /// <summary>The job the die fitted as <paramref name="stack"/> has for a piece of
  /// <paramref name="input"/> at <paramref name="stage"/> on <paramref name="family"/>, or null.</summary>
  public static ProcessJob? JobFor(
    ItemStack? stack,
    string? input,
    float? stage,
    string? family
  ) {
    if (
      input == null
      || !TryParse(
        stack?.Collectible?.Attributes?[AttributeKey],
        out ProcessJobSet? set,
        out _
      )
    )
      return null;

    foreach (ProcessJob job in set!.Jobs)
      if (job.Matches(input, stage, family))
        return job;
    return null;
  }

  /// <summary>One die's job, as the nested attribute a variant carries.</summary>
  public static object Job(
    string machine,
    string input,
    string output,
    int count = 1,
    int minTier = 0,
    double minTorque = 0,
    double seconds = ProcessJob.DefaultSeconds
  ) =>
    new {
      machinejob = new {
        schema = ProcessJobSet.CurrentSchema,
        machine,
        jobs = new[]
        {
          new
          {
            input,
            output,
            count,
            minTier,
            minTorque,
            seconds,
          },
        },
      },
    };

  /// <summary>Builds a mod's whole <c>die</c> itemtype from its own job table.</summary>
  /// <param name="domain">The mod's domain; its dies land there.</param>
  /// <param name="jobs">Variant name to its spec, from <see cref="Job"/> or hand-built.</param>
  /// <param name="shape">Shape every variant renders as, unless <paramref name="shapeByType"/> overrides it.</param>
  /// <param name="shapeByType">Per-variant shape override, keyed by the same variant names.</param>
  /// <param name="assetName">The itemtype's asset path under <c>itemtypes/</c>, sub-folders allowed;
  /// <c>die</c> when null.</param>
  public static ExItemDef Itemtype(
    string domain,
    IReadOnlyDictionary<string, object> jobs,
    string shape = "game:item/ingot",
    IReadOnlyDictionary<string, string>? shapeByType = null,
    string? assetName = null
  ) {
    var byType = new Dictionary<string, object>();
    foreach ((string type, object job) in jobs)
      byType["*-" + type] = job;

    ExItemDef def = ExItemDef
      .Create(domain, "die", assetName ?? "die")
      .Shape(shape)
      .VariantGroup("type", [.. jobs.Keys])
      // Each die carries its own wear.
      .MaxStackSize(1)
      .RootKey("attributesByType", byType)
      .CreativeCommon("*");

    if (shapeByType is { Count: > 0 }) {
      var shapes = new Dictionary<string, object>();
      foreach ((string type, string path) in shapeByType)
        shapes["*-" + type] = new { @base = path };
      def = def.RootKey("shapeByType", shapes);
    }
    return def;
  }
}
