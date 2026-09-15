using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace ExpandedLib.Registries;

/// <summary>The recipe-registry rung: registers a <see cref="RecipeRegistryGeneric{T}"/> the same
/// way vanilla's own recipe kinds do, so a mod's own recipe type gets client sync and the handbook
/// for free.</summary>
public static class ExRecipeRegistry {
  /// <summary>Registers a <see cref="RecipeRegistryGeneric{T}"/> under <paramref name="code"/> and
  /// returns its (initially empty) recipe list. Call with the same <paramref name="code"/> on both
  /// sides.</summary>
  public static List<T> Register<T>(ICoreAPI api, string code)
    where T : IByteSerializable, new() =>
    api.RegisterRecipeRegistry<RecipeRegistryGeneric<T>>(code).Recipes;

  /// <summary>Reads every JSON asset under <c>recipes/{folder}</c> and appends one
  /// <typeparamref name="T"/> per entry to <paramref name="into"/>. <paramref name="resolve"/>, if
  /// given, runs on each recipe and returning <c>false</c> drops it. Server-only.</summary>
  public static void LoadRecipes<T>(
    ICoreServerAPI sapi,
    string folder,
    List<T> into,
    System.Func<T, bool>? resolve = null
  )
    where T : IByteSerializable, new() {
    Dictionary<AssetLocation, JToken> assets = sapi.Assets.GetMany<JToken>(
      sapi.Server.Logger,
      "recipes/" + folder
    );

    foreach (var (loc, token) in assets) {
      if (token is JArray array)
        foreach (JToken entry in array)
          LoadOne(loc, entry, into, resolve);
      else
        LoadOne(loc, token, into, resolve);
    }
  }

  private static void LoadOne<T>(
    AssetLocation loc,
    JToken token,
    List<T> into,
    System.Func<T, bool>? resolve
  )
    where T : IByteSerializable, new() {
    T? recipe = token.ToObject<T>(loc.Domain);
    if (recipe == null)
      return;

    if (resolve != null && !resolve(recipe))
      return;

    into.Add(recipe);
  }
}
