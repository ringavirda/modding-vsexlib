using System.Reflection;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Vintagestory.GameContent;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>Checks <see cref="HandbookUnitPatch"/>'s Harmony patch applies cleanly
/// headlessly.</summary>
[Collection(ExHarmonyCollection.Name)]
public class HandbookUnitPatchTests {
  [Fact]
  public void The_patch_applies_to_GuiHandbookTextPage_Init() {
    MethodBase original = typeof(GuiHandbookTextPage).GetMethod(
      "Init",
      BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
    )!;

    using var fixture = new HarmonyFixture(
      "exlibtest.handbookunitpatch",
      typeof(HandbookUnitPatch).Assembly
    );

    Assert.True(fixture.IsPatched(original));
  }
}

/// <summary>The prose conversion <see cref="HandbookUnitPatch"/> drives,
/// <see cref="ExMeasure.ConvertMetricText"/> in imperial mode.</summary>
[Collection(ExMeasureCollection.Name)]
public class HandbookUnitPatchConvertTextTests {
  #region ConvertMetricText in imperial mode

  // TestLang echoes lang keys unresolved; the matched symbol is the literal key "exlib:unit-litres", not "L".
  [Fact]
  public void A_single_value_converts_litres_to_gallons() {
    ExMeasure.System = MeasurementSystem.Imperial;
    try {
      string result = ExMeasure.ConvertMetricText(
        "Holds 30 exlib:unit-litres of water."
      );

      // 30 * 0.219969248 = 6.599... -> "6.6" at format "0.##"
      Assert.Contains("6.6 exlib:unit-gallons", result);
    } finally {
      ExMeasure.System = MeasurementSystem.Metric;
    }
  }

  [Fact]
  public void A_range_converts_both_ends_and_keeps_the_dash() {
    ExMeasure.System = MeasurementSystem.Imperial;
    try {
      string result = ExMeasure.ConvertMetricText("2-4 exlib:unit-atm");

      // 2 * 14.6959488 = 29.39..., 4 * 14.6959488 = 58.78... -> "29.39-58.78" at format "0.##"
      Assert.Equal("29.39-58.78 exlib:unit-psi", result);
    } finally {
      ExMeasure.System = MeasurementSystem.Metric;
    }
  }

  [Fact]
  public void The_longer_compound_symbol_wins_over_its_prefix() {
    ExMeasure.System = MeasurementSystem.Imperial;
    try {
      // "exlib:unit-litres" is a prefix of "exlib:unit-litres-per-second", standing in for the "L" vs "L/s" collision.
      string result = ExMeasure.ConvertMetricText(
        "8 exlib:unit-litres-per-second"
      );

      Assert.Contains("exlib:unit-gallons-per-second", result);
      Assert.DoesNotContain("exlib:unit-gallons ", result);
    } finally {
      ExMeasure.System = MeasurementSystem.Metric;
    }
  }

  #endregion
}
