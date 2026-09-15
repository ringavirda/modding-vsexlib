using System;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="ResourceInvariant{TState}"/> against a tiny counter with a planted bug: a
/// negative move that lets the total drift below zero.</summary>
public class ResourceInvariantTests {
  private sealed class Pool {
    public int Total = 5;
  }

  private static readonly System.Collections.Generic.IReadOnlyList<
    Action<Pool>
  > CorrectMoves =
  [
    p => p.Total += 10,
    p => p.Total = Math.Max(0, p.Total - 10),
    p => { },
  ];

  private static readonly System.Collections.Generic.IReadOnlyList<
    Action<Pool>
  > BuggyMoves =
  [
    p => p.Total += 10,
    p => p.Total -= 10, // no floor: can go negative
  ];

  [Fact]
  public void Run_passes_when_every_move_keeps_the_invariant() {
    var invariant = new ResourceInvariant<Pool>(
      () => new Pool(),
      CorrectMoves,
      p => Assert.True(p.Total >= 0, "total went negative")
    );

    var ex = Record.Exception(() => invariant.Run());
    Assert.Null(ex);
  }

  [Fact]
  public void Run_reports_the_failing_sequence_when_a_move_breaks_the_invariant() {
    var invariant = new ResourceInvariant<Pool>(
      () => new Pool(),
      BuggyMoves,
      p => Assert.True(p.Total >= 0, "total went negative")
    );

    var ex = Assert.Throws<InvalidOperationException>(() =>
      invariant.Run(sequences: 20, movesPerSequence: 30, seed: 7)
    );

    Assert.Contains("sequence", ex.Message);
    Assert.Contains("seed 7", ex.Message);
  }
}
