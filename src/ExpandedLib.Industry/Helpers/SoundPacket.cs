using System.ComponentModel;
using ProtoBuf;

namespace ExpandedLib.Industry.Helpers;

/// <summary>One server-played one-shot, carried to each client in range by the <c>exlibSound</c>
/// channel.</summary>
[ProtoContract]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SoundPacket {
  /// <summary>The sound's asset location, as <c>domain:path</c>.</summary>
  [ProtoMember(1)]
  public string Sound = string.Empty;

  /// <summary>World position, block units.</summary>
  [ProtoMember(2)]
  public double X;

  /// <summary>World position, block units.</summary>
  [ProtoMember(3)]
  public double Y;

  /// <summary>World position, block units.</summary>
  [ProtoMember(4)]
  public double Z;

  /// <summary>Whether the client varies the pitch.</summary>
  [ProtoMember(5)]
  public bool RandomizePitch;

  /// <summary>Audible distance, blocks.</summary>
  [ProtoMember(6)]
  public float Range;

  /// <summary>Volume before the client's <see cref="ExSounds.MachineVolume"/>.</summary>
  [ProtoMember(7)]
  public float Volume;
}
