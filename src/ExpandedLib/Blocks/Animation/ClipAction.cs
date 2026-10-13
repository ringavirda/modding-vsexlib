using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Blocks;

/// <summary>How a block's animator shows one clip.</summary>
public enum ClipPose {
  /// <summary>The clip is held at a frame between its first and its last, the frame a synced state
  /// sets, moving there at the action's <see cref="ClipAction.Seconds"/>.</summary>
  Frame,

  /// <summary>The clip plays once when its state takes the <see cref="ClipAction.When"/> value.</summary>
  Play,

  /// <summary>The clip repeats while its state takes the <see cref="ClipAction.When"/> value, at a
  /// rate that follows the state.</summary>
  Run,

  /// <summary>The clip's frame follows an axle's angle.</summary>
  Lock,
}

/// <summary>One declared action of a block: which clip it shows, how, what sets it and how long it
/// takes. A block declares its actions in <c>attributes.clipActions</c>, an array of these written
/// as JSON; direction and amount are never declared, they are the clip's keyframes.</summary>
/// <remarks>Every property but <see cref="Clip"/> is optional. <see cref="ClipActions.Resolve"/>
/// fills the defaults from the block's shape.</remarks>
/// <param name="Clip">The shape animation's code.</param>
public sealed record ClipAction(string Clip) {
  /// <summary>How the clip is shown; null takes it from the clip's <c>onAnimationEnd</c>: Repeat
  /// is <see cref="ClipPose.Run"/>, any other <see cref="ClipPose.Frame"/>.</summary>
  public ClipPose? Pose { get; init; }

  /// <summary>The key of the synced tree attribute that sets the action, a bool or a number; null
  /// for an action only the block entity or a help line works.</summary>
  public string? State { get; init; }

  /// <summary>The state values that mean the clip's first and its last frame, low then high; null
  /// is 0 to 1. A bool reads 0 or 1.</summary>
  public (float Low, float High)? Range { get; init; }

  /// <summary>For <see cref="ClipPose.Play"/> and <see cref="ClipPose.Run"/>, the state value
  /// whose clip this is; null otherwise.</summary>
  public float? When { get; init; }

  /// <summary>The block-help action lang codes that work the action: one toggles it, two raise
  /// then lower it; null for none.</summary>
  public string[]? By { get; init; }

  /// <summary>Seconds, from 0 above, for <see cref="ClipPose.Frame"/> from one end of the clip to
  /// the other, for <see cref="ClipPose.Play"/> the whole clip, for <see cref="ClipPose.Run"/> one
  /// cycle at state 1; null is the clip's frames minus one over 30.</summary>
  public float? Seconds { get; init; }

  /// <summary>The shape element names the action moves; null is every element the clip keys.</summary>
  public string[]? Parts { get; init; }

  internal JObject ToJson() {
    var json = new JObject { ["clip"] = Clip };
    if (Pose is { } pose)
      json["pose"] = pose.ToString().ToLowerInvariant();
    if (State != null)
      json["state"] = State;
    if (Range is { } range)
      json["range"] = new JArray(range.Low, range.High);
    if (When is { } when)
      json["when"] = when;
    if (By != null)
      json["by"] = new JArray(By);
    if (Seconds is { } seconds)
      json["seconds"] = seconds;
    if (Parts != null)
      json["parts"] = new JArray(Parts);
    return json;
  }

  internal static ClipAction Read(JsonObject entry, string owner) {
    string clip =
      entry["clip"].AsString()
      ?? throw new InvalidOperationException(
        $"{owner}: a clip action names no clip."
      );
    string? state = entry["state"].AsString();
    string[]? by = entry["by"].AsArray<string>();
    if (state == null && by == null)
      throw new InvalidOperationException(
        $"{owner}: clip action '{clip}' has neither a state nor a by line, so nothing works it."
      );
    ClipPose? pose = null;
    if (entry["pose"].Exists) {
      string text = entry["pose"].AsString() ?? "";
      if (
        !Enum.TryParse<ClipPose>(text, ignoreCase: true, out ClipPose parsed)
        || !Enum.IsDefined(parsed)
      )
        throw new InvalidOperationException(
          $"{owner}: clip action '{clip}' has pose '{text}', which is not frame, play, run or lock."
        );
      pose = parsed;
    }
    if (pose == ClipPose.Lock)
      throw new InvalidOperationException(
        $"{owner}: clip action '{clip}' is a lock, which is not built yet."
      );
    (float Low, float High)? range = null;
    if (entry["range"].Exists) {
      if (
        entry["range"].AsArray<float>() is not [var low, var high]
        || low == high
      )
        throw new InvalidOperationException(
          $"{owner}: clip action '{clip}' has a range that is not two distinct numbers."
        );
      range = (low, high);
    }
    return new ClipAction(clip) {
      Pose = pose,
      State = state,
      Range = range,
      When = entry["when"].Exists ? entry["when"].AsFloat() : null,
      By = by,
      Seconds = entry["seconds"].Exists ? entry["seconds"].AsFloat() : null,
      Parts = entry["parts"].AsArray<string>(),
    };
  }
}
