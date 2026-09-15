using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace ExpandedLib.Catalogues;

/// <summary>Reads a JSON catalogue out of every domain's assets, deserializing each matching file
/// and reporting what failed. Must run at <c>AssetsFinalize</c>.</summary>
public static class AssetCatalogueLoader {
  /// <summary>One path's read: every asset that parsed, its source location, the file count, and
  /// one message per asset that did not parse.</summary>
  /// <typeparam name="T">The catalogue type each asset deserializes to.</typeparam>
  /// <param name="Items">Every asset that parsed, deserialized to <typeparamref name="T"/>.</param>
  /// <param name="Sources">Each item's asset location, same index as <paramref name="Items"/>.</param>
  /// <param name="Files">How many assets matched the path, whether or not they parsed.</param>
  /// <param name="Errors">One message per asset that failed to parse, naming the asset.</param>
  public readonly record struct ReadResult<T>(
    IReadOnlyList<T> Items,
    IReadOnlyList<string> Sources,
    int Files,
    IReadOnlyList<string> Errors
  );

  /// <summary>Deserializes every loaded asset whose path begins with <paramref name="pathBegins"/>
  /// (across all domains) into <typeparamref name="T"/>.</summary>
  /// <typeparam name="T">The catalogue type each matching asset deserializes to.</typeparam>
  /// <returns>Every asset that parsed. A malformed asset is dropped, not thrown.</returns>
  public static List<T> GetMany<T>(ICoreAPI api, string pathBegins)
    where T : class => [.. Read<T>(api, pathBegins).Items];

  /// <summary>As <see cref="GetMany{T}"/>, but keeps each item's source location and every
  /// failure.</summary>
  /// <typeparam name="T">The catalogue type each matching asset deserializes to.</typeparam>
  /// <returns>The full read outcome: parsed items, their sources, the file count, and errors.</returns>
  public static ReadResult<T> Read<T>(ICoreAPI api, string pathBegins)
    where T : class {
    var items = new List<T>();
    var sources = new List<string>();
    var errors = new List<string>();
    int files = 0;
    foreach (IAsset asset in api.Assets.GetMany(pathBegins)) {
      files++;
      T? def = SafeToObject<T>(asset, errors);
      if (def != null) {
        items.Add(def);
        sources.Add(asset.Location.ToString());
      }
    }
    return new ReadResult<T>(items, sources, files, errors);
  }

  // A fresh settings instance per asset: ToObject<T> may add a domain-specific converter to it.
  private static T? SafeToObject<T>(IAsset asset, List<string> errors)
    where T : class {
    try {
      return asset.ToObject<T>(
        new JsonSerializerSettings {
          MissingMemberHandling = MissingMemberHandling.Error,
        }
      );
    } catch (Exception e) {
      errors.Add($"{asset.Location}: {e.Message}");
      return null;
    }
  }
}
