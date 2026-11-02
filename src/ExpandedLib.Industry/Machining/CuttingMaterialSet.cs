using System.Collections.Generic;
using ExpandedLib.Catalogues;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.Machining;

/// <summary>The cutting materials, and optionally the hand power, one catalogue file or one C# contribution declares.</summary>
/// <param name="Schema">Schema version of the declaration.</param>
/// <param name="Materials">The declared materials, in order.</param>
/// <param name="HandPower">A man's working power, or null when the file declares none.</param>
public sealed record CuttingMaterialSet(
  int Schema,
  IReadOnlyList<CuttingMaterial> Materials,
  HandPower? HandPower = null
) {
  /// <summary>The schema this parser writes and reads up to.</summary>
  public const int CurrentSchema = SpecSchema.First;

  /// <summary>Parses one <c>config/cuttingmaterials</c> file. Returns false with a human-readable
  /// <paramref name="error"/> naming the malformed field; <paramref name="set"/> is then null.</summary>
  internal static bool TryParse(
    JsonObject? node,
    out CuttingMaterialSet? set,
    out string? error
  ) {
    set = null;
    if (node is not { Exists: true }) {
      error = "missing cutting material declaration";
      return false;
    }
    if (!SpecSchema.TryRead(node, CurrentSchema, out int schema, out error))
      return false;

    var materials = new List<CuttingMaterial>();
    foreach (JsonObject materialNode in node["materials"].AsArray() ?? []) {
      string code = materialNode["code"].AsString("");
      if (string.IsNullOrWhiteSpace(code)) {
        error = "a cutting material names no 'code'";
        return false;
      }
      if (code != code.ToLowerInvariant() || code.Contains(':')) {
        error =
          $"cutting material code '{code}' must be a bare lowercase string";
        return false;
      }

      float density = materialNode["density"].AsFloat(0f);
      float rotary = materialNode["energy"]["rotary"].AsFloat(0f);
      float reciprocating = materialNode["energy"]["reciprocating"].AsFloat(0f);
      if (density <= 0f || rotary <= 0f || reciprocating <= 0f) {
        error =
          $"cutting material '{code}' needs a 'density' and an 'energy' with 'rotary' and 'reciprocating', all above 0";
        return false;
      }
      materials.Add(new CuttingMaterial(code, density, rotary, reciprocating));
    }

    HandPower? hand = null;
    if (node["handPower"].Exists) {
      float rotary = node["handPower"]["rotary"].AsFloat(0f);
      float reciprocating = node["handPower"]["reciprocating"].AsFloat(0f);
      if (rotary <= 0f || reciprocating <= 0f) {
        error = "'handPower' needs 'rotary' and 'reciprocating', both above 0";
        return false;
      }
      hand = new HandPower(rotary, reciprocating);
    }

    error = null;
    set = new CuttingMaterialSet(schema, materials, hand);
    return true;
  }
}
