using System.Linq;
using ExpandedLib.Industry;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The break guard on molten holders (<see cref="MoltenBreakGuard"/>): a liquid holder refuses an
/// ordinary break, a sneaking, Creative or set one goes through.</summary>
public class MoltenBreakGuardTests {
  private const string Iron = "game:ingot-iron";
  private static readonly BlockPos Pos = new(64, 16, 64, 0);

  public enum Kind {
    Canal,
    Barrel,
    Hosted,
  }

  private sealed class CanalEntity : BlockEntity, IMoltenCell {
    public int CellAmount { get; set; }
    public string CellMetalType => Iron;
    public float CellTemperature => 1450f;
    public int MaxUnitCapacity => 100;
    public bool Sealed => false;
    public bool Solidified { get; set; }
    public bool IsFlowSource => false;
    public bool AcceptsSubMinimumFlow => false;

    public void EnsureMetalStack(IWorldAccessor world) { }

    public int PushMetalRaw(
      int amount,
      string metalType,
      float temperature,
      IWorldAccessor world
    ) => 0;

    public int DrainMetal(int amount) => 0;

    public void UpdateThermal(IWorldAccessor world) { }
  }

  private sealed class BarrelEntity : BlockEntity, IMoltenSurface {
    public bool Liquid;

    public float? SurfaceTemperature => 1450f;

    public bool SurfaceLiquid => Liquid;
  }

  private sealed class Rig {
    public readonly TestWorld World = new();
    public readonly Block Block;
    public readonly TestPlayer Player;

    public Rig(Kind kind, bool liquid, bool carriesBurn = true) {
      World.RegisterItem(Iron, 1500f);
      Block = TestBlocks.Configure(
        new BlockStructureFiller(),
        "exlib:holder",
        70
      );
      World.Register(Block);
      if (carriesBurn) {
        var burn = new BlockBehaviorMoltenBurn(Block);
        Block.BlockBehaviors = [burn];
        Block.CollectibleBehaviors = [burn];
      }
      PlaceHolder(Pos, kind, liquid);
      Player = World.Player("breaker");
      Player.GameMode = EnumGameMode.Survival;
    }

    public void PlaceHolder(BlockPos pos, Kind kind, bool liquid) {
      switch (kind) {
        case Kind.Canal:
          World.Place(
            pos,
            Block,
            new CanalEntity { CellAmount = 50, Solidified = !liquid }
          );
          break;
        case Kind.Barrel:
          World.Place(pos, Block, new BarrelEntity { Liquid = liquid });
          break;
        default:
          var be = new BlockEntityStructureFiller();
          World.Place(pos, Block, be);
          World.Attach(be);
          var cell = new BEBehaviorMoltenCell(be);
          be.Behaviors.Add(cell);
          cell.ConfigureFromFiller(
            null,
            null,
            new JsonObject(JToken.Parse("{\"solidifies\":true}"))
          );
          cell.PushMetalRaw(50, Iron, liquid ? 1600f : 300f, World.World);
          cell.UpdateThermal(World.World);
          break;
      }
    }

    public EnumHandling Break(BlockPos? at = null) {
      var handling = EnumHandling.PassThrough;
      float drop = 1f;
      MoltenBreakGuard.OnBreak(
        Player.ServerPlayer!,
        new BlockSelection { Position = at ?? Pos },
        ref drop,
        ref handling
      );
      return handling;
    }

    public int Refusals() =>
      Player
        .ServerPlayer!.ReceivedCalls()
        .Count(c =>
          c.GetMethodInfo().Name == nameof(IServerPlayer.SendIngameError)
          && Equals(c.GetArguments()[0], MoltenBreakGuard.ErrorCode)
        );
  }

  // Fails when the guard skips a kind of holder: hosted cells, parked content or a block entity's own cell.
  [Theory]
  [InlineData(Kind.Canal)]
  [InlineData(Kind.Barrel)]
  [InlineData(Kind.Hosted)]
  public void Guard_LiquidRefused(Kind kind) {
    var rig = new Rig(kind, liquid: true);

    Assert.Equal(EnumHandling.PreventDefault, rig.Break());
    Assert.Equal(1, rig.Refusals());
  }

  // Fails when the sneak test is removed: the sneaking break is refused too.
  [Theory]
  [InlineData(Kind.Canal)]
  [InlineData(Kind.Barrel)]
  [InlineData(Kind.Hosted)]
  public void Guard_SneakAllows(Kind kind) {
    var rig = new Rig(kind, liquid: true);
    rig.Player.Sneaking = true;

    Assert.Equal(EnumHandling.PassThrough, rig.Break());
    Assert.Equal(0, rig.Refusals());
  }

  // Fails when the liquid test is removed: a set holder is refused.
  [Theory]
  [InlineData(Kind.Canal)]
  [InlineData(Kind.Barrel)]
  [InlineData(Kind.Hosted)]
  public void Guard_SetAllows(Kind kind) {
    var rig = new Rig(kind, liquid: false);

    Assert.Equal(EnumHandling.PassThrough, rig.Break());
    Assert.Equal(0, rig.Refusals());
  }

  // Fails when the Creative test is removed.
  [Fact]
  public void Guard_CreativeAllows() {
    var rig = new Rig(Kind.Canal, liquid: true);
    rig.Player.GameMode = EnumGameMode.Creative;

    Assert.Equal(EnumHandling.PassThrough, rig.Break());
  }

  // Fails when the behaviour test is removed: a block the scan never gave the burn is guarded.
  [Fact]
  public void Guard_BlockWithoutTheBehaviourIsIgnored() {
    var rig = new Rig(Kind.Canal, liquid: true, carriesBurn: false);

    Assert.Equal(EnumHandling.PassThrough, rig.Break());
  }

  // Fails when a filler's break is judged by its own entity only: the liquid is in the principal.
  [Fact]
  public void Guard_FillerBreakChecksItsPrincipal() {
    var rig = new Rig(Kind.Barrel, liquid: true);
    BlockPos filler = Pos.AddCopy(1, 0, 0);
    var be = new BlockEntityStructureFiller { Principal = Pos.Copy() };
    rig.World.Place(filler, rig.Block, be);
    rig.World.Attach(be);

    Assert.Equal(EnumHandling.PreventDefault, rig.Break(filler));
  }

  // Fails when Register subscribes nothing, or StartServerSide does not call it.
  [Fact]
  public void Register_SubscribesTheBreakEvent() {
    var api = Substitute.For<ICoreServerAPI>();

    new IndustryModule().StartServerSide(api);

    api.Event.Received().BreakBlock += Arg.Any<BlockBreakDelegate>();
  }
}
