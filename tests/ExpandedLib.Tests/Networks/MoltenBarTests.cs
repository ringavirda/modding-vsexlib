using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Networks;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Covers <see cref="MoltenBar"/>: a held crowbar breaks the struck cell and the connected set cells of
/// the same metal out of a run, hot, up to the cell limit, one durability a cell.
/// </summary>
public class MoltenBarTests {
  #region Fixtures

  private const string Iron = "game:ingot-iron";
  private const string Copper = "game:ingot-copper";
  private const int Durability = 100;

  private sealed class Rig {
    public readonly TestWorld World = new();
    public readonly TestPlayer Player;
    public readonly ItemStack Bar;
    public readonly List<BarCell> Cells = [];
    private int _ids;

    public Rig() {
      World.RegisterItem("game:metalbit-iron");
      World.RegisterItem("game:metalbit-copper");
      Item crowbar = World.RegisterItem("game:crowbar-iron");
      crowbar.Tool = EnumTool.Crowbar;
      crowbar.Durability = Durability;
      Bar = new ItemStack(crowbar);
      Player = World.Player("breaker");
      Player.GameMode = EnumGameMode.Survival;
    }

    public BarCell Put(
      int x,
      string metal = Iron,
      bool set = true,
      float temperature = 1000f,
      bool hardened = false,
      params BlockFacing[] faces
    ) {
      var cell = new BarCell {
        CellMetalType = metal,
        CellAmount = 12,
        Set = set,
        CellTemperature = temperature,
        Hardened = hardened,
      };
      BlockFacing[] open =
        faces.Length > 0 ? faces : [BlockFacing.WEST, BlockFacing.EAST];
      World.Place(new BlockPos(x, 0, 0), BarNode.Make(++_ids, open), cell);
      World.Attach(cell);
      Cells.Add(cell);
      return cell;
    }

    public BlockPos At(int x) => new(x, 0, 0);

    public bool Use(int x, ItemStack? held = null, float seconds = 1f) =>
      Player.Interact(At(x), held ?? Bar, seconds: seconds);

    public int Remaining => Bar.Collectible.GetRemainingDurability(Bar);

    public int Refusals() =>
      Player
        .ServerPlayer!.ReceivedCalls()
        .Count(c =>
          c.GetMethodInfo().Name == "SendIngameError"
          && Equals(c.GetArguments()[0], MoltenBar.LiquidError)
        );
  }

  private sealed class BarNode : BlockNetworkNode {
    public BlockFacing[] Faces { get; set; } = [];

    public override string NetworkType => "molten";

    public override bool HasConnectorAt(BlockFacing face) =>
      Array.IndexOf(Faces, face) >= 0;

    public override bool OnBlockInteractStart(
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection blockSel
    ) => MoltenBar.Start(world, byPlayer, blockSel);

    public override bool OnBlockInteractStep(
      float secondsUsed,
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection blockSel
    ) => MoltenBar.Step(secondsUsed, byPlayer);

    public override void OnBlockInteractStop(
      float secondsUsed,
      IWorldAccessor world,
      IPlayer byPlayer,
      BlockSelection blockSel
    ) => MoltenBar.Stop(secondsUsed, world, byPlayer, blockSel);

    public static BarNode Make(int id, params BlockFacing[] faces) =>
      TestBlocks.Configure(
        new BarNode { Faces = faces },
        $"test:moltenbarnode-{id}",
        id
      );
  }

  private sealed class BarCell : BlockEntity, IMoltenCell, IChiselableMolten {
    public int CellAmount { get; set; }
    public string CellMetalType { get; set; } = "";
    public float CellTemperature { get; set; }
    public bool Set { get; set; }
    public bool Hardened { get; set; }
    public int MaxUnitCapacity => 50;
    public bool Sealed => false;
    public bool Solidified => Set;
    public bool IsFlowSource => false;
    public bool AcceptsSubMinimumFlow => false;

    public bool HasChiselableContent => Set && CellAmount > 0;
    public bool CanChiselOut => HasChiselableContent && Hardened;
    public bool CanBreakOut => HasChiselableContent;
    public string? ChiselBlockedError => null;

    public ItemStack? ChiselOut() {
      string bit = CellMetalType.Replace("ingot", "metalbit");
      var drop = new ItemStack(Api.World.GetItem(new AssetLocation(bit)), 2);
      CellAmount = 0;
      CellMetalType = "";
      return drop;
    }

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

  private sealed class LoneHolder : BlockEntity, IChiselableMolten {
    public bool Cleared;
    public bool HasChiselableContent => !Cleared;
    public bool CanChiselOut => !Cleared;
    public bool CanBreakOut => !Cleared;
    public string? ChiselBlockedError => null;

    public ItemStack? ChiselOut() {
      Cleared = true;
      return null;
    }
  }

  #endregion

  #region Break-out

  // Fails when Start gates on CanChiselOut: the skull at 1000 C is set but above the hardened threshold,
  // so the use breaks nothing.
  [Fact]
  public void Bar_BreaksOutSetHotSkull() {
    var rig = new Rig();
    BarCell cell = rig.Put(0, temperature: 1000f, hardened: false);

    Assert.True(rig.Use(0));

    Assert.Equal(0, cell.CellAmount);
    (ItemStack drop, Vec3d at) = Assert.Single(rig.World.Spawns);
    Assert.Equal("game:metalbit-iron", drop.Collectible.Code.ToString());
    Assert.Equal(new Vec3d(0.5, 0.5, 0.5), at);
  }

  // Fails when Start drops its CanBreakOut gate: the bar claims a liquid cell and sends no refusal.
  [Fact]
  public void Bar_RefusesLiquid() {
    var rig = new Rig();
    BarCell cell = rig.Put(0, set: false, temperature: 1450f);

    Assert.False(rig.Use(0));

    Assert.Equal(12, cell.CellAmount);
    Assert.Empty(rig.World.Spawns);
    Assert.Equal(1, rig.Refusals());
    Assert.Equal(Durability, rig.Remaining);
  }

  // Fails when the stretch ignores MoltenBarCells: ten set cells all clear. Struck mid-run, the eight nearest
  // go, so the two end cells stay.
  [Fact]
  public void Bar_StopsAtLimit() {
    var rig = new Rig();
    for (int x = 0; x < 10; x++)
      rig.Put(x);

    rig.Use(4);

    Assert.Equal(
      [12, 0, 0, 0, 0, 0, 0, 0, 0, 12],
      rig.Cells.Select(c => c.CellAmount)
    );
  }

  // Fails when the stretch drops its metal test: the copper skull and the iron beyond it clear with the run.
  [Fact]
  public void Bar_StopsAtOtherMetal() {
    var rig = new Rig();
    rig.Put(0);
    rig.Put(1);
    rig.Put(2, Copper);
    rig.Put(3);

    rig.Use(0);

    Assert.Equal([0, 0, 12, 12], rig.Cells.Select(c => c.CellAmount));
  }

  // Fails when the stretch drops its set-content test: it walks through the liquid cell and clears the set
  // cell beyond.
  [Fact]
  public void Bar_StopsAtLiquidCell() {
    var rig = new Rig();
    rig.Put(0);
    rig.Put(1, set: false, temperature: 1450f);
    rig.Put(2);

    rig.Use(0);

    Assert.Equal([0, 12, 12], rig.Cells.Select(c => c.CellAmount));
  }

  // Fails when the stretch drops the connector test: the cell with no connector west of it clears with the run.
  [Fact]
  public void Bar_StopsAtClosedJoint() {
    var rig = new Rig();
    rig.Put(0);
    rig.Put(1, faces: [BlockFacing.EAST]);
    rig.Put(2);

    rig.Use(0);

    Assert.Equal([0, 12, 12], rig.Cells.Select(c => c.CellAmount));
  }

  // Fails when the wear is once a use: three cells clear and the bar loses 1.
  [Fact]
  public void Bar_WearsOneACell() {
    var rig = new Rig();
    for (int x = 0; x < 3; x++)
      rig.Put(x);

    rig.Use(0);

    Assert.Equal(Durability - 3, rig.Remaining);
  }

  // Fails when the wear skips the Creative test: a Creative use still costs the bar durability.
  [Fact]
  public void Bar_CreativeBreaksOutWithoutWear() {
    var rig = new Rig();
    rig.Player.GameMode = EnumGameMode.Creative;
    rig.Put(0);
    rig.Put(1);

    rig.Use(0, seconds: 0.2f);

    Assert.Equal([0, 0], rig.Cells.Select(c => c.CellAmount));
    Assert.Equal(Durability, rig.Remaining);
  }

  // Fails when Stop acts on any hold: a quarter second breaks the skull out.
  [Fact]
  public void Bar_ShortHoldBreaksNothing() {
    var rig = new Rig();
    BarCell cell = rig.Put(0);

    rig.Use(0, seconds: 0.3f);

    Assert.Equal(12, cell.CellAmount);
    Assert.Empty(rig.World.Spawns);
  }

  // Fails when Start skips the tool test: a chisel starts the hold.
  [Fact]
  public void Bar_IgnoresOtherTools() {
    var rig = new Rig();
    rig.Put(0);
    var chisel = new ItemStack(
      new Item {
        Code = new AssetLocation("game:chisel-iron"),
        Tool = EnumTool.Chisel,
      }
    );

    Assert.False(rig.Use(0, chisel));
  }

  // Fails when the claim test goes: a player the claims refuse breaks the skull out.
  [Fact]
  public void Bar_RespectsLandClaims() {
    var rig = new Rig();
    BarCell cell = rig.Put(0);
    rig.World.World.Claims.TryAccess(
        Arg.Any<IPlayer>(),
        Arg.Any<BlockPos>(),
        EnumBlockAccessFlags.BuildOrBreak
      )
      .Returns(false);

    Assert.False(rig.Use(0));
    Assert.Equal(12, cell.CellAmount);
  }

  // Fails when a holder that is no IMoltenCell walks like one: its neighbour (a set canal cell) clears too.
  [Fact]
  public void Bar_BreaksALoneHolderAlone() {
    var rig = new Rig();
    var lone = new LoneHolder();
    rig.World.Place(rig.At(0), BarNode.Make(90, BlockFacing.EAST), lone);
    rig.World.Attach(lone);
    BarCell next = rig.Put(1);

    rig.Use(0);

    Assert.True(lone.Cleared);
    Assert.Equal(12, next.CellAmount);
  }

  #endregion
}
