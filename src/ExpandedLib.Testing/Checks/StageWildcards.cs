using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Checks;

namespace ExpandedLib.Testing;

/// <summary>
/// <see cref="StageWildcardsCheck"/> over code-first definitions read from assemblies: every
/// construction stage table one domain declares, with the family's block definitions as the
/// catalogue its wildcards are decided against.
/// </summary>
public static class StageWildcards {
  /// <summary>What one run read and found.</summary>
  /// <param name="Blocks">The block types carrying construction stages.</param>
  /// <param name="Stages">The stages across those block types, stage 0 included.</param>
  /// <param name="Findings">One line per violation, prefixed with its rule, (a) to (f).</param>
  public sealed record Result(
    int Blocks,
    int Stages,
    IReadOnlyList<string> Findings
  );

  /// <summary>Runs the stage rules over <paramref name="domain"/>'s definitions.</summary>
  /// <param name="domain">The domain whose stage tables are checked.</param>
  /// <param name="family">Every domain whose block wildcards are decided, each with the assembly
  /// declaring its definitions; must hold <paramref name="domain"/>. A wildcard in any other
  /// domain is left to the loaded game.</param>
  /// <returns>The block types and stages read, and one finding per violation; no findings when
  /// clean.</returns>
  /// <exception cref="ArgumentException"><paramref name="family"/> does not hold
  /// <paramref name="domain"/>, or a stage table reads a JSON object or array where a string is
  /// read, or an array where an object is read (<see cref="StageWildcardsCheck.Run"/>).</exception>
  /// <exception cref="InvalidOperationException">A stage table reads a JSON value where an object
  /// is read.</exception>
  public static Result Check(
    string domain,
    params (string Domain, Assembly Assembly)[] family
  ) {
    if (!family.Any(f => f.Domain == domain))
      throw new ArgumentException(
        $"the family does not hold {domain}",
        nameof(family)
      );
    var source = new AssemblyCheckSource(family);
    var constructions = StageWildcardsCheck
      .Constructions(source, domain)
      .ToList();
    return new Result(
      constructions.Count,
      constructions.Sum(c => c.Stages.Count),
      StageWildcardsCheck.Run(source, domain).Errors
    );
  }
}
