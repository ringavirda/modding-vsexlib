using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Vintagestory.API.Common;

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
public sealed record MachineKind(
  string Code,
  float RatedSpeed,
  float StandingNm,
  bool Fixture,
  IReadOnlyList<MachineAnchor> Anchors,
  IReadOnlyList<MachineKindStep> Steps
) {
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
    && Anchors.SequenceEqual(other.Anchors)
    && Steps.SequenceEqual(other.Steps);
}
