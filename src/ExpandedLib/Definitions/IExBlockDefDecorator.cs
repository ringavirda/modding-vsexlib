namespace ExpandedLib.Definitions;

/// <summary>
/// Implemented by a class with a parameterless constructor that edits every block def of the process
/// as it registers: a provider's and a contributor's. Discovered per assembly by
/// <see cref="ExDefinitions.DiscoverDecorators"/>, with <see cref="IExItemDefDecorator"/>.
/// </summary>
public interface IExBlockDefDecorator {
  /// <summary>Edits <paramref name="def"/> in place. Runs once for each def object, on every def of
  /// every domain, so an implementation filters by <see cref="ExBlockDef.Domain"/> and
  /// <see cref="ExBlockDef.Code"/> and leaves a def it does not own untouched.</summary>
  void Decorate(ExBlockDef def);
}
