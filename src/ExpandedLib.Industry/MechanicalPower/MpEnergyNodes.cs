namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>A node that drives a mechanical-energy run, such as an engine generator.</summary>
public interface IMpEnergyProducer {
  /// <summary>Drive torque (N*m) applied at the run's current shaft speed <paramref name="speed"/>
  /// (rad/s).</summary>
  float DriveTorque(float speed);
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
}

/// <summary>A node that knows which way the run turns, since shaft speed itself is unsigned.</summary>
public interface IMpEnergyDirection {
  /// <summary>True when the run turns in reverse.</summary>
  bool IsReversed { get; }
}
