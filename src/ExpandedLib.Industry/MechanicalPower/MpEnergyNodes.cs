using ExpandedLib.Networks;

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
  /// in-game hour while the wheel is past its limit and never falls.</summary>
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

  /// <summary>Torque (N*m) the link carries locked before it slips.</summary>
  float CapNm { get; }

  /// <summary>Fraction of the carried torque delivered, 0 to 1.</summary>
  float Efficiency { get; }

  /// <summary>False while the link is released, as a disengaged clutch.</summary>
  bool Engaged { get; }

  /// <summary>Torque (N*m) the link carries while slipping at <paramref name="relativeSpeed"/>
  /// (rad/s, input over output side); 0 by default.</summary>
  float SlipTorque(float relativeSpeed) => 0f;
}
