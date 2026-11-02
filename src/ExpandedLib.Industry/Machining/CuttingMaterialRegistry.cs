using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using ExpandedLib.Catalogues;

namespace ExpandedLib.Industry.Machining;

/// <summary>
/// The merged catalogue of cutting materials, keyed by code, and the hand power machining jobs are timed
/// against. A material or the hand power redeclared differently is reported and the first stands.
/// World-free; runs headless.
/// </summary>
public sealed class CuttingMaterialRegistry {
  /// <summary>The process-wide catalogue, repopulated at <c>AssetsFinalize</c>.</summary>
  public static CuttingMaterialRegistry Shared { get; } = new();

  /// <summary>Code contributions to <see cref="Shared"/>, invoked by the loader after its JSON
  /// read on every load; emptied when a world starts loading.</summary>
  public static CatalogueContributors Contributors { get; } = new();

  /// <summary>Empties <see cref="Shared"/> and drops every contributor; run when a world starts
  /// loading.</summary>
  internal static void ResetForWorld() {
    Shared.Clear();
    Contributors.Clear();
  }

  private readonly Dictionary<string, CuttingMaterial> _byCode = new(
    StringComparer.OrdinalIgnoreCase
  );
  private HandPower? _handPower;

  /// <summary>
  /// Merges <paramref name="set"/>. An identical redeclaration is a no-op; a different one is ignored.
  /// Returns one human-readable message per ignored declaration.
  /// </summary>
  public IReadOnlyList<string> Contribute(CuttingMaterialSet set) {
    var conflicts = new List<string>();
    foreach (CuttingMaterial material in set.Materials) {
      if (!_byCode.TryGetValue(material.Code, out CuttingMaterial? held)) {
        _byCode[material.Code] = material;
        continue;
      }
      if (held != material)
        conflicts.Add(
          $"cutting material '{material.Code}' is already declared with different figures, so this "
            + "declaration is ignored; the first declaration stands"
        );
    }
    if (set.HandPower is { } hand) {
      if (_handPower is null)
        _handPower = hand;
      else if (_handPower != hand)
        conflicts.Add(
          "hand power is already declared with different figures, so this declaration is ignored; "
            + "the first declaration stands"
        );
    }
    return conflicts;
  }

  /// <summary>Looks up the material declared as <paramref name="code"/>, ignoring case.</summary>
  public bool TryGet(
    string? code,
    [NotNullWhen(true)] out CuttingMaterial? material
  ) {
    material = null;
    return code != null && _byCode.TryGetValue(code, out material);
  }

  /// <summary>Every declared material.</summary>
  public IReadOnlyCollection<CuttingMaterial> All => _byCode.Values;

  /// <summary>How many materials are declared.</summary>
  public int Count => _byCode.Count;

  /// <summary>A man's sustained working power for <paramref name="motion"/>, W; 0 when no catalogue file
  /// declared one.</summary>
  public float HandPower(CutMotion motion) => _handPower?.For(motion) ?? 0f;

  /// <summary>Drops every material and the hand power. The loader clears before repopulating on each world load.</summary>
  public void Clear() {
    _byCode.Clear();
    _handPower = null;
  }
}
