namespace ExpandedLib.Machines;

/// <summary>
/// One answer to whether a machine may run its production process, published by whatever knows it.
/// Read through <see cref="ProductionReadiness"/>, never by naming the type that answers.
/// </summary>
public interface IProductionReadiness {
  /// <summary>Whether the machine may run production right now.</summary>
  bool IsReadyToProduce { get; }

  /// <summary>Whether losing readiness also unregisters the production tick; a machine that must keep
  /// running while un-ready answers <c>false</c> here rather than widening
  /// <see cref="IsReadyToProduce"/>.</summary>
  bool StopsProductionWhenNotReady => true;
}
