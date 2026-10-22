using ExpandedLib.Networks;

namespace ExpandedLib.Industry.Pipes;

/// <summary>
/// An orifice that passes gas from a node of one run into a node of another. The sending run draws it
/// against the receiving node's published pressure and books the mass it drew; the receiving run takes
/// that mass as a source one step later, so neither reads the other's live state.
/// </summary>
internal sealed class FlowCoupling(NodeKey from, NodeKey to, FlowGas gas) {
  public NodeKey From { get; } = from;

  public NodeKey To { get; } = to;

  public FlowGas Gas { get; } = gas;

  /// <summary>Orifice area in m2.</summary>
  public double AreaM2 { get; set; }

  /// <summary>Mass in kg drawn in the step in progress, with its heat in kg C.</summary>
  public double NextKg { get; private set; }

  private double _nextKgC;

  /// <summary>Mass in kg published and not yet taken by the receiving run, with its heat in kg C.</summary>
  public double PublishedKg { get; private set; }

  private double _publishedKgC;

  /// <summary>Books <paramref name="kg"/> drawn at <paramref name="temperatureC"/> in the step in progress.</summary>
  public void Book(double kg, double temperatureC) {
    NextKg += kg;
    _nextKgC += kg * temperatureC;
  }

  /// <summary>Makes the booked mass available to the receiving run.</summary>
  public void Publish() {
    PublishedKg += NextKg;
    _publishedKgC += _nextKgC;
    NextKg = 0;
    _nextKgC = 0;
  }

  /// <summary>Takes the published mass and returns it with its mean temperature in C.</summary>
  public (double Kg, double TemperatureC) Take() {
    double kg = PublishedKg;
    double c = kg > 0 ? _publishedKgC / kg : double.NaN;
    PublishedKg = 0;
    _publishedKgC = 0;
    return (kg, c);
  }
}
