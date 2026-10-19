#if GAME_GE_1_22
using System;
using ExpandedLib.Machines;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The craft effects on an entity: the holder's animation manager and the sound at its
/// position.</summary>
public sealed class EntityCraftEffectsTests
{
  private static Entity Holder(
    TestWorld world,
    out IAnimationManager animations
  )
  {
    animations = Substitute.For<IAnimationManager>();
    return new EntityItem { World = world.World, AnimManager = animations };
  }

  // Fails when start or stop does not reach the holder's animation manager with the animation's code.
  [Fact]
  public void Start_and_stop_reach_the_holders_animation_manager()
  {
    using var world = new TestWorld();
    var effects = new EntityCraftEffects(Holder(world, out var animations));

    effects.StartAnimation("hammerhit");
    effects.StopAnimation("hammerhit");

    animations.Received(1).StartAnimation("hammerhit");
    animations.Received(1).StopAnimation("hammerhit");
  }

  // Fails when the strike is played at another asset, not at the holder, for a named player, or with
  // another range or volume.
  [Fact]
  public void A_strike_plays_at_the_holder_for_every_nearby_player()
  {
    using var world = new TestWorld();
    Entity holder = Holder(world, out _);
    var effects = new EntityCraftEffects(holder);

    effects.Strike("sounds/effect/anvilhit");

    world
      .World.Received(1)
      .PlaySoundAt(
        Arg.Is<AssetLocation>(l => l.ToString() == "game:sounds/effect/anvilhit"),
        holder,
        null,
        Arg.Is<float>(p => p >= 0.9f && p <= 1.1f),
        16f,
        0.35f
      );
  }
}
#endif
