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
      Control.Refresh(scene.World.World, scene.Player.Player, aim, null);
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
          Control.Refresh(scene.World.World, scene.Player.Player, aim, null);
        for (; nextPress <= now; nextPress += RepeatMs)
          Control.Press(
            scene.World.World,
            scene.Player.Player,
            aim,
            sneak,
            null
          );
        Control.Frame(1f / FramesPerSecond, true, sneak, null);
        Shown.Add(Control.Index);
      }
    }

    public void Up(double seconds, bool sneak) {
      int frames = (int)Math.Round(seconds * FramesPerSecond);
      for (int i = 0; i < frames; i++, _frame++)
        Control.Frame(1f / FramesPerSecond, false, sneak, null);
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

  // Fails when a repeat of the held press begins the hold again (nothing is ever sent) or begins a second hold after the first sent.
  [Fact]
  public void A_held_sneak_press_sends_one_fix_and_the_server_places_it_from_the_sent_bytes() {
    Scene scene = Joined();
    var driver = new Driver(
      scene,
      Scene.Select(Ground, BlockFacing.UP)
    ).Start();
    AssemblyCandidate shown = driver.Control.Shown!;
    Assert.True(shown.Joints > 0);

    driver.Hold(6, sneak: true);

    AssemblyFix sent = Assert.IsType<AssemblyFix>(
      Assert.Single(driver.Channel.Sent)
    );
    byte[] bytes = SerializerUtil.Serialize(sent);
    AssemblyFix arrived = SerializerUtil.Deserialize<AssemblyFix>(bytes);
    AssemblyResult? refusal = HandAssemblyServer.Fix(
      scene.World.World,
      scene.Player.Player,
      arrived
    );

    Assert.Null(refusal);
    Assert.Equal(shown.Block, scene.World.World.BlockAccessor.GetBlock(Cell));
    Assert.True(scene.RecordAt(Cell).Fixed);
    Assert.Equal(10 - (shown.Joints * 2), scene.Carried);
    Assert.Empty(driver.Refusals);
  }

  // Fails when the hold runs on after sneak is let go while the button stays down.
  [Fact]
  public void Letting_go_of_sneak_cancels_the_hold_and_sends_nothing() {
    Scene scene = Joined();
    var driver = new Driver(
      scene,
      Scene.Select(Ground, BlockFacing.UP)
    ).Start();

    driver.Hold(0.5, sneak: true);
    Assert.True(driver.Control.Progress > 0f);
    driver.Hold(5, sneak: false);

    Assert.Empty(driver.Channel.Sent);
    Assert.Equal(0f, driver.Control.Progress);
  }

  // Fails when the hold runs on after the button comes up while sneak is still held.
  [Fact]
  public void The_button_coming_up_cancels_the_hold_and_sends_nothing() {
    Scene scene = Joined();
    var driver = new Driver(
      scene,
      Scene.Select(Ground, BlockFacing.UP)
    ).Start();

    driver.Hold(0.5, sneak: true);
    Assert.True(driver.Control.Progress > 0f);
    driver.Up(5, sneak: true);

    Assert.Empty(driver.Channel.Sent);
    Assert.Equal(0f, driver.Control.Progress);
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
    driver.Up(0.1, sneak: false);
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

    Assert.False(control.Press(world, scene.Player.Player, null, false, null));
    Assert.True(control.Press(world, scene.Player.Player, aim, false, null));
    Assert.Equal(1, control.Index);
    control.Release(null);
    Assert.True(control.Press(world, scene.Player.Player, aim, false, null));
    Assert.Equal(2, control.Index);
  }
}
