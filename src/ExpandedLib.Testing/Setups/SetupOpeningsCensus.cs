#if GAME_GE_1_22
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Industry.Pipes;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>The unjoined, uncapped connector faces of a built scene's pipe and pipeflow nodes, each
/// with the class of its neighbour.</summary>
internal static class SetupOpeningsCensus
{
  /// <summary>Appends one tab-separated line to <paramref name="file"/> per open face of every node
  /// of the scene's <c>pipe</c> and <c>pipeflow</c> networks: setup, case (the variant, or
  /// <c>main</c>), position as <c>x,y,z</c>, face code, own code, neighbour code, class. Cells run
  /// by y, then z, then x, faces in <see cref="BlockFacing.ALLFACES"/> order. A node block that is
  /// not a <see cref="BlockNetworkNode"/> has no class and is left out.</summary>
  public static void Append(
    SetupScene scene,
    string setup,
    string? variant,
    string file
  )
  {
    TestWorld world = scene.World;
    List<string> lines = [];
    foreach (
      BlockPos at in world
        .SolidBlocks.Where(kv => kv.Value.Id != 0)
        .Select(kv => kv.Key)
        .OrderBy(p => p.Y)
        .ThenBy(p => p.Z)
        .ThenBy(p => p.X)
    )
    {
      if (
        world.NetworkAt(at) is not { NetworkType: "pipe" or "pipeflow" } net
        || !net.Nodes.Contains(at)
        || world.GetBlock(at) is not BlockNetworkNode node
        || NetworkMembership.Resolve(world.Accessor, at, net.NetworkType)
          is not { } member
      )
        continue;
      IPipeVentStrategy? vent = (net as PipeNetwork)?.VentFor(node);
      foreach (
        BlockFacing face in SetupSceneDump.UnjoinedFaces(
          world,
          at,
          [(member, net.NetworkType)]
        )
      )
        lines.Add(
          string.Join(
            '\t',
            setup,
            variant ?? "main",
            $"{at.X},{at.Y},{at.Z}",
            face.Code,
            node.Code,
            world.GetBlock(at.AddCopy(face)).Code,
            PipeFaces
              .Classify(world.Accessor, vent, node, at, face)
              .ToString()
              .ToLowerInvariant()
          )
        );
    }
    if (lines.Count > 0)
      File.AppendAllLines(file, lines);
  }
}
#endif
