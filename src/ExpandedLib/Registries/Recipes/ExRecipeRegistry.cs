using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace ExpandedLib.Registries;

/// <summary>
/// The recipe-registry rung: registers a <see cref="RecipeRegistryGeneric{T}"/> the same way
/// vanilla's own recipe kinds do (<c>ICoreAPI.RegisterRecipeRegistry</c>), so a mod's own recipe
/// type gets client sync and the handbook for free. <see cref="Register{T}"/> runs identically on
/// both sides, from <c>Start</c>; <see cref="LoadRecipes{T}"/> is server-only and belongs in
/// <c>AssetsLoaded</c>, after every mod's <see cref="Register{T}"/> call has run.
/// </summary>
public static class ExRecipeRegistry {
  /// <summary>
  /// Registers a <see cref="RecipeRegistryGeneric{T}"/> under <paramref name="code"/> and returns its
  /// (initially empty) recipe list. Call with the same <paramref name="code"/> on both sides - a code
  /// that differs between client and server leaves the two unable to sync the recipes it carries.
  /// </summary>
  public static List<T> Register<T>(ICoreAPI api, string code)
    where T : IByteSerializable, new() =>
    api.RegisterRecipeRegistry<RecipeRegistryGeneric<T>>(code).Recipes;

  /// <summary>
  /// Reads every JSON asset under <c>recipes/{folder}</c> - each either one recipe object or an array
  /// of them - and appends one <typeparamref name="T"/> per entry to <paramref name="into"/>, resolved
  /// against the asset's own domain. <paramref name="resolve"/>, if given, runs on each recipe before
  /// it is added and returning <c>false</c> drops it - an ingredient resolve, an <c>Enabled</c> check,
  /// whatever the recipe type needs done once loaded, mirroring the per-recipe step vanilla's own
  /// recipe kinds take at this same phase, including the drop
  /// (<c>RecipeRegistrySystem.loadRecipe</c>'s <c>if (!recipe.Enabled) return;</c>). Server-only; call
  /// from <c>AssetsLoaded</c>.
  /// </summary>
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
