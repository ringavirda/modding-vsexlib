using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
#if !GAME_GE_1_21
using FastVec3f = Vintagestory.API.MathTools.Vec3f;
#endif

namespace ExpandedLib.Helpers;

/// <summary>Fills the held, slot and ground views of every collectible whose mod depends on exlib from
/// <see cref="ItemViews.Compute(ICoreAPI, CollectibleObject)"/>, once the object loader has built them.</summary>
internal static class ItemViewsPass {
  private const double Tolerance = 1e-4;

  // The game's own default clips, which its blocks and items name and the player's animation list maps or leaves unmapped.
  private static readonly HashSet<string> GameClips = new(
    [
      ViewReferences.DefaultReady,
      ViewReferences.DefaultHit,
      ViewReferences.DefaultUse,
      ViewReferences.BlockReady,
      ViewReferences.BlockUse,
    ],
    StringComparer.OrdinalIgnoreCase
  );

  private static readonly AssetLocation DefaultPlayerShape = new(
    "game:shapes/entity/humanoid/seraph-faceless.json"
  );

  /// <summary>Replaces each default transform and fills each absent held clip, leaving authored ones, other domains and
  /// collectibles that cannot be computed as they are, with a warning or error naming the code. On the client it also
  /// warns once per animation the player's entity type does not map to a clip of its shape.</summary>
  /// <param name="api">The api of either side, after the object loader.</param>
  internal static void Run(ICoreAPI api) {
    HashSet<string> domains =
    [
      .. api
        .ModLoader.Mods.Where(m =>
          m.Info.Dependencies.Any(d => d.ModID == "exlib")
        )
        .Select(m => m.Info.ModID),
    ];
    if (domains.Count == 0)
      return;
    foreach (
      CollectibleObject collectible in api
        .World.Blocks.Cast<CollectibleObject>()
        .Concat(api.World.Items)
    ) {
      if (
        collectible?.Code == null
        || collectible.IsMissing
        || !domains.Contains(collectible.Code.Domain)
        || CollectibleViews.OptedOut(collectible)
      )
        continue;
      ItemViewSet? views;
      try {
        CollectibleViews.GripsOf(collectible);
        views = ItemViews.Compute(
          api,
          collectible,
          collectible is not Item
            || Equal(
              collectible.TpHandTransform,
              ModelTransform.ItemDefaultTp()
            )
        );
      } catch (Exception e)
          when (e is InvalidOperationException or ArgumentException) {
        api.Logger.Error(
          "{0}: item views left as the game's, the declaration does not parse: {1}",
          collectible.Code,
          e.Message
        );
        continue;
      }
      if (views == null) {
        WarnUnloadedShape(api, collectible);
        continue;
      }
      Apply(collectible, views);
    }
    if (api.Side == EnumAppSide.Client)
      WarnMissingClips(api, domains);
  }

  private static void WarnMissingClips(ICoreAPI api, HashSet<string> domains) {
    EntityProperties? player = api.World.GetEntityType(
      new AssetLocation("game", "player")
    );
    AssetLocation shapeLocation = player?.Client?.Shape?.Base is { } shapeBase
      ? shapeBase
        .Clone()
        .WithPathPrefixOnce("shapes/")
        .WithPathAppendixOnce(".json")
      : DefaultPlayerShape;
    Shape? shape = Shape.TryGet(api, shapeLocation);
    if (shape == null)
      return;
    HashSet<string> clips = new(
      (shape.Animations ?? []).Select(a => a.Code),
      StringComparer.OrdinalIgnoreCase
    );
    AnimationMetaData[]? mapped = player?.Client?.Animations;
    bool HasClip(string named) {
      if (mapped == null)
        return clips.Contains(named);
      AnimationMetaData? entry = mapped.FirstOrDefault(a =>
        string.Equals(a.Code, named, StringComparison.OrdinalIgnoreCase)
      );
      return entry != null && clips.Contains(entry.Animation ?? entry.Code);
    }
    SortedDictionary<string, SortedSet<string>> missing = [];
    foreach (
      CollectibleObject collectible in api
        .World.Blocks.Cast<CollectibleObject>()
        .Concat(api.World.Items)
    ) {
      if (
        collectible?.Code == null
        || !domains.Contains(collectible.Code.Domain)
      )
        continue;
      foreach (
        string? named in new[]
        {
          collectible.HeldRightTpIdleAnimation,
          collectible.HeldRightReadyAnimation,
          collectible.HeldTpHitAnimation,
          collectible.HeldTpUseAnimation,
        }
      ) {
        if (
          string.IsNullOrEmpty(named)
          || HasClip(named)
          || GameClips.Contains(named)
        )
          continue;
        if (!missing.TryGetValue(named, out SortedSet<string>? codes))
          missing[named] = codes = [];
        codes.Add(collectible.Code.ToString());
      }
    }
    foreach ((string clip, SortedSet<string> codes) in missing)
      api.Logger.Warning(
        "The player's shape has no clip '{0}', which {1} name; the hands stay idle",
        clip,
        string.Join(", ", codes)
      );
  }

  private static void WarnUnloadedShape(
    ICoreAPI api,
    CollectibleObject collectible
  ) {
    CompositeShape? shape = collectible switch {
      Block block => block.ShapeInventory ?? block.Shape,
      Item item => item.Shape,
      _ => null,
    };
    if (shape?.Base == null || CollectibleViews.BoxOf(api, collectible) != null)
      return;
    api.Logger.Warning(
      "{0}: shape {1} does not load or has no drawn face, so its item views stay the game's",
      collectible.Code,
      shape.Base
    );
  }

  private static void Apply(CollectibleObject c, ItemViewSet views) {
    bool block = c is Block;
    if (
      Equal(
        c.GuiTransform,
        block
          ? ModelTransform.BlockDefaultGui()
          : ModelTransform.ItemDefaultGui()
      )
    )
      c.GuiTransform = views.Gui;
    if (
      Equal(
        c.TpHandTransform,
        block ? ModelTransform.BlockDefaultTp() : ModelTransform.ItemDefaultTp()
      )
    )
      c.TpHandTransform = views.TpHand;
    if (
      Equal(
        c.GroundTransform,
        block
          ? ModelTransform.BlockDefaultGround()
          : ModelTransform.ItemDefaultGround()
      )
    )
      c.GroundTransform = views.Ground;

    string? namedIdle = c.HeldRightTpIdleAnimation;
    if (views.IdleClip != null && Absent(c.HeldRightTpIdleAnimation, null))
      c.HeldRightTpIdleAnimation = views.IdleClip;
    if (
      block
      && !string.IsNullOrEmpty(namedIdle)
      && Absent(c.HeldRightReadyAnimation, ViewReferences.DefaultReady)
    )
      c.HeldRightReadyAnimation = namedIdle;
    else if (
      views.ReadyClip != null
      && Absent(c.HeldRightReadyAnimation, ViewReferences.DefaultReady)
    )
      c.HeldRightReadyAnimation = views.ReadyClip;
    if (
      views.HitClip != null
      && Absent(c.HeldTpHitAnimation, ViewReferences.DefaultHit)
    )
      c.HeldTpHitAnimation = views.HitClip;
    if (
      views.UseClip != null
      && Absent(c.HeldTpUseAnimation, ViewReferences.DefaultUse)
    )
      c.HeldTpUseAnimation = views.UseClip;
  }

  private static bool Absent(string? clip, string? gameDefault) =>
    string.IsNullOrEmpty(clip) || clip == gameDefault;

  private static bool Equal(ModelTransform? a, ModelTransform b) =>
    a == null
    || Near(a.Translation, b.Translation)
      && Near(a.Rotation, b.Rotation)
      && Near(a.Origin, b.Origin)
      && Near(a.ScaleXYZ, b.ScaleXYZ);

  private static bool Near(FastVec3f a, FastVec3f b) =>
    Math.Abs(a.X - b.X) <= Tolerance
    && Math.Abs(a.Y - b.Y) <= Tolerance
    && Math.Abs(a.Z - b.Z) <= Tolerance;
}
