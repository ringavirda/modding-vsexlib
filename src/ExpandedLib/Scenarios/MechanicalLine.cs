using System;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Scenarios;

/// <summary>A mechanical line as a setup reads it, in the harness and the live runner alike: the
/// speed without its sign, the torque and the resistance.</summary>
internal sealed class MechanicalLine(MechanicalNetwork network) {
  public float Speed => Math.Abs(network.Speed);

  public float TotalAvailableTorque => network.TotalAvailableTorque;

  public float NetworkResistance => network.NetworkResistance;
}
