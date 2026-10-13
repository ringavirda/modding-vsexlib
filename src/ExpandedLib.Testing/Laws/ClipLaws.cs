#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Definitions;

namespace ExpandedLib.Testing;

/// <summary>Every animated block plays its declared clips as it declares them: the action runs the
/// clip, each part ends at the keyframed pose without jumping, the action takes its declared time,
/// a block loaded at the far end shows it at once, and no two shapes share an animator cache key.
/// Each block is traced on a <see cref="ClientTwin"/>.</summary>
public static class ClipLaws
{
  internal const string ActionName = "clip action";

  internal const string TimeName = "clip time";

  internal const string KeyName = "animator key";

  internal const string ExceptionsName = "clip exceptions";

  /// <summary>Traces the <see cref="Blocks.ClipPose.Frame"/> actions of every variant of
  /// <paramref name="defs"/> and the animator cache key each animated variant builds under.</summary>
  /// <remarks>A finding is <c>key | what | variant codes</c>; an action's <c>by</c> lines are clicked
  /// on the server entity until its tree stops changing.</remarks>
  /// <param name="domain">The domain whose blocks are judged.</param>
  /// <param name="defs">Block and item definitions; others are skipped.</param>
  /// <param name="assemblies">Where the definitions' classes live, exlib's included.</param>
  /// <param name="exceptionsFile">A <c>clip-actions.json</c>; a known row accepts its law's findings under
  /// its key, and is a finding when it accepts none or names a law that did not run. Null or missing accepts nothing.</param>
  /// <param name="prepare">Registers on the world what the blocks need, before any is defined.</param>
  /// <param name="assetRoot">Maps a domain to the directory holding its <c>shapes</c> folder; null reads
  /// <c>RepoPaths.Assets</c>.</param>
  /// <returns>The laws <c>clip action</c>, <c>clip time</c>, <c>animator key</c> and <c>clip exceptions</c>, in order.</returns>
  /// <exception cref="InvalidOperationException">A declaration is malformed or names a clip its shape
  /// lacks, or a declaring block's shape is missing.</exception>
  public static BlockLaws.Result Run(
    string domain,
    IEnumerable<IExDef> defs,
    IReadOnlyList<Assembly> assemblies,
    string? exceptionsFile = null,
    Action<TestWorld>? prepare = null,
    Func<string, string>? assetRoot = null
  )
  {
    IExDef[] given = [.. defs];
    ExBlockDef[] blocks = [.. given.OfType<ExBlockDef>()];
    ExItemDef[] items = [.. given.OfType<ExItemDef>()];
    ClipExceptions exceptions = ClipExceptions.Read(exceptionsFile);
    TestWorld world = BlockLaws.Stand(blocks, assemblies, prepare, items);
    if (assetRoot != null)
      foreach (
        string shapes in blocks.Select(d => d.Domain).Append(domain).Distinct()
      )
        world.ClientAssets(shapes, assetRoot(shapes));

    var trace = new ClipTrace(world, domain);
    var found = new List<(string Code, ClipProblem Problem)>();
    (int keyTypes, int twins) = trace.Keys(found);
    (int types, int actions, int timed) = trace.Actions(found);
    string[] ran = [ActionName, TimeName, KeyName];
    return new BlockLaws.Result([
      Law(ActionName, types, actions, found, exceptions),
      Law(TimeName, types, timed, found, exceptions),
      Law(KeyName, keyTypes, twins, found, exceptions),
      new BlockLaws.Law(ExceptionsName, 0, 0, exceptions.UnnamedLaws(ran)),
    ]);
  }

  private static BlockLaws.Law Law(
    string name,
    int blocks,
    int cases,
    List<(string Code, ClipProblem Problem)> found,
    ClipExceptions exceptions
  ) =>
    new(
      name,
      blocks,
      cases,
      exceptions.Apply(
        name,
        [
          .. found
            .Where(f => f.Problem.Law == name)
            .GroupBy(f => (f.Problem.Key, f.Problem.Message))
            .Select(g =>
              (
                g.Key.Key,
                $"{g.Key.Key} | {g.Key.Message} | "
                  + string.Join(", ", g.Select(f => f.Code).Distinct())
              )
            ),
        ]
      )
    );
}
#endif
