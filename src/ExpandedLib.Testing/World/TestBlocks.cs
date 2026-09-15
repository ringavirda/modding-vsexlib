using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace ExpandedLib.Testing;

/// <summary>
/// Helpers for turning a bare <see cref="Block"/> or <see cref="BlockEntity"/> instance into one the
/// simulation can read, without the engine's asset-load pipeline. <see cref="Configure"/> primes the
/// null <see cref="RegistryObject.Variant"/> a fresh block starts with.
/// </summary>
public static class TestBlocks {
  /// <summary>
  /// Assigns <paramref name="code"/> and <paramref name="id"/> and builds the relaxed variant map
  /// from <paramref name="variants"/>; absent keys return <c>null</c>, not an exception.
  /// </summary>
  public static T Configure<T>(
    T block,
    string code,
    int id,
    params (string key, string value)[] variants
  )
    where T : Block {
    block.Code = new AssetLocation(code);
    block.BlockId = id;
    foreach (var (key, value) in variants)
      block.VariantStrict[key] = value;
    block.Variant = new RelaxedReadOnlyDictionary<string, string>(
      block.VariantStrict
    );
    return block;
  }
}
