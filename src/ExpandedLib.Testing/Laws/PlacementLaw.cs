using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Checks;
using ExpandedLib.Networks;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>A block whose placement writes variant groups lands a variant its blocktype declares,
/// placed by a player from every side and against every face, and lands every token it
/// declares.</summary>
public static class PlacementLaw {
  internal const string Name = "placement";

  /// <summary>Where the player stands, as a step from the target cell: the four horizontal sides,
  /// then above and below.</summary>
  private static readonly (string Where, Vec3d Eye)[] Stands =
  [
    ("from the north", new Vec3d(0.5, 0.5, -3.5)),
    ("from the east", new Vec3d(4.5, 0.5, 0.5)),
    ("from the south", new Vec3d(0.5, 0.5, 4.5)),
    ("from the west", new Vec3d(-3.5, 0.5, 0.5)),
    ("from above", new Vec3d(0.5, 5.5, 0.6)),
    ("from below", new Vec3d(0.5, -4.5, 0.6)),
  ];

  /// <summary>Places each block of <paramref name="domain"/> that carries placed groups
  /// (<c>BlockSignals.PlacedGroups</c>) through its own <see cref="Block.TryPlaceBlock"/>, as a
  /// survival player holding it would, from each of six stands against each of six faces, each at a
  /// fresh cell of <paramref name="world"/>.</summary>
  /// <remarks>One stack is held per variant that differs in a group placement does not write. A
  /// refused placement is no finding; a placement that throws is, as is one that lands a block of
  /// another blocktype or with another state in a group placement does not write, one that lands a
  /// token its blocktype does not declare, a stack that lands from no stand at all, named with
  /// the failure codes its refusals gave, and, for a stack with no other finding, a token its
  /// variants of the held stack's other states declare that no placement lands. A network node,
  /// whose token its neighbours pick, is not held to the last.</remarks>
  /// <param name="world">A world holding every variant of the blocks judged
  /// (<see cref="BlockLaws.Run"/> stands one).</param>
  /// <param name="domain">The domain whose blocks are placed.</param>
  /// <returns>The law's blocktypes, placements and findings, each finding keyed by the held
  /// variant's code.</returns>
  public static BlockLaws.Law Run(TestWorld world, string domain) {
    var findings = new List<string>();
    var sites = new BlockLaws.Sites();
    int blocks = 0,
      cases = 0;
    TestPlayer player = world.Player("placer");
    player.GameMode = EnumGameMode.Survival;
    player.Entity.LocalEyePos.Returns(new Vec3d());

    foreach (
      IGrouping<string, Block> type in BlockLaws.Blocktypes(world, domain)
    ) {
      Block[] variants = [.. type];
      string[] placed = [.. BlockSignals.PlacedGroupsOf(variants[0])];
      if (placed.Length == 0)
        continue;
      blocks++;
      Dictionary<string, HashSet<string>> declared = placed.ToDictionary(
        g => g,
        g => variants.Select(v => v.Variant?[g]).OfType<string>().ToHashSet()
      );

      foreach (
        Block held in variants
          .GroupBy(v => HeldKey(v, placed))
          .Select(g => g.First())
      ) {
        bool landed = false;
        int earlier = findings.Count;
        Dictionary<string, HashSet<string>> reached = placed.ToDictionary(
          g => g,
          _ => new HashSet<string>()
        );
        var refusals = new SortedSet<string>(StringComparer.Ordinal);
        foreach ((string where, Vec3d eye) in Stands)
          foreach (BlockFacing face in BlockFacing.ALLFACES) {
            cases++;
            BlockPos at = sites.Next();
            player.Entity.Pos.SetPos(at.X + eye.X, at.Y + eye.Y, at.Z + eye.Z);
            string against = $"{where} against its {face.Code} face";
            string failure = "";
            bool went;
            try {
              went = held.TryPlaceBlock(
                world.World,
                player.Player,
                new ItemStack(held),
                new BlockSelection {
                  Position = at.Copy(),
                  Face = face,
                  HitPosition = new Vec3d(
                    0.5 + 0.5 * face.Normali.X,
                    0.5 + 0.5 * face.Normali.Y,
                    0.5 + 0.5 * face.Normali.Z
                  ),
                },
                ref failure
              );
            } catch (Exception e) {
              findings.Add(
                $"{held.Code} placed {against} threw {BlockLaws.Describe(e)}"
              );
              continue;
            }
            if (!went) {
              refusals.Add(failure ?? "");
              continue;
            }
            landed = true;
            Block down = world.GetBlock(at);
            if (
              down.Id == 0
              || BlockLaws.TypeOf(down) != BlockLaws.TypeOf(held)
              || HeldKey(down, placed) != HeldKey(held, placed)
            ) {
              findings.Add($"{held.Code} placed {against} landed {down.Code}");
              continue;
            }
            foreach (string group in placed)
              if (down.Variant?[group] is { } token)
                reached[group].Add(token);
            foreach (string group in placed)
              if (
                down.Variant?[group] is not { } token
                || !declared[group].Contains(token)
              )
                findings.Add(
                  $"{held.Code} placed {against} landed {down.Code}, whose {group} "
                    + $"'{down.Variant?[group]}' its blocktype does not declare"
                );
          }
        if (!landed)
          findings.Add(
            $"{held.Code} lands from no stand against no face (refused: "
              + $"{string.Join(", ", refusals.Select(r => r == "" ? "no code" : r))})"
          );
        else if (findings.Count == earlier && held is not BlockNetworkNode)
          foreach (string group in placed) {
            string[] unreached =
            [
              .. variants
                .Where(v => HeldKey(v, placed) == HeldKey(held, placed))
                .Select(v => v.Variant?[group])
                .OfType<string>()
                .Distinct()
                .Where(t => !reached[group].Contains(t))
                .Order(StringComparer.Ordinal),
            ];
            if (unreached.Length > 0)
              findings.Add(
                $"{held.Code} lands no {group} '{string.Join("', '", unreached)}' from any "
                  + "stand or face"
              );
          }
      }
    }
    return new BlockLaws.Law(Name, blocks, cases, findings);
  }

  private static string HeldKey(Block variant, string[] placed) =>
    variant.Variant == null
      ? ""
      : string.Join(
        "|",
        variant
          .Variant.Where(p => !placed.Contains(p.Key))
          .Select(p => $"{p.Key}={p.Value}")
      );
}
