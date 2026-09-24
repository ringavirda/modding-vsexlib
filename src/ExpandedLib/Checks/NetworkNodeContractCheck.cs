using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Networks;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Checks;

/// <summary>The rules a definition obeys to join a network graph: a node declares a <c>type</c>
/// state and the scheme it ships, and a membership names its network. A node's <c>class</c>
/// resolves to a <see cref="BlockNetworkNode"/> or it declares <c>ExOrientable</c> in
/// <c>network</c> mode; a membership is <c>BEBehaviorNetworkMember</c> or a subclass.</summary>
public static class NetworkNodeContractCheck {
  private const string MembershipKey = nameof(BEBehaviorNetworkMember);

  /// <summary>Every contract violation among <paramref name="domain"/>'s network-node and membership defs, as the check's <see cref="CheckResult"/>.</summary>
  public static CheckResult Run(ICheckSource source, string domain) =>
    new(
      "NetworkNodeContract",
      domain,
      [
        .. TypeGroupViolations(source, domain),
        .. SchemeViolations(source, domain, out _),
        .. MembershipViolations(source, domain),
      ]
    );

  // A node with no `type` states gets an empty OrientationMap entry; TryPlaceBlock then refuses.
  internal static IEnumerable<string> TypeGroupViolations(
    ICheckSource source,
    string domain
  ) {
    foreach (ExBlockDef def in source.BlockDefinitions(domain)) {
      JObject json = (JObject)def.ToJson();
      if (!IsNode(source, json) || def.VariantStates("type").Length > 0)
        continue;

      yield return $"{Named(source, domain, def, json)} declares NO `type` variant group - "
        + "OrientationMap has nothing to contribute, so AllowedOrientations is empty, "
        + "ComputeValidOrientations returns [] and TryPlaceBlock refuses. The block can never be "
        + "placed, silently.";
    }
  }

  // nodesChecked counts the nodes examined. A misspelled scheme falls back to ExOrientations.Axis.
  internal static IReadOnlyList<string> SchemeViolations(
    ICheckSource source,
    string domain,
    out int nodesChecked
  ) {
    nodesChecked = 0;
    var problems = new List<string>();
    foreach (ExBlockDef def in source.BlockDefinitions(domain)) {
      JObject json = (JObject)def.ToJson();
      if (!IsNode(source, json))
        continue;

      nodesChecked++;
      if (SchemeViolation(domain, def, json) is { } problem)
        problems.Add(problem);
    }
    return problems;
  }

  private static string? SchemeViolation(
    string domain,
    ExBlockDef def,
    JObject json
  ) {
    string where = $"{def.Location} ({domain}:{def.Code})";

    JObject[] declared = [.. OrientableDeclarations(json)];
    if (declared.Length != 1)
      return $"{where} declares {declared.Length} `ExOrientable` behaviour(s), not 1 - a network "
        + "node takes its orientation from its neighbours, so it needs exactly one to write the "
        + "`orientation` variant through.";

    JToken? properties = declared[0]["properties"];
    string? mode = (string?)properties?["mode"];
    if (mode != "network")
      return $"{where} declares `ExOrientable` with mode '{mode ?? "<absent>"}', not 'network' - "
        + "IsNetworkOriented stays false, so the behaviour writes the `side` variant this block "
        + "does not have and every orientation swap resolves to no block, silently.";

    string[] states = def.VariantStates("orientation");
    string? actual = ExOrientations.Resolve(states)?.Name;
    if (actual == null)
      return $"{where} declares orientation states [{string.Join(",", states)}], which set-equal "
        + "no scheme in ExOrientations.All - declare the scheme there rather than naming a near "
        + "match, whose rotation fallback would map onto a token this block does not have.";

    string? scheme = (string?)properties?["scheme"];
    if (scheme != actual)
      return $"{where} names scheme '{scheme ?? "<absent>"}' but its `orientation` states are "
        + $"{actual}'s - an unresolved name falls back to Axis, which rejects every token "
        + "outside [ns,we,ud] and stops the node re-orienting with no exception and no log line.";

    return null;
  }

  // A membership with no `networkType` logs an error and joins no graph.
  internal static IEnumerable<string> MembershipViolations(
    ICheckSource source,
    string domain
  ) {
    foreach (ExBlockDef def in source.BlockDefinitions(domain)) {
      JObject json = (JObject)def.ToJson();
      foreach (
        (string where, JObject declaration) in MembershipDeclarations(
          source,
          json
        )
      ) {
        string? networkType = (string?)
          declaration["properties"]?["networkType"];
        if (!string.IsNullOrEmpty(networkType))
          continue;

        yield return $"{Named(source, domain, def, json)} declares a network membership in "
          + $"{where} with no `networkType` - the behaviour has no other source for one, so the "
          + "cell logs an error and joins no graph. Give the declaration a networkType, or drop "
          + "it.";
      }
    }
  }

  // The def's code, followed by its block class's name when the source resolves one.
  private static string Named(
    ICheckSource source,
    string domain,
    ExBlockDef def,
    JObject json
  ) =>
    ClassOf(source, json) is { } type
      ? $"{domain}:{def.Code} ({type.Name})"
      : $"{domain}:{def.Code}";

  private static Type? ClassOf(ICheckSource source, JObject json) =>
    (string?)json["class"] is { } key ? source.BlockClass(key) : null;

  private static bool IsNode(ICheckSource source, JObject json) =>
    typeof(BlockNetworkNode).IsAssignableFrom(ClassOf(source, json))
    || OrientableDeclarations(json)
      .Any(b => (string?)b["properties"]?["mode"] == "network");

  private static IEnumerable<JObject> OrientableDeclarations(JObject json) =>
    ArrayAt(json["behaviors"])
      .OfType<JObject>()
      .Where(b => (string?)b["name"] == "ExOrientable");

  private static bool IsMembership(ICheckSource source, string? key) =>
    BareKey(key) == MembershipKey
    || key != null
      && typeof(BEBehaviorNetworkMember).IsAssignableFrom(
        source.BlockEntityBehaviorClass(key)
      );

  private static IEnumerable<(
    string Where,
    JObject Declaration
  )> MembershipDeclarations(ICheckSource source, JObject json) {
    foreach (JObject beh in ArrayAt(json["entityBehaviors"]).OfType<JObject>())
      if (IsMembership(source, (string?)beh["name"]))
        yield return ("entityBehaviors", beh);

    foreach (JToken table in FillerTables(json))
      foreach (JObject cell in ArrayAt(table).OfType<JObject>()) {
        string at = $"fillerOffsets cell ({cell["x"]},{cell["y"]},{cell["z"]})";
        foreach (JObject beh in ArrayAt(cell["behaviors"]).OfType<JObject>())
          if (IsMembership(source, (string?)beh["code"]))
            yield return (at, beh);
      }
  }

  private static IEnumerable<JToken> FillerTables(JObject json) {
    if (json["attributes"]?["fillerOffsets"] is JToken shared)
      yield return shared;

    if (json["attributesByType"] is JObject byType)
      foreach (JProperty variant in byType.Properties())
        if (variant.Value["fillerOffsets"] is JToken perType)
          yield return perType;
  }

  // The part of a declared behaviour key after its mod-id prefix.
  private static string BareKey(string? code) {
    string key = code ?? "";
    int dot = key.LastIndexOf('.');
    return dot < 0 ? key : key[(dot + 1)..];
  }

  private static IEnumerable<JToken> ArrayAt(JToken? token) =>
    token as JArray ?? Enumerable.Empty<JToken>();
}
