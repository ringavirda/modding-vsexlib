using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Definitions;
using ExpandedLib.Helpers;
using ExpandedLib.Networks;
using ExpandedLib.Registries;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Testing;

/// <summary>The two structural rules a definition must obey to join a network graph: a
/// <see cref="BlockNetworkNode"/> def must declare a <c>type</c> variant state, and a declared
/// <c>BEBehaviorNetworkMember</c> must name the network it joins.</summary>
public static class NetworkNodeContract {
  /// <summary>Every violation of either rule in <paramref name="asm"/>, as a human-readable line.</summary>
  public static IReadOnlyList<string> Violations(string domain, Assembly asm) =>
    [.. TypeGroupViolations(domain, asm), .. MembershipViolations(domain, asm)];

  /// <summary>Every network-node def in <paramref name="asm"/> whose <c>ExOrientable</c> declaration
  /// does not agree with the states it actually ships, reporting how many defs were examined.</summary>
  public static IReadOnlyList<string> SchemeViolations(
    string domain,
    Assembly asm,
    out int defsChecked
  ) {
    var problems = new List<string>();
    IReadOnlySet<string> nodeClasses = NetworkNodeClassKeys(domain, asm);
    int examined = 0;

    foreach (IExDef any in DefinitionGoldens.Collect(domain, asm)) {
      // Collected defs are blocks, items and recipe files together; only a block carries a class key.
      if (any is not ExBlockDef def)
        continue;
      JObject json = def.ToJson();
      if (!nodeClasses.Contains((string?)json["class"] ?? ""))
        continue;

      examined++;
      string where = $"{def.Location} ({(string?)json["class"]})";

      JObject[] declared =
      [
        .. ArrayAt(json["behaviors"])
          .OfType<JObject>()
          .Where(b => (string?)b["name"] == "ExOrientable"),
      ];

      if (declared.Length != 1) {
        problems.Add(
          $"{where} declares {declared.Length} `ExOrientable` behaviour(s), not 1 - a network node "
            + "takes its orientation from its neighbours, so it needs exactly one to write the "
            + "`orientation` variant through."
        );
        continue;
      }

      JToken? properties = declared[0]["properties"];
      string? mode = (string?)properties?["mode"];
      if (mode != "network") {
        problems.Add(
          $"{where} declares `ExOrientable` with mode '{mode ?? "<absent>"}', not 'network' - "
            + "IsNetworkOriented stays false, so the behaviour writes the `side` variant this block "
            + "does not have and every orientation swap resolves to no block, silently."
        );
        continue;
      }

      string[] states = def.VariantStates("orientation");
      string? actual = ExOrientations.Resolve(states)?.Name;
      string? scheme = (string?)properties?["scheme"];

      if (actual == null) {
        problems.Add(
          $"{where} declares orientation states [{string.Join(",", states)}], which set-equal no "
            + "scheme in ExOrientations.All - declare the scheme there rather than naming a near "
            + "match, whose rotation fallback would map onto a token this block does not have."
        );
        continue;
      }

      if (scheme != actual)
        problems.Add(
          $"{where} names scheme '{scheme ?? "<absent>"}' but its `orientation` states are "
            + $"{actual}'s - an unresolved name falls back to Axis, which rejects every token "
            + "outside [ns,we,ud] and stops the node re-orienting with no exception and no log line."
        );
    }

    defsChecked = examined;
    return problems;
  }

  /// <summary>The registered <c>class</c> keys, across exlib and <paramref name="asm"/>, that name a
  /// <see cref="BlockNetworkNode"/>.</summary>
  private static IReadOnlySet<string> NetworkNodeClassKeys(
    string domain,
    Assembly asm
  ) {
    var keys = new HashSet<string>(StringComparer.Ordinal);

    foreach (
      Assembly a in new[] { typeof(BlockNetworkNode).Assembly, asm }.Distinct()
    )
      foreach (Type type in ReflectionScan.GetCandidateTypes(a)) {
        if (!typeof(BlockNetworkNode).IsAssignableFrom(type) || type.IsAbstract)
          continue;
        keys.Add(EntityRegistry.KeyFor(domain, type));
      }

    return keys;
  }

  /// <summary>Every <see cref="BlockNetworkNode"/> def in <paramref name="asm"/> that declares no
  /// <c>type</c> variant state.</summary>
  public static IReadOnlyList<string> TypeGroupViolations(
    string domain,
    Assembly asm
  ) {
    var problems = new List<string>();

    foreach (Type type in ReflectionScan.GetCandidateTypes(asm)) {
      if (!typeof(BlockNetworkNode).IsAssignableFrom(type) || type.IsAbstract)
        continue;

      foreach (ExBlockDef def in ExDefinitions.DefinitionsOf(type, domain)) {
        if (def.VariantStates("type").Length > 0)
          continue;

        problems.Add(
          $"{domain}:{def.Code} ({type.Name}) declares NO `type` variant group - OrientationMap has "
            + "nothing to contribute, so AllowedOrientations is empty, ComputeValidOrientations returns "
            + "[] and TryPlaceBlock refuses. The block can never be placed, silently."
        );
      }
    }

    return problems;
  }

  /// <summary>Every declared network membership in <paramref name="asm"/> that names no
  /// <c>networkType</c>.</summary>
  public static IReadOnlyList<string> MembershipViolations(
    string domain,
    Assembly asm
  ) {
    var problems = new List<string>();
    IReadOnlySet<string> memberCodes = MembershipCodes(asm);

    foreach (Type type in ReflectionScan.GetCandidateTypes(asm))
      foreach (ExBlockDef def in ExDefinitions.DefinitionsOf(type, domain)) {
        JObject json = def.ToJson();

        foreach (
          (string where, JObject declaration) in MembershipDeclarations(
            json,
            memberCodes
          )
        ) {
          string? networkType = (string?)
            declaration["properties"]?["networkType"];
          if (!string.IsNullOrEmpty(networkType))
            continue;

          problems.Add(
            $"{domain}:{def.Code} ({type.Name}) declares a network membership in {where} with no "
              + "`networkType` - the behaviour has no other source for one, so the cell logs an error "
              + "and joins no graph. Give the declaration a networkType, or drop it."
          );
        }
      }

    return problems;
  }

  /// <summary>The registry key suffixes that name a network membership: <c>BEBehaviorNetworkMember</c>
  /// and any subclass, from exlib and from <paramref name="asm"/> itself.</summary>
  private static IReadOnlySet<string> MembershipCodes(Assembly asm) {
    var codes = new HashSet<string>(StringComparer.Ordinal);

    foreach (
      Assembly a in new[]
      {
        typeof(BEBehaviorNetworkMember).Assembly,
        asm,
      }.Distinct()
    )
      foreach (Type type in ReflectionScan.GetCandidateTypes(a)) {
        if (!typeof(BEBehaviorNetworkMember).IsAssignableFrom(type))
          continue;
        codes.Add(
          type.GetCustomAttribute<BlockEntityBehaviorRegisterAttribute>()?.Code
            ?? type.Name
        );
      }

    return codes;
  }

  /// <summary>The part of a declared behaviour key after its mod-id prefix.</summary>
  private static string BareKey(string? code) {
    string key = code ?? "";
    int dot = key.LastIndexOf('.');
    return dot < 0 ? key : key[(dot + 1)..];
  }

  /// <summary>Every membership declaration in a def's JSON, paired with where it was found: the
  /// block's own <c>entityBehaviors</c>, and each footprint cell's <c>behaviors</c> under either
  /// <c>fillerOffsets</c> table.</summary>
  private static IEnumerable<(
    string Where,
    JObject Declaration
  )> MembershipDeclarations(JObject json, IReadOnlySet<string> memberCodes) {
    foreach (JObject beh in ArrayAt(json["entityBehaviors"]).OfType<JObject>())
      if (memberCodes.Contains(BareKey((string?)beh["name"])))
        yield return ("entityBehaviors", beh);

    foreach (JToken table in FillerTables(json))
      foreach (JObject cell in ArrayAt(table).OfType<JObject>()) {
        string at = $"fillerOffsets cell ({cell["x"]},{cell["y"]},{cell["z"]})";
        foreach (JObject beh in ArrayAt(cell["behaviors"]).OfType<JObject>())
          if (memberCodes.Contains(BareKey((string?)beh["code"])))
            yield return (at, beh);
      }
  }

  /// <summary>Both places a footprint can be declared: the block-wide <c>attributes</c> table and each
  /// per-type override under <c>attributesByType</c>.</summary>
  private static IEnumerable<JToken> FillerTables(JObject json) {
    if (json["attributes"]?["fillerOffsets"] is JToken shared)
      yield return shared;

    if (json["attributesByType"] is JObject byType)
      foreach (JProperty variant in byType.Properties())
        if (variant.Value["fillerOffsets"] is JToken perType)
          yield return perType;
  }

  private static IEnumerable<JToken> ArrayAt(JToken? token) =>
    token as JArray ?? Enumerable.Empty<JToken>();
}
