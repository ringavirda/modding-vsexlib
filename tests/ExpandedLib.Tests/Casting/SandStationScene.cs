using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Industry.Casting;
using ExpandedLib.Industry.Metals;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedLib.Tests.Casting;

/// <summary>A placed sand station on a hand-built block with a real <see cref="BEBehaviorMoltenCell"/>, a
/// player, a launder cell and captured error codes. Registers its stations into
/// <see cref="CastingStationRegistry.Shared"/>; the owning test class resets it.</summary>
internal sealed class SandStationScene {
  public const string Station = "boxflask";
  public const string OtherStation = "otherflask";
  public const string SandCode = "game:sand-granite";
  public const string Metal = "game:ingot-bronze";
  public const string PartCode = "test:cast-part";

  private const string Stations = """
    {
      "schema": 1,
      "stations": [
        { "code": "boxflask", "moulds": ["open", "closed"],
          "sand": { "base": "test:casting/base", "half": "test:casting/half", "item": "game:sand-*" } },
        { "code": "otherflask", "moulds": ["open"],
          "sand": { "base": "test:casting/base", "half": "test:casting/half", "item": "game:sand-*" } }
      ]
    }
    """;

  private static readonly BlockPos At = new(32, 8, 32, 0);

  private readonly DummySlot _hotbar = new();
  private readonly List<string> _errors = [];
  private LaunderCell? _launder;

  /// <summary>The launder cell's block entity: holds metal a station drains.</summary>
  public sealed class LaunderCell : BlockEntity, IMoltenCell {
    public int CellAmount { get; set; }
    public string CellMetalType { get; set; } = "";
    public float CellTemperature { get; set; }
    public int MaxUnitCapacity => 1000;
    public bool Sealed => false;
    public bool Solidified => false;
    public bool IsFlowSource => false;
    public bool AcceptsSubMinimumFlow => false;

    public void EnsureMetalStack(IWorldAccessor world) { }

    public int PushMetalRaw(
      int amount,
      string metalType,
      float temperature,
      IWorldAccessor world
    ) => 0;

    public int DrainMetal(int amount) {
      int taken = Math.Min(amount, CellAmount);
      CellAmount -= taken;
      return taken;
    }

    public void UpdateThermal(IWorldAccessor world) { }
  }

  private SandStationScene(
    TestWorld world,
    BlockEntitySandStation station,
    BEBehaviorMoltenCell molten
  ) {
    World = world;
    Entity = station;
    Molten = molten;
    Player = BuildPlayer();
  }

  public TestWorld World { get; }

  public BlockEntitySandStation Entity { get; }

  public BEBehaviorMoltenCell Molten { get; }

  public IPlayer Player { get; }

  public string? LastError => _errors.Count == 0 ? null : _errors[^1];

  public ItemStack? Held => _hotbar.Itemstack;

  public IReadOnlyList<ItemStack> Harvested => World.Drops;

  public string ScrapCode =>
    MetalRegistry.SolidDropOf(new AssetLocation(Metal)).ToString();

  public static SandStationScene Build(
    string side = "n",
    SandStationLevel sand = SandStationLevel.Full,
    Func<BlockEntitySandStation>? make = null,
    string? cup = null
  ) {
    CastingStationRegistry.ResetForWorld();
    CastingStationLoader.Load(
      [("test:config/castingstations/scene.json", Stations)],
      CastingStationRegistry.Shared
    );

    var world = new TestWorld();
    var block = TestBlocks.Configure(
      new BlockSandStation(),
      "test:station-" + side,
      140,
      side == "" ? [] : [("side", side)]
    );
    block.Attributes = new JsonObject(
      JToken.Parse(
        $$"""{ "sandStation": { "station": "{{Station}}"{{(
          cup == null ? "" : $", \"cup\": {cup}"
        )}} } }"""
      )
    );
    BlockEntitySandStation be = make?.Invoke() ?? new BlockEntitySandStation();
    world.Place(At, block, be);
    world.Attach(be);

    var molten = new BEBehaviorMoltenCell(be);
    be.Behaviors.Add(molten);
    molten.ConfigureFromFiller(null, null, new JsonObject(JToken.Parse("{}")));

    ReflectionHelpers.SetField(be, "_sand", sand);
    world.RegisterItem(Metal, 950f);
    world.RegisterItem(
      MetalRegistry.SolidDropOf(new AssetLocation(Metal)).ToString()
    );
    return new SandStationScene(world, be, molten);
  }

  public ItemStack PatternStack(
    string type,
    int units = 100,
    int pieces = 1,
    float minPourTemp = 1000f,
    string mould = "open",
    int share = 0,
    params string[] stations
  ) {
    stations = stations.Length == 0 ? [Station] : stations;
    string stationJson = string.Join(
      ",",
      stations.Select(s =>
        $$"""{ "size": "{{s}}", "shape": "test:casting/{{type}}", "pieces": {{pieces}}, "mould": "{{mould}}", "share": {{share}}, "cavity": [{ "x1": 3, "y1": 1, "z1": 3, "x2": 13, "y2": 4, "z2": 13 }] }"""
      )
    );
    var attributes = JToken.Parse(
      $$"""
      {
        "mold": {
          "units": {{units}},
          "output": { "type": "item", "code": "{{PartCode}}" },
          "minPourTemp": {{minPourTemp}},
          "stations": [{{stationJson}}]
        }
      }
      """
    );
    string code = $"test:pattern-{type}";
    Item item =
      World.GetItem(new AssetLocation(code)) ?? World.RegisterItem(code);
    item.Attributes = new JsonObject(attributes);
    item.Durability = 64;
    if (World.GetItem(new AssetLocation(PartCode)) == null)
      World.RegisterItem(PartCode);
    return new ItemStack(item);
  }

  public ItemStack SandStack(string code = SandCode) =>
    new(World.GetItem(new AssetLocation(code)) ?? World.RegisterItem(code));

  public bool Interact(ItemStack? held) {
    _errors.Clear();
    _hotbar.Itemstack = held;
    return Entity.OnInteract(Player);
  }

  public SandStationScene Tick() {
    ReflectionHelpers.Invoke(Entity, "OnServerTick", 1f);
    return this;
  }

  public SandStationScene Pour(int units, float temp) {
    Molten.PushMetalRaw(units, Metal, temp, World.World);
    return this;
  }

  public SandStationScene CoolToHardened() {
    Tick();
    World.AdvanceHours(200);
    return Tick();
  }

  public LaunderCell Launder(int units, float temp) {
    if (_launder == null) {
      BlockPos pos = Entity.Pos.SouthCopy();
      var block = TestBlocks.Configure(new Block(), "test:launder", 150);
      _launder = new LaunderCell();
      World.Place(pos, block, _launder);
      World.Attach(_launder);
    }
    _launder.CellAmount = units;
    _launder.CellMetalType = Metal;
    _launder.CellTemperature = temp;
    return _launder;
  }

  private IPlayer BuildPlayer() {
    var player = Substitute.For<IServerPlayer>();
    var inventory = Substitute.For<IPlayerInventoryManager>();
    inventory.ActiveHotbarSlot.Returns(_hotbar);
    player.InventoryManager.Returns(inventory);

    var data = Substitute.For<IWorldPlayerData>();
    data.CurrentGameMode.Returns(EnumGameMode.Survival);
    player.WorldData.Returns(data);

    var entity = Substitute.For<EntityPlayer>();
    entity.Pos.SetPos(At.X + 0.5, At.Y + 0.5, At.Z + 1.5);
    entity.SelectionBox = new Cuboidf(0, 0, 0, 0.6f, 1.8f, 0.6f);
    player.Entity.Returns(entity);

    player
      .When(p =>
        p.SendIngameError(
          Arg.Any<string>(),
          Arg.Any<string>(),
          Arg.Any<object[]>()
        )
      )
      .Do(ci => _errors.Add(ci.ArgAt<string>(0)));
    return player;
  }
}
