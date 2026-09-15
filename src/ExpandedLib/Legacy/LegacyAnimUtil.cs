// Legacy shim for BlockEntityAnimationUtil.CreateMesh, reproducing the 1.22 5-arg signature.
#if !GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ExpandedLib.Legacy;

public static class LegacyAnimUtil {
  private static readonly FieldInfo BeField =
    typeof(BlockEntityAnimationUtil).GetField(
      "be",
      BindingFlags.NonPublic | BindingFlags.Instance
    )!;

  extension(BlockEntityAnimationUtil util) {
    public MeshData CreateMesh(
      string nameForLogging,
      Shape? shape,
      out Shape resultingShape,
      ITexPositionSource? texSource,
      TesselationMetaData? metaOverride
    ) {
      var be = (BlockEntity)BeField.GetValue(util)!;
      var api = be.Api;
      var capi = (ICoreClientAPI)api;
      Block block = api.World.BlockAccessor.GetBlock(be.Pos);

      texSource ??= capi.Tesselator.GetTextureSource(
        block,
        0,
        returnNullWhenMissing: false
      );

      if (shape == null) {
        AssetLocation loc = block
          .Shape.Base.Clone()
          .WithPathPrefixOnce("shapes/")
          .WithPathAppendixOnce(".json");
        shape = Shape.TryGet(api, loc);
        if (shape == null) {
          api.World.Logger.Error(
            "Shape for block {0} not found at {1}; block animations not loaded.",
            block.Code,
            loc
          );
          resultingShape = null!;
          return new MeshData(initialiseArrays: true);
        }
      }

      // The Dictionary-returning overloads exist only on 1.21 and later 1.20.x patches.
      shape.ResolveReferences(api.World.Logger, nameForLogging);
      shape.CacheInvTransforms();
      shape.ResolveAndFindJoints(
        api.World.Logger,
        nameForLogging,
        Array.Empty<string>()
      );

      var meta = new TesselationMetaData {
        QuantityElements =
          metaOverride?.QuantityElements ?? block.Shape.QuantityElements,
        SelectiveElements =
          metaOverride?.SelectiveElements ?? block.Shape.SelectiveElements,
        IgnoreElements =
          metaOverride?.IgnoreElements ?? block.Shape.IgnoreElements,
        TexSource = texSource,
        WithJointIds = true,
        WithDamageEffect = true,
        TypeForLogging = nameForLogging,
      };

      capi.Tesselator.TesselateShape(meta, shape, out MeshData mesh);
      util.OnAfterTesselate?.Invoke(mesh);
      resultingShape = shape;
      return mesh;
    }
  }
}
#endif
