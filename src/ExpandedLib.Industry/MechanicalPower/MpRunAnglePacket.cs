using System.ComponentModel;
using ProtoBuf;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>The angle and speed of each run of one "mpshaft" network, carried to every client by the
/// <c>exlibMpTurn</c> channel. The arrays run in parallel, one entry per run.</summary>
[ProtoContract]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class MpRunAnglePacket {
  /// <summary>Block X of each run's anchor node.</summary>
  [ProtoMember(1)]
  public int[] X = [];

  /// <summary>Block Y of each run's anchor node.</summary>
  [ProtoMember(2)]
  public int[] Y = [];

  /// <summary>Block Z of each run's anchor node.</summary>
  [ProtoMember(3)]
  public int[] Z = [];

  /// <summary>Slot of each run's anchor node.</summary>
  [ProtoMember(4)]
  public int[] Slot = [];

  /// <summary>Reference angle of each run in rad, unwrapped.</summary>
  [ProtoMember(5)]
  public double[] Angle = [];

  /// <summary>Reference speed of each run in rad/s, signed.</summary>
  [ProtoMember(6)]
  public float[] Speed = [];
}
