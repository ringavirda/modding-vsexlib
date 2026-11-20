using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ExpandedLib.Assembling;

/// <summary>The ghost of a fit onto an <see cref="IHandFitCellTarget"/>.</summary>
internal static class HandAssemblyFitGhost {
  /// <summary>The meshes the target answers for the aimed fit.</summary>
  /// <param name="capi">The client API, to mesh with.</param>
  /// <param name="aim">The aimed fit.</param>
  /// <param name="held">The stack in the main hand.</param>
  /// <returns>The target's meshes with their places; null when the target answers none, which the client draws as the default mesh of the principal's block.</returns>
  public static IReadOnlyList<HandFitMesh>? Of(
    ICoreClientAPI capi,
    HandFitAim.Cell aim,
    ItemStack held
  ) =>
    aim.Target.Ghost(capi, aim.Principal, aim.At, held, aim.Plan)
      is { Count: > 0 } meshes
      ? meshes
      : null;
}
