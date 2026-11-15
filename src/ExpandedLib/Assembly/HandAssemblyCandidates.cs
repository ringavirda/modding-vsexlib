using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Assembling;

/// <summary>The placements a held part offers at an aim, in the order the player steps through them. Pure: reads the world and the player's position and look, sends nothing.</summary>
public static class HandAssemblyCandidates {
  private const string TurnVariant = "turn";

  private enum Crossing {
    None,
    Mates,
    Refused,
  }

  /// <summary>The placements of <paramref name="part"/> at the aim, first-offered first.</summary>
  /// <remarks>A network node offers the tokens of its scheme (and, with a <c>turn</c> variant group, each turn), an <see cref="BlockBehaviorExOrientable"/> block its facings with the one toward the player first, any other block itself. A placement is offered when none of its arms meets a forbidden crossing or another joint family, it has an arm on the anchor when the anchor is joinable, <see cref="Block.CanPlaceBlock"/> passes and <see cref="IHandAssemblyPart.Allows"/> accepts it. Variants missing from the registry are skipped.</remarks>
  /// <param name="world">The world read; a loaded neighbourhood of the target cell is assumed.</param>
  /// <param name="player">The placing player; its position and look order the list.</param>
  /// <param name="part">The held block.</param>
  /// <param name="selection">The block the player aims at.</param>
  /// <returns>The offered placements; empty when none is.</returns>
  public static IReadOnlyList<AssemblyCandidate> Of(
    IWorldAccessor world,
    IPlayer player,
    Block part,
    BlockSelection selection
  ) {
    HandAssemblyTarget target = HandAssemblyTarget.Resolve(
      world.BlockAccessor,
      selection,
      part
    );
    var placeSelection = new BlockSelection {
      Position = target.Cell.Copy(),
      Face = selection.Face,
      HitPosition = selection.HitPosition,
      DidOffset = true,
    };

    var offered = new List<(AssemblyCandidate Candidate, int Declared)>();
    int declared = 0;
    foreach (
      (Block block, string token, string? turn) in Variants(
        world,
        player,
        part,
        selection,
        target
      )
    ) {
      declared++;
      IReadOnlyList<BlockFacing> arms =
        (block as BlockNetworkNode)?.GetConnectorFaces() ?? [];

      if (!ArmsAllowed(world.BlockAccessor, target, block, arms))
        continue;
      string failure = "";
      if (!block.CanPlaceBlock(world, player, placeSelection, ref failure))
        continue;
      if (!PartAllows(part, world, target.Cell, block))
        continue;

      offered.Add(
        (
          new AssemblyCandidate(
            block,
            token,
            turn,
            arms,
            [
              .. arms.Where(face =>
                CrossingAt(world.BlockAccessor, target.Cell, face, block)
                != Crossing.Mates
              ),
            ],
            JointsMade(world.BlockAccessor, target.Cell, block)
          ),
          declared
        )
      );
    }

    return Order(offered, target, player);
  }

  /// <summary>The number of joints a placement makes now: one per arm whose neighbour has a crossing back that couples and shares its joint family. A block that is not a network node makes one.</summary>
  /// <param name="world">The accessor to read the neighbours from.</param>
  /// <param name="cell">The cell the placement would fill.</param>
  /// <param name="candidate">The variant that would stand there.</param>
  public static int JointsMade(
    IBlockAccessor world,
    BlockPos cell,
    Block candidate
  ) =>
    candidate is BlockNetworkNode node
      ? (node.GetConnectorFaces() ?? []).Count(face =>
        CrossingAt(world, cell, face, candidate) == Crossing.Mates
      )
      : 1;

  /// <summary>Whether the block across <paramref name="face"/> of <paramref name="cell"/> offers a crossing <paramref name="part"/> mates: the same network type, a connector back, and a shared joint family.</summary>
  /// <param name="world">The accessor to read the neighbour from.</param>
  /// <param name="cell">The cell the part would fill.</param>
  /// <param name="face">The face of <paramref name="cell"/> toward the neighbour.</param>
  /// <param name="part">The held block, or a variant of it.</param>
  public static bool OffersCrossing(
    IBlockAccessor world,
    BlockPos cell,
    BlockFacing face,
    Block part
  ) => CrossingAt(world, cell, face, part) == Crossing.Mates;

  private static Crossing CrossingAt(
    IBlockAccessor world,
    BlockPos cell,
    BlockFacing face,
    Block part
  ) {
    if (part is not INetworkConnector connector)
      return Crossing.None;

    BlockPos neighbourPos = cell.AddCopy(face);
    Block neighbourBlock = world.GetBlock(neighbourPos);
    INetworkMember? neighbour = NetworkMembership.Resolve(
      world,
      neighbourPos,
      connector.NetworkTypeAt(world, cell)
    );
    if (
      neighbour == null
      || !neighbour.HasConnectorAt(world, neighbourPos, face.Opposite)
    )
      return Crossing.None;

    bool accepted =
      (part as BlockNetworkNode)?.AcceptsNeighbour(neighbourBlock) != false
      && neighbour.AcceptsNeighbour(part);
    if (!accepted)
      return Crossing.Refused;
    return BlockNetworkModSystem.CouplesFrom(world, neighbourPos, neighbour)
      ? Crossing.Mates
      : Crossing.None;
  }

  private static bool ArmsAllowed(
    IBlockAccessor world,
    HandAssemblyTarget target,
    Block candidate,
    IReadOnlyList<BlockFacing> arms
  ) {
    if (candidate is not BlockNetworkNode node)
      return true;

    BlockFacing[] forbidden = node.ForbiddenFaces(world, target.Cell);
    if (
      !node.KeepsRefusedArmsOpen
      && arms.Any(face =>
        forbidden.Contains(face)
        || CrossingAt(world, target.Cell, face, candidate) == Crossing.Refused
      )
    )
      return false;
    return !target.Joinable || arms.Contains(target.Anchor);
  }

  private static bool PartAllows(
    Block part,
    IWorldAccessor world,
    BlockPos cell,
    Block candidate
  ) => PartRefusal(part, world, cell, candidate) == null;

  /// <summary>The refusal <paramref name="part"/> gives a placement through <see cref="IHandAssemblyPart.Allows"/>.</summary>
  /// <returns>Null when every refiner accepts; otherwise the failure code the first refusing one gave, or <c>exlib-handassembly-refused</c> when it gave none.</returns>
  internal static string? PartRefusal(
    Block part,
    IWorldAccessor world,
    BlockPos cell,
    Block candidate
  ) {
    IEnumerable<IHandAssemblyPart> refiners = part is IHandAssemblyPart own
      ? [own, .. part.BlockBehaviors.OfType<IHandAssemblyPart>()]
      : part.BlockBehaviors.OfType<IHandAssemblyPart>();
    foreach (IHandAssemblyPart refiner in refiners)
      if (!refiner.Allows(world, cell, candidate, out string? failureCode))
        return failureCode ?? HandAssemblyServer.Refused;
    return null;
  }

  private static IEnumerable<(
    Block Block,
    string Token,
    string? Turn
  )> Variants(
    IWorldAccessor world,
    IPlayer player,
    Block part,
    BlockSelection selection,
    HandAssemblyTarget target
  ) {
    BlockBehaviorExOrientable? orientable =
      part.GetBehavior<BlockBehaviorExOrientable>();

    if (part is BlockNetworkNode node) {
      string key = BlockBehaviorExOrientable.OrientationVariant;
      IEnumerable<string> tokens =
        orientable?.Scheme.Tokens ?? node.AllowedOrientations.Values.First();
      if (
        node.Type != null
        && node.AllowedOrientations.TryGetValue(node.Type, out var allowed)
      )
        tokens = tokens.Where(allowed.Contains);

      bool turns = part.Variant.ContainsKey(TurnVariant);
      foreach (string token in tokens) {
        if (!turns) {
          Block? variant = world.BlockAccessor.GetBlock(
            part.CodeWithVariant(key, token)
          );
          if (variant != null)
            yield return (variant, token, null);
          continue;
        }

        BlockFacing? axis = BlockNetworkModSystem
          .SidesToFaces(token)
          .FirstOrDefault();
        if (axis == null)
          continue;
        foreach (string turn in TurnsAbout(axis, target.Cell, player)) {
          Block? variant = world.BlockAccessor.GetBlock(
            part.CodeWithVariants(
              new Dictionary<string, string> {
                [key] = token,
                [TurnVariant] = turn,
              }
            )
          );
          if (variant != null)
            yield return (variant, token, turn);
        }
      }
    } else if (orientable != null) {
      string first = orientable.TokenFor(player, selection);
      foreach (
        string token in orientable.Scheme.Tokens.OrderBy(t =>
          t == first ? 0 : 1
        )
      ) {
        Block? variant = world.BlockAccessor.GetBlock(
          part.CodeWithVariant(orientable.VariantKey, token)
        );
        if (variant != null)
          yield return (variant, token, null);
      }
    } else
      yield return (part, "", null);
  }

  // The four faces across an axis as turn letters: on a horizontal axis up, toward the player, away, down; on a
  // vertical axis toward the player, then clockwise from above.
  private static IEnumerable<string> TurnsAbout(
    BlockFacing axis,
    BlockPos cell,
    IPlayer player
  ) {
    var toPlayer = player.Entity.Pos.XYZ.SubCopy(
      cell.X + 0.5,
      cell.Y + 0.5,
      cell.Z + 0.5
    );
    BlockFacing Toward(IEnumerable<BlockFacing> among) =>
      among.MaxBy(f => f.Normali.X * toPlayer.X + f.Normali.Z * toPlayer.Z)!;

    BlockFacing[] ordered;
    if (axis.IsVertical) {
      BlockFacing toward = Toward(BlockFacing.HORIZONTALS);
      ordered = [toward, toward.GetCW(), toward.Opposite, toward.GetCCW()];
    } else {
      BlockFacing toward = Toward(
        BlockFacing.HORIZONTALS.Where(f => f.Axis != axis.Axis)
      );
      ordered = [BlockFacing.UP, toward, toward.Opposite, BlockFacing.DOWN];
    }
    return ordered.Select(f => f.Code[..1]);
  }

  private static IReadOnlyList<AssemblyCandidate> Order(
    List<(AssemblyCandidate Candidate, int Declared)> offered,
    HandAssemblyTarget target,
    IPlayer player
  ) {
    BlockFacing lookH = BlockFacing.HorizontalFromYaw(player.Entity.Pos.Yaw);
    Vec3f view = player.Entity.Pos.GetViewVector();

    int StraightRank(AssemblyCandidate c) {
      if (c.Arms.Count != 2 || c.Arms[0].Axis != c.Arms[1].Axis)
        return 3;
      EnumAxis axis = c.Arms[0].Axis;
      if (target.Joinable)
        return axis == target.Anchor.Axis ? 0 : 3;
      if (axis == lookH.Axis)
        return 0;
      return axis == EnumAxis.Y ? 2 : 1;
    }

    double OpenArmLook(AssemblyCandidate c) {
      return c.OpenArms.Count > 0
        ? c.OpenArms.Max(f =>
          Math.Round(
            f.Normali.X * view.X + f.Normali.Y * view.Y + f.Normali.Z * view.Z,
            4
          )
        )
        : double.NegativeInfinity;
    }

    return offered
      .OrderBy(o => StraightRank(o.Candidate))
      .ThenByDescending(o => o.Candidate.Joints)
      .ThenBy(o => o.Candidate.OpenArms.Count)
      .ThenBy(o => o.Candidate.Arms.Count)
      .ThenByDescending(o => OpenArmLook(o.Candidate))
      .ThenBy(o => o.Declared)
      .Select(o => o.Candidate)
      .ToList();
  }
}
