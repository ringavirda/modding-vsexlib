using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Industry;
using ExpandedLib.Registries;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>No ModSystem in exlib.dll or exlib.industry.dll other than an <see cref="ExModSystem"/>
/// reads or finalizes assets at the vendored 0.1 default; <see cref="ExpandedLib.ExpandedLibModSystem"/>'s
/// AssetsFinalize is pinned at 0.06 and runs first.</summary>
public class ModSystemOrderTests {
  #region Corpus

  private static readonly Assembly[] Assemblies =
  [
    typeof(ExpandedLibModSystem).Assembly,
    typeof(IndustryModule).Assembly,
  ];

  // Concrete ModSystems only, excluding ExModSystem and its descendants (pinned at 0.1 by design).
  private static IEnumerable<Type> ConcreteModSystems() =>
    Assemblies
      .SelectMany(a => a.GetTypes())
      .Where(t =>
        typeof(ModSystem).IsAssignableFrom(t)
        && !t.IsAbstract
        && !typeof(ExModSystem).IsAssignableFrom(t)
      );

  // Walks t's base chain within the assemblies, catching an inherited override too.
  private static bool OverridesPhase(
    Type t,
    string methodName,
    IReadOnlyCollection<Assembly> assemblies
  ) {
    MethodInfo? m = t.GetMethod(
      methodName,
      BindingFlags.Public | BindingFlags.Instance
    );
    if (m == null || m.DeclaringType == typeof(ModSystem))
      return false;
    return assemblies.Contains(m.DeclaringType!.Assembly);
  }

  #endregion

  /// <summary>Each concrete <see cref="ModSystem"/> of <paramref name="types"/>, other than an
  /// <see cref="ExModSystem"/>, that overrides <c>AssetsLoaded</c> or <c>AssetsFinalize</c> in one of
  /// <paramref name="assemblies"/> and whose fresh instance orders at 0.1 or later.</summary>
  /// <returns>The offenders' full names, in input order.</returns>
  /// <exception cref="MissingMethodException">An offender candidate has no parameterless
  /// constructor.</exception>
  public static IReadOnlyList<string> DefaultOrderAssetReaders(
    IEnumerable<Type> types,
    IReadOnlyCollection<Assembly> assemblies
  ) {
    var offenders = new List<string>();
    foreach (
      Type t in types.Where(t =>
        typeof(ModSystem).IsAssignableFrom(t)
        && !t.IsAbstract
        && !typeof(ExModSystem).IsAssignableFrom(t)
      )
    ) {
      if (
        !OverridesPhase(t, nameof(ModSystem.AssetsLoaded), assemblies)
        && !OverridesPhase(t, nameof(ModSystem.AssetsFinalize), assemblies)
      )
        continue;

      var instance = (ModSystem)Activator.CreateInstance(t)!;
      if (instance.ExecuteOrder() >= 0.1)
        offenders.Add(t.FullName!);
    }
    return offenders;
  }

  [Fact]
  public void Every_ModSystem_reading_or_finalizing_assets_sits_below_the_default_order() {
    IReadOnlyList<string> offenders = DefaultOrderAssetReaders(
      Premise.NotEmpty(ConcreteModSystems(), "mod systems"),
      Assemblies
    );

    Assert.True(
      offenders.Count == 0,
      "Overrides AssetsLoaded/AssetsFinalize at the 0.1 default, defeating exlib's pinned order: "
        + string.Join(", ", offenders)
    );
  }

  [Fact]
  public void ExpandedLibModSystem_sits_above_the_module_driver_and_definitions() {
    var exlib = (ModSystem)
      Activator.CreateInstance(typeof(ExpandedLibModSystem))!;
    var moduleDriver = (ModSystem)
      Activator.CreateInstance(typeof(ExModuleModSystem))!;
    var definitions = (ModSystem)
      Activator.CreateInstance(typeof(Definitions.ExDefinitionModSystem))!;

    Assert.True(exlib.ExecuteOrder() > moduleDriver.ExecuteOrder());
    Assert.True(exlib.ExecuteOrder() > definitions.ExecuteOrder());
    Assert.True(exlib.ExecuteOrder() < 0.1);
  }

  private sealed class DefaultReader : ModSystem {
    public override void AssetsLoaded(ICoreAPI api) { }
  }

  private sealed class PinnedFinalizer : ModSystem {
    public override double ExecuteOrder() => 0.05;

    public override void AssetsFinalize(ICoreAPI api) { }
  }

  private abstract class ReaderBase : ModSystem {
    public override void AssetsLoaded(ICoreAPI api) { }
  }

  private sealed class InheritedReader : ReaderBase { }

  private sealed class Idle : ModSystem { }

  // Fails when DefaultOrderAssetReaders passes a default-order reader, direct or inherited, or
  // names a pinned one, an idle one, an abstract one, or an override from outside the assemblies.
  [Fact]
  [PlantedDefect(typeof(ModSystemOrderTests), nameof(DefaultOrderAssetReaders))]
  public void A_default_order_asset_reader_is_named() {
    Type[] planted =
    [
      typeof(DefaultReader),
      typeof(PinnedFinalizer),
      typeof(ReaderBase),
      typeof(InheritedReader),
      typeof(Idle),
    ];

    Assert.Equal(
      [typeof(DefaultReader).FullName, typeof(InheritedReader).FullName],
      DefaultOrderAssetReaders(planted, [typeof(ModSystemOrderTests).Assembly])
    );
    Assert.Empty(DefaultOrderAssetReaders(planted, Assemblies));
  }
}
