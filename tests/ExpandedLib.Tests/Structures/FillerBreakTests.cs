#if GAME_GE_1_22
using System;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A player breaking a megablock through one of its filler cells: the cell the break started
/// from after the principal's own break has run.</summary>
public class FillerBreakTests
{
  private static readonly BlockPos PrincipalPos = new(4, 4, 4);
  private static readonly BlockPos EastCell = new(5, 4, 4);
  private static readonly BlockPos OtherPrincipal = new(5, 3, 4);

  #region Breaking from a filler cell

  // Fails when the filler clears its cell without asking which principal the cell's filler now
  // belongs to.
  [Fact]
  public void A_cell_the_break_hands_to_another_principal_keeps_that_filler()
  {
    var (world, filler, mega) = NewWorld();
    world.Place(OtherPrincipal, filler);
    mega.OnRemoved = () =>
      world.Place(
        EastCell,
        filler,
        new BlockEntityStructureFiller { Principal = OtherPrincipal.Copy() }
      );

    Break(world, EastCell);

    Assert.Equal(0, world.Accessor.GetBlock(PrincipalPos).Id);
    Assert.Equal(filler.Id, world.Accessor.GetBlock(EastCell).Id);
    Assert.Equal(
      OtherPrincipal,
      Assert
        .IsType<BlockEntityStructureFiller>(
          world.Accessor.GetBlockEntity(EastCell)
        )
        .Principal
    );
  }

  // Fails when the filler stops clearing its own cell after the principal's break: a principal whose
  // removal leaves its fillers would leave the broken cell standing.
  [Fact]
  public void A_cell_the_principal_left_standing_is_cleared()
  {
    var (world, _, mega) = NewWorld();
    mega.KeepFillers = true;

    Break(world, EastCell);

    Assert.Equal(0, world.Accessor.GetBlock(PrincipalPos).Id);
    Assert.Equal(0, world.Accessor.GetBlock(EastCell).Id);
  }

  #endregion

  #region Helpers

  private static void Break(TestWorld world, BlockPos pos)
  {
    TestPlayer player = world.Player();
    player.Player.WorldData.CurrentGameMode.Returns(EnumGameMode.Survival);
    world.Accessor.BreakBlock(pos, player.Player);
  }

  /// <summary>A server world holding a principal at <see cref="PrincipalPos"/> with one filler cell
  /// east of it, linked to the principal.</summary>
  private static (
    TestWorld world,
    BlockStructureFiller filler,
    HandOverMega mega
  ) NewWorld()
  {
    var world = new TestWorld();
    // PlaceFillers and RemoveFillers are server-only.
    world.World.Side.Returns(EnumAppSide.Server);
    var filler = TestBlocks.Configure(
      new BlockStructureFiller(),
      "exlib:structurefiller",
      70
    );
    world.Register(filler);
    var mega = TestBlocks.Configure(
      new HandOverMega
      {
        Attributes = new JsonObject(
          JToken.Parse(
            "{ \"fillerOffsets\": [ { \"x\": 1, \"y\": 0, \"z\": 0 } ] }"
          )
        ),
      },
      "test:mega",
      71
    );
    world.Register(mega);
    // The engine assigns the api at registration; the principal's break spawns particles through it.
    ReflectionHelpers.SetField(mega, "api", world.Api);
    world.Place(PrincipalPos, mega);
    world.Place(
      EastCell,
      filler,
      new BlockEntityStructureFiller { Principal = PrincipalPos.Copy() }
    );
    return (world, filler, mega);
  }

  /// <summary>A megablock whose removal can leave its fillers standing or run a callback after they
  /// are cleared, as a neighbour that takes a freed cell back does.</summary>
  private sealed class HandOverMega : BlockFilledMegastructure
  {
    public bool KeepFillers;
    public Action? OnRemoved;

    public override int StructureAngle => 0;

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
      if (!KeepFillers)
        base.OnBlockRemoved(world, pos);
      OnRemoved?.Invoke();
    }
  }

  #endregion
}
#endif
