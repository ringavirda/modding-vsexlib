using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExpandedLib.Industry;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Repo-wide `ExecuteOrder` invariant: no non-<see cref="ExModSystem"/> ModSystem in `exlib.dll` or
/// `exlib.industry.dll` is allowed to read or finalize assets at the vendored 0.1 default, because
/// exlib's own consumer contract rests on that default running strictly after the framework's own
/// `AssetsFinalize` (<see cref="ExpandedLib.ExpandedLibModSystem"/>, pinned at 0.06).
/// <see cref="ExModSystem"/> itself sits at 0.1 by design and is exempt.
/// </summary>
public class ModSystemOrderTests {
  #region Corpus

  private static readonly Assembly[] Assemblies =
  [
    typeof(ExpandedLibModSystem).Assembly,
    typeof(IndustryModule).Assembly,
  ];

  // Every concrete ModSystem the two assemblies declare, skipping the abstract bases (ExModSystem)
  // that leave the choice to a subclass, and skipping ExModSystem's own descendants, which
  // legitimately inherit its pinned 0.1.
  private static IEnumerable<Type> ConcreteModSystems() =>
    Assemblies
      .SelectMany(a => a.GetTypes())
      .Where(t =>
        typeof(ModSystem).IsAssignableFrom(t)
        && !t.IsAbstract
        && !typeof(ExModSystem).IsAssignableFrom(t)
      );

  // Walks t's base chain within the two assemblies above, so a concrete class that inherits its
  // AssetsLoaded/AssetsFinalize override from an in-assembly abstract base (rather than declaring it
  // itself) is still caught.
  private static bool OverridesPhase(Type t, string methodName) {
    MethodInfo? m = t.GetMethod(
      methodName,
      BindingFlags.Public | BindingFlags.Instance
    );
    if (m == null || m.DeclaringType == typeof(ModSystem))
      return false;
    return Assemblies.Contains(m.DeclaringType!.Assembly);
  }

  #endregion

  [Fact]
  public void Every_ModSystem_reading_or_finalizing_assets_sits_below_the_default_order() {
    var offenders = new List<string>();
    foreach (Type t in ConcreteModSystems()) {
      if (!OverridesPhase(t, nameof(ModSystem.AssetsLoaded)) && !OverridesPhase(t, nameof(ModSystem.AssetsFinalize)))
        continue;

      var instance = (ModSystem)Activator.CreateInstance(t)!;
      if (instance.ExecuteOrder() >= 0.1)
        offenders.Add(t.FullName!);
    }

    Assert.True(
      offenders.Count == 0,
      "Overrides AssetsLoaded/AssetsFinalize at the 0.1 default, defeating exlib's pinned order: "
        + string.Join(", ", offenders)
    );
  }

  [Fact]
  public void ExpandedLibModSystem_sits_above_the_module_driver_and_definitions() {
    var exlib = (ModSystem)Activator.CreateInstance(typeof(ExpandedLibModSystem))!;
    var moduleDriver = (ModSystem)Activator.CreateInstance(typeof(ExModuleModSystem))!;
    var definitions = (ModSystem)Activator.CreateInstance(
      typeof(Definitions.ExDefinitionModSystem)
    )!;

    Assert.True(exlib.ExecuteOrder() > moduleDriver.ExecuteOrder());
    Assert.True(exlib.ExecuteOrder() > definitions.ExecuteOrder());
    Assert.True(exlib.ExecuteOrder() < 0.1);
  }
}
