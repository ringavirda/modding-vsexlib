using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Industry.Machining;

/// <summary>Where a mounted piece is drawn on a machine.</summary>
/// <param name="Name">The name a step refers to.</param>
/// <param name="Origin">Offset from the block origin, voxels (16 to the metre).</param>
/// <param name="Rotation">Rotation about X, Y and Z, degrees.</param>
/// <param name="Scale">Uniform scale of the mounted piece. Above 0.</param>
public sealed record MachineAnchor(
  string Name,
  Vector3 Origin,
  Vector3 Rotation,
  float Scale
);

/// <summary>Where one item is drawn when it is mounted, whichever step it is mounted for.</summary>
/// <param name="Item">Domain-qualified item code of the mounted piece.</param>
/// <param name="Anchor">Name of the <see cref="MachineAnchor"/> the piece is drawn at.</param>
public sealed record MachineMount(string Item, string Anchor);

/// <summary>The line a machine's mounted piece turns about while it cuts.</summary>
/// <param name="Axis">The axis the line runs along, in the block's own frame.</param>
/// <param name="Through">A point on the line, voxels (16 to the metre) from the block origin.</param>
/// <param name="DegreesPerSecond">The turn rate while the machine cuts, degrees a second. Above 0.</param>
public sealed record MachineSpindle(
  EnumAxis Axis,
  Vector3 Through,
  float DegreesPerSecond
);

/// <summary>The envelope a piece must fit to be mounted for a step.</summary>
/// <param name="Across">Widest the piece may be across the cut axis, voxels. Above 0.</param>
/// <param name="Length">Longest it may be along the cut axis, voxels. Above 0.</param>
public sealed record MachineEnvelope(float Across, float Length);

/// <summary>One step a machine kind does.</summary>
/// <param name="Step">The cutting step, matching a <see cref="MachiningJob.Step"/>.</param>
/// <param name="Motion">The motion of the cut, which picks the material's specific energy.</param>
/// <param name="Power">Cut power at the kind's rated speed, W. Above 0.</param>
/// <param name="Tooling">The tool kind (<c>machinetool</c> kind) the step needs fitted.</param>
/// <param name="Anchor">Name of the <see cref="MachineAnchor"/> the piece is drawn at, or null.</param>
/// <param name="Envelope">The largest piece the step takes, or null when unchecked.</param>
public sealed record MachineKindStep(
  string Step,
  CutMotion Motion,
  float Power,
  string Tooling,
  string? Anchor,
  MachineEnvelope? Envelope
);

/// <summary>How a machine's cut is fed.</summary>
public enum MachineFeed {
  /// <summary>A player holds the machine's feed control while it cuts; <c>"held"</c>.</summary>
  Held,

  /// <summary>The machine cuts on its own once started; <c>"self"</c>.</summary>
  Self,
}

/// <summary>
/// One kind of machine tool: the steps it does, in the order a host picks between them, and how fast
/// its port turns. Blocks name their kind in a <see cref="AttributeKey"/> attribute.
/// </summary>
/// <param name="Code">Domain-qualified code, e.g. <c>iiex:lathe</c>; one namespace across mods.</param>
/// <param name="RatedSpeed">Port speed at which each step's power is delivered, rad/s. Above 0.</param>
/// <param name="StandingNm">Torque the machine costs while engaged, N*m. 0 or more.</param>
/// <param name="Fixture">Whether the machine has a fixture slot.</param>
/// <param name="Anchors">The mount points, by name.</param>
/// <param name="Steps">The steps it does, in declaration order.</param>
/// <param name="Feed">Whether a player must hold the feed control for the cut to run. Defaults to <see cref="MachineFeed.Held"/>.</param>
/// <param name="Mounts">The anchor each named item is drawn at, read before its step's anchor. Null reads as none.</param>
/// <param name="Spindle">The line the mounted piece turns about while the machine cuts, or null when it stands still. A kind with a spindle draws its piece through a block-entity renderer, not the chunk mesh.</param>
public sealed record MachineKind(
  string Code,
  float RatedSpeed,
  float StandingNm,
  bool Fixture,
  IReadOnlyList<MachineAnchor> Anchors,
  IReadOnlyList<MachineKindStep> Steps,
  MachineFeed Feed = MachineFeed.Held,
  IReadOnlyList<MachineMount>? Mounts = null,
  MachineSpindle? Spindle = null
) {
  /// <summary>The anchor each named item is drawn at, read before its step's anchor; empty when the kind declares none.</summary>
  public IReadOnlyList<MachineMount> Mounts { get; init; } = Mounts ?? [];

  /// <summary>The block attribute that names a block's machine kind.</summary>
  public const string AttributeKey = "machinekind";

  /// <summary>The kind <paramref name="block"/> names in its <c>machinekind</c> attribute, or null when it
  /// names none or the kind is not declared.</summary>
  public static MachineKind? Of(Block? block) =>
    Of(block, MachineKindRegistry.Shared);

  internal static MachineKind? Of(Block? block, MachineKindRegistry registry) =>
    registry.TryGet(
      block?.Attributes?[AttributeKey].AsString(),
      out MachineKind? kind
    )
      ? kind
      : null;

  /// <summary>The step <paramref name="step"/> as this kind does it, or null when it does not.</summary>
  public MachineKindStep? StepOf(string? step) =>
    Steps.FirstOrDefault(s =>
      string.Equals(s.Step, step, StringComparison.OrdinalIgnoreCase)
    );

  /// <summary>Whether <paramref name="other"/> declares the same kind, figure for figure and in the same step order.</summary>
  public bool SameAs(MachineKind other) =>
    string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase)
    && RatedSpeed == other.RatedSpeed
    && StandingNm == other.StandingNm
    && Fixture == other.Fixture
    && Feed == other.Feed
    && Anchors.SequenceEqual(other.Anchors)
    && Steps.SequenceEqual(other.Steps)
    && Mounts.SequenceEqual(other.Mounts)
    && Equals(Spindle, other.Spindle);
}
