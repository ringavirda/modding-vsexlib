using System.Collections;
using Vintagestory.API.Common;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Testing;

/// <summary>Helpers for headless mechanical-power tests, building and binding a
/// <see cref="MechanicalNetwork"/> onto a real <see cref="BEBehaviorMPBase"/> directly, without the
/// chunk-load wiring the headless harness skips.</summary>
public static class MechPower {
  /// <summary>A fake mechanical network turning at <paramref name="speed"/> with the given load.</summary>
  public static MechanicalNetwork Network(float speed, float resistance = 0f) =>
    new() { Speed = speed, NetworkResistance = resistance };

  /// <summary>Binds <paramref name="network"/> onto <paramref name="behavior"/>'s private
  /// <c>network</c> field and attaches the behavior to <paramref name="be"/>.</summary>
  public static T Attach<T>(
    BlockEntity be,
    T behavior,
    MechanicalNetwork? network
  )
    where T : BEBehaviorMPBase {
    if (network != null)
      ReflectionHelpers.SetField(behavior, "network", network);
    var list = (IList)ReflectionHelpers.GetField(be, "Behaviors")!;
    list.Add(behavior);
    return behavior;
  }
}
