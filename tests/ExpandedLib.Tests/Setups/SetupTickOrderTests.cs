#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Testing;
using Vintagestory.GameContent.Mechanics;
using Xunit;

namespace ExpandedLib.Tests.Setups;

/// <summary>The tick orders a setup runs in (setups.md, Tick orders): the orders <c>tickOrders</c>
/// names and how a scene fires its listeners in one.</summary>
[Collection("Setups")]
public class SetupTickOrderTests
{
  private static readonly string FixturePath = Path.Combine(
    RepoPaths.Root,
    "tests",
    "ExpandedLib.Tests",
    "Setups",
    "Fixtures",
    "SetupHold"
  );

  private static readonly IReadOnlyList<(string Mod, string Version)> Mods =
  [
    ("exlib", "0.8.4"),
    ("setuphold", "1.0.0"),
  ];

  private static string Place(string id, int x, string code = "ticker") =>
    $"{{\"place\": {{\"id\": \"{id}\", \"code\": \"setuphold:{code}\", \"at\": [{x}, 0, 0]}}}}";

  internal static void Register(TestWorld w, List<string> log)
  {
    SetupHoldTests.Register(w);
    RegisterTick(w, log);
  }

  internal static TestWorld RegisterTick(TestWorld w, List<string> log) =>
    w.RegisterClass("SetupTickEntity", typeof(SetupTickEntity))
      .RegisterBlockEntityFactory(
        "SetupTickEntity",
        () => new SetupTickEntity(log)
      )
      .RegisterBlockEntityBehaviorFactory(
        "SetupTickBehavior",
        be => new SetupTickBehavior(be, log)
      );

  private static SetupScene Build(List<string> log, params string[] steps) =>
    SetupScene.Build(
      SetupFile.Parse(
        "{\"setup\": \"t\", \"description\": \"d\", \"mods\": [\"setuphold\"], \"steps\": ["
          + string.Join(", ", steps)
          + "], \"seconds\": 6, \"record\": {}}",
        "t.json"
      ),
      [FixturePath],
      null,
      w => Register(w, log)
    );

  private static string[][] Seconds(List<string> log, int perSecond) =>
    [.. log.Chunk(perSecond)];

  private static SetupScene Four(List<string> log)
  {
    SetupScene scene = Build(
      log,
      Place("a", 0),
      Place("b", 1),
      Place("c", 2),
      Place("d", 3)
    );
    scene.NetworkTick = () => log.Add("network");
    return scene;
  }

  #region Orders

  public static TheoryData<int, int> Counts =>
    new()
    {
      { 2, 6 },
      { 3, 12 },
      { 4, 32 },
    };

  // Fails when the permutations or the two walks of the network tick miscount: n! + 2n orders.
  [Theory]
  [MemberData(nameof(Counts))]
  public void The_orders_of_n_machines_are_n_factorial_and_two_n(
    int n,
    int expected
  )
  {
    string[] ids = [.. "abcd".Take(n).Select(c => c.ToString())];

    var orders = SetupTickOrders.Of(ids);

    Assert.Equal(expected, orders.Count);
    Assert.Equal(
      expected,
      orders.Select(SetupTickOrders.Name).Distinct().Count()
    );
    Assert.All(orders, o => Assert.Equal(n + 1, o.Length));
  }

  // Fails when the network tick follows only the given order's machines, only the reverse's, or
  // sits at the end of the walk instead of before each machine.
  [Fact]
  public void The_network_tick_walks_the_given_order_and_its_reverse()
  {
    var orders = SetupTickOrders
      .Of(["a", "b", "c"])
      .Select(SetupTickOrders.Name)
      .ToList();

    Assert.Equal("a,b,c,network", orders[0]);
    Assert.Contains("network,a,b,c", orders);
    Assert.Contains("a,network,b,c", orders);
    Assert.Contains("a,b,network,c", orders);
    Assert.Contains("network,c,b,a", orders);
    Assert.Contains("c,network,b,a", orders);
    Assert.Contains("c,b,network,a", orders);
    Assert.DoesNotContain("c,b,a,network,network", orders);
  }

  #endregion

  #region Firing

  // Fails when the listeners fire other than in placement order with the network tick last.
  [Fact]
  public void A_scene_with_no_order_fires_in_placement_order_and_the_network_last()
  {
    var log = new List<string>();
    using SetupScene scene = Four(log);

    scene.Run(Mods);

    Assert.All(
      Seconds(log, 5),
      s => Assert.Equal(["0", "1", "2", "3", "network"], s)
    );
    Assert.Equal(6, Seconds(log, 5).Length);
  }

  // Fails when the named machines' listeners stay in place, move in placement order instead of the
  // order given, or the unnamed ones do not keep their order ahead of them.
  [Fact]
  public void An_order_moves_the_named_machines_last_in_the_order_given()
  {
    var log = new List<string>();
    using SetupScene scene = Four(log);

    scene.Order(["c", "a", "network"]);
    scene.Run(Mods);

    Assert.All(
      Seconds(log, 5),
      s => Assert.Equal(["1", "3", "2", "0", "network"], s)
    );
  }

  private static long LineTicks(SetupScene scene) =>
    scene.World.Mods.GetModSystem<MechanicalPowerMod>()!.getTickNumber();

  // Fails when the mechanical power listener also fires with the one-second tick, whichever way the
  // second is ordered: it takes 50 ticks of 20 ms a second and no more.
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void The_mechanical_listener_ticks_fifty_times_a_second_and_not_with_the_second(
    bool ordered
  )
  {
    var log = new List<string>();
    using SetupScene scene = Four(log);
    if (ordered)
      scene.Order(["a", "network"]);
    long before = LineTicks(scene);

    scene.Run(Mods);

    int seconds = log.Count / 5;
    Assert.True(seconds > 0);
    Assert.Equal(50 * seconds, LineTicks(scene) - before);
  }

  // Fails when the network tick runs before the wrong machine: the one before it, the first of the
  // list, or the end.
  [Fact]
  public void An_order_runs_the_network_tick_before_the_machine_that_follows_it()
  {
    var log = new List<string>();
    using SetupScene scene = Four(log);

    scene.Order(["c", "network", "a"]);
    scene.Run(Mods);

    Assert.All(
      Seconds(log, 5),
      s => Assert.Equal(["1", "3", "2", "network", "0"], s)
    );
  }

  // Fails when a network tick named first runs after the unnamed machines' listeners.
  [Fact]
  public void An_order_naming_the_network_first_runs_it_before_every_named_machine()
  {
    var log = new List<string>();
    using SetupScene scene = Four(log);

    scene.Order(["network", "c", "a"]);
    scene.Run(Mods);

    Assert.All(
      Seconds(log, 5),
      s => Assert.Equal(["1", "3", "network", "2", "0"], s)
    );
  }

  // Fails when the drawn second fires a delayed callback after the network tick instead of before it.
  [Fact]
  public void A_delayed_callback_fires_before_the_network_tick_in_the_drawn_second()
  {
    var log = new List<string>();
    using SetupScene scene = Four(log);
    (
      (SetupTickEntity)
        scene.World.Accessor.GetBlockEntity(scene.PositionOf("a"))
    ).CallLater();

    scene.Run(Mods);

    Assert.Equal(
      ["0", "1", "2", "3", "callback", "network"],
      Seconds(log, 6)[0]
    );
  }

  // Fails when an order that puts the network tick last fires a delayed callback after it.
  [Fact]
  public void A_delayed_callback_fires_before_a_network_tick_an_order_puts_last()
  {
    var log = new List<string>();
    using SetupScene scene = Four(log);
    (
      (SetupTickEntity)
        scene.World.Accessor.GetBlockEntity(scene.PositionOf("a"))
    ).CallLater();

    scene.Order(["c", "a", "network"]);
    scene.Run(Mods);

    Assert.Equal(
      ["1", "3", "2", "0", "callback", "network"],
      Seconds(log, 6)[0]
    );
  }

  // Fails when a listener a behaviour registers is not counted as its machine's.
  [Fact]
  public void A_listener_a_behavior_registers_belongs_to_its_machine()
  {
    var log = new List<string>();
    using SetupScene scene = Build(
      log,
      Place("a", 0, "behaving"),
      Place("b", 1),
      Place("c", 2)
    );

    scene.Order(["a", "c", "network"]);
    scene.Run(Mods);

    Assert.All(Seconds(log, 3), s => Assert.Equal(["1", "0", "2"], s));
  }

  // Fails when a listener registered after the machine was placed is not counted as its machine's.
  [Fact]
  public void A_listener_registered_after_placement_belongs_to_the_machine_that_owns_it()
  {
    var log = new List<string>();
    using SetupScene scene = Build(
      log,
      Place("a", 0),
      Place("b", 1),
      Place("c", 2)
    );
    var a = (SetupTickEntity)
      scene.World.Accessor.GetBlockEntity(scene.PositionOf("a"));
    a.ListenLate();

    scene.Order(["a", "c", "network"]);
    scene.Run(Mods);

    Assert.All(Seconds(log, 4), s => Assert.Equal(["1", "0", "late", "2"], s));
  }

  // Fails when a machine that registered no listener is let into an order.
  [Fact]
  public void A_machine_with_no_listener_cannot_be_named_in_an_order()
  {
    var log = new List<string>();
    using SetupScene scene = Build(log, Place("a", 0), Place("s", 1, "slab"));

    InvalidOperationException e = Assert.Throws<InvalidOperationException>(() =>
      scene.Order(["s", "a", "network"])
    );

    Assert.Contains("tickOrders", e.Message);
    Assert.Contains("\"s\" registered no tick listener", e.Message);
  }

  #endregion
}
#endif
