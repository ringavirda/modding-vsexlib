using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Machines;

/// <summary>
/// Reads the readiness a machine publishes, reduced across every publisher on the block entity and
/// its behaviours to one answer for the machine.
/// </summary>
public static class ProductionReadiness {
  /// <summary>Every readiness answer <paramref name="be"/> publishes; empty when it publishes none.</summary>
  public static IEnumerable<IProductionReadiness> PublishersOn(BlockEntity? be) {
    if (be == null)
      yield break;

    if (be is IProductionReadiness self)
      yield return self;

    foreach (
      IProductionReadiness behaviour in be.Behaviors.OfType<IProductionReadiness>()
    )
      yield return behaviour;
  }

  /// <summary>Whether every publisher on <paramref name="be"/> is ready; a machine with no publisher
  /// is ready.</summary>
  public static bool IsReady(BlockEntity? be) =>
    PublishersOn(be).All(p => p.IsReadyToProduce);

  /// <summary>Whether losing readiness unregisters <paramref name="be"/>'s production tick; a single
  /// publisher answering <c>false</c> keeps the tick for the whole machine.</summary>
  public static bool StopsProductionWhenNotReady(BlockEntity? be) =>
    PublishersOn(be).All(p => p.StopsProductionWhenNotReady);
}
