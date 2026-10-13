#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>A block's own shape file read outside every animator cache, from which the pose of a
/// clip at a frame is read as the engine's animator poses it.</summary>
internal sealed class ClipReference
{
  private ClipReference(Shape shape) => Shape = shape;

  internal Shape Shape { get; }

  /// <summary>Reads <paramref name="block"/>'s shape through the world's client assets and resolves it as
  /// the engine's animation utility does.</summary>
  /// <exception cref="InvalidOperationException">The shape is missing or does not parse.</exception>
  internal static ClipReference Load(TestWorld world, Block block)
  {
    ICoreClientAPI capi = world.ClientApi;
    AssetLocation path = ExMeshCache.ShapePathOf(block);
    Shape shape =
      ExMeshCache.LoadShape(capi, path)
      ?? throw new InvalidOperationException(
        $"{block.Code}: shape {path} not found."
      );
    string name = block.Code.ToString();
    var elements = shape.CollectAndResolveReferences(capi.World.Logger, name);
    shape.CacheInvTransforms();
    shape.ResolveAndFindJoints(capi.World.Logger, name, elements);
    foreach (Animation clip in shape.Animations ?? [])
      clip.GenerateAllFrames(shape.Elements, shape.JointsById);
    return new ClipReference(shape);
  }

  /// <summary>The clip's last frame number.</summary>
  internal float Last(string clip) =>
    Shape.Animations.First(a => a.Code == clip).QuantityFrames - 1;

  internal bool HasClip(string clip) =>
    Shape.Animations.Any(a =>
      string.Equals(a.Code, clip, StringComparison.OrdinalIgnoreCase)
    );

  internal bool HasPart(string part) =>
    new ClientAnimator(
      () => 1,
      Shape.Animations,
      Shape.Elements,
      Shape.JointsById
    ).GetPosebyName(part) != null;

  /// <summary>The pose of each of <paramref name="parts"/> with <paramref name="clip"/> held at
  /// <paramref name="frame"/> at full blend weight, as degrees X, Y, Z then translations X, Y, Z.</summary>
  internal Dictionary<string, float[]> Pose(
    string clip,
    float frame,
    IEnumerable<string> parts
  )
  {
    var animator = new ClientAnimator(
      () => 1,
      Shape.Animations,
      Shape.Elements,
      Shape.JointsById
    );
    var held = new Dictionary<string, AnimationMetaData>
    {
      [clip] = ClipTrace.Hold(clip, frame),
    };
    animator.OnFrame(held, 1f);
    animator.GetAnimationState(clip).CurrentFrame = frame;
    animator.OnFrame(held, 0f);
    return parts.ToDictionary(p => p, p => ClipTrace.PoseOf(animator, p));
  }
}
#endif
