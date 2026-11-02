using System;
using System.Collections.Generic;
using System.Numerics;
using ExpandedLib.Catalogues;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.Machining;

/// <summary>The machine kinds one catalogue file or one C# contribution declares.</summary>
/// <param name="Schema">Schema version of the declaration.</param>
/// <param name="Kinds">The declared kinds, in order.</param>
public sealed record MachineKindSet(
  int Schema,
  IReadOnlyList<MachineKind> Kinds
) {
  /// <summary>The schema this parser writes and reads up to.</summary>
  public const int CurrentSchema = SpecSchema.First;

  /// <summary>Parses one <c>config/machinekinds</c> file. Returns false with a human-readable
  /// <paramref name="error"/> naming the malformed field; <paramref name="set"/> is then null.</summary>
  internal static bool TryParse(
    JsonObject? node,
    out MachineKindSet? set,
    out string? error
  ) {
    set = null;
    if (node is not { Exists: true }) {
      error = "missing machine kind declaration";
      return false;
    }
    if (!SpecSchema.TryRead(node, CurrentSchema, out int schema, out error))
      return false;

    var kinds = new List<MachineKind>();
    foreach (JsonObject kindNode in node["kinds"].AsArray() ?? []) {
      string code = kindNode["code"].AsString("");
      if (string.IsNullOrWhiteSpace(code) || !code.Contains(':')) {
        error = $"machine kind code '{code}' must be domain-qualified";
        return false;
      }
      float ratedSpeed = kindNode["ratedSpeed"].AsFloat(0f);
      if (ratedSpeed <= 0f) {
        error = $"machine kind '{code}' needs a 'ratedSpeed' above 0";
        return false;
      }
      float standing = kindNode["standing"].AsFloat(0f);
      if (standing < 0f) {
        error = $"machine kind '{code}' declares a negative 'standing'";
        return false;
      }

      var anchors = new List<MachineAnchor>();
      foreach (
        (string name, JsonObject anchorNode) in Members(kindNode["anchors"])
      ) {
        if (
          !TryVector(anchorNode["origin"], 0f, out Vector3 origin)
          || !TryVector(anchorNode["rotation"], 0f, out Vector3 rotation)
        ) {
          error =
            $"machine kind '{code}': anchor '{name}' needs 'origin' and 'rotation' as [x, y, z]";
          return false;
        }
        float scale = anchorNode["scale"].AsFloat(1f);
        if (scale <= 0f) {
          error =
            $"machine kind '{code}': anchor '{name}' has a 'scale' of {scale}";
          return false;
        }
        anchors.Add(new MachineAnchor(name, origin, rotation, scale));
      }

      var steps = new List<MachineKindStep>();
      foreach ((string step, JsonObject stepNode) in Members(kindNode["steps"])) {
        string motionName = stepNode["motion"].AsString("");
        if (
          !Enum.TryParse(motionName, true, out CutMotion motion)
          || !Enum.IsDefined(motion)
        ) {
          error =
            $"machine kind '{code}': step '{step}' has 'motion' '{motionName}', which is not rotary or reciprocating";
          return false;
        }
        float power = stepNode["power"].AsFloat(0f);
        if (power <= 0f) {
          error =
            $"machine kind '{code}': step '{step}' needs a 'power' above 0";
          return false;
        }
        string tooling = stepNode["tooling"].AsString("");
        if (string.IsNullOrWhiteSpace(tooling)) {
          error = $"machine kind '{code}': step '{step}' names no 'tooling'";
          return false;
        }
        string? anchor = string.IsNullOrWhiteSpace(
          stepNode["anchor"].AsString("")
        )
          ? null
          : stepNode["anchor"].AsString();
        if (anchor != null && !anchors.Exists(a => a.Name == anchor)) {
          error =
            $"machine kind '{code}': step '{step}' names anchor '{anchor}', which the kind does not declare";
          return false;
        }
        MachineEnvelope? envelope = null;
        if (stepNode["envelope"].Exists) {
          float across = stepNode["envelope"]["across"].AsFloat(0f);
          float length = stepNode["envelope"]["length"].AsFloat(0f);
          if (across <= 0f || length <= 0f) {
            error =
              $"machine kind '{code}': step '{step}' needs an 'envelope' with 'across' and 'length' above 0";
            return false;
          }
          envelope = new MachineEnvelope(across, length);
        }
        steps.Add(
          new MachineKindStep(step, motion, power, tooling, anchor, envelope)
        );
      }

      kinds.Add(
        new MachineKind(
          code,
          ratedSpeed,
          standing,
          kindNode["fixture"].AsBool(false),
          anchors,
          steps
        )
      );
    }

    error = null;
    set = new MachineKindSet(schema, kinds);
    return true;
  }

  private static IEnumerable<(string Name, JsonObject Node)> Members(
    JsonObject node
  ) {
    if (node.Token is not JObject obj)
      yield break;
    foreach (JProperty property in obj.Properties())
      yield return (property.Name, node[property.Name]);
  }

  private static bool TryVector(
    JsonObject node,
    float fallback,
    out Vector3 vector
  ) {
    vector = new Vector3(fallback);
    if (!node.Exists)
      return true;
    JsonObject[]? parts = node.AsArray();
    if (parts is not { Length: 3 })
      return false;
    vector = new Vector3(
      parts[0].AsFloat(fallback),
      parts[1].AsFloat(fallback),
      parts[2].AsFloat(fallback)
    );
    return true;
  }
}
