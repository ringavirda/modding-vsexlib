using Vintagestory.API.Common;

namespace ExpandedLib.Industry.Molten;

/// <summary>
/// A block entity holding molten metal that, once solidified and cooled, can be chipped out with a
/// chisel and hammer (<see cref="MoltenChisel"/>) or broken out hot with a crowbar (<see cref="MoltenBar"/>).
/// </summary>
public interface IChiselableMolten {
  /// <summary>Whether any solidified content is present; false when empty or still liquid.</summary>
  bool HasChiselableContent { get; }

  /// <summary>Whether the content can be chipped out now: solidified, cooled past the hardened
  /// threshold, and, where the holder caps it, small enough.</summary>
  bool CanChiselOut { get; }

  /// <summary>Whether set content is present that a crowbar can break out at any temperature: metal
  /// below its melting point, however hot. False when empty or still liquid; a holder that does not
  /// override it is never broken out.</summary>
  bool CanBreakOut => false;

  /// <summary>The <c>game:ingameerror-*</c> code to surface when <see cref="HasChiselableContent"/> is
  /// true but <see cref="CanChiselOut"/> is false; <c>null</c> claims the click silently.</summary>
  string? ChiselBlockedError { get; }

  /// <summary>Clears the set content and returns the recovered metal-bit drop, or <c>null</c>;
  /// server-side, called only once <see cref="CanChiselOut"/> or <see cref="CanBreakOut"/> is confirmed;
  /// a holder whose content is not set returns <c>null</c> and clears nothing.</summary>
  ItemStack? ChiselOut();
}
