using System;
using System.Linq;
using System.Reflection;
using ExpandedLib.Scenarios;
using ExpandedLib.Testing;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Boundary guard: the setup model lives in <c>ExpandedLib.Setups.dll</c>, which only the
/// test harness and the live mod carry; the player's <c>exlib.dll</c> neither references it nor
/// declares the model's namespace.</summary>
public class SetupModelBoundaryTests {
  private const string SetupsAssembly = "ExpandedLib.Setups";
  private const string ModelNamespace = "ExpandedLib.Scenarios";

  /// <summary>What ties <paramref name="assembly"/> to the setup model: a reference to
  /// <c>ExpandedLib.Setups</c>, or a type in the <c>ExpandedLib.Scenarios</c> namespace.</summary>
  /// <returns>One message per tie, empty for none.</returns>
  public static string[] SetupModelTies(Assembly assembly) =>
    [
      .. assembly
        .GetReferencedAssemblies()
        .Where(a =>
          string.Equals(
            a.Name,
            SetupsAssembly,
            StringComparison.OrdinalIgnoreCase
          )
        )
        .Select(_ => $"references {SetupsAssembly}"),
      .. assembly
        .GetTypes()
        .Where(t => t.Namespace == ModelNamespace)
        .Select(t => $"declares {t.FullName}"),
    ];

  [Fact]
  public void The_framework_assembly_carries_no_setup_model() {
    Premise.NotEmpty(
      typeof(ExpandedLibModSystem).Assembly.GetTypes(),
      "exlib types"
    );
    string[] ties = SetupModelTies(typeof(ExpandedLibModSystem).Assembly);

    Assert.True(
      ties.Length == 0,
      "exlib.dll is tied to the setup model: " + string.Join("; ", ties)
    );
  }

  // Fails when SetupModelTies misses the Setups assembly's own types or the harness's reference
  // to it, or names a tie in the framework assembly.
  [Fact]
  [PlantedDefect(typeof(SetupModelBoundaryTests), nameof(SetupModelTies))]
  public void A_setup_model_tie_is_named() {
    Assert.Contains(
      "declares ExpandedLib.Scenarios.SetupRun",
      SetupModelTies(typeof(SetupRun).Assembly)
    );
    Assert.Contains(
      "references ExpandedLib.Setups",
      SetupModelTies(typeof(SetupScene).Assembly)
    );
    Assert.Empty(SetupModelTies(typeof(ExpandedLibModSystem).Assembly));
  }
}
