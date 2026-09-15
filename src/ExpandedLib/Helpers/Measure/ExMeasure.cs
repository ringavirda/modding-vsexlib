using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Config;

namespace ExpandedLib.Helpers;

/// <summary>The unit system used when formatting measurements for the look-at HUD, block info and handbook.</summary>
public enum MeasurementSystem {
  /// <summary>Litres, atmospheres, degrees Celsius.</summary>
  Metric,

  /// <summary>Imperial gallons, pounds per square inch, degrees Fahrenheit.</summary>
  Imperial,
}

/// <summary>Display-unit formatting shared across the mods, rendering metric gameplay values into the active <see cref="System"/>.</summary>
public static class ExMeasure {
  /// <summary>The display system currently active on this client.</summary>
  public static MeasurementSystem System { get; set; } =
    MeasurementSystem.Metric;

  private const float LitresToImperialGallons = 0.219969248f;
  private const float AtmToPsi = 14.6959488f;
  private const float RadPerSecToRpm = 9.549296586f;
  private const float WattsToKilowatts = 0.001f;
  private const float WattsToHorsepower = 1f / 745.699872f;
  private const float JoulesToKilojoules = 0.001f;

  private static bool Imperial => System == MeasurementSystem.Imperial;

  /// <summary>Parses a system name, case-insensitive; returns <see cref="MeasurementSystem.Metric"/> for anything unrecognised.</summary>
  public static MeasurementSystem Parse(string? name) =>
    string.Equals(name, "imperial", StringComparison.OrdinalIgnoreCase)
      ? MeasurementSystem.Imperial
      : MeasurementSystem.Metric;

  #region Volume

  /// <summary>A volume given in litres, e.g. <c>"800 L"</c> or <c>"176 gal"</c>.</summary>
  public static string Volume(float litres, string format = "F0") {
    var (num, unit) = Vol(litres, format);
    return num + " " + unit;
  }

  /// <summary>A volume range from a shared pool, e.g. <c>"400 / 800 L"</c>.</summary>
  public static string VolumeRange(
    float litres,
    float maxLitres,
    string format = "F0"
  ) {
    var (a, _) = Vol(litres, format);
    var (b, unit) = Vol(maxLitres, format);
    return a + " / " + b + " " + unit;
  }

  /// <summary>A flow rate given in litres per second, e.g. <c>"48 L/s"</c> or <c>"11 gal/s"</c>.</summary>
  public static string FlowRate(float litresPerSecond, string format = "F1") {
    var (num, unit) = Flow(litresPerSecond, format);
    return num + " " + unit;
  }

  #endregion

  #region Pressure

  /// <summary>A pressure given in atmospheres, e.g. <c>"5.00 atm"</c> or <c>"73.48 psi"</c>.</summary>
  public static string Pressure(float atm, string format = "F2") {
    var (num, unit) = Pres(atm, format);
    return num + " " + unit;
  }

  /// <summary>A pressure range (current / limit), e.g. <c>"3.0 / 8.0 atm"</c>.</summary>
  public static string PressureRange(
    float atm,
    float maxAtm,
    string format = "F1"
  ) {
    var (a, _) = Pres(atm, format);
    var (b, unit) = Pres(maxAtm, format);
    return a + " / " + b + " " + unit;
  }

  #endregion

  #region Temperature

  /// <summary>A temperature given in degrees Celsius, e.g. <c>"100 deg C"</c> or <c>"212 deg F"</c>.</summary>
  public static string Temperature(float celsius, string format = "F0") {
    var (num, unit) = Temp(celsius, format);
    return num + " " + unit;
  }

  #endregion

  #region Mechanical energy (rotation / power / energy)

  /// <summary>A shaft speed given in rad/s, shown as revolutions per minute in both display systems, e.g. <c>"480 RPM"</c>.</summary>
  public static string Speed(float radPerSecond, string format = "F0") =>
    Num(radPerSecond * RadPerSecToRpm, format) + " " + Unit("rpm");

  /// <summary>A power given in watts, shown as kilowatts in metric and horsepower in imperial, e.g.
  /// <c>"3.5 kW"</c> or <c>"4.7 hp"</c>.</summary>
  public static string Power(float watts, string format = "F1") {
    var (num, unit) = Pow(watts, format);
    return num + " " + unit;
  }

  /// <summary>An energy given in joules, shown in kJ and promoted to MJ above 1000 kJ, e.g. <c>"12.4 kJ"</c> or <c>"1.8 MJ"</c>.</summary>
  public static string Energy(float joules, string format = "F1") {
    float kilojoules = joules * JoulesToKilojoules;
    return MathF.Abs(kilojoules) >= 1000f
      ? Num(kilojoules * 0.001f, format) + " " + Unit("megajoules")
      : Num(kilojoules, format) + " " + Unit("kilojoules");
  }

  /// <summary>A reservoir charge readout: fill percentage plus absolute energy, e.g. <c>"73% (12.4 kJ)"</c>.</summary>
  public static string Charge(
    float joules,
    float capacityJoules,
    string format = "F1"
  ) {
    if (capacityJoules <= 0f)
      return Energy(joules, format);
    int percent = (int)
      MathF.Round(Math.Clamp(joules / capacityJoules, 0f, 1f) * 100f);
    return percent + "% (" + Energy(joules, format) + ")";
  }

  #endregion

  #region Handbook prose conversion

  /// <summary>One metric to imperial unit conversion, keyed by the localized metric symbol.</summary>
  private readonly record struct UnitConversion(
    string MetricSymbol,
    string ImperialSymbol,
    Func<float, float> ToImperial,
    string Format
  );

  // Rebuilt whenever the active language changes the unit symbols, keyed by _conversionSig.
  private static string? _conversionSig;
  private static Regex? _metricRegex;
  private static UnitConversion[]? _conversions;

  private static void EnsureConversions() {
    var conversions = new[]
    {
      new UnitConversion(
        Unit("litres-per-second"),
        Unit("gallons-per-second"),
        v => v * LitresToImperialGallons,
        "0.##"
      ),
      new UnitConversion(
        Unit("litres"),
        Unit("gallons"),
        v => v * LitresToImperialGallons,
        "0.##"
      ),
      new UnitConversion(Unit("atm"), Unit("psi"), v => v * AtmToPsi, "0.##"),
      new UnitConversion(
        Unit("celsius"),
        Unit("fahrenheit"),
        v => v * 9f / 5f + 32f,
        "0"
      ),
    };

    string sig = string.Join("", conversions.Select(c => c.MetricSymbol));
    if (sig == _conversionSig && _metricRegex != null)
      return;

    // Longest metric symbol first: a compound symbol ("L/s") must win over its prefix ("L").
    string alternation = string.Join(
      "|",
      conversions
        .OrderByDescending(c => c.MetricSymbol.Length)
        .Select(c => Regex.Escape(c.MetricSymbol))
    );
    _metricRegex = new Regex(
      @"((?:\d+(?:\.\d+)?\s*[-/]\s*)*\d+(?:\.\d+)?)\s*("
        + alternation
        + @")(?![A-Za-z])",
      RegexOptions.Compiled
    );
    _conversions = conversions;
    _conversionSig = sig;
  }

  /// <summary>Converts plain metric unit mentions in free prose to the active <see cref="System"/>, e.g. "30 L" to "6.6 gal".</summary>
  public static string ConvertMetricText(string text) {
    if (!Imperial || string.IsNullOrEmpty(text))
      return text;

    EnsureConversions();

    return _metricRegex!.Replace(
      text,
      m => {
        string numbers = m.Groups[1].Value;
        string symbol = m.Groups[2].Value;
        UnitConversion conv = Array.Find(
          _conversions!,
          c => c.MetricSymbol == symbol
        );
        // Converts each number, keeping the original "-" / "/" separators and spacing.
        string converted = Regex.Replace(
          numbers,
          @"\d+(?:\.\d+)?",
          nm =>
            Num(
              conv.ToImperial(
                float.Parse(nm.Value, CultureInfo.InvariantCulture)
              ),
              conv.Format
            )
        );
        return converted + " " + conv.ImperialSymbol;
      }
    );
  }

  #endregion

  #region Conversions

  private static (string num, string unit) Vol(float litres, string format) =>
    Imperial
      ? (Num(litres * LitresToImperialGallons, format), Unit("gallons"))
      : (Num(litres, format), Unit("litres"));

  private static (string num, string unit) Flow(
    float litresPerSecond,
    string format
  ) =>
    Imperial
      ? (
        Num(litresPerSecond * LitresToImperialGallons, format),
        Unit("gallons-per-second")
      )
      : (Num(litresPerSecond, format), Unit("litres-per-second"));

  private static (string num, string unit) Pres(float atm, string format) =>
    Imperial
      ? (Num(atm * AtmToPsi, format), Unit("psi"))
      : (Num(atm, format), Unit("atm"));

  private static (string num, string unit) Temp(float celsius, string format) =>
    Imperial
      ? (Num(celsius * 9f / 5f + 32f, format), Unit("fahrenheit"))
      : (Num(celsius, format), Unit("celsius"));

  private static (string num, string unit) Pow(float watts, string format) =>
    Imperial
      ? (Num(watts * WattsToHorsepower, format), Unit("horsepower"))
      : (Num(watts * WattsToKilowatts, format), Unit("kilowatts"));

  /// <summary>Localized symbol for a unit, e.g. <c>Unit("litres")</c> returns "L".</summary>
  private static string Unit(string key) => Lang.Get("exlib:unit-" + key);

  // Invariant culture: the decimal separator stays a dot regardless of the player's locale.
  private static string Num(float value, string format) =>
    value.ToString(format, CultureInfo.InvariantCulture);

  #endregion
}
