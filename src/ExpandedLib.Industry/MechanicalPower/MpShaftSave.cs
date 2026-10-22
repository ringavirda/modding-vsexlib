using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Industry.MechanicalPower;

/// <summary>The state of one rigid "mpshaft" run as a flywheel saves it.</summary>
/// <param name="Speed">Reference speed of the run in rad/s, signed.</param>
/// <param name="Inertia">Inertia in kg*m^2 the saving block entity stands for; the weight its speed carries when runs meet again on load.</param>
internal sealed record MpShaftRunState(float Speed, float Inertia);

/// <summary>
/// The save format of the "mpshaft" model: a run sub-tree <c>mpRun</c> in a flywheel's block entity tree with
/// <c>v</c> (the format version), <c>w</c> and <c>i</c> (floats), and <c>mpStrain</c>, a segment's strain as a
/// float in a segment's tree.
/// </summary>
internal static class MpShaftSave {
  /// <summary>The version a write stamps and a read accepts.</summary>
  public const int FormatVersion = 1;

  /// <summary>The key of the run sub-tree.</summary>
  public const string RunKey = "mpRun";

  /// <summary>The key of a segment's strain.</summary>
  public const string StrainKey = "mpStrain";

  /// <summary>Writes <paramref name="run"/> into <paramref name="host"/>, replacing an earlier entry.</summary>
  public static void WriteRun(ITreeAttribute host, MpShaftRunState run) {
    var tree = new TreeAttribute();
    tree.SetInt("v", FormatVersion);
    tree.SetFloat("w", run.Speed);
    tree.SetFloat("i", run.Inertia);
    host[RunKey] = tree;
  }

  /// <summary>Reads the run saved in <paramref name="host"/>; false when there is none or its format version is not <see cref="FormatVersion"/>.</summary>
  public static bool TryReadRun(ITreeAttribute host, out MpShaftRunState run) {
    run = new MpShaftRunState(0f, 0f);
    var tree = host.GetTreeAttribute(RunKey);
    if (tree == null || tree.GetInt("v") != FormatVersion)
      return false;
    run = new MpShaftRunState(tree.GetFloat("w"), tree.GetFloat("i"));
    return true;
  }

  /// <summary>Writes a segment's strain, 0 to 1, into <paramref name="host"/>.</summary>
  public static void WriteStrain(ITreeAttribute host, float strain) =>
    host.SetFloat(StrainKey, strain);

  /// <summary>Reads a segment's strain; 0 for a tree that holds none.</summary>
  public static float ReadStrain(ITreeAttribute host) =>
    host.GetFloat(StrainKey);
}
