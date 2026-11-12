using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Registries;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ExpandedLib.Storage;

/// <summary>Makes a hot piece on the ground draw its own state mesh. The game's hot renderer draws
/// <c>MeshRefs</c> from 500 C, and <c>BlockEntityGroundStorage.getOrCreateMesh</c> fills it with the
/// item's default mesh, bypassing <see cref="IContainedMeshSource"/>, which the cold path uses. The
/// postfix replaces the entry with the mesh the base call returned for an item that implements the
/// interface, in the layouts that fill <c>MeshRefs</c> from the item (not Stacking, not Messy12).
/// Skipped, with one log line, when the game's method is not found.</summary>
[HarmonyPatch]
internal static class GroundStorageHotMeshPatch {
  private const string CacheKey = "exlibGroundStorageHotMesh";

  private static readonly MethodInfo? Target = AccessTools.Method(
    typeof(BlockEntityGroundStorage),
    "getOrCreateMesh",
    [typeof(ItemSlot), typeof(int)]
  );

  private static readonly System.Func<
    BlockEntityGroundStorage,
    ItemSlot,
    string
  >? CacheKeyOf = AccessTools.Method(
    typeof(BlockEntityGroundStorage),
    "getMeshCacheKey"
  )
    is { } keyOf
    ? AccessTools.MethodDelegate<System.Func<
      BlockEntityGroundStorage,
      ItemSlot,
      string
    >>(keyOf)
    : null;

  public static bool Prepare() {
    if (Target != null && CacheKeyOf != null)
      return true;
    RegistryLog.Logger?.Warning(
      "[exlib] BlockEntityGroundStorage.getOrCreateMesh not found; hot ground pieces keep the default mesh."
    );
    return false;
  }

  public static MethodBase TargetMethod() => Target!;

  public static void Postfix(
    BlockEntityGroundStorage __instance,
    ItemSlot slot,
    int index,
    MeshData? __result
  ) {
    if (
      __result == null
      || __instance.Api is not ICoreClientAPI capi
      || slot.Itemstack is not { Class: EnumItemClass.Item } stack
      || __instance.StorageProps?.Layout
        is null
          or EnumGroundStorageLayout.Stacking
          or EnumGroundStorageLayout.Messy12
      || stack.Collectible.GetCollectibleInterface<IContainedMeshSource>()
        == null
    )
      return;

    string key = CacheKeyOf!(__instance, slot);
    Dictionary<string, MultiTextureMeshRef> uploaded =
      ObjectCacheUtil.GetOrCreate(
        capi,
        CacheKey,
        () => new Dictionary<string, MultiTextureMeshRef>()
      );
    if (uploaded.TryGetValue(key, out MultiTextureMeshRef? cached)) {
      __instance.MeshRefs[index] = cached;
      return;
    }

    if (System.Environment.CurrentManagedThreadId == RuntimeEnv.MainThreadId) {
      Upload(capi, __instance, uploaded, key, __result, index);
      return;
    }
    capi.Event.EnqueueMainThreadTask(
      () => {
        Upload(capi, __instance, uploaded, key, __result, index);
        capi.World.BlockAccessor.MarkBlockDirty(__instance.Pos);
      },
      "exlibgroundstoragehotmesh"
    );
  }

  private static void Upload(
    ICoreClientAPI capi,
    BlockEntityGroundStorage be,
    Dictionary<string, MultiTextureMeshRef> uploaded,
    string key,
    MeshData mesh,
    int index
  ) {
    MultiTextureMeshRef meshRef = capi.Render.UploadMultiTextureMesh(mesh);
    uploaded[key] = meshRef;
    be.MeshRefs[index] = meshRef;
  }
}
