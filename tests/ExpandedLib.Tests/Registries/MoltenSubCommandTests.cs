using ExpandedLib.Industry.Helpers;
using ExpandedLib.Industry.Molten;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="MoltenSubCommand.Dispatch"/>: which target <c>/exmod molten</c> fills, who may run it and what it hands over.</summary>
public class MoltenSubCommandTests {
  private const string Iron = "game:ingot-iron";
  private const int Capacity = 40;

  // A vessel that takes up to Capacity units and records what it was handed.
  private sealed class VesselBlock : Block, IMoltenFillable {
    public int Refuse;
    public float LastTemperature;

    public int FillHeld(
      IWorldAccessor world,
      ItemStack vessel,
      ItemStack metal,
      int units
    ) {
      if (Refuse > 0)
        return 0;
      LastTemperature = MoltenMetal.GetTemperature(world, metal);
      int taken = System.Math.Min(units, Capacity);
      vessel.Attributes.SetInt("filled", taken);
      return taken;
    }
  }

  private sealed class SinkEntity : BlockEntity, ILiquidMetalSink {
    public int Room = 30;
    public bool Stuck;
    public bool NeedsBeginFill;
    public bool RefusesAny;
    public bool RefusesMetal;
    public int Received;
    public float Temperature;
    public Vec3d? Began;
    public int Overs;

    public bool CanReceiveAny => Room > 0 && !RefusesAny;

    public bool CanReceive(ItemStack key) => !RefusesMetal;

    public void BeginFill(Vec3d hitPosition) => Began = hitPosition;

    public void ReceiveLiquidMetal(
      ItemStack metal,
      ref int amount,
      float temperature
    ) {
      if (Stuck || (NeedsBeginFill && Began == null))
        return;
      int accepted = System.Math.Min(amount, Room);
      Received += accepted;
      amount -= accepted;
      Temperature = temperature;
    }

    public void OnPourOver() => Overs++;
  }

  private static (TestWorld world, TestPlayer player) Setup(
    EnumGameMode mode = EnumGameMode.Creative,
    bool op = false
  ) {
    var world = new TestWorld();
    world.RegisterItem(Iron, 1500f);
    TestPlayer player = world.Player();
    player.GameMode = mode;
    player.ServerPlayer!.HasPrivilege(Privilege.controlserver).Returns(op);
    player.Entity.World = world.World;
    return (world, player);
  }

  private static (VesselBlock block, ItemStack stack) Hold(
    TestWorld world,
    TestPlayer player
  ) {
    var block = TestBlocks.Configure(new VesselBlock(), "t:vessel", 70);
    world.Register(block);
    var stack = new ItemStack(block);
    player.Hold(stack);
    return (block, stack);
  }

  private static readonly Vec3d Hit = new(0.25, 0.5, 0.75);

  private static SinkEntity Look(TestWorld world, TestPlayer player) {
    var pos = new BlockPos(2, 3, 4);
    var sink = new SinkEntity();
    world.Place(pos, TestBlocks.Configure(new Block(), "t:sink", 71), sink);
    sink.Api = world.Api;
    player.Player.CurrentBlockSelection.Returns(
      new BlockSelection { Position = pos, HitPosition = Hit }
    );
    return sink;
  }

  // The player looks at a filler cell one block above and east of the sink's own cell.
  private static SinkEntity LookAtFillerCell(TestWorld world, TestPlayer player) {
    SinkEntity sink = Look(world, player);
    var cell = new BlockPos(3, 4, 4);
    world.Place(
      cell,
      TestBlocks.Configure(new Block(), "t:cell", 72),
      new BlockEntityStructureFiller { Principal = sink.Pos.Copy() }
    );
    player.Player.CurrentBlockSelection.Returns(
      new BlockSelection { Position = cell, HitPosition = Hit }
    );
    return sink;
  }

  private static TextCommandResult Run(
    TestPlayer player,
    string metal = "iron",
    int units = 100,
    float temperature = 0f
  ) =>
    MoltenSubCommand.Dispatch(
      player.ServerPlayer!,
      metal,
      units,
      temperature,
      "en"
    );

  // Fails when the units handed over are not the units typed: the vessel is offered one.
  [Fact]
  public void A_held_vessel_is_filled_with_the_units_typed_up_to_its_room() {
    var (world, player) = Setup();
    var (_, stack) = Hold(world, player);

    TextCommandResult result = Run(player, units: 25);

    Assert.Equal(EnumCommandStatus.Success, result.Status);
    Assert.Contains("command-molten-held", result.StatusMessage);
    Assert.Equal(25, stack.Attributes.GetInt("filled"));
  }

  // Fails when a vessel's refusal is reported as success or falls through to the look-at path.
  [Fact]
  public void A_held_vessel_that_takes_nothing_is_an_error_and_the_look_is_not_tried() {
    var (world, player) = Setup();
    var (block, _) = Hold(world, player);
    block.Refuse = 1;
    var sink = Look(world, player);

    TextCommandResult result = Run(player);

    Assert.Equal(EnumCommandStatus.Error, result.Status);
    Assert.Contains("command-molten-refused", result.StatusMessage);
    Assert.Equal(0, sink.Received);
  }

  // Fails when the sink path is dropped: the looked-at block receives nothing.
  [Fact]
  public void A_looked_at_sink_receives_up_to_its_room() {
    var (world, player) = Setup();
    var sink = Look(world, player);

    TextCommandResult result = Run(player, units: 100);

    Assert.Equal(EnumCommandStatus.Success, result.Status);
    Assert.Contains("command-molten-sink", result.StatusMessage);
    Assert.Equal(30, sink.Received);
  }

  // Fails when BeginFill is not called before the pour: a sink that takes metal only after it takes none.
  [Fact]
  public void A_sink_that_takes_metal_only_after_begin_fill_is_filled_at_the_hit() {
    var (world, player) = Setup();
    var sink = Look(world, player);
    sink.NeedsBeginFill = true;

    TextCommandResult result = Run(player, units: 100);

    Assert.Equal(EnumCommandStatus.Success, result.Status);
    Assert.Equal(30, sink.Received);
    Assert.Equal(Hit, sink.Began);
  }

  // Fails when OnPourOver is not called after the pour.
  [Fact]
  public void A_pour_into_a_sink_ends_with_one_pour_over() {
    var (world, player) = Setup();
    var sink = Look(world, player);

    Run(player);

    Assert.Equal(1, sink.Overs);
  }

  // Fails when the filler cell is not resolved to its principal, or the hit is not offset into the principal's frame.
  [Fact]
  public void A_looked_at_filler_cell_fills_its_principal_with_the_hit_offset() {
    var (world, player) = Setup();
    var sink = LookAtFillerCell(world, player);

    TextCommandResult result = Run(player);

    Assert.Equal(EnumCommandStatus.Success, result.Status);
    Assert.Equal(30, sink.Received);
    Assert.Equal(new Vec3d(1.25, 1.5, 0.75), sink.Began);
  }

  // Fails when the CanReceiveAny guard is dropped: a sink that says it takes nothing, but would take the pour, is filled.
  [Fact]
  public void A_sink_that_receives_nothing_now_is_refused_unpoured() {
    var (world, player) = Setup();
    var sink = Look(world, player);
    sink.RefusesAny = true;

    TextCommandResult result = Run(player);

    Assert.Equal(EnumCommandStatus.Error, result.Status);
    Assert.Equal(0, sink.Received);
  }

  // Fails when the CanReceive guard is dropped: a sink that refuses this metal, but would take the pour, is filled.
  [Fact]
  public void A_sink_that_refuses_the_metal_is_refused_unpoured() {
    var (world, player) = Setup();
    var sink = Look(world, player);
    sink.RefusesMetal = true;

    TextCommandResult result = Run(player);

    Assert.Equal(EnumCommandStatus.Error, result.Status);
    Assert.Equal(0, sink.Received);
  }

  // Fails when an item with no melting point is accepted as a metal.
  [Fact]
  public void An_item_with_no_melting_point_is_not_a_metal() {
    var (world, player) = Setup();
    world.RegisterItem("t:stick", 0f);
    var sink = Look(world, player);

    TextCommandResult result = Run(player, "t:stick");

    Assert.Equal(EnumCommandStatus.Error, result.Status);
    Assert.Contains("command-molten-unknown", result.StatusMessage);
    Assert.Equal(0, sink.Received);
  }

  // Fails when a sink with no room is reported as filled; the guards before it and the units-left check each reject it too, so each has its own test.
  [Fact]
  public void A_full_sink_is_refused() {
    var (world, player) = Setup();
    var sink = Look(world, player);
    sink.Room = 0;

    TextCommandResult result = Run(player);

    Assert.Equal(EnumCommandStatus.Error, result.Status);
    Assert.Contains("command-molten-refused", result.StatusMessage);
  }

  // Fails when the units left are not compared: a sink that keeps none is reported as filled.
  [Fact]
  public void A_sink_that_keeps_none_of_the_pour_is_refused() {
    var (world, player) = Setup();
    var sink = Look(world, player);
    sink.Stuck = true;

    TextCommandResult result = Run(player);

    Assert.Equal(EnumCommandStatus.Error, result.Status);
    Assert.Contains("command-molten-refused", result.StatusMessage);
  }

  // Fails when the look-at path runs with no selection or an unreceptive block: the reply is not the target error.
  [Fact]
  public void Nothing_held_and_nothing_looked_at_is_an_error() {
    var (_, player) = Setup();

    TextCommandResult result = Run(player);

    Assert.Equal(EnumCommandStatus.Error, result.Status);
    Assert.Contains("command-molten-target", result.StatusMessage);
  }

  // Fails when the privilege check is dropped: a survival player without controlserver fills the sink.
  [Fact]
  public void A_survival_player_who_is_not_an_op_is_refused() {
    var (world, player) = Setup(EnumGameMode.Survival, op: false);
    var sink = Look(world, player);

    TextCommandResult result = Run(player);

    Assert.Equal(EnumCommandStatus.Error, result.Status);
    Assert.Contains("command-molten-denied", result.StatusMessage);
    Assert.Equal(0, sink.Received);
  }

  // Fails when creative is the only way through: an op in survival is refused.
  [Fact]
  public void A_survival_op_is_served() {
    var (world, player) = Setup(EnumGameMode.Survival, op: true);
    var sink = Look(world, player);

    Run(player);

    Assert.Equal(30, sink.Received);
  }

  // Fails when privilege is the only way through: a creative player who is no op is refused.
  [Fact]
  public void A_creative_player_who_is_not_an_op_is_served() {
    var (world, player) = Setup(EnumGameMode.Creative, op: false);
    var sink = Look(world, player);

    Run(player);

    Assert.Equal(30, sink.Received);
  }

  // Fails when the margin is dropped: the pour is at the melting point itself.
  [Fact]
  public void The_default_temperature_is_the_melting_point_plus_the_margin() {
    var (world, player) = Setup();
    var sink = Look(world, player);

    Run(player);

    Assert.Equal(1500f + MoltenSubCommand.TemperatureMargin, sink.Temperature);
  }

  // Fails when the typed temperature is replaced by the default.
  [Fact]
  public void A_typed_temperature_is_used_as_given() {
    var (world, player) = Setup();
    var (block, _) = Hold(world, player);

    Run(player, temperature: 1777f);

    Assert.Equal(1777f, block.LastTemperature);
  }

  // Fails when a bare token is looked up as typed instead of as game:ingot-<token>.
  [Fact]
  public void A_metal_token_resolves_to_its_ingot_and_a_full_code_is_used_as_typed() {
    var (world, player) = Setup();
    world.RegisterItem("t:ingot-odd", 900f);
    var sink = Look(world, player);

    Assert.Equal(EnumCommandStatus.Success, Run(player, "iron").Status);
    Assert.Equal(EnumCommandStatus.Success, Run(player, "t:ingot-odd").Status);
    TextCommandResult unknown = Run(player, "nosuchmetal");

    Assert.Equal(EnumCommandStatus.Error, unknown.Status);
    Assert.Contains("command-molten-unknown", unknown.StatusMessage);
    Assert.Equal(60, sink.Received);
  }

  // Fails when the units guard is dropped: zero units reach the target.
  [Fact]
  public void Units_above_zero_are_required() {
    var (world, player) = Setup();
    var sink = Look(world, player);

    TextCommandResult result = Run(player, units: 0);

    Assert.Equal(EnumCommandStatus.Error, result.Status);
    Assert.Contains("command-molten-units", result.StatusMessage);
    Assert.Equal(0, sink.Received);
  }
}
