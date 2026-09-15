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
/// Repo-wide invariant: no non-<see cref="ExModSystem"/> ModSystem in `exlib.dll` or
/// `exlib.industry.dll` may read or finalize assets at the vendored 0.1 default;
/// <see cref="ExpandedLib.ExpandedLibModSystem"/>'s own `AssetsFinalize` is pinned at 0.06 and
/// must run first.
/// </summary>
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

  // Walks t's base chain within the two assemblies, catching an inherited override too.
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
      if (
        !OverridesPhase(t, nameof(ModSystem.AssetsLoaded))
        && !OverridesPhase(t, nameof(ModSystem.AssetsFinalize))
      )
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
}
