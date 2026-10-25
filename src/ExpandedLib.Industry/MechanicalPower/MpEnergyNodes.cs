using System.Collections.Generic;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>A node that drives a mechanical-energy run, such as an engine generator.</summary>
public interface IMpEnergyProducer {
  /// <summary>Drive torque (N*m) applied at the run's current shaft speed <paramref name="speed"/>
  /// (rad/s).</summary>
  float DriveTorque(float speed);

  /// <summary>Slope of <see cref="DriveTorque"/> against speed (N*m per rad/s) at
  /// <paramref name="speed"/> (rad/s); zero or negative. The implicit shaft step reads it; 0 by
  /// default.</summary>
  float DriveSlope(float speed) => 0f;
}

/// <summary>A node that stores energy: a flywheel, or the small inherent inertia of a cast-iron
/// shaft or gear.</summary>
public interface IMpEnergyStorage {
  /// <summary>Rotational inertia (kg*m^2) this node adds to the run.</summary>
  float Inertia { get; }
}

/// <summary>A node that loads the run: a heavy machine such as a rolling pass, hammer or
/// crusher.</summary>
public interface IMpEnergyConsumer {
  /// <summary>Resisting torque (N*m) imposed at the run's current shaft speed <paramref name="speed"/>
  /// (rad/s), or 0 when idle.</summary>
  float LoadTorque(float speed);

  /// <summary>Slope of <see cref="LoadTorque"/> against speed (N*m per rad/s) at
  /// <paramref name="speed"/> (rad/s); zero or positive. 0 by default.</summary>
  float LoadSlope(float speed) => 0f;

  /// <summary>Coulomb standing torque (N*m) this machine adds to the run while it turns, constant
  /// with speed. 0 by default.</summary>
  float StandingNm => 0f;
}

/// <summary>A node that knows which way the run turns, since shaft speed itself is unsigned.</summary>
public interface IMpEnergyDirection {
  /// <summary>True when the run turns in reverse.</summary>
  bool IsReversed { get; }
}

/// <summary>The size class of a shaft segment, which decides what it may join and what it
/// rates.</summary>
public enum MpShaftSize {
  /// <summary>Not a shaft: a machine port or a hub.</summary>
  None,

  /// <summary>The wrought line shaft.</summary>
  Line,

  /// <summary>The cast main shaft.</summary>
  Main,
}

/// <summary>A shaft or bevel cell of a rigid run, with the numbers the torque path rates it
/// by.</summary>
public interface IMpEnergySegment {
  /// <summary>The segment's size class.</summary>
  MpShaftSize Size { get; }

  /// <summary>Working torque (N*m) the segment carries without taking set.</summary>
  float RatingNm { get; }

  /// <summary>Coulomb standing torque (N*m) of the segment's journals, constant with speed.</summary>
  float StandingNm { get; }

  /// <summary>Torsional stiffness (N*m per rad) to a neighbour in the same run.</summary>
  float StiffnessNmPerRad { get; }

  /// <summary>Accumulated permanent set as a fraction of failure, 0 to 1; saved with the block.</summary>
  float Strain { get; set; }

  /// <summary>Takes a reading the torque path chose to write; the block entity keeps it in its tree
  /// (<see cref="MpSegmentReading.WriteTo"/>) and marks itself dirty. Does nothing by default.</summary>
  void StoreReading(MpSegmentReading reading) { }

  /// <summary>Torque (N*m) at which the segment parts at once, whatever its strain; zero or less for
  /// none. 0 by default.</summary>
  float UltimateNm => 0f;

  /// <summary>Called each step while the segment is at strain 1 or past its
  /// <see cref="UltimateNm"/> and shaft failure is on; the block entity breaks the shaft. Does nothing
  /// by default.</summary>
  void Part() { }
}

/// <summary>A flywheel whose rim strains when the run turns it past its rim limit.</summary>
public interface IMpEnergyRim {
  /// <summary>Speed (rad/s) at which the rim moves at its safe limit; zero or less for no limit.</summary>
  float RimLimitRadS { get; }

  /// <summary>Accumulated rim strain, 0 to 1; saved with the block. Rises at (speed / limit - 1) per
  /// <c>MpStrainTauHours</c> of in-game time while the wheel is past its limit and never falls.</summary>
  float RimStrain { get; set; }

  /// <summary>Called each step while <see cref="RimStrain"/> is 1 and shaft failure is on; the block
  /// entity drops the wheel as scrap.</summary>
  void Burst();
}

/// <summary>A block that joins two runs turning at different speeds without being a node of
/// either: a frame, a belt, a clutch.</summary>
public interface IMpEnergyLink {
  /// <summary>Port key of the input side.</summary>
  NodeKey From { get; }

  /// <summary>Port key of the output side.</summary>
  NodeKey To { get; }

  /// <summary>Speed ratio, input over output, above zero.</summary>
  float Ratio { get; }

  /// <summary>+1 when the output turns the same way as the input, -1 when it turns against it.</summary>
  int Sign { get; }

  /// <summary>Torque (N*m), at the input node's own speed, the link carries locked before it slips; 0 for a link that always slips.</summary>
  float CapNm { get; }

  /// <summary>Fraction of the carried torque delivered, 0 to 1.</summary>
  float Efficiency { get; }

  /// <summary>False while the link is released, as a disengaged clutch.</summary>
  bool Engaged { get; }

  /// <summary>Torque (N*m) the link carries while slipping at <paramref name="relativeSpeed"/>
  /// (rad/s, input side over output side taken to the input's speed), at the input's own speed and
  /// never negative; 0.8 of <see cref="CapNm"/> by default.</summary>
  float SlipTorque(float relativeSpeed) => 0.8f * CapNm;

  /// <summary>True when a locked link must let go rather than carry the torque it is under: the torque would
  /// leave the input side running ahead of the output side in direction <paramref name="heading"/>. False by
  /// default.</summary>
  /// <param name="heading">+1 or -1, the way the input side would run ahead of the output side, in the
  /// run's signed speed.</param>
  bool Freewheels(int heading) => false;

  /// <summary>Called once a step on every link of the run, engaged or not.</summary>
  /// <param name="dt">Step length in seconds.</param>
  /// <param name="hours">In-game hours the step spans; 0 when the calendar is unavailable.</param>
  /// <param name="slipPowerW">Power (W) the link dissipated slipping over the step; 0 while it was locked or open.</param>
  void Advance(float dt, float hours, float slipPowerW) { }

  /// <summary>True once the link is worn out and breaks when shaft failure is on. False by default.</summary>
  bool WornOut => false;

  /// <summary>Called when the run finds the link <see cref="WornOut"/> with shaft failure on; the block
  /// entity opens the link and drops it. Does nothing by default.</summary>
  void Break() { }
}

/// <summary>A block that will own links once placed, so the "mpshaft" network can try their joints before the block entity exists.</summary>
public interface IMpEnergyLinkSource {
  /// <summary>The links the block would hold standing at <paramref name="pos"/>, their port keys in that cell.</summary>
  /// <param name="world">The accessor the placement reads.</param>
  /// <param name="pos">The cell the block would fill.</param>
  IEnumerable<IMpEnergyLink> LinksAt(IBlockAccessor world, BlockPos pos);
}
