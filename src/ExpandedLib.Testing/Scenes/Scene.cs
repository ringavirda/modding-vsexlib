using System;
using System.Collections.Generic;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>
/// Test-facing builder over <see cref="TestWorld"/>: lays out blocks, nodes and machines,
/// advances them with <see cref="Step"/> and runs what <see cref="At"/> and <see cref="Every"/>
/// schedule. Placement is grid-based and additive; call <see cref="Build"/> once after it.
/// </summary>
public sealed class Scene {
  /// <summary>The underlying in-memory world (store, graph manager, fake API).</summary>
  public TestWorld World { get; } = new();

  private readonly List<(BlockPos pos, string networkType)> _pendingNodes =
    new();
  private readonly List<(int first, int period, Action action)> _scheduled =
    new();
  private bool _built;

  /// <summary>The seconds <see cref="Step"/> has run, from 0; second <c>t</c>'s ticks are the ones
  /// that take the clock from <c>t</c> to <c>t + 1</c>.</summary>
  public int Second { get; private set; }

  /// <summary>Registers a network factory, as a mod does at startup.</summary>
  public Scene Network(
    string type,
    System.Func<BlockNetworkModSystem, BlockNetwork> factory
  ) {
    World.RegisterNetwork(type, factory);
    return this;
  }

  /// <summary>Places a plain block with no entity and no network node: terrain, caps, housings.</summary>
  public Scene Block(BlockPos pos, Block block) {
    World.Place(pos, block);
    return this;
  }

  /// <summary>Places a network-node block and its entity, and queues it to join the graph on
  /// <see cref="Build"/>. The entity is linked to the world API so it can resolve its network and
  /// schedule ticks.</summary>
  public Scene Node(
    BlockPos pos,
    Block block,
    BlockEntity be,
    string networkType
  ) {
    World.Place(pos, block, be);
    World.Attach(be);
    _pendingNodes.Add((pos, networkType));
    return this;
  }

  /// <summary>Places a machine entity - a non-node block that reads or feeds adjacent networks - and
  /// runs its real <see cref="BlockEntity.Initialize"/>. Construction or structure state must already
  /// be forced.</summary>
  public Scene Machine(BlockPos pos, Block block, BlockEntity be) {
    World.Place(pos, block, be);
    World.Initialize(be);
    return this;
  }

  /// <summary>Fills the inclusive box between <paramref name="a"/> and <paramref name="b"/>, given in
  /// any order, with a plain block. Spans all three axes.</summary>
  public Scene Fill(BlockPos a, BlockPos b, Block block) {
    int x0 = Math.Min(a.X, b.X),
      x1 = Math.Max(a.X, b.X);
    int y0 = Math.Min(a.Y, b.Y),
      y1 = Math.Max(a.Y, b.Y);
    int z0 = Math.Min(a.Z, b.Z),
      z1 = Math.Max(a.Z, b.Z);
    for (int x = x0; x <= x1; x++)
      for (int y = y0; y <= y1; y++)
        for (int z = z0; z <= z1; z++)
          World.Place(new BlockPos(x, y, z), block);
    return this;
  }

  /// <summary>Adds every queued node to the graph (merging adjacent runs). Call once after placement.</summary>
  public Scene Build() {
    foreach (var (pos, type) in _pendingNodes)
      World.AddNode(pos, type);
    _built = true;
    return this;
  }

  /// <summary>Schedules <paramref name="action"/> to run once, before second
  /// <paramref name="second"/>'s ticks.</summary>
  /// <param name="second">The scene second, <see cref="Second"/> or later.</param>
  /// <param name="action">Run by <see cref="Step"/>; actions due at one second run in the order
  /// they were scheduled.</param>
  /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="second"/> has already
  /// passed.</exception>
  public Scene At(int second, Action action) =>
    Schedule(second, 0, action, nameof(second));

  /// <summary>Schedules <paramref name="action"/> to run before the ticks of second
  /// <paramref name="first"/> and of every <paramref name="period"/> seconds after it.</summary>
  /// <param name="period">Seconds between runs, 1 or more.</param>
  /// <param name="action">Run by <see cref="Step"/>; actions due at one second run in the order
  /// they were scheduled.</param>
  /// <param name="first">The first scene second it runs at, <see cref="Second"/> or later.</param>
  /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="period"/> is below 1, or
  /// <paramref name="first"/> has already passed.</exception>
  public Scene Every(int period, Action action, int first = 0) {
    if (period < 1)
      throw new ArgumentOutOfRangeException(
        nameof(period),
        period,
        "a period is 1 second or more"
      );
    return Schedule(first, period, action, nameof(first));
  }

  private Scene Schedule(int first, int period, Action action, string name) {
    ArgumentNullException.ThrowIfNull(action);
    if (first < Second)
      throw new ArgumentOutOfRangeException(
        name,
        first,
        $"second {first} has passed; the scene is at {Second}"
      );
    _scheduled.Add((first, period, action));
    return this;
  }

  /// <summary>Advances the simulation by <paramref name="seconds"/> server ticks. Each second runs
  /// the actions <see cref="At"/> and <see cref="Every"/> scheduled for it, then fires all
  /// block-entity production ticks, then ticks all networks (flow, leak, burst, broadcast) - the
  /// same order the live server uses.</summary>
  public void Step(int seconds = 1) {
    if (!_built)
      Build();
    for (int i = 0; i < seconds; i++) {
      RunScheduled();
      World.FireBlockEntityTicks();
      World.Tick(1);
      Second++;
    }
  }

  private void RunScheduled() {
    foreach (var (first, period, action) in _scheduled.ToArray()) {
      bool due =
        period == 0
          ? Second == first
          : Second >= first && (Second - first) % period == 0;
      if (due)
        action();
    }
  }

  /// <summary>The network of type <typeparamref name="TNet"/> owning <paramref name="pos"/>, or null.</summary>
  public TNet? NetworkAt<TNet>(BlockPos pos)
    where TNet : BlockNetwork => World.NetworkAt(pos) as TNet;

  /// <summary>The block entity at <paramref name="pos"/> as <typeparamref name="TBe"/>, or null.</summary>
  public TBe? EntityAt<TBe>(BlockPos pos)
    where TBe : BlockEntity => World.GetBlockEntity(pos) as TBe;
}
