namespace ExpandedLib.Definitions;

/// <summary>
/// Implemented by a class with a parameterless constructor that edits every item def of the process
/// as it registers: a provider's, a generated family's and a contributor's. Discovered per assembly
/// by <see cref="ExDefinitions.DiscoverDecorators"/>.
/// </summary>
public interface IExItemDefDecorator {
  /// <summary>Edits <paramref name="def"/> in place. Runs once for each def object, on every def of
  /// every domain, so an implementation filters by <see cref="ExItemDef.Domain"/> and
  /// <see cref="ExItemDef.Code"/> and leaves a def it does not own untouched.</summary>
  void Decorate(ExItemDef def);
}
