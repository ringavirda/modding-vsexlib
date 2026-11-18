using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Assembling;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Xunit;
using Scene = ExpandedLib.Tests.HandAssemblyFixTests.Scene;

namespace ExpandedLib.Tests;

/// <summary>The client's press of the right button driven at the game's cadence: a press at once, its repeats every 0.25 s while the button stays down, the offer recomputed every 0.1 s and a frame every 1/60 s.</summary>
public class HandAssemblyControlTests {
  private const int RepeatMs = 250;
  private const int RefreshMs = 100;
  private const int FramesPerSecond = 60;

  private static readonly BlockPos Cell = new(64, 16, 64, 0);
  private static readonly BlockPos Ground = new(64, 15, 64, 0);
  private static readonly BlockPos North = Cell.AddCopy(BlockFacing.NORTH);
  private static readonly BlockPos East = Cell.AddCopy(BlockFacing.EAST);

  internal sealed class SentPackets : IClientNetworkChannel {
    public List<object> Sent { get; } = [];

    public string ChannelName => HandAssemblyModSystem.ChannelName;

    public bool Connected => true;

    public IClientNetworkChannel RegisterMessageType(Type type) => this;

    public IClientNetworkChannel RegisterMessageType<T>() => this;

    public IClientNetworkChannel SetMessageHandler<T>(
      NetworkServerMessageHandler<T> handler
    ) => this;

    public void SendPacket<T>(T message) => Sent.Add(message!);

    INetworkChannel INetworkChannel.RegisterMessageType(Type type) => this;

    INetworkChannel INetworkChannel.RegisterMessageType<T>() => this;
  }

  internal sealed class Driver(Scene scene, BlockSelection aim) {
    public SentPackets Channel { get; } = new();
    public List<string> Refusals { get; } = [];
    public HandAssemblyControl Control { get; private set; } = null!;
    public List<int> Shown { get; } = [];
    private int _frame;

    public Driver Start() {
      Control = new HandAssemblyControl(
        Channel,
        (code, _) => Refusals.Add(code)
      );
      Control.Refresh(scene.World.World, scene.Player.Player, aim);
      return this;
    }

    public void Hold(double seconds, bool sneak) {
      int frames = (int)Math.Round(seconds * FramesPerSecond);
      double start = Ms(_frame);
      double nextPress = start;
      double nextRefresh = start + RefreshMs;
      for (int i = 0; i < frames; i++, _frame++) {
        double now = Ms(_frame);
        for (; nextRefresh <= now; nextRefresh += RefreshMs)
          Control.Refresh(scene.World.World, scene.Player.Player, aim);
        for (; nextPress <= now; nextPress += RepeatMs)
          Control.Press(scene.World.World, scene.Player.Player, aim, sneak);
        Control.Frame(true);
        Shown.Add(Control.Index);
      }
    }

    public void Up(double seconds) {
      int frames = (int)Math.Round(seconds * FramesPerSecond);
      for (int i = 0; i < frames; i++, _frame++)
        Control.Frame(false);
    }

    public void Click(bool sneak) {
      Hold(1.0 / FramesPerSecond, sneak);
      Up(1.0 / FramesPerSecond);
    }

    private static double Ms(int frame) => frame * 1000.0 / FramesPerSecond;
  }

  private static Scene Joined() {
    var scene = Scene.Standing(["nw", "nes"]);
    scene.Pipe(North, "ns");
    scene.Pipe(East, "we");
    scene.Bolts(10);
    return scene;
  }

  // Fails when the HUD does not read the part's own reason for an aim where it filtered every placement.
  [Fact]
  public void Refresh_ReadsThePartsReasonWhenItFilteredEveryPlacement() {
    var scene = Scene.Standing(["we"], refusing: "we", refusalCode: "wall");

    var driver = new Driver(
      scene,
      Scene.Select(Ground, BlockFacing.UP)
    ).Start();

    Assert.Null(driver.Control.Shown);
    Assert.Equal("wall", driver.Control.Refusal);
  }

  // Fails when the part's reason is read for a placement that arms or crossings had already ruled out.
  [Fact]
  public void Refresh_KeepsNoReasonWhenArmsRuledEveryPlacementOut() {
    var scene = Scene.Standing(["we"], refusing: "we", refusalCode: "wall");
    scene.Pipe(North, "ns");

    var driver = new Driver(
      scene,
      Scene.Select(North, BlockFacing.SOUTH)
    ).Start();

    Assert.Null(driver.Control.Shown);
    Assert.Null(driver.Control.Refusal);
  }

  // Fails when BeginFix no longer sends on the click, so a sneak press with a fix shown sends nothing.
  [Fact]
  public void Press_FixSendsOnTheClick() {
    Scene scene = Joined();
    var driver = new Driver(
      scene,
      Scene.Select(Ground, BlockFacing.UP)
    ).Start();
    AssemblyCandidate shown = driver.Control.Shown!;
    Assert.True(shown.Joints > 0);

    Assert.True(
      driver.Control.Press(
        scene.World.World,
        scene.Player.Player,
        Scene.Select(Ground, BlockFacing.UP),
        sneak: true
      )
    );

    AssemblyFix sent = Assert.IsType<AssemblyFix>(
      Assert.Single(driver.Channel.Sent)
    );
    AssemblyFix arrived = SerializerUtil.Deserialize<AssemblyFix>(
      SerializerUtil.Serialize(sent)
    );
    Assert.Null(
      HandAssemblyServer.Fix(scene.World.World, scene.Player.Player, arrived)
    );
    Assert.Equal(shown.Block, scene.World.World.BlockAccessor.GetBlock(Cell));
    Assert.True(scene.RecordAt(Cell).Fixed);
    Assert.Equal(10 - (shown.Joints * 2), scene.Carried);
    Assert.Empty(driver.Refusals);
  }

  // Fails when BeginTakeOff no longer sends on the click, so a sneak press with the tool in hand on a fixed piece sends nothing.
  [Fact]
  public void Press_TakeOffSendsOnTheClick() {
    var scene = Scene.Standing(["ns"]);
    scene.Bolts(10);
    Assert.Null(scene.Fix("ns", Ground, BlockFacing.UP));
    scene.Player.Hold(new ItemStack(scene.Wrench));
    scene.Player.Sneaking = true;
    var driver = new Driver(scene, Scene.Select(Cell, BlockFacing.UP)).Start();

    Assert.True(
      driver.Control.Press(
        scene.World.World,
        scene.Player.Player,
        Scene.Select(Cell, BlockFacing.UP),
        sneak: true
      )
    );

    AssemblyTakeOff sent = Assert.IsType<AssemblyTakeOff>(
      Assert.Single(driver.Channel.Sent)
    );
    Assert.Equal((Cell.X, Cell.Y, Cell.Z), (sent.X, sent.Y, sent.Z));
  }

  // Fails when the _pressed check is removed from Press, so each repeat of a held press sends the fix again.
  [Fact]
  public void Press_RepeatOfAHeldPressSendsOnce() {
    Scene scene = Joined();
    var driver = new Driver(
      scene,
      Scene.Select(Ground, BlockFacing.UP)
    ).Start();

    driver.Hold(1.6, sneak: true);

    Assert.IsType<AssemblyFix>(Assert.Single(driver.Channel.Sent));
  }

  // Fails when Frame with the button up does not end the press, so a second click after release sends nothing.
  [Fact]
  public void Press_AfterTheButtonCameUpSendsAgain() {
    Scene scene = Joined();
    var driver = new Driver(
      scene,
      Scene.Select(Ground, BlockFacing.UP)
    ).Start();

    driver.Click(sneak: true);
    driver.Click(sneak: true);

    Assert.Equal(2, driver.Channel.Sent.Count);
  }

  // Fails when a repeat of the held press steps again, or a press after the button came up does not.
  [Fact]
  public void A_held_plain_press_steps_once_and_the_next_press_steps_again() {
    var scene = Scene.Standing(["ns", "we", "ne"]);
    scene.Bolts(10);
    var driver = new Driver(
      scene,
      Scene.Select(Ground, BlockFacing.UP)
    ).Start();
    Assert.Equal(3, driver.Control.Count);
    Assert.Equal(0, driver.Control.Index);

    driver.Hold(1.6, sneak: false);
    Assert.Equal([1], driver.Shown.Distinct());
    driver.Up(0.1);
    driver.Shown.Clear();
    driver.Hold(0.6, sneak: false);

    Assert.Equal([2], driver.Shown.Distinct());
    Assert.Empty(driver.Channel.Sent);
  }

  // Fails when a held refused press shows its refusal again on each repeat.
  [Fact]
  public void A_held_refused_press_shows_its_refusal_once() {
    Scene scene = Joined();
    scene.Bolts(1);
    var driver = new Driver(
      scene,
      Scene.Select(Ground, BlockFacing.UP)
    ).Start();
    Assert.True(driver.Control.Short);

    driver.Hold(1.6, sneak: true);

    Assert.Equal([HandAssemblyServer.NeedsFasteners], driver.Refusals);
    Assert.Empty(driver.Channel.Sent);
  }

  // Fails when a press the game keeps is latched, so a repeat that finds a placement is not taken, or a release does not end the press.
  [Fact]
  public void A_press_not_taken_is_not_latched_and_a_release_ends_a_taken_one() {
    var scene = Scene.Standing(["ns", "we", "ne"]);
    HandAssemblyControl control = new Driver(
      scene,
      Scene.Select(Ground, BlockFacing.UP)
    )
      .Start()
      .Control;
    IWorldAccessor world = scene.World.World;
    BlockSelection aim = Scene.Select(Ground, BlockFacing.UP);

    Assert.False(control.Press(world, scene.Player.Player, null, false));
    Assert.True(control.Press(world, scene.Player.Player, aim, false));
    Assert.Equal(1, control.Index);
    control.Release();
    Assert.True(control.Press(world, scene.Player.Player, aim, false));
    Assert.Equal(2, control.Index);
  }
}
