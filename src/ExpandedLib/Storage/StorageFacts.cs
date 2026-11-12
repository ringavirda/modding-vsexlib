namespace ExpandedLib.Storage;

/// <summary>
/// What <see cref="ExStorage.Classify"/> reads of one item variant.
/// </summary>
/// <param name="A">Longest posed extent in voxels (16 is one block). For a stock piece code, the
/// largest legal piece's.</param>
/// <param name="B">Middle posed extent in voxels, at most <paramref name="A"/>.</param>
/// <param name="C">Thinnest posed extent in voxels, at most <paramref name="B"/>.</param>
/// <param name="Fill">Share of the A x B x C box the drawn elements cover, 0 to 1.</param>
/// <param name="MaxStack">The item's max stack size.</param>
/// <param name="Kept">The def already carries GroundStorable.</param>
/// <param name="UnderTools">The def's asset path is under <c>tools/</c>.</param>
/// <param name="StockPiece">The code is a stock piece code.</param>
/// <param name="LooseShape">The shape is a crushed or powder one.</param>
/// <param name="Fastener">The item is a bolt, bolt blank or rivet.</param>
/// <param name="Unplaceable">Why the item is never stored whatever its size (no shape, a work item,
/// a block), or null.</param>
public sealed record StorageFacts(
  double A,
  double B,
  double C,
  double Fill,
  int MaxStack,
  bool Kept = false,
  bool UnderTools = false,
  bool StockPiece = false,
  bool LooseShape = false,
  bool Fastener = false,
  string? Unplaceable = null
);
