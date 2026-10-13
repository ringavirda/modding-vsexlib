using System;
using System.IO;
using ExpandedLib.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>The box the game draws for an item that has no shape: its texture voxelized, one block 1.5 across and up
/// per texture, one voxel thick.</summary>
internal static class TextureBox {
  // The game's voxelizer: a texture spans 1.5 blocks, starts 0.125 block before the item's origin, and is 1.5 / 24 block thick.
  private const double Span = 1.5;
  private const double Start = -0.125;
  private const double ThicknessBlocks = 1.5 / 24;
  private const double DepthBlocks = 0.5;
  private const double Voxels = 16;

  /// <summary>Whether the game draws <paramref name="item"/> from its texture: it has no shape, or a shape that asks
  /// for the texture to be voxelized.</summary>
  internal static bool IsTextureOnly(Item item) =>
    item.Shape == null || item.Shape.VoxelizeTexture;

  /// <summary>The box of the opaque pixels of a texture-only item's texture, in voxels.</summary>
  /// <param name="api">An api whose assets hold the game's textures.</param>
  /// <param name="item">A texture-only item; the texture is the one its shape names, else its first.</param>
  /// <returns>The box, whose depth is one voxel; null when no pixel of the texture is opaque.</returns>
  /// <exception cref="InvalidOperationException">The item names no texture, or the asset is missing.</exception>
  /// <exception cref="NotSupportedException">The texture is a PNG the reader cannot read; the message is the
  /// reason.</exception>
  /// <exception cref="InvalidDataException">The texture is not a well-formed PNG.</exception>
  internal static CollectibleViews.ViewBox? Of(ICoreAPI api, Item item) {
    CompositeTexture texture =
      (
        item.Shape?.Base != null
        && item.Textures.TryGetValue(
          item.Shape.Base.ToShortString(),
          out CompositeTexture? named
        )
          ? named
          : item.FirstTexture
      )
      ?? throw new InvalidOperationException($"{item.Code} names no texture.");
    AssetLocation location = texture
      .Base.Clone()
      .WithPathPrefixOnce("textures/")
      .WithPathAppendixOnce(".png");
    byte[] png =
      api.Assets.TryGet(location)?.Data
      ?? throw new InvalidOperationException(
        $"{item.Code}: texture {location} is not in the assets."
      );
    OpaqueRect? rect = PngReader.OpaqueBounds(png);
    if (rect is not { } r)
      return null;
    (int width, int height) = PngReader.Size(png);
    double x0 = Edge(r.X0, width),
      x1 = Edge(r.X1, width),
      y0 = Edge(r.Y0, height),
      y1 = Edge(r.Y1, height),
      z0 = DepthBlocks * Voxels,
      z1 = (DepthBlocks + ThicknessBlocks) * Voxels;
    var corners = new Vec3d[8];
    for (int i = 0; i < 8; i++)
      corners[i] = new Vec3d(
        (i & 1) == 0 ? x0 : x1,
        (i & 2) == 0 ? y0 : y1,
        (i & 4) == 0 ? z0 : z1
      );
    return new CollectibleViews.ViewBox(
      new Vec3d(x0, y0, z0),
      new Vec3d(x1, y1, z1),
      [new Vec3d(1, 0, 0), new Vec3d(0, 1, 0), new Vec3d(0, 0, 1)],
      [corners],
      [
        [true, true, true, true, true, true],
      ]
    );
  }

  private static double Edge(int pixel, int size) =>
    (Span * pixel / size + Start) * Voxels;
}
