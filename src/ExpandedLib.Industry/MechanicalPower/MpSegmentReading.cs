using System;
using System.Collections.Generic;
using ExpandedLib.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>What the torque path last measured at one shaft segment, as a block entity shows it.</summary>
/// <param name="TorqueNm">Largest torque a joint of the segment carried, N*m, zero or more.</param>
/// <param name="RatingNm">The segment's working torque, N*m.</param>
/// <param name="Strain">Permanent set as a fraction of failure, 0 to 1.</param>
/// <param name="SpeedRadS">Segment speed in rad/s, signed.</param>
/// <param name="Size">The segment's size class.</param>
public readonly record struct MpSegmentReading(
  float TorqueNm,
  float RatingNm,
  float Strain,
  float SpeedRadS,
  MpShaftSize Size
) {
  /// <summary>The key of the reading's sub-tree.</summary>
  public const string TreeKey = "mpReading";

  /// <summary>Writes the reading into <paramref name="host"/> under <see cref="TreeKey"/>, replacing an earlier one.</summary>
  public void WriteTo(ITreeAttribute host) {
    var tree = new TreeAttribute();
    tree.SetFloat("t", TorqueNm);
    tree.SetFloat("r", RatingNm);
    tree.SetFloat("s", Strain);
    tree.SetFloat("w", SpeedRadS);
    tree.SetInt("z", (int)Size);
    host[TreeKey] = tree;
  }

  /// <summary>Reads the reading written by <see cref="WriteTo"/>.</summary>
  /// <returns>False, with <paramref name="reading"/> default, when <paramref name="host"/> holds none.</returns>
  public static bool TryReadFrom(
    ITreeAttribute host,
    out MpSegmentReading reading
  ) {
    reading = default;
    var tree = host.GetTreeAttribute(TreeKey);
    if (tree == null)
      return false;
    reading = new MpSegmentReading(
      tree.GetFloat("t"),
      tree.GetFloat("r"),
      tree.GetFloat("s"),
      tree.GetFloat("w"),
      (MpShaftSize)tree.GetInt("z")
    );
    return true;
  }
}

/// <summary>Decides when a segment's reading is worth writing: past 2 % of rating in torque or 1 % in strain from the last one written, and at most once a second.</summary>
internal sealed class MpReadingGate {
  /// <summary>Torque change, as a fraction of rating, that earns a write.</summary>
  public const float TorqueFraction = 0.02f;

  /// <summary>Strain change that earns a write.</summary>
  public const float StrainStep = 0.01f;

  /// <summary>Least time between two writes of one segment, seconds.</summary>
  public const float MinIntervalS = 1f;

  private readonly Dictionary<
    NodeKey,
    (MpSegmentReading Reading, float TimeS)
  > _written = [];

  /// <summary>True, and the reading is remembered, when <paramref name="reading"/> of <paramref name="key"/> should be written at <paramref name="nowS"/> (seconds on any steady clock). A segment never written is admitted once.</summary>
  public bool TryAdmit(NodeKey key, MpSegmentReading reading, float nowS) {
    if (_written.TryGetValue(key, out var last)) {
      if (nowS - last.TimeS < MinIntervalS)
        return false;
      bool torque =
        MathF.Abs(reading.TorqueNm - last.Reading.TorqueNm)
        > TorqueFraction * reading.RatingNm;
      bool strain =
        MathF.Abs(reading.Strain - last.Reading.Strain) > StrainStep;
      if (!torque && !strain)
        return false;
    }
    _written[key] = (reading, nowS);
    return true;
  }
}
