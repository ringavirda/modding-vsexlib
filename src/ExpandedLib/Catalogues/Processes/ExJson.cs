using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Catalogues;

/// <summary>Turns a plain object into the <see cref="JsonObject"/> the spec parsers read.</summary>
internal static class ExJson {
  /// <summary>The object as a parser-ready node.</summary>
  internal static JsonObject Of(object value) => new(JToken.FromObject(value));
}
