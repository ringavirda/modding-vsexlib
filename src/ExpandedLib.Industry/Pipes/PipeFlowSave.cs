using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.Pipes;

/// <summary>What a block entity saves for one pipe node.</summary>
/// <param name="GasKg">Gas in the void, in kg.</param>
/// <param name="AirKg">Air in the void of a water node, in kg.</param>
/// <param name="LiquidKg">Liquid in the node, in kg.</param>
/// <param name="TemperatureC">Content temperature in C.</param>
/// <param name="WallTemperatureC">Wall temperature in C.</param>
/// <param name="Grade">Fuel grade, 0..1.</param>
/// <param name="Damage">Damage state code.</param>
internal readonly record struct PipeNodeSave(
  double GasKg,
  double AirKg,
  double LiquidKg,
  double TemperatureC,
  double WallTemperatureC,
  double Grade,
  byte Damage
);

/// <summary>
/// The save format of a physical pipe node: a sub-tree <c>pf{slot}</c> in its host block entity's tree with
/// <c>g</c>, <c>a</c>, <c>l</c>, <c>t</c>, <c>w</c>, <c>q</c> (doubles), <c>dmg</c> (a byte array of one) and
/// <c>v</c>, the format version.
/// </summary>
internal static class PipeFlowSave {
  /// <summary>The version a write stamps and a read accepts.</summary>
  public const int FormatVersion = 1;

  /// <summary>The key of the sub-tree holding the node at <paramref name="slot"/>.</summary>
  public static string Key(byte slot) => $"pf{slot}";

  /// <summary>Writes <paramref name="save"/> into <paramref name="host"/> under the key of <paramref name="slot"/>, replacing an earlier entry.</summary>
  public static void Write(ITreeAttribute host, byte slot, PipeNodeSave save) {
    var tree = new TreeAttribute();
    tree.SetInt("v", FormatVersion);
    tree.SetDouble("g", save.GasKg);
    tree.SetDouble("a", save.AirKg);
    tree.SetDouble("l", save.LiquidKg);
    tree.SetDouble("t", save.TemperatureC);
    tree.SetDouble("w", save.WallTemperatureC);
    tree.SetDouble("q", save.Grade);
    tree.SetBytes("dmg", [save.Damage]);
    host[Key(slot)] = tree;
  }

  /// <summary>Reads the node saved at <paramref name="slot"/>; false when there is none or its format version is not <see cref="FormatVersion"/>.</summary>
  public static bool TryRead(
    ITreeAttribute host,
    byte slot,
    out PipeNodeSave save
  ) {
    save = default;
    var tree = host.GetTreeAttribute(Key(slot));
    if (tree == null || tree.GetInt("v") != FormatVersion)
      return false;
    byte[]? damage = tree.GetBytes("dmg");
    save = new PipeNodeSave(
      tree.GetDouble("g"),
      tree.GetDouble("a"),
      tree.GetDouble("l"),
      tree.GetDouble("t"),
      tree.GetDouble("w"),
      tree.GetDouble("q"),
      damage is { Length: > 0 } ? damage[0] : (byte)0
    );
    return true;
  }
}
