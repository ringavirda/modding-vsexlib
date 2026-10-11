using System;

namespace ExpandedLib.Testing.Live;

/// <summary>Who a failed step is blamed on (scenarios.md, When a step fails).</summary>
internal static class Suspect {
  public const string Mod = "mod";
  public const string Scenario = "scenario";
}

/// <summary>A step that did not hold or could not run: the reason it prints and the suspect
/// the case's fail line names.</summary>
internal sealed class StepFailure(
  string reason,
  string suspect = Suspect.Mod,
  Exception? cause = null
) : Exception(reason, cause) {
  public string Suspect => suspect;

  /// <summary>The step's own FAIL line is already printed, so a sequence above it does not
  /// print another.</summary>
  public bool Reported { get; set; }
}
