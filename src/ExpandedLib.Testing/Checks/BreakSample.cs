#if GAME_GE_1_22
using System;
using System.Collections.Generic;
using System.Linq;

namespace ExpandedLib.Testing;

/// <summary>Which blocks and which breaks a <see cref="StructureBreaks"/> run covers.</summary>
/// <remarks>The whole matrix breaks every variant of a block type from every cell at every stage in
/// every payment. The reduced sample breaks the type's first variant from every cell at every stage
/// paid in survival, and from its principal at every stage in the other two payments; every other
/// variant is broken once from its principal unpaid and once from its principal fully paid in
/// survival.</remarks>
/// <param name="Full">Whether the whole matrix is broken.</param>
/// <param name="Blocks">The block types covered, each as <c>domain:code</c> or the bare code;
/// every block type when null.</param>
public sealed record BreakSample(bool Full, IReadOnlySet<string>? Blocks)
{
  /// <summary>Variable that, set to <c>1</c>, makes <see cref="FromEnvironment"/> take the whole
  /// matrix.</summary>
  public const string FullVariable = "EXLIB_BREAK_FULL";

  /// <summary>Variable listing, comma-separated, the only block types <see cref="FromEnvironment"/>
  /// covers.</summary>
  public const string BlocksVariable = "EXLIB_BREAK_BLOCKS";

  /// <summary>Every break of every block type.</summary>
  public static BreakSample Whole { get; } = new(true, null);

  /// <summary>The reduced sample of every block type.</summary>
  public static BreakSample Reduced { get; } = new(false, null);

  /// <summary>Whether the sample covers only some block types.</summary>
  public bool Filtered => Blocks != null;

  /// <summary>The sample the environment asks for: the whole matrix when
  /// <see cref="FullVariable"/> is <c>1</c>, else the reduced one, limited to the block types
  /// <see cref="BlocksVariable"/> lists when it is set and not empty.</summary>
  public static BreakSample FromEnvironment() =>
    Parse(
      Environment.GetEnvironmentVariable(FullVariable),
      Environment.GetEnvironmentVariable(BlocksVariable)
    );

  internal static BreakSample Parse(string? full, string? blocks)
  {
    HashSet<string> listed =
    [
      .. (blocks ?? "").Split(
        ',',
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
      ),
    ];
    return new BreakSample(full == "1", listed.Count == 0 ? null : listed);
  }

  /// <summary>Whether the block type <paramref name="type"/>, as <c>domain:code</c>, is
  /// covered.</summary>
  public bool Takes(string type) =>
    Blocks == null
    || Blocks.Contains(type)
    || Blocks.Contains(type[(type.IndexOf(':') + 1)..]);

  internal StructureBreaks.Coverage CoverageOf(bool firstVariant) =>
    Full ? StructureBreaks.Coverage.Full
    : firstVariant ? StructureBreaks.Coverage.Cells
    : StructureBreaks.Coverage.Principal;
}
#endif
