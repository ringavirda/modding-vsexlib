using Vintagestory.API.Common;

namespace ExpandedLib.FastForwarding;

/// <summary>How a production machine takes part in the fast-forward of the game time its real ticks did not cover.</summary>
public enum FastForwardMode {
  /// <summary>The machine is a member of its group: held while the group replays, stepped by the replay, frozen with the group. The default.</summary>
  Replay,

  /// <summary>The machine is no member of any group and keeps the span its own process measures from the calendar.</summary>
  Off,
}

internal static class FastForwardModes {
  /// <summary>The mode a block declares in its <c>fastForward</c> attribute, <c>"replay"</c> or <c>"off"</c>; <see cref="FastForwardMode.Replay"/> when absent or unrecognised.</summary>
  internal static FastForwardMode Of(Block? block) =>
    block?.Attributes?["fastForward"]?.AsString("replay") == "off"
      ? FastForwardMode.Off
      : FastForwardMode.Replay;
}
