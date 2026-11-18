using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Assembling;
using ExpandedLib.Blocks;
using ExpandedLib.Helpers;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using ExpandedLib.Scenarios;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Live;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A cap fitted to an open crossing of a fixed piece from the main hand with the wrench in the off hand, or taken off it by the wrench click: what it checks, what it takes and returns, and what a fitted cap does to the piece's networks, break and take-off.</summary>
public class HandAssemblyCapTests {
  private static readonly BlockPos Cell = new(64, 16, 64, 0);
  private static readonly string[] Axis = ["ns", "we", "ud"];

  private const string CastSpec =
    """{"handAssembly":{"tool":"test:wrench-*","fastener":"test:bolt","perJoint":4,"cap":"test:cap-cast"}}""";

  private const string PlatedSpec =
    """{"handAssembly":{"tool":"test:wrench-*","fastener":"test:bolt-bundle","perJoint":2,"cap":"test:cap-plated"}}""";

  private const string NoCapSpec =
    """{"handAssembly":{"tool":"test:wrench-*","fastener":"test:bolt","perJoint":4}}""";

  private static Vec3d Hit(BlockFacing face) =>
    new(
      0.5 + 0.5 * face.Normali.X,
      0.5 + 0.5 * face.Normali.Y,
      0.5 + 0.5 * face.Normali.Z
    );

  #region Held help

  // Fails when the cap item is not given the behaviour, or its help drops the sneak-hold gesture or the off-hand tool.
  [Fact]
  public void CapHeldHelp_NamesTheGesture() {
    var scene = CapScene.Standing();

    int attached = CollectibleBehaviorCapHelp.AttachTo(scene.World.Api);

    Assert.Equal(1, attached);
    var handling = EnumHandling.PassThrough;
    WorldInteraction help = Assert.Single(
      scene
        .CastCap.CollectibleBehaviors.OfType<CollectibleBehaviorCapHelp>()
        .Single()
        .GetHeldInteractionHelp(scene.Player.ActiveSlot, ref handling)
    );
    Assert.Equal(ExlibLang.BlockhelpHandassemblyCap, help.ActionLangCode);
    Assert.Equal(EnumMouseButton.Right, help.MouseButton);
    Assert.Equal("shift", help.HotKeyCode);
    Assert.Equal(scene.Wrench, Assert.Single(help.Itemstacks).Collectible);
    Assert.Empty(scene.Bolt.CollectibleBehaviors);
  }

  // Fails when Attach on the client appends nothing, so the held help never shows.
  [Fact]
  public void ClientAttach_GivesTheCapItsHelp() {
    var scene = CapScene.Standing();

    int attached = CollectibleBehaviorCapHelp.Attach(scene.World.ClientApi);

    Assert.Equal(1, attached);
    Assert.Single(
      scene.CastCap.CollectibleBehaviors.OfType<CollectibleBehaviorCapHelp>()
    );
  }

  // Fails when Attach puts the client-only help on a server item, whose type packet then names a behaviour class no registry holds.
  [Fact]
  public void ServerAttach_LeavesEveryBehaviourWithAClassName() {
    var scene = CapScene.Standing();
    CollectibleObject[] corpus =
    [
      .. Premise.NotEmpty(
        scene
          .World.World.Items.Where(i => i?.Code != null)
          .Cast<CollectibleObject>()
          .Concat(scene.World.World.Blocks.Where(b => b is { Id: not 0 })),
        "items and blocks of the cap scene"
      ),
    ];
    Assert.Contains(scene.CastCap, corpus);
    HashSet<Type> registered =
    [
      .. corpus
        .SelectMany(c => c.CollectibleBehaviors)
        .Select(b => b.GetType()),
    ];
    var registry = Substitute.For<IClassRegistryAPI>();
    registry
      .GetCollectibleBehaviorClassName(default!)
      .ReturnsForAnyArgs(ci =>
        registered.Contains(ci.Arg<Type>()) ? "named" : null
      );
    registry
      .GetBlockBehaviorClassName(default!)
      .ReturnsForAnyArgs(ci =>
        registered.Contains(ci.Arg<Type>()) ? "named" : null
      );

    CollectibleBehaviorCapHelp.Attach(scene.World.Api);

    string[] unnamed =
    [
      .. corpus
        .SelectMany(c =>
          c.CollectibleBehaviors.Select(b => (Code: c.Code.ToString(), B: b))
        )
        .Where(e =>
          (
            e.B is BlockBehavior
              ? registry.GetBlockBehaviorClassName(e.B.GetType())
              : registry.GetCollectibleBehaviorClassName(e.B.GetType())
          ) == null
        )
        .Select(e => $"{e.Code}: {e.B.GetType().FullName}")
        .Distinct()
        .Order(),
    ];
    Assert.True(
      unnamed.Length == 0,
      "Behaviours with no registered class name:\n" + string.Join("\n", unnamed)
    );
  }

  #endregion

  #region Fitting

  // Fails when the cap fitted is not taken from the main hand, or the fasteners of the def's joint are not taken.
  [Fact]
  public void FitCap_SpendsCapAndJointFasteners() {
    var scene = CapScene.Standing(bolts: 10);

    Assert.Null(scene.Fit(BlockFacing.NORTH));

    Assert.Equal(1, scene.Player.ActiveSlot.StackSize);
    Assert.Equal(6, scene.Carried(scene.Bolt));
    Assert.Equal(
      "test:cap-cast",
      scene.Record.CapAt(BlockFacing.NORTH)!.Collectible.Code.ToString()
    );
    Assert.Equal(4, scene.Record.CapFastenersAt(BlockFacing.NORTH)!.StackSize);
    Assert.Null(scene.Record.CapAt(BlockFacing.SOUTH));
  }

  // Fails when the fit wears the main hand's stack, or nothing, and not the wrench in the off hand.
  [Fact]
  public void FitCap_WearsTheOffHandWrench() {
    var scene = CapScene.Standing();

    Assert.Null(scene.Fit(BlockFacing.NORTH));

    Assert.Equal(1, scene.Wear);
  }

  // Fails when the fit caps another arm than the face the packet names.
  [Fact]
  public void FitCap_FitsTheFaceThePacketNames() {
    var scene = CapScene.Standing();

    Assert.Null(scene.Fit(BlockFacing.SOUTH));

    Assert.NotNull(scene.Record.CapAt(BlockFacing.SOUTH));
    Assert.Null(scene.Record.CapAt(BlockFacing.NORTH));
  }

  // Fails when air beyond an end is read as closing it.
  [Fact]
  public void FitCap_FitsOnEndFacingAir() {
    var scene = CapScene.Standing();

    Assert.Null(scene.Fit(BlockFacing.NORTH));

    Assert.NotNull(scene.Record.CapAt(BlockFacing.NORTH));
  }

  // Fails when the fasteners are spent as one, not as the def's perJoint, or the def's own fastener is not the one spent.
  [Fact]
  public void FitCap_PlatedFitSpendsTwoBoltBundles() {
    var scene = CapScene.Standing(PlatedSpec, cap: "plated");
    scene.Give(scene.Bundle, 5);

    Assert.Null(scene.Fit(BlockFacing.NORTH));

    Assert.Equal(3, scene.Carried(scene.Bundle));
    Assert.Equal(2, scene.Record.CapFastenersAt(BlockFacing.NORTH)!.StackSize);
    Assert.Equal(
      "test:cap-plated",
      scene.Record.CapAt(BlockFacing.NORTH)!.Collectible.Code.ToString()
    );
  }

  // Fails when the clear-cell check is dropped from OpenRefusal, so an end whose next cell holds a pipe side, a mismatched mouth, a machine wall, a canal or a part block takes a cap.
  [Theory]
  [InlineData("air", null)]
  [InlineData("full block", HandAssemblyServer.CapClosed)]
  [InlineData("joined pipe", HandAssemblyServer.CapJoined)]
  [InlineData("turned pipe", HandAssemblyServer.CapNoRoom)]
  [InlineData("other family", HandAssemblyServer.CapNoRoom)]
  [InlineData("passthrough arm", HandAssemblyServer.CapJoined)]
  [InlineData("passthrough wall", HandAssemblyServer.CapClosed)]
  [InlineData("filler host", HandAssemblyServer.CapNoRoom)]
  [InlineData("canal", HandAssemblyServer.CapNoRoom)]
  [InlineData("solid on top only", HandAssemblyServer.CapNoRoom)]
  [InlineData("unloaded", HandAssemblyServer.CapJoined)]
  public void FitCap_AnswersByWhatStandsInFront(string beside, string? code) {
    var scene = CapScene.Standing();
    scene.Beside(BlockFacing.NORTH, beside);

    AssemblyResult? result = scene.Fit(BlockFacing.NORTH);

    Assert.Equal(code, result?.Code);
    Assert.Equal(code == null, scene.Record.CapAt(BlockFacing.NORTH) != null);
  }

  // Fails when the check that the held stack is still the cap the packet names is dropped.
  [Fact]
  public void FitCap_RefusedWhenTheCapLeftTheSlot() {
    var scene = CapScene.Standing();
    AssemblyCap fit = scene.Packet(BlockFacing.NORTH);
    ItemStack caps = scene.Player.ActiveSlot.Itemstack!;
    scene.Player.Hold(null);

    Assert.Equal(HandAssemblyServer.PartMoved, scene.Fit(fit)!.Code);

    scene.Player.Hold(caps);
    scene.AssertNothingTaken();
  }

  // Fails when the player's distance to the piece is not checked.
  [Fact]
  public void FitCap_RefusedBeyondThePickingRange() {
    var scene = CapScene.Standing();
    scene.Player.Entity.Pos.SetPos(Cell.X + 40, Cell.Y, Cell.Z);

    Assert.Equal(
      HandAssemblyServer.OutOfRange,
      scene.Fit(BlockFacing.NORTH)!.Code
    );

    scene.AssertNothingTaken();
  }

  // Fails when the land claim is not asked.
  [Fact]
  public void FitCap_RefusedInAClaimedCell() {
    var scene = CapScene.Standing();
    scene
      .World.World.Claims.TryAccess(
        Arg.Any<IPlayer>(),
        Arg.Any<BlockPos>(),
        Arg.Any<EnumBlockAccessFlags>()
      )
      .Returns(false);

    Assert.Equal(
      HandAssemblyServer.Claimed,
      scene.Fit(BlockFacing.NORTH)!.Code
    );

    scene.AssertNothingTaken();
  }

  // Fails when a piece that was never fixed takes a cap.
  [Fact]
  public void FitCap_RefusedOnAPieceThatIsNotFixed() {
    var scene = CapScene.Standing(fixedPiece: false);

    Assert.Equal(
      HandAssemblyServer.NotOffered,
      scene.Fit(BlockFacing.NORTH)!.Code
    );

    scene.AssertNothingTaken();
  }

  // Fails when a def that declares no cap takes the cast cap the player holds.
  [Fact]
  public void FitCap_DefWithoutACapOffersNothing() {
    var scene = CapScene.Standing(NoCapSpec);

    Assert.Equal(
      HandAssemblyServer.NotOffered,
      scene.Fit(BlockFacing.NORTH)!.Code
    );

    scene.AssertNothingTaken();
  }

  // Fails when a face that carries a cap already takes a second one.
  [Fact]
  public void FitCap_RefusedOnACappedFace() {
    var scene = CapScene.Standing();
    Assert.Null(scene.Fit(BlockFacing.NORTH));

    Assert.Equal(
      HandAssemblyServer.Occupied,
      scene.Fit(BlockFacing.NORTH)!.Code
    );

    Assert.Equal(1, scene.Player.ActiveSlot.StackSize);
    Assert.Equal(6, scene.Carried(scene.Bolt));
  }

  // Fails when the wrench in the off hand is not required: a wrench elsewhere in the hotbar fits nothing.
  [Fact]
  public void FitCap_RefusedWithoutTheWrenchInTheOffHand() {
    var scene = CapScene.Standing();
    scene.Player.HoldOffHand(null);
    scene.Player.Hotbar[2].Itemstack = new ItemStack(scene.Wrench);

    Assert.Equal(
      HandAssemblyServer.NeedsTool,
      scene.Fit(BlockFacing.NORTH)!.Code
    );

    scene.AssertNothingTaken();
  }

  // Fails when a face joined to the pipe beside it is capped.
  [Fact]
  public void FitCap_RefusedOnJoinedFace() {
    var scene = CapScene.Standing();
    scene.Beside(BlockFacing.NORTH, "joined pipe");

    Assert.Equal(
      HandAssemblyServer.CapJoined,
      scene.Fit(BlockFacing.NORTH)!.Code
    );

    scene.AssertNothingTaken();
  }

  // Fails when the closing-block check is dropped.
  [Fact]
  public void FitCap_RefusedBesideSolidFace() {
    var scene = CapScene.Standing();
    scene.Beside(BlockFacing.NORTH, "full block");

    Assert.Equal(
      HandAssemblyServer.CapClosed,
      scene.Fit(BlockFacing.NORTH)!.Code
    );

    scene.AssertNothingTaken();
  }

  // Fails when the fasteners the joint needs are not counted before the cap is taken.
  [Fact]
  public void FitCap_RefusedWithoutEnoughFasteners() {
    var scene = CapScene.Standing(bolts: 3);

    Assert.Equal(
      HandAssemblyServer.NeedsFasteners,
      scene.Fit(BlockFacing.NORTH)!.Code
    );

    Assert.Equal(2, scene.Player.ActiveSlot.StackSize);
    Assert.Equal(3, scene.Carried(scene.Bolt));
    Assert.Null(scene.Record.CapAt(BlockFacing.NORTH));
    Assert.Equal(0, scene.Wear);
  }

  // Fails when creative takes the cap or the fasteners, wears the wrench, or asks for it.
  [Fact]
  public void FitCap_CreativeSpendsNothing() {
    var scene = CapScene.Standing(bolts: 0);
    scene.Player.GameMode = EnumGameMode.Creative;
    scene.Player.HoldOffHand(null);

    Assert.Null(scene.Fit(BlockFacing.NORTH));

    Assert.Equal(2, scene.Player.ActiveSlot.StackSize);
    Assert.NotNull(scene.Record.CapAt(BlockFacing.NORTH));
    Assert.Null(scene.Record.CapFastenersAt(BlockFacing.NORTH));
    Assert.Equal(0, scene.Wear);
  }

  // Fails when the wrench click fits the cap the off hand holds.
  [Fact]
  public void WrenchClick_DoesNotFitTheOffHandCap() {
    var scene = CapScene.Standing();
    scene.CastCap.StorageFlags =
      EnumItemStorageFlags.General | EnumItemStorageFlags.Offhand;
    scene.Player.Hotbar[2].Itemstack = scene.Player.ActiveSlot.Itemstack;
    scene.Player.Hold(new ItemStack(scene.Wrench));
    scene.Player.HoldOffHand(scene.Player.Hotbar[2].Itemstack);
    scene.Player.Hotbar[2].Itemstack = null;

    scene.Rotate(BlockFacing.NORTH);

    Assert.Null(scene.Record.CapAt(BlockFacing.NORTH));
    Assert.Equal(2, scene.Player.OffHandSlot.StackSize);
    Assert.Equal(10, scene.Carried(scene.Bolt));
    Assert.Contains(
      scene.Player.ServerPlayer!.ReceivedCalls(),
      call =>
        call.GetMethodInfo().Name == "SendIngameError"
        && (string?)call.GetArguments()[0] == HandAssemblyServer.Fixed
    );
  }

  #endregion

  #region The cap gesture

  private static BlockSelection Aim(BlockFacing face) =>
    new() {
      Position = Cell.Copy(),
      Face = BlockFacing.UP,
      HitPosition = Hit(face),
    };

  private static HandAssemblyControl Gesture(
    CapScene scene,
    BlockFacing face,
    bool sneak,
    double seconds,
    HandAssemblyControlTests.SentPackets channel,
    out bool taken
  ) {
    var control = new HandAssemblyControl(channel, (_, _) => { });
    taken = control.Press(
      scene.World.World,
      scene.Player.Player,
      Aim(face),
      sneak,
      null
    );
    for (int frame = 0; frame < seconds * 60; frame++)
      control.Frame(1f / 60, true, sneak, null);
    return control;
  }

  // Fails when a held cap aimed at a fixed piece's open end is not offered, so the sneak hold sends nothing.
  [Fact]
  public void CapGesture_HoldSendsAssemblyCapAndTheServerFits() {
    var scene = CapScene.Standing(bolts: 10);
    var channel = new HandAssemblyControlTests.SentPackets();

    Gesture(scene, BlockFacing.NORTH, sneak: true, 6, channel, out bool taken);

    Assert.True(taken);
    AssemblyCap sent = Assert.IsType<AssemblyCap>(Assert.Single(channel.Sent));
    AssemblyCap arrived = SerializerUtil.Deserialize<AssemblyCap>(
      SerializerUtil.Serialize(sent)
    );
    Assert.Null(scene.Fit(arrived));
    Assert.NotNull(scene.Record.CapAt(BlockFacing.NORTH));
    Assert.Equal(6, scene.Carried(scene.Bolt));
  }

  // Fails when a press without sneak begins the cap hold, so the game's own use of the click is skipped.
  [Fact]
  public void CapGesture_PlainClickDoesNothing() {
    var scene = CapScene.Standing();
    var channel = new HandAssemblyControlTests.SentPackets();

    Gesture(scene, BlockFacing.NORTH, sneak: false, 3, channel, out bool taken);

    Assert.False(taken);
    Assert.Empty(channel.Sent);
    scene.AssertNothingTaken();
  }

  // Fails when the open-arm filter is restored in AimCap, so an end shut by a block beside it snaps the aim to the far open end.
  [Fact]
  public void CapGesture_AimsTheEndNearestTheCrosshair() {
    var scene = CapScene.Standing();
    scene.Beside(BlockFacing.NORTH, "full block");
    var channel = new HandAssemblyControlTests.SentPackets();

    HandAssemblyControl control = Gesture(
      scene,
      BlockFacing.NORTH,
      sneak: false,
      0,
      channel,
      out _
    );

    Assert.Equal(BlockFacing.NORTH, control.CapFace);
    Assert.Equal(HandAssemblyServer.CapClosed, control.CapRefusal);
    Assert.True(control.Short);
  }

  #endregion

  #region Taking off

  // Fails when the wrench click through Rotate does not take off the cap of the end nearest the hit.
  [Fact]
  public void WrenchClick_TakesOffACap() {
    var scene = CapScene.Standing(bolts: 10);
    Assert.Null(scene.Fit(BlockFacing.NORTH));
    Assert.Null(scene.Fit(BlockFacing.SOUTH));

    scene.Rotate(BlockFacing.NORTH);

    Assert.Null(scene.Record.CapAt(BlockFacing.NORTH));
    Assert.NotNull(scene.Record.CapAt(BlockFacing.SOUTH));
    Assert.Equal(1, scene.Carried(scene.CastCap));
    Assert.Equal(6, scene.Carried(scene.Bolt));
  }

  // Fails when taking a cap off does not give the cap back.
  [Fact]
  public void Cap_TakeOffReturnsCapAndFasteners() {
    var scene = CapScene.Standing(bolts: 10);
    Assert.Null(scene.Fit(BlockFacing.NORTH));
    Assert.Equal(1, scene.Carried(scene.CastCap));

    Assert.Null(scene.Click(BlockFacing.NORTH));

    Assert.Equal(10, scene.Carried(scene.Bolt));
    Assert.Equal(2, scene.Carried(scene.CastCap));
    Assert.Null(scene.Record.CapAt(BlockFacing.NORTH));
    Assert.Null(scene.Record.CapFastenersAt(BlockFacing.NORTH));
  }

  // Fails when the fasteners a cap carried are not given back.
  [Fact]
  public void Cap_TakeOffReturnsTheFastenersItCarried() {
    var scene = CapScene.Standing(bolts: 10);
    Assert.Null(scene.Fit(BlockFacing.NORTH));
    Assert.Equal(6, scene.Carried(scene.Bolt));

    Assert.Null(scene.Click(BlockFacing.NORTH));

    Assert.Equal(10, scene.Carried(scene.Bolt));
  }

  // Fails when a click on an end with no cap is not answered as a fixed piece.
  [Fact]
  public void Cap_ClickOnAnUncappedEndAnswersFixed() {
    var scene = CapScene.Standing();
    Assert.Null(scene.Fit(BlockFacing.SOUTH));

    Assert.Equal(
      HandAssemblyServer.Fixed,
      scene.Click(BlockFacing.NORTH)!.Code
    );

    Assert.NotNull(scene.Record.CapAt(BlockFacing.SOUTH));
  }

  // Fails when the fit does not mark the cell for redraw, so a client draws no cap until the chunk is sent again.
  [Fact]
  public void Cap_FitMarksTheCellForRedraw() {
    var scene = CapScene.Standing();
    scene.World.TakeRedrawMark(Cell);

    Assert.Null(scene.Fit(BlockFacing.NORTH));

    Assert.True(scene.World.TakeRedrawMark(Cell));
  }

  // Fails when the take-off does not mark the cell for redraw, so a client keeps drawing the cap.
  [Fact]
  public void Cap_TakeOffMarksTheCellForRedraw() {
    var scene = CapScene.Standing();
    Assert.Null(scene.Fit(BlockFacing.NORTH));
    scene.World.TakeRedrawMark(Cell);

    Assert.Null(scene.Click(BlockFacing.NORTH));

    Assert.True(scene.World.TakeRedrawMark(Cell));
  }

  // Fails when the fit leaves the piece in the network built before the cap, not in one built afresh.
  [Fact]
  public void Cap_FitRebuildsTheNetwork() {
    var scene = CapScene.Lumped(bolts: 10);
    BlockNetwork? before = scene.World.NetworkAt(Cell);

    Assert.Null(scene.Fit(BlockFacing.UP));

    BlockNetwork? after = scene.World.NetworkAt(Cell);
    Assert.NotNull(after);
    Assert.NotSame(before, after);
    Assert.Contains(Cell, after.Nodes);
  }

  // Fails when a take-off needs a cap held, or is refused to a player with a full pack instead of dropping at the feet.
  [Fact]
  public void Cap_TakeOffWithAFullPackDropsAtTheFeet() {
    var scene = CapScene.Standing(bolts: 4);
    Assert.Null(scene.Fit(BlockFacing.NORTH));
    scene.Player.Hold(null);
    scene.FillPack();

    Assert.Null(scene.Click(BlockFacing.NORTH));

    Assert.Contains(scene.World.Drops, drop => drop.Item == scene.CastCap);
    Assert.Null(scene.Record.CapAt(BlockFacing.NORTH));
  }

  // Fails when creative takes the cap and fasteners back.
  [Fact]
  public void Cap_CreativeTakeOffReturnsNothing() {
    var scene = CapScene.Standing(bolts: 10);
    Assert.Null(scene.Fit(BlockFacing.NORTH));
    scene.Player.GameMode = EnumGameMode.Creative;

    Assert.Null(scene.Click(BlockFacing.NORTH));

    Assert.Equal(6, scene.Carried(scene.Bolt));
    Assert.Equal(1, scene.Carried(scene.CastCap));
    Assert.Null(scene.Record.CapAt(BlockFacing.NORTH));
  }

  // Fails when taking the cap off a lumped pipe's end does not rebuild its network, so a pipe laid beside while it was capped stays apart.
  [Fact]
  public void Cap_TakeOffReopens() {
    var scene = CapScene.Lumped(bolts: 10);
    Assert.Equal(
      ExlibValues.LitresPerPipe - ExlibValues.GasLeakRate,
      scene.Ticked(),
      3
    );

    Assert.Null(scene.Fit(BlockFacing.UP));
    Assert.Equal(ExlibValues.LitresPerPipe, scene.Ticked(), 3);
    BlockPos above = scene.JoinAbove();
    Assert.NotSame(scene.World.NetworkAt(Cell), scene.World.NetworkAt(above));
    scene.Rotate(BlockFacing.UP);

    Assert.Same(scene.World.NetworkAt(Cell), scene.World.NetworkAt(above));
  }

  #endregion

  #region The scenario player's step

  // Fails when the live run's wear departs from the hands': a fit wears the wrench once, a refused fit and creative not at all, a take-off once.
  [Theory]
  [InlineData("fit", 1)]
  [InlineData("refusal", 0)]
  [InlineData("creative", 0)]
  [InlineData("take-off", 2)]
  public void Cap_PlayWearsTheWrenchOnEachFitAndTakeOffOutsideCreative(
    string click,
    int wear
  ) {
    var scene = CapScene.Standing();
    scene.Player.Hold(null);
    scene.Player.HoldOffHand(null);
    if (click == "refusal")
      scene.Beside(BlockFacing.NORTH, "full block");
    if (click == "creative")
      scene.Player.GameMode = EnumGameMode.Creative;
    var ctx = new ScenarioContext(
      scene.World.World,
      new ScenarioWorld(
        scene.World.World,
        new BlockPos(0, 0, 0),
        scene.World.Networks
      ),
      () => 0,
      new ScenarioErrors([]),
      _ => { },
      "case.json"
    );
    AssemblyResult? Play() =>
      HandAssemblyPlay.Cap(
        ctx,
        scene.World.World,
        scene.Player.Player,
        Cell,
        BlockFacing.NORTH
      );

    AssemblyResult? refused = Play();
    if (click == "take-off")
      refused = Play();

    Assert.Equal(
      click == "refusal" ? HandAssemblyServer.CapClosed : null,
      refused?.Code
    );
    Assert.Equal(
      click == "take-off" ? null
        : click is "fit" or "creative" ? "test:cap-cast"
        : null,
      scene.Record.CapAt(BlockFacing.NORTH)?.Collectible.Code.ToString()
    );
    Assert.Equal(wear, scene.Wear);
  }

  #endregion

  #region Breaking and taking the piece off

  // Fails when breaking the piece returns the fasteners the cap carried.
  [Fact]
  public void Cap_BreakLosesFasteners() {
    var scene = CapScene.Standing(bolts: 10);
    Assert.Null(scene.Fit(BlockFacing.NORTH));

    scene.World.Accessor.BreakBlock(Cell, null);

    Assert.Equal(6, scene.Carried(scene.Bolt));
    Assert.All(
      scene.World.Drops,
      drop => Assert.NotSame(scene.Bolt, drop.Item)
    );
  }

  // Fails when the cap is not dropped with the piece.
  [Fact]
  public void Cap_BreakDropsTheCapWithThePiece() {
    var scene = CapScene.Standing(bolts: 10);
    Assert.Null(scene.Fit(BlockFacing.NORTH));

    scene.World.Accessor.BreakBlock(Cell, null);

    Assert.Contains(scene.World.Drops, drop => drop.Item == scene.CastCap);
  }

  // Fails when a creative break drops the cap.
  [Fact]
  public void Cap_CreativeBreakDropsNoCap() {
    var scene = CapScene.Standing(bolts: 10);
    Assert.Null(scene.Fit(BlockFacing.NORTH));
    scene.Player.GameMode = EnumGameMode.Creative;

    scene.World.Accessor.BreakBlock(Cell, scene.Player.Player);

    Assert.DoesNotContain(
      scene.World.Drops,
      drop => drop.Item == scene.CastCap
    );
  }

  // Fails when taking the whole piece off leaves its caps behind.
  [Fact]
  public void Cap_PieceTakeOffReturnsItsCaps() {
    var scene = CapScene.Standing(bolts: 10);
    Assert.Null(scene.Fit(BlockFacing.NORTH));
    scene.Player.Hold(null);
    int before = scene.Carried(scene.CastCap);

    Assert.Null(scene.TakeOffPiece());

    Assert.Equal(before + 1, scene.Carried(scene.CastCap));
  }

  // Fails when taking the whole piece off keeps the fasteners its caps carried.
  [Fact]
  public void Cap_PieceTakeOffReturnsTheCapFasteners() {
    var scene = CapScene.Standing(bolts: 10);
    Assert.Null(scene.Fit(BlockFacing.NORTH));
    Assert.Equal(6, scene.Carried(scene.Bolt));

    Assert.Null(scene.TakeOffPiece());

    Assert.Equal(10, scene.Carried(scene.Bolt));
  }

  #endregion

  private sealed class CapScene {
    private readonly TestNetworkBlock[] _family = [];
    private readonly LumpedNode? _lumped;
    private int _nextId = 200;

    private CapScene(TestWorld world, string spec, string? cap, int bolts) {
      World = world;
      Player = world.Player();
      Player.StandAt(Cell, BlockFacing.SOUTH);
      Player.GameMode = EnumGameMode.Survival;
      Wrench = new WearCounter {
        Code = new AssetLocation("test:wrench-iron"),
        ItemId = 900,
        Durability = 100,
        StorageFlags =
          EnumItemStorageFlags.General | EnumItemStorageFlags.Offhand,
      };
      world.Register(Wrench);
      Bolt = world.RegisterItem("test:bolt");
      Bundle = world.RegisterItem("test:bolt-bundle");
      CastCap = world.RegisterItem("test:cap-cast");
      PlatedCap = world.RegisterItem("test:cap-plated");
      Player.Hold(
        cap == "plated"
          ? new ItemStack(PlatedCap, 2)
          : new ItemStack(CastCap, 2)
      );
      Player.Hotbar[1].Itemstack =
        bolts > 0 ? new ItemStack(Bolt, bolts) : null;
      Player.HoldOffHand(new ItemStack(Wrench));
      Player.Player.InventoryManager.Inventories["backpack-test"] =
        TestInventory.Of(world, 4, "backpack-test");
      Spec = spec;
    }

    private string Spec { get; }
    public TestWorld World { get; }
    public TestPlayer Player { get; }
    public Item Wrench { get; }
    public int Wear => ((WearCounter)Wrench).Uses;
    public Item Bolt { get; }
    public Item Bundle { get; }
    public Item CastCap { get; }
    public Item PlatedCap { get; }
    public BEBehaviorAssembled Record { get; private set; } = null!;

    public static CapScene Standing(
      string spec = CastSpec,
      string cap = "cast",
      int bolts = 10,
      bool fixedPiece = true
    ) {
      var world = new TestWorld();
      world.RegisterNetwork("test", sys => new StubNetwork(sys));
      world.Place(
        Cell.AddCopy(BlockFacing.DOWN),
        TestBlocks.Configure(new Block(), "game:rock", 5)
      );
      var scene = new CapScene(world, spec, cap, bolts);
      TestNetworkBlock[] family = TestNetworkBlock.Family(
        "test",
        "test:node",
        "Axis",
        Axis
      );
      foreach (TestNetworkBlock block in family) {
        block.Attributes = new JsonObject(JToken.Parse(spec));
        world.Register(block);
      }
      var entity = new OrientableNode {
        Orientation = "ns",
        PossibleOrientations = Axis,
      };
      scene.Record = new BEBehaviorAssembled(entity) { Fixed = fixedPiece };
      entity.Behaviors.Add(scene.Record);
      world.Place(Cell, family.First(b => b.Orientation == "ns"), entity);
      world.Initialize(entity);
      return scene;
    }

    public static CapScene Lumped(int bolts) {
      var world = new TestWorld();
      world.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
      var scene = new CapScene(world, CastSpec, "cast", bolts);
      var entity = new BlockEntityAssembled();
      scene.Record = new BEBehaviorAssembled(entity) { Fixed = true };
      entity.Behaviors.Add(scene.Record);
      LumpedNode node = TestBlocks.Configure(
        new LumpedNode(),
        "test:lumped",
        300
      );
      node.Attributes = new JsonObject(JToken.Parse(CastSpec));
      world.Place(Cell, node, entity);
      world.AddNode(Cell, "pipe");
      return scene;
    }

    public BlockPos JoinAbove() {
      BlockPos pos = Cell.AddCopy(BlockFacing.UP);
      var entity = new BlockEntityAssembled();
      LumpedNode node = TestBlocks.Configure(
        new LumpedNode(BlockFacing.DOWN),
        "test:lumped-above",
        301
      );
      World.Place(pos, node, entity);
      World.AddNode(pos, "pipe");
      return pos;
    }

    public int Carried(Item item) =>
      ExCarried.Count(Player.Player, item.Code.ToString());

    public void Give(Item item, int count) =>
      Assert.True(
        Player.Player.InventoryManager.TryGiveItemstack(
          new ItemStack(item, count),
          true
        )
      );

    public AssemblyCap Packet(BlockFacing face) =>
      new() {
        X = Cell.X,
        Y = Cell.Y,
        Z = Cell.Z,
        Dimension = Cell.dimension,
        Face = face.Index,
        Slot = Player.ActiveNumber,
        CapCode =
          Player.ActiveSlot.Itemstack?.Collectible.Code.ToString() ?? "",
      };

    public AssemblyResult? Fit(BlockFacing face) => Fit(Packet(face));

    public AssemblyResult? Fit(AssemblyCap fit) =>
      HandAssemblyServer.FitCap(World.World, Player.Player, fit);

    /// <summary>Takes the wrench into the main hand, moving what the main hand held to hotbar slot 2.</summary>
    private void WrenchInHand() {
      if (Player.ActiveSlot.Itemstack?.Item == Wrench)
        return;
      Player.Hotbar[2].Itemstack = Player.ActiveSlot.Itemstack;
      Player.Hold(new ItemStack(Wrench));
    }

    public AssemblyResult? Click(BlockFacing face) {
      WrenchInHand();
      Player.Sneaking = false;
      return HandAssemblyServer.Cap(
        World.World,
        Player.Player,
        Cell,
        Hit(face)
      );
    }

    public void Rotate(BlockFacing face) {
      WrenchInHand();
      ((BlockNetworkNode)World.GetBlock(Cell)).Rotate(
        Player.Entity,
        new BlockSelection {
          Position = Cell.Copy(),
          Face = BlockFacing.UP,
          HitPosition = Hit(face),
        },
        1
      );
    }

    public AssemblyResult? TakeOffPiece() {
      Player.Hold(new ItemStack(Wrench));
      Player.Sneaking = true;
      return HandAssemblyServer.TakeOff(
        World.World,
        Player.Player,
        new AssemblyTakeOff {
          X = Cell.X,
          Y = Cell.Y,
          Z = Cell.Z,
        }
      );
    }

    public float Ticked() {
      var net = (PipeNetwork)World.NetworkAt(Cell)!;
      net.RestoreState(
        new PipeNetworkState {
          Volume = ExlibValues.LitresPerPipe,
          MediumType = "Steam",
        }
      );
      World.Tick();
      return net.State!.Volume;
    }

    public void FillPack() {
      Item stone = World.RegisterItem("test:stone");
      foreach (
        ItemSlot slot in Player
          .Player.InventoryManager.GetHotbarInventory()
          .Concat(Player.Player.InventoryManager.GetOwnInventory("backpack"))
      )
        if (slot.Empty)
          slot.Itemstack = new ItemStack(stone);
    }

    public void AssertNothingTaken() {
      Assert.Equal(2, Player.ActiveSlot.StackSize);
      Assert.Equal(10, Carried(Bolt));
      Assert.Null(Record.CapAt(BlockFacing.NORTH));
      Assert.Null(Record.CapFastenersAt(BlockFacing.NORTH));
      Assert.Equal(0, Wear);
    }

    public void Beside(BlockFacing face, string kind) {
      BlockPos pos = Cell.AddCopy(face);
      switch (kind) {
        case "joined pipe":
          World.Place(pos, TestNetworkBlock.Create("test", "ns", _nextId++));
          World.AddNode(pos, "test");
          break;
        case "turned pipe":
          TestNetworkBlock turned = TestNetworkBlock.Create(
            "test",
            "we",
            _nextId++
          );
          turned.SideSolid = new SmallBoolArray(0);
          World.Place(pos, turned);
          World.AddNode(pos, "test");
          break;
        case "other family":
          World.Place(pos, new MouthNode(_nextId++));
          World.AddNode(pos, "test");
          break;
        case "passthrough arm":
          TestNetworkBlock arm = TestNetworkBlock.Create(
            "test",
            "ns",
            _nextId++
          );
          arm.SideSolid = new SmallBoolArray(
            0x3F & ~(1 << BlockFacing.SOUTH.Index)
          );
          World.Place(pos, arm);
          World.AddNode(pos, "test");
          break;
        case "passthrough wall":
          TestNetworkBlock wall = TestNetworkBlock.Create(
            "test",
            "we",
            _nextId++
          );
          wall.SideSolid = new SmallBoolArray(0x3F);
          World.Place(pos, wall);
          World.AddNode(pos, "test");
          break;
        case "canal":
          World.Place(pos, Solid(new Block(), 0));
          break;
        case "unloaded":
          World.UnloadChunkAt(pos);
          break;
        case "air":
          break;
        case "filler host":
          World.Place(pos, Solid(new FillerHost(), 0x3F));
          break;
        case "solid on top only":
          World.Place(pos, Solid(new Block(), 1 << BlockFacing.UP.Index));
          break;
        default:
          World.Place(pos, Solid(new Block(), 0x3F));
          break;
      }
    }

    private Block Solid(Block block, int sides) {
      TestBlocks.Configure(block, $"test:solid-{_nextId}", _nextId++);
      block.SideSolid = new SmallBoolArray(sides);
      return block;
    }
  }

  private sealed class WearCounter : Item {
    public int Uses;

    public override void DamageItem(
      IWorldAccessor world,
      Entity byEntity,
      ItemSlot itemslot,
      int amount = 1,
      bool destroyOnZeroDurability = true
    ) => Uses += amount;
  }

  private sealed class FillerHost : Block, IFillerHost {
    public JsonObject? FillerOffsets => null;
  }

  private sealed class MouthNode : BlockNetworkNode {
    public MouthNode(int id) {
      TestBlocks.Configure(this, $"test:mouth-{id}", id);
      SideSolid = new SmallBoolArray(0);
      Type = "straight";
      Orientation = "ns";
    }

    public override string NetworkType => "test";

    public override bool HasConnectorAt(BlockFacing face) =>
      face == BlockFacing.SOUTH;

    public override bool AcceptsNeighbour(Block neighbour) => false;
  }

  private sealed class LumpedNode : BlockNetworkNode {
    private readonly BlockFacing _arm;

    public LumpedNode(BlockFacing? arm = null) {
      _arm = arm ?? BlockFacing.UP;
      Type = "straight";
      Orientation = _arm.Code[..1];
    }

    public override string NetworkType => "pipe";

    public override bool HasConnectorAt(BlockFacing face) => face == _arm;
  }
}
