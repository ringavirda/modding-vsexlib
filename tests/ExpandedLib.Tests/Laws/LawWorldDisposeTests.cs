#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ExpandedLib.Blocks;
using ExpandedLib.Definitions;
using ExpandedLib.Structures;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The worlds the laws and the structure break check stand up are collected once the run is
/// over, though a substitute of each is still held.</summary>
public class LawWorldDisposeTests
{
  private static readonly System.Reflection.Assembly[] Exlib =
  [
    typeof(BlockStructureFiller).Assembly,
  ];

  private sealed class Watch
  {
    public List<WeakReference> Worlds { get; } = [];
    public List<IBlockAccessor> Held { get; } = [];

    public void Record(TestWorld world)
    {
      lock (this)
      {
        Worlds.Add(new WeakReference(world));
        Held.Add(world.Accessor);
      }
    }

    public void AssertCollected()
    {
      GC.Collect();
      GC.WaitForPendingFinalizers();
      GC.Collect();
      Assert.NotEmpty(Worlds);
      Assert.All(Worlds, w => Assert.False(w.IsAlive));
      GC.KeepAlive(Held);
    }
  }

  // Fails when BlockLaws.Run does not dispose the world a law ran on (OnStood without the finally).
  [Fact]
  public void BlockLaws_disposes_the_world_of_each_law()
  {
    var watch = new Watch();
    RunBlockLaws(watch);
    watch.AssertCollected();
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void RunBlockLaws(Watch watch) =>
    BlockLaws.Run("test", [], Exlib, watch.Record, 1, ["placement"]);

  // Fails when InteractionLaw.Run does not dispose its group worlds (the finally after the runs).
  [Fact]
  public void InteractionLaw_disposes_the_world_of_each_group()
  {
    var watch = new Watch();
    RunInteraction(watch);
    watch.AssertCollected();
    Assert.Equal(2, watch.Worlds.Count);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void RunInteraction(Watch watch) =>
    InteractionLaw.Run(
      () =>
      {
        TestWorld world = BlockLaws.Stand([], Exlib, null);
        watch.Record(world);
        return world;
      },
      "test",
      2
    );

  // Fails when ClipLaws.Run holds its world without a using.
  [Fact]
  public void ClipLaws_disposes_its_world()
  {
    var watch = new Watch();
    RunClip(watch);
    watch.AssertCollected();
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void RunClip(Watch watch) =>
    ClipLaws.Run("test", [], Exlib, null, watch.Record);

  // Fails when StructureBreaks.Run does not dispose an owned site's world, after StageCount or after
  // each BreakOnce (Site.Release removed from either).
  [Fact]
  public void StructureBreaks_disposes_the_world_of_each_stand()
  {
    var watch = new Watch();
    RunBreaks(watch);
    watch.AssertCollected();
    Assert.True(watch.Worlds.Count > 1);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void RunBreaks(Watch watch) =>
    StructureBreaks.Run(
      [
        ExBlockDef
          .Create("test", "mega")
          .Class("ExFilledMegastructure")
          .FillerOffsets([new(-1, 0, 0), new(1, 0, 0)]),
      ],
      Exlib,
      watch.Record
    );
}
#endif
