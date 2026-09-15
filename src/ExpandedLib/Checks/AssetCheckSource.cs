using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Definitions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Checks;

/// <summary>
/// The in-game <see cref="ICheckSource"/>: codes and recipes from <see cref="ICoreAPI.Assets"/>,
/// block definitions from the process-wide <see cref="ExDefinitions"/> registry. A domain is exlib
/// and every enabled mod that depends on it.
/// </summary>
public sealed class AssetCheckSource(ICoreAPI api) : ICheckSource {
  /// <inheritdoc/>
  public IEnumerable<string> Domains =>
    api
      .ModLoader.Mods.Where(m =>
        m.Info.ModID == "exlib"
        || m.Info.Dependencies.Any(d => d.ModID == "exlib")
      )
      .Select(m => m.Info.ModID)
      .Distinct();

  /// <inheritdoc/>
  public IEnumerable<AssetLocation> BlockCodes =>
    api.World.Blocks.Where(b => b?.Code != null).Select(b => b.Code);

  /// <inheritdoc/>
  public IEnumerable<AssetLocation> ItemCodes =>
    api.World.Items.Where(i => i?.Code != null).Select(i => i.Code);

  /// <inheritdoc/>
  public IEnumerable<(AssetLocation File, JObject Json)> Recipes(string domain) {
    foreach (IAsset asset in api.Assets.GetMany("recipes/", domain))
      foreach (JObject recipe in ReadRecipeObjects(asset))
        yield return (asset.Location, recipe);
  }

  /// <inheritdoc/>
  public IEnumerable<(string Locale, JObject Json)> Lang(string domain) {
    foreach (IAsset asset in api.Assets.GetMany("lang/", domain)) {
      if (TryParseObject(asset, out JObject json))
        yield return (
          Path.GetFileNameWithoutExtension(asset.Location.Path),
          json
        );
    }
  }

  /// <inheritdoc/>
  public IEnumerable<ExBlockDef> BlockDefinitions(string domain) =>
    ExDefinitions.Blocks.Where(d => d.Domain == domain);

  // A recipe file is one object or a JSON array; every element shares the file's location.
  private static IEnumerable<JObject> ReadRecipeObjects(IAsset asset) {
    if (!TryParseToken(asset, out JToken token))
      yield break;

    foreach (JToken item in token is JArray array ? array : [token])
      if (item is JObject obj)
        yield return obj;
  }

  private static bool TryParseObject(IAsset asset, out JObject json) {
    json = null!;
    if (!TryParseToken(asset, out JToken token) || token is not JObject obj)
      return false;
    json = obj;
    return true;
  }

  // A malformed file is skipped, not reported: the asset loader already logs the parse failure.
  private static bool TryParseToken(IAsset asset, out JToken token) {
    token = null!;
    try {
      token = JToken.Parse(asset.ToText());
      return true;
    } catch (Newtonsoft.Json.JsonException) {
      return false;
    }
  }
}
