using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ExpandedLib.Helpers;

/// <summary>Shared cache for tesselated block-entity meshes, keyed by everything that changes the mesh.</summary>
public static class ExMeshCache {
  // Shares one prefix in the API's object cache to avoid colliding with another mod's key.
  private const string Prefix = "exlib:mesh:";

  /// <summary>Returns the mesh for <paramref name="key"/>, tesselating it through <paramref name="build"/> on first use.</summary>
  public static MeshData? GetOrCreate(
    ICoreClientAPI capi,
    string key,
    Func<MeshData?> build
  ) =>
    ObjectCacheUtil.GetOrCreate<MeshData?>(capi, Prefix + key, () => build());

  /// <summary>Returns the mesh for one variant of <paramref name="block"/>, with the block's code folded into the key.</summary>
  public static MeshData? GetOrCreate(
    ICoreClientAPI capi,
    Block block,
    string variantKey,
    Func<MeshData?> build
  ) => GetOrCreate(capi, $"{block.Code}|{variantKey}", build);

  /// <summary>Drops one cached mesh so the next request rebuilds it.</summary>
  public static void Invalidate(ICoreAPI api, string key) =>
    ObjectCacheUtil.Delete(api, Prefix + key);

  /// <summary>Loads a shape asset through <see cref="Shape.TryGet(ICoreAPI, AssetLocation)"/>; returns null when the asset is missing or will not parse.</summary>
  public static Shape? LoadShape(ICoreAPI api, AssetLocation shapePath) =>
    Shape.TryGet(api, shapePath);

  /// <summary>Returns the full asset path of a block's own shape, prefixed and suffixed as the asset system expects.</summary>
  public static AssetLocation ShapePathOf(Block block) =>
    block
      .Shape.Base.Clone()
      .WithPathPrefixOnce("shapes/")
      .WithPathAppendixOnce(".json");

  // Uploaded refs live behind their own prefix; each ref is GPU-backed and disposed explicitly.
  private const string RefPrefix = "exlib:meshref:";

  /// <summary>Returns the uploaded GPU mesh ref for <paramref name="key"/> within <paramref name="group"/>, building it through <paramref name="build"/> on first use.</summary>
  public static MultiTextureMeshRef GetOrCreateRef(
    ICoreClientAPI capi,
    string group,
    string key,
    Func<MeshData> build
  ) {
    Dictionary<string, MultiTextureMeshRef> refs = GetGroup(capi, group);
    if (!refs.TryGetValue(key, out MultiTextureMeshRef? meshRef)) {
      meshRef = capi.Render.UploadMultiTextureMesh(build());
      refs[key] = meshRef;
    }
    return meshRef;
  }

  private static Dictionary<string, MultiTextureMeshRef> GetGroup(
    ICoreClientAPI capi,
    string group
  ) =>
    ObjectCacheUtil.GetOrCreate(
      capi,
      RefPrefix + group,
      () => new Dictionary<string, MultiTextureMeshRef>()
    );

  /// <summary>Disposes and drops every ref <see cref="GetOrCreateRef"/> has uploaded under <paramref name="group"/>.</summary>
  public static void DisposeGroup(ICoreAPI api, string group) {
    string cacheKey = RefPrefix + group;
    if (
      api.ObjectCache.TryGetValue(cacheKey, out object? existing)
      && existing is Dictionary<string, MultiTextureMeshRef> refs
    ) {
      foreach (MultiTextureMeshRef meshRef in refs.Values)
        meshRef.Dispose();
      api.ObjectCache.Remove(cacheKey);
    }
  }
}
