using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>The player's shape posed at the first frame of one clip, with the right hand's anchor.</summary>
internal sealed class SeraphPose {
  /// <summary>The shape the player's entity draws, which holds the clips and the <c>RightHand</c> attachment point.</summary>
  internal const string ShapePath = "game:entity/humanoid/seraph-faceless";

  // A clip enters at easing factor 0 and reaches full weight after dt * EaseInSpeed; this step at this speed reaches it
  // in one call and advances the clip by 3e-5 of a frame.
  private const float FrameStep = 1e-6f;
  private const float FullWeightSpeed = 1e9f;

  private static readonly ConditionalWeakTable<
    ICoreAPI,
    Dictionary<string, SeraphPose>
  > Poses = new();

  private SeraphPose(Mat4 itemAnchor, AttachmentPoint attachment) {
    ItemAnchor = itemAnchor;
    Attachment = attachment;
  }

  /// <summary>The animated matrix of the <c>ItemAnchor</c> element, in the shape's frame: its origin is the element's
  /// <c>from</c> corner, in blocks (16 voxels).</summary>
  internal Mat4 ItemAnchor { get; }

  /// <summary>The <c>RightHand</c> attachment point the held item's transform adds to.</summary>
  internal AttachmentPoint Attachment { get; }

  /// <summary>The codes of the clips the seraph's shape holds.</summary>
  /// <param name="api">An api whose assets hold the game's shapes.</param>
  /// <exception cref="InvalidOperationException">The shape does not load.</exception>
  internal static IReadOnlySet<string> Clips(ICoreAPI api) =>
    Load(api).Animations.Select(a => a.Code).ToHashSet();

  private static Shape Load(ICoreAPI api) {
    Shape shape =
      Shape.TryGet(
        api,
        new AssetLocation(ShapePath)
          .WithPathPrefixOnce("shapes/")
          .WithPathAppendixOnce(".json")
      ) ?? throw new InvalidOperationException($"{ShapePath} does not load.");
    shape.InitForAnimations(api.Logger, ShapePath);
    return shape;
  }

  /// <summary>Poses the seraph with the game's own animator; a pose is built once per api and clip.</summary>
  /// <param name="api">An api whose assets hold the game's shapes.</param>
  /// <param name="clip">A clip code of the shape, such as <c>spearidle</c> or <c>spearidle-fp</c>, at its frame 0;
  /// null for the rest pose.</param>
  /// <exception cref="InvalidOperationException">The shape does not load.</exception>
  /// <exception cref="ArgumentException">The shape has no clip <paramref name="clip"/>.</exception>
  internal static SeraphPose At(ICoreAPI api, string? clip) {
    Dictionary<string, SeraphPose> cache = Poses.GetOrCreateValue(api);
    lock (cache) {
      string key = clip ?? "";
      if (!cache.TryGetValue(key, out SeraphPose? pose))
        cache[key] = pose = Build(api, clip);
      return pose;
    }
  }

  private static SeraphPose Build(ICoreAPI api, string? clip) {
    Shape shape = Load(api);
    if (clip != null && shape.Animations.All(a => a.Code != clip))
      throw new ArgumentException(
        $"{ShapePath} has no clip '{clip}'.",
        nameof(clip)
      );
    foreach (Animation animation in shape.Animations)
      animation.GenerateAllFrames(shape.Elements, shape.JointsById);
    var animator = new ServerAnimator(
      () => 1,
      shape.Animations,
      shape.Elements,
      shape.JointsById
    );
    var active = new Dictionary<string, AnimationMetaData>();
    if (clip != null)
      active[clip] = new AnimationMetaData {
        Code = clip,
        Animation = clip,
        EaseInSpeed = FullWeightSpeed,
      };
    animator.OnFrame(active, FrameStep);
    AttachmentPointAndPose hand = animator.GetAttachmentPointPose("RightHand");
    return new SeraphPose(
      Mat4.FromColumnMajor(hand.AnimModelMatrix),
      hand.AttachPoint
    );
  }
}
