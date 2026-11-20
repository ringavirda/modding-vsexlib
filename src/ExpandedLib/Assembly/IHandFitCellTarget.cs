using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>The cell a hand fit was aimed at and where in it the player pointed.</summary>
/// <param name="Cell">The aimed cell: a filler cell of a megablock, or the principal's own cell.</param>
/// <param name="Hit">The hit position inside <paramref name="Cell"/>, each axis in 0 to 1; the centre when the client sent none.</param>
public readonly record struct HandFitPoint(BlockPos Cell, Vec3d Hit);

/// <summary>An item a fit takes from the player's inventory besides the held one.</summary>
/// <param name="Code">Item or block code, wildcards allowed.</param>
/// <param name="Count">How many the fit takes, at least 1. When <paramref name="Code"/> is the held item's code, the held one is not counted in it.</param>
public sealed record HandFitCompanion(string Code, int Count);

/// <summary>What a fit costs beyond the held item.</summary>
/// <param name="Companions">The further items the fit takes; empty for none.</param>
/// <param name="Fasteners">How many of <see cref="HandAssemblySpec.Fastener"/> the whole fit spends; 0 for none.</param>
public sealed record HandFitPlan(
  IReadOnlyList<HandFitCompanion> Companions,
  int Fasteners
);

/// <summary>One mesh of a fit's ghost.</summary>
/// <param name="Mesh">The mesh, in block units like a block's own mesh. The target returns the same instance while the ghost does not change, so the client uploads it once.</param>
/// <param name="Place">Where the mesh's origin lies, in blocks from the corner of the principal's cell.</param>
public sealed record HandFitMesh(MeshData Mesh, Vec3d Place);

/// <summary>What a fitted part gives back when it is taken off.</summary>
/// <param name="Parts">The held part and its companions, as stacks.</param>
/// <param name="Fasteners">How many of <see cref="HandAssemblySpec.Fastener"/> come back with them; 0 for none.</param>
public sealed record HandFitRemoval(
  IReadOnlyList<ItemStack> Parts,
  int Fasteners
);

/// <summary>
/// Implemented by the block of a megablock's principal (or of any placed block) that takes parts fitted by hand onto one of its cells without turning into another block: the part is held in the main hand with the block's <see cref="HandAssemblySpec.Tool"/> in the off hand. Unlike <see cref="IHandFitTarget"/>, the aim reaches the principal through a filler cell and carries the cell clicked and the hit inside it, the target answers its own ghost, the fit may take companion items, and a fitted part is taken off again by sneaking with the tool in the main hand. A block implementing both is served as this one.
/// </summary>
public interface IHandFitCellTarget {
  /// <summary>Asked on the client for the ghost and the line and on the server before anything is spent: whether the held item fits at the point, and what the fit costs besides it.</summary>
  /// <param name="accessor">The accessor of the world the block stands in.</param>
  /// <param name="principal">The principal's cell.</param>
  /// <param name="at">The cell aimed and the hit in it; with several candidate places on one megablock, the one it points at is the one meant.</param>
  /// <param name="held">The stack in the player's main hand; never null, count at least 1.</param>
  /// <param name="failureCode">When the result is null, the placement failure code the player is shown, without the <c>game:placefailure-</c> prefix; null otherwise.</param>
  /// <returns>The plan; null when the held item does not fit at the point.</returns>
  HandFitPlan? Plan(
    IBlockAccessor accessor,
    BlockPos principal,
    HandFitPoint at,
    ItemStack held,
    out string? failureCode
  );

  /// <summary>The ghost of the fit, client only; asked while the fit is aimed.</summary>
  /// <param name="capi">The client API, to mesh with.</param>
  /// <param name="principal">The principal's cell.</param>
  /// <param name="at">The cell aimed and the hit in it.</param>
  /// <param name="held">The stack in the player's main hand.</param>
  /// <param name="plan">The plan <see cref="Plan"/> answered for the same aim.</param>
  /// <returns>The meshes of the held part and its companions, each with its place; null to show the default mesh of the principal's block instead.</returns>
  IReadOnlyList<HandFitMesh>? Ghost(
    ICoreClientAPI capi,
    BlockPos principal,
    HandFitPoint at,
    ItemStack held,
    HandFitPlan plan
  );

  /// <summary>Makes the fit and takes one of the held item out of <paramref name="held"/>, unless the player is in creative. Runs on the server after the range, claim, tool, companion and fastener checks passed and before the companions and fasteners are taken, so a refusal here costs nothing; the target also checks here whatever only the server can see.</summary>
  /// <param name="world">The server world.</param>
  /// <param name="principal">The principal's cell.</param>
  /// <param name="at">The cell aimed and the hit in it.</param>
  /// <param name="player">The fitting player.</param>
  /// <param name="held">The slot of the main hand.</param>
  /// <param name="failureCode">When the result is false, the placement failure code the player is shown, without the <c>game:placefailure-</c> prefix.</param>
  /// <returns>True when the fit was made; false, with nothing changed, when it was refused.</returns>
  bool Fit(
    IWorldAccessor world,
    BlockPos principal,
    HandFitPoint at,
    IPlayer player,
    ItemSlot held,
    ref string failureCode
  );

  /// <summary>What a fit already made at the point gives back; asked on the client for the take-off line and on the server before the take-off.</summary>
  /// <param name="accessor">The accessor of the world the block stands in.</param>
  /// <param name="principal">The principal's cell.</param>
  /// <param name="at">The cell aimed and the hit in it.</param>
  /// <returns>The removal; null when no fitted part stands at the point, which leaves the click to the block's other take-off.</returns>
  HandFitRemoval? FittedAt(
    IBlockAccessor accessor,
    BlockPos principal,
    HandFitPoint at
  );

  /// <summary>Takes the fitted part at the point off, without returning anything: the server gives <see cref="HandFitRemoval.Parts"/> and the fasteners to the player.</summary>
  /// <param name="world">The server world.</param>
  /// <param name="principal">The principal's cell.</param>
  /// <param name="at">The cell aimed and the hit in it.</param>
  /// <param name="player">The player taking it off.</param>
  /// <returns>True when the part was taken off; false, with nothing changed, when it was not.</returns>
  bool TakeOff(
    IWorldAccessor world,
    BlockPos principal,
    HandFitPoint at,
    IPlayer player
  );
}
