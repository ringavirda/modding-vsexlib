using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary><see cref="TreeKeys.AssertDeclaresBaseKeys"/>, the guard for the "call
/// <c>base.DeclareState</c> first" convention, and <see cref="TreeKeys.AssertGolden"/>.</summary>
[Collection(RepoRootCollection.Name)]
public class TreeKeysTests {
  private const string Domain = "plantedtreekeys";

  private class BaseEntity : ExBlockEntity {
    public int BaseField;

    protected override void DeclareState(ExBlockState state) =>
      state.Int("baseKey", () => BaseField, v => BaseField = v);
  }

  private sealed class GoodDerived : BaseEntity {
    public int DerivedField;

    protected override void DeclareState(ExBlockState state) {
      base.DeclareState(state);
      state.Int("derivedKey", () => DerivedField, v => DerivedField = v);
    }
  }

  private sealed class BadDerived : BaseEntity {
    public int DerivedField;

    // Skips base.DeclareState(state): BaseField silently stops saving.
    protected override void DeclareState(ExBlockState state) =>
      state.Int("derivedKey", () => DerivedField, v => DerivedField = v);
  }

  private static void Place(BlockEntity be) {
    be.Pos = new BlockPos(0, 0, 0);
    be.Block = TestBlocks.Configure(new Block(), "test:treekeysguard", 1);
  }

  [Fact]
  public void A_subclass_that_calls_base_passes() {
    var be = new GoodDerived();
    Place(be);

    TreeKeys.AssertDeclaresBaseKeys(be);
  }

  [Fact]
  [PlantedDefect(typeof(TreeKeys), nameof(TreeKeys.AssertDeclaresBaseKeys))]
  public void A_subclass_that_skips_base_fails_naming_the_type_and_the_missing_keys() {
    var be = new BadDerived();
    Place(be);

    InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
      () =>
        TreeKeys.AssertDeclaresBaseKeys(be)
    );

    Assert.Contains("BaseEntity", ex.Message, StringComparison.Ordinal);
    Assert.Contains("baseKey", ex.Message, StringComparison.Ordinal);
  }

  /// <summary>A repository holding <paramref name="keys"/> as <see cref="GoodDerived"/>'s tree-key
  /// golden, made the process's repo root until disposed.</summary>
  private sealed class Golden : IDisposable {
    private readonly string? _previousRoot = DefinitionGoldens.RepoRootOverride;
    private readonly PlantedFiles _files = new();

    public Golden(IEnumerable<string> keys) {
      _files.Write(
        $"mods/{Domain}/tests/goldens/{Domain}/treekeys/{nameof(GoodDerived)}.txt",
        string.Join("\n", keys) + "\n"
      );
      DefinitionGoldens.RepoRootOverride = _files.Root;
    }

    public void Dispose() {
      DefinitionGoldens.RepoRootOverride = _previousRoot;
      _files.Dispose();
    }
  }

  [Fact]
  [PlantedDefect(typeof(TreeKeys), nameof(TreeKeys.AssertGolden))]
  public void A_key_missing_from_the_golden_fails_naming_the_type() {
    var be = new GoodDerived();
    Place(be);
    using var golden = new Golden(
      TreeKeys.Of(be).Where(k => !k.StartsWith("derivedKey"))
    );

    InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
      () =>
        TreeKeys.AssertGolden(be, Domain)
    );

    Assert.StartsWith(
      "GoodDerived tree keys diverged from its golden",
      ex.Message
    );
    Assert.Contains("derivedKey:int", ex.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void Keys_matching_the_golden_pass() {
    var be = new GoodDerived();
    Place(be);
    using var golden = new Golden(TreeKeys.Of(be));

    TreeKeys.AssertGolden(be, Domain);
  }
}
