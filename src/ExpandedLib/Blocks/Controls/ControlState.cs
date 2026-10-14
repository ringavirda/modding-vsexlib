using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

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

/// <summary>Implemented by a principal's block entity to answer the collision of its cells from the entity's
/// state, for a body whose shape follows its contents. The answer replaces the cell's row collision; the
/// selection stays the row's.</summary>
/// <remarks>
/// The entity is asked for a filler whose <c>fillerOffsets</c> row carries box keys, and for the principal's own
/// cell of a block with a <c>principalCell</c> that is a mega-block, an <c>IFillerHost</c> node or a
/// <c>ControlBoxes.Collision</c> caller. Read on physics threads through the box lookups, so it must not block or
/// mutate.
/// The client answers from its copy of the entity's tree: the server writes the state into the tree and calls
/// <c>MarkDirty(true)</c> on every change, as vanilla's coal pile does.
/// </remarks>
public interface ICellCollision {
  /// <summary>The collision boxes of the cell at <paramref name="northOffset"/> from the principal, in the placed frame.</summary>
  /// <param name="northOffset">The cell's offset from the principal in cells, in the north-oriented footprint; (0, 0, 0)
  /// is the principal's own cell.</param>
  /// <param name="angle">The structure's angle in degrees, 0 to 359, that turns the north footprint into the placed one.</param>
  /// <returns>The cell's collision boxes in cell units from 0 to 1, or null to keep the row's boxes.</returns>
  Cuboidf[]? CollisionAt(Vec3i northOffset, int angle);
}
