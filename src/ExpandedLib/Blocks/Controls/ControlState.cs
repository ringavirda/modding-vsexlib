using Vintagestory.API.Common;

namespace ExpandedLib.Blocks;

/// <summary>What a control or door looks like to the player right now.</summary>
public enum ControlState {
  /// <summary>The default: a control selectable, a door shut.</summary>
  Shown,

  /// <summary>A door open: its open leaves select, and collide when the door's row says so.</summary>
  Open,

  /// <summary>The control's part is not there: no box, no leaves, no verb.</summary>
  Hidden,
}

/// <summary>Implemented by a principal's block entity or one of its behaviours to answer for the controls it
/// owns. <see cref="ControlBoxes.StateOf"/> asks the entity, then each behaviour, and combines the
/// answers: Hidden if any says Hidden, else Open if any says Open, else Shown.</summary>
public interface IControlStates {
  /// <summary>The state of the control or door <paramref name="controlId"/>, or null for an id this
  /// object does not own. Read on physics threads through the box lookups, so it must not block or
  /// mutate.</summary>
  ControlState? StateOf(string controlId);
}
