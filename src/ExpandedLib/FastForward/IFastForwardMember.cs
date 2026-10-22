using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace ExpandedLib.FastForwarding;

/// <summary>A block entity (or one of its behaviours) whose server tick the fast-forward replay steps. Declared on the block entity with <see cref="BEBehaviorFastForward"/> in the block's JSON as <c>exlib.FastForward</c>; a block entity that registers its own tick adds the behaviour in its constructor instead and passes the tick.</summary>
public interface IFastForwardMember {
  /// <summary>Runs one live tick of <paramref name="dt"/> seconds. The replay calls it once per whole <see cref="FastForwardIntervalMs"/> of machine time with <c>dt</c> equal to that interval.</summary>
  /// <param name="dt">Seconds, the interval in seconds.</param>
  void FastForwardStep(float dt);

  /// <summary>The live tick's interval, in milliseconds, at least 1.</summary>
  int FastForwardIntervalMs { get; }

  /// <summary>Positions this member reads or writes straight from the world, outside any network lookup, so that the members and networks there share its group. Read on each rebuild of the groups; empty by default.</summary>
  IEnumerable<BlockPos> FastForwardCouplings => [];

  /// <summary>Whether the member has no grace or delay timer running; false while one does. True by default.</summary>
  bool FastForwardSettled => true;
}
