using ExpandedLib.Catalogues;

namespace ExpandedLib.Industry.Pipes;

/// <summary>
/// Live state of a pipe run: exactly one medium, gas (Air, Steam, Exhaust) or liquid (Water). A
/// gas's <see cref="Pressure"/> is the uncapped volume ratio <c>Volume / MaxVolume</c>; a
/// liquid's is set by the pump.
/// </summary>
public class PipeNetworkState {
  /// <summary>Content currently held by the network, in litres (gas or water).</summary>
  public float Volume { get; set; }

  /// <summary>Maximum the network can hold at 1 atm (<see cref="ExlibValues.LitresPerPipe"/> per pipe node).</summary>
  public float MaxVolume { get; set; }

  /// <summary>Temperature (C) of the content, injected by the producing source.</summary>
  public float Temperature { get; set; } = 20f;

  /// <summary>Current medium: "Air", "Steam", "Exhaust", "Water", or "" when empty.</summary>
  public string MediumType { get; set; } = "";

  /// <summary>Pressure in atm: for a gas the uncapped <c>Volume / MaxVolume</c>; for a liquid the
  /// fill ratio until brim-full, then <see cref="FeedPressure"/>.</summary>
  public float Pressure { get; set; }

  /// <summary>Pump-commanded feed pressure (atm) for a liquid run; realised as
  /// <see cref="Pressure"/> only once brim-full. Unused for gas.</summary>
  public float FeedPressure { get; set; }

  /// <summary>Number of open-ended connectors (leaks) on the network.</summary>
  public int OpeningsCount { get; set; } = 0;

  /// <summary>Throughput in L/s: the greater of produced and consumed volume over the last
  /// second.</summary>
  public float FlowRate { get; set; } = 0f;

  /// <summary>Whether the network currently carries a liquid, resolved through
  /// <see cref="ExLiquids.Taxonomy"/>.</summary>
  public bool IsLiquid => ExLiquids.Taxonomy.IsLiquid(MediumType);

  /// <summary>Whether the network has any open-ended connectors.</summary>
  public bool IsLeaking => OpeningsCount > 0;

  /// <summary>Gas pressure (atm) for a given pool state.</summary>
  public static float ComputeGasPressure(
    float currentVolume,
    float maxVolume
  ) => maxVolume > 0f ? currentVolume / maxVolume : 0f;

  /// <summary>Liquid pressure (atm): the fill ratio <c>Volume / MaxVolume</c> while below
  /// capacity, or the pump-set <paramref name="feedPressure"/> once brim-full.</summary>
  public static float ComputeLiquidPressure(
    float currentVolume,
    float maxVolume,
    float feedPressure
  ) =>
    maxVolume <= 0f ? 0f
    : currentVolume >= maxVolume - 0.001f ? feedPressure
    : currentVolume / maxVolume;
}
