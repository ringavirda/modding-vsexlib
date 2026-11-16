using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ExpandedLib.Scenarios;

/// <summary>What a <c>complete</c> step's <c>to</c> and <c>materials</c> mean, read the same way by
/// the fake world and the live runner.</summary>
internal static class SetupConstruction {
  /// <summary>The last stage index <paramref name="to"/> asks for.</summary>
  /// <param name="to">A stage index, or "all", true or null for the last.</param>
  /// <param name="stages">The number of stages the construction has.</param>
  /// <exception cref="SetupFileException">The stage is not an index of the construction.</exception>
  public static int Target(JToken? to, int stages) {
    if (
      to == null
      || to.Type == JTokenType.Boolean && (bool)to
      || to.Type == JTokenType.String && (string?)to == "all"
    )
      return stages - 1;
    if (to.Type != JTokenType.Integer || (int)to < 0 || (int)to >= stages)
      throw new SetupFileException(
        $"\"to\" {to} is not a stage of a construction of {stages} stage(s), 0 to {stages - 1}."
      );
    return (int)to;
  }

  /// <summary>The variant each stored wildcard key is supplied in.</summary>
  /// <exception cref="SetupFileException">The token is not an object of strings.</exception>
  public static Dictionary<string, string> Materials(JToken? token) {
    var materials = new Dictionary<string, string>();
    if (token == null)
      return materials;
    if (
      token is not JObject o
      || o.Properties().Any(p => p.Value.Type != JTokenType.String)
    )
      throw new SetupFileException(
        "\"materials\" maps each wildcard key to a variant name."
      );
    foreach (JProperty p in o.Properties())
      materials[p.Name] = (string)p.Value!;
    return materials;
  }

  /// <summary>A stage requirement's code as the pack is drawn for it: each <c>{key}</c> read as the
  /// variant <paramref name="materials"/> names for the key, or <c>*</c> when it names none.</summary>
  public static string DrawnCode(
    string code,
    IReadOnlyDictionary<string, string> materials
  ) =>
    Regex.Replace(
      code,
      @"\{([^}]*)\}",
      m => materials.GetValueOrDefault(m.Groups[1].Value, "*")
    );
}
