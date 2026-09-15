using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;

namespace ExpandedLib.Machines;

/// <summary>
/// Drives the production process a machine carries, without naming the class that carries it. The
/// counterpart of <see cref="ProductionReadiness"/>: readiness is what a form publishes, this is what
/// it commands.
/// </summary>
public static class ProductionProcess {
  /// <summary>Every process <paramref name="be"/> carries; empty when it carries none.</summary>
  public static IEnumerable<BEBehaviorProductionMachine> ProcessesOn(
    BlockEntity? be
  ) => be == null ? [] : be.Behaviors.OfType<BEBehaviorProductionMachine>();

  /// <summary>Registers the production tick of every process on <paramref name="be"/>; idempotent and
  /// server-side only.</summary>
  public static void Start(BlockEntity? be) {
    foreach (BEBehaviorProductionMachine process in ProcessesOn(be))
      process.StartProductionTick();
  }

  /// <summary>Unregisters the production tick of every process on <paramref name="be"/>.</summary>
  public static void Stop(BlockEntity? be) {
    foreach (BEBehaviorProductionMachine process in ProcessesOn(be))
      process.StopProductionTick();
  }
}
