using Vintagestory.API.Common;

namespace ExpandedLib.Industry.Molten;

/// <summary>
/// A block entity holding molten metal that, once solidified and cooled, can be chipped out with a
/// chisel and hammer. <see cref="MoltenChisel"/> drives the interaction; an implementer supplies only
/// the content-specific state and the clear-and-recover step.
/// </summary>
public interface IChiselableMolten {
  /// <summary>Whether any solidified content is present; false when empty or still liquid.</summary>
  bool HasChiselableContent { get; }

  /// <summary>Whether the content can be chipped out now: solidified, cooled past the hardened
  /// threshold, and, where the holder caps it, small enough.</summary>
  bool CanChiselOut { get; }

  /// <summary>The <c>game:ingameerror-*</c> code to surface when <see cref="HasChiselableContent"/> is
  /// true but <see cref="CanChiselOut"/> is false; <c>null</c> claims the click silently.</summary>
  string? ChiselBlockedError { get; }

  /// <summary>Chips the hardened content out and returns the recovered metal-bit drop, or <c>null</c>;
  /// server-side, called only once <see cref="CanChiselOut"/> is confirmed.</summary>
  ItemStack? ChiselOut();
}
