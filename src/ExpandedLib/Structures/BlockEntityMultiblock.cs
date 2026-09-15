using ExpandedLib.Helpers;
using ExpandedLib.Registries;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Structures;

/// <summary>
/// Concrete <see cref="BlockEntityMultiblockStructure"/> for a JSON-only mega-block: no C# subclass is
/// needed to get orientation, completion monitoring and the incomplete/complete messages.
/// </summary>
[BlockEntityRegister("ExMultiblock", PrefixModId = false)]
public class BlockEntityMultiblock : BlockEntityMultiblockStructure {
  /// <inheritdoc/>
  protected override void UpdateStructureRotation() {
    if (Block == null)
      return;
    SetStructureAngle(
      ExOrientation.AngleFromSide(
        Block.Variant?["side"] ?? Block.Variant?["orientation"]
      )
    );
  }

  /// <inheritdoc/>
  protected override string GetIncompleteMessage(int missingCount) =>
    Lang.GetWithFallback(
      DomainKey("incomplete"),
      FallbackKey("incomplete"),
      missingCount
    );

  /// <inheritdoc/>
  protected override string GetCompleteMessage() =>
    Lang.GetWithFallback(DomainKey("complete"), FallbackKey("complete"));

  /// <summary>Exposes <see cref="GetIncompleteMessage"/> to callers with no client API.</summary>
  internal string IncompleteMessageForTest(int missingCount) =>
    GetIncompleteMessage(missingCount);

  // The domain's own override key for the given suffix.
  private string DomainKey(string suffix) =>
    $"{Block.Code.Domain}:multiblock-{Block.Code.Path}-{suffix}";

  // The exlib default key for the given suffix.
  private static string FallbackKey(string suffix) =>
    $"exlib:multiblock-{suffix}";
}
