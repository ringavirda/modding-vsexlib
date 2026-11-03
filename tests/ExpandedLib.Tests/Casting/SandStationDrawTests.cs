using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.Casting;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Testing;
using Newtonsoft.Json;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests.Casting;

/// <summary>What the sand station hands the client: the sand mesh it pools, the renderer of the impression's metal and the
/// feed surface. The client is a substitute whose mesh cache holds the meshes a tesselation would build.
/// Shares the process-wide <see cref="CastingStationRegistry.Shared"/>.</summary>
[Collection("WorldState")]
public class SandStationDrawTests : IDisposable {
  public void Dispose() => CastingStationRegistry.ResetForWorld();

  private sealed class FeedStation : BlockEntitySandStation {
    protected internal override IReadOnlyList<Cuboidf> FeedSurfaces =>
      [new Cuboidf(6f, 2f, 0f, 10f, 4f, 3f)];
  }

  private sealed class ForcedFeedStation : BlockEntitySandStation {
    protected internal override IReadOnlyList<Cuboidf> FeedSurfaces =>
      [new Cuboidf(6f, 2f, 0f, 10f, 4f, 3f)];

    protected internal override bool FeedShown => true;
    protected internal override string? FeedMetalCode => SandStationScene.Metal;
    protected internal override float FeedTemperature => 1100f;
  }

  private sealed class Client {
    public ICoreClientAPI Api { get; } = Substitute.For<ICoreClientAPI>();
    public Dictionary<string, object> Cache { get; } = [];
    public List<IRenderer> Renderers { get; } = [];
    public List<IRenderer> Unregistered { get; } = [];

    public Client(SandStationScene scene) {
      Api.Side.Returns(EnumAppSide.Client);
      ((ICoreAPI)Api).World.Returns(scene.World.World);
      ((ICoreAPI)Api).ObjectCache.Returns(Cache);
      Api.Event.When(e =>
          e.RegisterRenderer(
            Arg.Any<IRenderer>(),
            Arg.Any<EnumRenderStage>(),
            Arg.Any<string>()
          )
        )
        .Do(ci => Renderers.Add(ci.Arg<IRenderer>()));
      Api.Event.When(e =>
          e.UnregisterRenderer(Arg.Any<IRenderer>(), Arg.Any<EnumRenderStage>())
        )
        .Do(ci => Unregistered.Add(ci.Arg<IRenderer>()));
      scene.Entity.Api = Api;
    }

    public MeshData Seed(BlockEntitySandStation be, string shape, int pressed) {
      var mesh = new MeshData(4, 6);
      Cache[$"exlib:mesh:{be.Block.Code}|{shape}|{pressed}"] = mesh;
      return mesh;
    }
  }

  private static (SandStationScene Scene, Client Client) Impressed(
    int units = 100,
    int share = 0,
    Func<BlockEntitySandStation>? make = null
  ) {
    var scene = SandStationScene.Build(make: make);
    Assert.True(
      scene.Interact(scene.PatternStack("plate", units, share: share))
    );
    return (scene, new Client(scene));
  }

  private static ITerrainMeshPool Pool(List<MeshData> added) {
    var pool = Substitute.For<ITerrainMeshPool>();
    pool.When(p => p.AddMeshData(Arg.Any<MeshData>(), Arg.Any<int>()))
      .Do(ci => added.Add(ci.Arg<MeshData>()));
    pool.When(p => p.AddMeshData(Arg.Any<MeshData>()))
      .Do(ci => added.Add(ci.Arg<MeshData>()));
    return pool;
  }

  // Fails when a rammed, unimpressed station draws anything but the kind's base sand.
  [Fact]
  public void A_rammed_station_tesselates_the_kinds_base_sand() {
    var scene = SandStationScene.Build();
    var client = new Client(scene);
    MeshData sand = client.Seed(scene.Entity, "test:casting/base", 0);
    var added = new List<MeshData>();

    scene.Entity.OnTesselation(Pool(added), Substitute.For<ITesselatorAPI>());

    Assert.Same(sand, Assert.Single(added.Distinct()));
  }

  // Fails when FillingShape returns the base after an imprint.
  [Fact]
  public void An_impressed_station_tesselates_the_impression_pruned_to_pressed() {
    var (scene, client) = Impressed();
    client.Seed(scene.Entity, "test:casting/base", 1);
    MeshData impression = client.Seed(scene.Entity, "test:casting/plate", 1);
    var added = new List<MeshData>();

    scene.Entity.OnTesselation(Pool(added), Substitute.For<ITesselatorAPI>());

    Assert.Same(impression, Assert.Single(added.Distinct()));
  }

  // Fails when the pressed count is left out of the mesh key: a second piece draws the first piece's mesh.
  [Fact]
  public void The_mesh_follows_the_pieces_pressed() {
    var scene = SandStationScene.Build();
    Assert.True(scene.Interact(scene.PatternStack("plate", pieces: 2)));
    Assert.True(scene.Interact(scene.PatternStack("plate", pieces: 2)));
    var client = new Client(scene);
    MeshData one = client.Seed(scene.Entity, "test:casting/plate", 1);
    MeshData two = client.Seed(scene.Entity, "test:casting/plate", 2);
    var added = new List<MeshData>();

    scene.Entity.OnTesselation(Pool(added), Substitute.For<ITesselatorAPI>());

    Assert.Same(two, Assert.Single(added.Distinct()));
    Assert.NotSame(one, two);
  }

  private static MoltenSurfaceRenderer Surface(Client client) =>
    Assert.IsType<MoltenSurfaceRenderer>(Assert.Single(client.Renderers));

  // Fails when the feed renderer ignores the FeedShown, FeedMetalCode and FeedTemperature overrides.
  [Fact]
  public void A_subclass_drives_the_feed_surface_through_the_feed_state() {
    var (scene, client) = Impressed(
      share: 10,
      make: () => new ForcedFeedStation()
    );

    scene.Entity.Initialize(client.Api);

    MoltenSurfaceRenderer feed = Assert.IsType<MoltenSurfaceRenderer>(
      client.Renderers[^1]
    );
    Assert.Equal(1f, feed.FillRatio);
    Assert.Equal(1100f, feed.Temperature);
    Assert.Equal(
      SandStationScene.Metal,
      feed.MetalStack!.Collectible.Code.ToString()
    );
  }

  // Fails when BuildFillingMesh hands the tesselator the whole shape instead of the one pruned to the pressed pieces.
  [Fact]
  public void An_uncached_impression_is_tesselated_pruned_to_the_pressed_pieces() {
    var scene = SandStationScene.Build();
    Assert.True(scene.Interact(scene.PatternStack("plate", pieces: 2)));
    var client = new Client(scene);
    scene.Entity.Block.Shape = new CompositeShape();
    static ShapeElement Group(string name, params ShapeElement[] kids) =>
      new() { Name = name, Children = kids };
    var plate = new Shape {
      Elements =
      [
        Group("Piece1", Group("Bedded", new ShapeElement { Name = "Core1" })),
        Group("Piece2", Group("Bedded", new ShapeElement { Name = "Core2" })),
      ],
    };
    var asset = Substitute.For<IAsset>();
    asset.ToObject<Shape>(Arg.Any<JsonSerializerSettings?>()).Returns(plate);
    client
      .Api.Assets.TryGet(
        Arg.Is<AssetLocation>(l =>
          l.ToString() == "test:shapes/casting/plate.json"
        )
      )
      .Returns(asset);
    Shape? tesselated = null;
    var tesselator = Substitute.For<ITesselatorAPI>();
    tesselator
      .When(t =>
        t.TesselateShape(
          Arg.Any<string>(),
          Arg.Any<Shape>(),
          out Arg.Any<MeshData>(),
          Arg.Any<ITexPositionSource>(),
          Arg.Any<Vec3f>(),
          Arg.Any<int>(),
          Arg.Any<byte>(),
          Arg.Any<byte>(),
          Arg.Any<int?>(),
          Arg.Any<string[]>()
        )
      )
      .Do(ci => {
        tesselated = ci.ArgAt<Shape>(1);
        ci[2] = new MeshData(4, 6);
      });

    scene.Entity.OnTesselation(Pool([]), tesselator);

    Assert.NotNull(tesselated);
    string[] paths = [.. ExShapeElements.AllPaths(tesselated)];
    Assert.Contains("Piece1/Bedded/Core1", paths);
    Assert.DoesNotContain("Piece2/Bedded/Core2", paths);
  }

  // Fails when the fill ratio is fixed instead of the cell's amount over its capacity.
  [Fact]
  public void The_impression_surface_fills_with_the_cell_amount_over_its_capacity() {
    var (scene, client) = Impressed();
    scene.Pour(50, 1300f);

    scene.Entity.Initialize(client.Api);

    MoltenSurfaceRenderer surface = Surface(client);
    Assert.Equal(0.5f, surface.FillRatio, 0.001f);
    Assert.Equal(1300f, surface.Temperature);
    Assert.Equal(
      SandStationScene.Metal,
      surface.MetalStack!.Collectible.Code.ToString()
    );
  }

  // Fails when the renderer is built over one box: the first piece's metal drops out once two are pressed.
  [Fact]
  public void The_surface_covers_every_pressed_piece() {
    var scene = SandStationScene.Build();
    Assert.True(scene.Interact(scene.PatternStack("plate", pieces: 2)));
    Assert.True(scene.Interact(scene.PatternStack("plate", pieces: 2)));
    var client = new Client(scene);
    var meshes = new List<MeshData>();
    client
      .Api.Render.When(r => r.UploadMesh(Arg.Any<MeshData>()))
      .Do(ci => meshes.Add(ci.Arg<MeshData>()));

    scene.Entity.Initialize(client.Api);

    Assert.Equal(8, Assert.Single(meshes).VerticesCount);
  }

  // Fails when the feed surface shows without the head standing in the runner box.
  [Fact]
  public void The_feed_surface_shows_only_while_the_head_stands() {
    var (scene, client) = Impressed(share: 10, make: () => new FeedStation());
    scene.Pour(100, 1300f);
    scene.Entity.Initialize(client.Api);
    MoltenSurfaceRenderer feed = Assert.IsType<MoltenSurfaceRenderer>(
      client.Renderers[^1]
    );
    Assert.Equal(0f, feed.FillRatio);

    scene.Pour(5, 1300f);
    ReflectionHelpers.Invoke(scene.Entity, "UpdateFeedRenderer");

    Assert.Equal(1f, feed.FillRatio);
  }

  // Fails when removing the block leaves its renderers registered.
  [Fact]
  public void Removing_the_block_unregisters_its_renderers() {
    var (scene, client) = Impressed(share: 10, make: () => new FeedStation());
    scene.Entity.Initialize(client.Api);
    IRenderer[] drawn = [.. client.Renderers];
    Assert.Equal(2, drawn.Length);

    scene.Entity.OnBlockRemoved();

    Assert.Equal(
      drawn.Order(new RefOrder()),
      client.Unregistered.Order(new RefOrder())
    );
  }

  private sealed class RefOrder : IComparer<IRenderer> {
    public int Compare(IRenderer? a, IRenderer? b) =>
      System
        .Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a)
        .CompareTo(
          System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(b)
        );
  }
}
