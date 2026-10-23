using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;

namespace ExpandedLib.Testing;

/// <summary>The player's shape posed at one frame of one clip, with the right and left hands' anchors.</summary>
internal sealed class SeraphPose {
  /// <summary>The shape the player's entity draws, which holds the clips and the <c>RightHand</c> attachment point.</summary>
  internal const string ShapePath = "game:entity/humanoid/seraph-faceless";

  // A clip enters at easing factor 0 and reaches full weight after dt * EaseInSpeed; this step at this speed reaches it
  // in one call and advances the clip by 3e-5 of a frame.
  private const float FrameStep = 1e-6f;
  private const float FramesPerSecond = 30f;
  private const float FullWeightSpeed = 1e9f;

  private static readonly ConditionalWeakTable<
    ICoreAPI,
    Dictionary<string, SeraphPose>
  > Poses = new();

  private static readonly ConditionalWeakTable<
    ICoreAPI,
    IReadOnlySet<string>
  > ClipSets = new();

  private SeraphPose(
    Mat4 itemAnchor,
    AttachmentPoint attachment,
    Mat4 leftAnchor
  ) {
    ItemAnchor = itemAnchor;
    Attachment = attachment;
    LeftAnchor = leftAnchor;
  }

  /// <summary>The animated matrix of the <c>ItemAnchor</c> element, in the shape's frame: its origin is the element's
  /// <c>from</c> corner, in blocks (16 voxels).</summary>
  internal Mat4 ItemAnchor { get; }

  /// <summary>The animated matrix of the <c>ItemAnchorL</c> element, as <see cref="ItemAnchor"/> gives the right one: the
  /// left hand, which carries no item.</summary>
  internal Mat4 LeftAnchor { get; }

  /// <summary>The <c>RightHand</c> attachment point the held item's transform adds to.</summary>
  internal AttachmentPoint Attachment { get; }

  /// <summary>The codes of the clips the seraph's shape holds.</summary>
  /// <param name="api">An api whose assets hold the game's shapes.</param>
  /// <exception cref="InvalidOperationException">The shape does not load.</exception>
  internal static IReadOnlySet<string> Clips(ICoreAPI api) =>
    ClipSets.GetValue(
      api,
      a => Load(a).Animations.Select(x => x.Code).ToHashSet()
    );

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

  /// <summary>Poses the seraph with the game's own animator; a pose is built once per api, clip and frame.</summary>
  /// <param name="api">An api whose assets hold the game's shapes.</param>
  /// <param name="clip">A clip code of the shape, such as <c>spearidle</c> or <c>spearidle-fp</c>;
  /// null for the rest pose.</param>
  /// <param name="frame">The frame of the clip to pose, from 0 to one less than its <c>quantityframes</c>, at the
  /// clip's own 30 frames a second; the keyframes are interpolated linearly. Ignored for the rest pose.</param>
  /// <exception cref="InvalidOperationException">The shape does not load.</exception>
  /// <exception cref="ArgumentException">The shape has no clip <paramref name="clip"/>.</exception>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="frame"/> lies outside the clip.</exception>
  internal static SeraphPose At(ICoreAPI api, string? clip, int frame = 0) {
    Dictionary<string, SeraphPose> cache = Poses.GetOrCreateValue(api);
    lock (cache) {
      string key = clip == null ? "" : $"{clip}@{frame}";
      if (!cache.TryGetValue(key, out SeraphPose? pose))
        cache[key] = pose = Build(api, clip, frame);
      return pose;
    }
  }

  private static SeraphPose Build(ICoreAPI api, string? clip, int frame) {
    Shape shape = Load(api);
    if (clip != null) {
      Animation named =
        shape.Animations.FirstOrDefault(a => a.Code == clip)
        ?? throw new ArgumentException(
          $"{ShapePath} has no clip '{clip}'.",
          nameof(clip)
        );
      if (frame < 0 || frame >= named.QuantityFrames)
        throw new ArgumentOutOfRangeException(
          nameof(frame),
          frame,
          $"Clip '{clip}' has {named.QuantityFrames} frames."
        );
    }
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
    animator.OnFrame(active, FrameStep + frame / FramesPerSecond);
    AttachmentPointAndPose hand = animator.GetAttachmentPointPose("RightHand");
    AttachmentPointAndPose left = animator.GetAttachmentPointPose("LeftHand");
    return new SeraphPose(
      Mat4.FromColumnMajor(hand.AnimModelMatrix),
      hand.AttachPoint,
      Mat4.FromColumnMajor(left.AnimModelMatrix)
    );
  }
}
