using System;
using System.Collections.Generic;
using ExpandedLib.Storage;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="GroundStorageHotMeshPatch"/>: the mesh a hot ground piece draws from
/// <c>MeshRefs</c>. The client is a substitute whose renderer hands out one reference per uploaded
/// mesh.</summary>
[Collection(ExHarmonyCollection.Name)]
public class GroundStorageHotMeshPatchTests : IDisposable {
  private const int Hot = 600;

  private readonly int _mainThread = RuntimeEnv.MainThreadId;

  public GroundStorageHotMeshPatchTests() =>
    RuntimeEnv.MainThreadId = Environment.CurrentManagedThreadId;

  public void Dispose() => RuntimeEnv.MainThreadId = _mainThread;

  private static HarmonyFixture Patched() =>
    new("exlibtest.groundhotmesh", typeof(GroundStorageHotMeshPatch).Assembly);

  private sealed class StateItem : Item, IContainedMeshSource {
    public MeshData GenMesh(
      ItemSlot slot,
      ITextureAtlasAPI targetAtlas,
      BlockPos atBlockPos
    ) => new(slot.Itemstack.Attributes.GetString("state") == "raw" ? 4 : 8, 6);

    public string GetMeshCacheKey(ItemSlot slot) =>
      "state-" + slot.Itemstack.Attributes.GetString("state");
  }

  private sealed class Scene {
    public readonly ICoreClientAPI Api = Substitute.For<ICoreClientAPI>();
    public readonly Dictionary<string, object> Cache = [];
    public readonly Dictionary<MeshData, MultiTextureMeshRef> Uploads = [];
    public readonly MultiTextureMeshRef DefaultRef = new([], []);
    public readonly List<Action> Queued = [];
    public readonly BlockEntityGroundStorage Entity = new();

    public Scene(EnumGroundStorageLayout layout) {
      Api.Side.Returns(EnumAppSide.Client);
      ((ICoreAPI)Api).ObjectCache.Returns(Cache);
      Api.TesselatorManager.GetDefaultItemMeshRef(Arg.Any<Item>())
        .Returns(DefaultRef);
      Api.Render.UploadMultiTextureMesh(Arg.Any<MeshData>())
        .Returns(ci => {
          var mesh = ci.Arg<MeshData>();
          return Uploads[mesh] = new MultiTextureMeshRef([], []);
        });
      Api.Event.When(e =>
          e.EnqueueMainThreadTask(Arg.Any<Action>(), Arg.Any<string>())
        )
        .Do(ci => Queued.Add(ci.Arg<Action>()));
      Entity.Api = Api;
      ReflectionHelpers.SetField(Entity, "capi", Api);
      ReflectionHelpers.SetProperty(
        Entity,
        nameof(BlockEntityGroundStorage.StorageProps),
        new GroundStorageProperties { Layout = layout }
      );
    }

    public ItemSlot Hold(CollectibleObject item, string? state = null) {
      var stack = new ItemStack((Item)item);
      if (state != null)
        stack.Attributes.SetString("state", state);
      stack.Attributes.SetFloat("temperature", Hot);
      ItemSlot slot = Entity.Inventory[0];
      slot.Itemstack = stack;
      return slot;
    }

    public void Seed(string key, MeshData mesh) {
      var meshes =
        (Dictionary<string, MeshData>)
          Cache.GetValueOrDefault("meshesDisplay-groundstorage")!;
      if (meshes == null)
        Cache["meshesDisplay-groundstorage"] = meshes = [];
      meshes[key] = mesh;
    }

    public void Build(ItemSlot slot) =>
      ReflectionHelpers.Invoke(Entity, "getOrCreateMesh", slot, 0);
  }

  private static StateItem Piece(string code = "stock-bloom") =>
    new() { Code = new AssetLocation("exlib", code) };

  // Fails when the postfix is removed: the hot entry stays the item's default mesh instead of the
  // uploaded mesh of the piece's state.
  [Fact]
  public void Hot_piece_draws_its_state_mesh() {
    using var fixture = Patched();
    var scene = new Scene(EnumGroundStorageLayout.SingleCenter);
    StateItem item = Piece();

    scene.Build(scene.Hold(item, "raw"));
    MultiTextureMeshRef raw = scene.Entity.MeshRefs[0];
    scene.Build(scene.Hold(item, "shaped"));
    MultiTextureMeshRef shaped = scene.Entity.MeshRefs[0];

    Assert.NotSame(scene.DefaultRef, raw);
    Assert.NotSame(scene.DefaultRef, shaped);
    Assert.NotSame(raw, shaped);
    Assert.Equal(2, scene.Uploads.Count);
    Assert.Contains(raw, scene.Uploads.Values);
    Assert.Equal(4, FindMesh(scene, raw).VerticesMax);
    Assert.Equal(8, FindMesh(scene, shaped).VerticesMax);
  }

  private static MeshData FindMesh(Scene scene, MultiTextureMeshRef meshRef) {
    foreach ((MeshData mesh, MultiTextureMeshRef value) in scene.Uploads)
      if (ReferenceEquals(value, meshRef))
        return mesh;
    throw new InvalidOperationException("Not an uploaded reference.");
  }

  // Fails when the cache is not consulted: a second build of the same state uploads again.
  [Fact]
  public void A_state_uploads_once() {
    using var fixture = Patched();
    var scene = new Scene(EnumGroundStorageLayout.SingleCenter);
    StateItem item = Piece();

    scene.Build(scene.Hold(item, "raw"));
    MultiTextureMeshRef first = scene.Entity.MeshRefs[0];
    scene.Entity.MeshRefs[0] = scene.DefaultRef;
    scene.Build(scene.Hold(item, "raw"));

    Assert.Same(first, scene.Entity.MeshRefs[0]);
    scene.Api.Render.Received(1).UploadMultiTextureMesh(Arg.Any<MeshData>());
  }

  // Fails when the interface check is dropped: a plain item's default mesh is replaced by an upload.
  [Fact]
  public void Plain_item_keeps_the_default_mesh() {
    using var fixture = Patched();
    var scene = new Scene(EnumGroundStorageLayout.SingleCenter);
    var plain = new Item { Code = new AssetLocation("exlib", "plain") };
    scene.Seed("1xexlib:plain", new MeshData(4, 6));

    scene.Build(scene.Hold(plain));

    Assert.Same(scene.DefaultRef, scene.Entity.MeshRefs[0]);
    Assert.Empty(scene.Uploads);
  }

  // Fails when the layout check is dropped: a Stacking slot's reference from the game's upload
  // cache is replaced by an upload.
  [Fact]
  public void Pile_layouts_are_untouched() {
    using var fixture = Patched();
    var scene = new Scene(EnumGroundStorageLayout.Stacking);
    StateItem item = Piece();
    var gameRef = new MultiTextureMeshRef([], []);
    scene.Seed("1xstate-raw", new MeshData(4, 6));
    scene.Cache["groundStorageUMC"] = new Dictionary<
      string,
      MultiTextureMeshRef
    > {
      ["1xstate-raw"] = gameRef,
    };

    scene.Build(scene.Hold(item, "raw"));

    Assert.Same(gameRef, scene.Entity.MeshRefs[0]);
    Assert.Empty(scene.Uploads);
  }

  // Fails when the upload runs off the main thread: the reference is set before the queued task.
  [Fact]
  public void Off_the_main_thread_the_upload_is_queued() {
    using var fixture = Patched();
    var scene = new Scene(EnumGroundStorageLayout.SingleCenter);
    RuntimeEnv.MainThreadId = Environment.CurrentManagedThreadId + 1;

    scene.Build(scene.Hold(Piece(), "raw"));

    Assert.Same(scene.DefaultRef, scene.Entity.MeshRefs[0]);
    Assert.Single(scene.Queued);
    scene.Queued[0]();
    Assert.NotSame(scene.DefaultRef, scene.Entity.MeshRefs[0]);
  }
}
