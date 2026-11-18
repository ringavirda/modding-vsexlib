using ProtoBuf;

namespace ExpandedLib.Assembling;

/// <summary>Client to server: fix the candidate the player has shown. Carries the aim and what is held, nothing about cost.</summary>
[ProtoContract]
internal class AssemblyFix {
  [ProtoMember(1)]
  public int X;

  [ProtoMember(2)]
  public int Y;

  [ProtoMember(3)]
  public int Z;

  [ProtoMember(4)]
  public int Dimension;

  /// <summary>Index into <c>BlockFacing.ALLFACES</c> of the aimed face.</summary>
  [ProtoMember(5)]
  public int Face;

  [ProtoMember(6)]
  public double HitX;

  [ProtoMember(7)]
  public double HitY;

  [ProtoMember(8)]
  public double HitZ;

  /// <summary>The hotbar slot number holding the part.</summary>
  [ProtoMember(9)]
  public int Slot;

  [ProtoMember(10)]
  public string PartCode = "";

  /// <summary>The full code of the variant to fix.</summary>
  [ProtoMember(11)]
  public string CandidateCode = "";
}

/// <summary>Server to client: a fix, take-off or cap was refused and nothing changed.</summary>
[ProtoContract]
internal class AssemblyResult {
  /// <summary>The placement failure code; its line is <c>game:placefailure-</c> and the code.</summary>
  [ProtoMember(1)]
  public string Code = "";

  /// <summary>The arguments the line formats.</summary>
  [ProtoMember(2)]
  public string[] Args = [];
}

/// <summary>Client to server: take the fixed piece at a position off. Carries the position only; the held tool and the sneak are read on the server.</summary>
[ProtoContract]
internal class AssemblyTakeOff {
  [ProtoMember(1)]
  public int X;

  [ProtoMember(2)]
  public int Y;

  [ProtoMember(3)]
  public int Z;

  [ProtoMember(4)]
  public int Dimension;
}

/// <summary>Client to server: fit the cap held in the main hand on one open end of the fixed piece at a position. Carries the aim and what is held, nothing about cost.</summary>
[ProtoContract]
internal class AssemblyCap {
  [ProtoMember(1)]
  public int X;

  [ProtoMember(2)]
  public int Y;

  [ProtoMember(3)]
  public int Z;

  [ProtoMember(4)]
  public int Dimension;

  /// <summary>Index into <c>BlockFacing.ALLFACES</c> of the end to cap.</summary>
  [ProtoMember(5)]
  public int Face;

  /// <summary>The hotbar slot number holding the cap.</summary>
  [ProtoMember(6)]
  public int Slot;

  [ProtoMember(7)]
  public string CapCode = "";
}
