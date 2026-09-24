namespace ExpandedLib.Industry.Pipes;

/// <summary>
/// A pipe-network node block that supplies its own <see cref="IPipeVentStrategy"/>, in place of
/// the one the "pipe" network factory registered. Implemented on the <c>Block</c> class; the
/// network does not ask block entities.
/// </summary>
public interface IPipeVentSource {
  /// <summary>Creates the strategy that classifies and vents this block's open faces.</summary>
  /// <remarks>Each <see cref="PipeNetwork"/> calls it once, the first tick this block is among its
  /// nodes, and keeps the instance for its own lifetime, so per-run state is never shared between
  /// runs. Each distinct strategy vents only the faces it classified, at its own rate (for
  /// <see cref="ChimneyVent"/>, litres per second per chimney).</remarks>
  /// <returns>The strategy, or <c>null</c> to classify this block's faces with the factory's
  /// strategy; when that is null too, the faces are never vents and a face open to air
  /// leaks.</returns>
  IPipeVentStrategy? CreateVentStrategy();
}
